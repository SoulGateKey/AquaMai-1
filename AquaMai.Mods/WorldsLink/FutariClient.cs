using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using AMDaemon;
using PartyLink;
using static Manager.Accounting;

namespace AquaMai.Mods.WorldsLink;

public class FutariClient(string keychip, string host, int port, int _)
{
    public static FutariClient Instance { get; set; }

    public FutariClient(string keychip, string host, int port) : this(keychip, host, port, 0)
    {
        Instance = this;
    }

    public string keychip { get; set; } = keychip;

    private TcpClient _tcpClient;
    private StreamWriter _writer;
    private StreamReader _reader;

    public readonly ConcurrentQueue<Msg> sendQ = new();
    // <Port + Stream ID, Message Queue>
    public readonly ConcurrentDictionary<int, ConcurrentQueue<Msg>> tcpRecvQ = new();
    // <Port, Message Queue>
    public readonly ConcurrentDictionary<int, ConcurrentQueue<Msg>> udpRecvQ = new();
    // <Port, Accept Queue>
    public readonly ConcurrentDictionary<int, ConcurrentQueue<Msg>> acceptQ = new();
    // <Port + Stream ID, Callback>
    public readonly ConcurrentDictionary<int, Action<Msg>> acceptCallbacks = new();

    private Thread _sendThread;
    private Thread _recvThread;
     
    private bool _reconnecting = false;

    private readonly Stopwatch _heartbeat = new Stopwatch().Also(it => it.Start());
    private readonly long[] _delayWindow = new int[20].Select(_ => -1L).ToArray();
    public int _delayIndex = 0;
    public long _delayAvg = 0;
    
    public IPAddress StubIP => FutariExt.KeychipToStubIp(keychip).ToIP();

    public int StatusCode { get => statusCode; }
    public string ErrorMsg { get => errorMsg; }
    public string Host { get => host; }
    public int Port { get => port; }

    public void ConnectAsync() => new Thread(Connect) { IsBackground = true }.Start();

    /// <summary>
    /// -1: Failed to connect
    /// 0: Not connect
    /// 1: Connecting
    /// 2: Connected
    /// </summary>
    int statusCode = 0;
    string errorMsg = "";

    public void Connect()
    {
        _tcpClient = new TcpClient();

        try
        {
            statusCode = 1;
            _tcpClient.Connect(host, port);
            statusCode = 2;
        }
        catch (Exception ex)
        {
            statusCode = -1;
            errorMsg = ex.Message;
            Log.Error($"Error connecting to server:\nHost:{host}:{port}\n{ex.Message}");
            ConnectAsync();
            return;
        }
        var networkStream = _tcpClient.GetStream();
        _writer = new StreamWriter(networkStream, Encoding.UTF8) { AutoFlush = true };
        _reader = new StreamReader(networkStream, Encoding.UTF8);
        _reconnecting = false;

        // Register
        Send(new Msg { cmd = Cmd.CTL_START, data = keychip });
        Log.Info($"Connected to server at {host}:{port}");

        // Start communication and message receiving in separate threads
        _sendThread = new Thread(SendThread) { IsBackground = true };
        _recvThread = new Thread(RecvThread) { IsBackground = true };

        _sendThread.Start();
        _recvThread.Start();
    }

    public void Bind(int port, ProtocolType proto)
    {
        if (proto == ProtocolType.Tcp) 
            acceptQ.TryAdd(port, new ConcurrentQueue<Msg>());
        else if (proto == ProtocolType.Udp)
            udpRecvQ.TryAdd(port, new ConcurrentQueue<Msg>());
    }

    private void Reconnect()
    {
        Log.Warn("Reconnect Entered");
        if (_reconnecting) return;
        _reconnecting = true;
        
        try { _tcpClient.Close(); }
        catch { /* ignored */ }

        try { _sendThread.Abort(); }
        catch { /* ignored */ }
        
        try { _recvThread.Abort(); }
        catch { /* ignored */ }
        
        _sendThread = null;
        _recvThread = null;
        _tcpClient = null;
        
        // Reconnect
        Log.Warn("Reconnecting...");
        ConnectAsync();
    }

    private void SendThread()
    {
        try
        {
            while (true)
            {
                if (_heartbeat.ElapsedMilliseconds > 1000)
                {
                    _heartbeat.Restart();
                    Send(new Msg { cmd = Cmd.CTL_HEARTBEAT });
                }

                // Send any data in the send queue
                while (sendQ.TryDequeue(out var msg))
                    Send(msg);

                Thread.Sleep(10);
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Error during communication: {ex.Message}");
        }
        finally
        {
            Log.Error("SendThread finally reached");
            Reconnect();
        }
    }

    private void RecvThread()
    {
        try
        {
            while (true)
            {
                var line = _reader.ReadLine();
                if (line == null) continue;

                var message = Msg.FromString(line);
                HandleIncomingMessage(message);
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Error receiving messages: {ex.Message}");
        }
        finally
        {
            Log.Error("RecvThread finally reached");
            Reconnect();
        }
    }

    private void HandleIncomingMessage(Msg msg)
    {
        if (msg.cmd != Cmd.CTL_HEARTBEAT)
            Log.Info($"{FutariExt.KeychipToStubIp(keychip).ToIP()} <<< {msg.ToReadableString()}");

        switch (msg.cmd)
        {
            // Heartbeat
            case Cmd.CTL_HEARTBEAT:
                var delay = _heartbeat.ElapsedMilliseconds;
                _delayWindow[_delayIndex] = delay;
                _delayIndex = (_delayIndex + 1) % _delayWindow.Length;
                _delayAvg = (long) _delayWindow.Where(x => x != -1).Average();
                Log.Info($"Heartbeat: {delay}ms, Avg: {_delayAvg}ms");
                break;
            
            // UDP message
            case Cmd.DATA_SEND or Cmd.DATA_BROADCAST when msg is { proto: ProtocolType.Udp, dPort: not null }:
                udpRecvQ.Get(msg.dPort.Value)?.Also(q =>
                {
                    Log.Info($"+ Added to UDP queue, there are {q.Count + 1} messages in queue");
                })?.Enqueue(msg);
                break;
            
            // TCP message
            case Cmd.DATA_SEND when msg.proto == ProtocolType.Tcp && msg is { sid: not null, dPort: not null }:
                tcpRecvQ.Get(msg.sid.Value + msg.dPort.Value)?.Also(q =>
                {
                    Log.Info($"+ Added to TCP queue, there are {q.Count + 1} messages in queue for port {msg.dPort}");
                })?.Enqueue(msg);
                break;
            
            // TCP connection request
            case Cmd.CTL_TCP_CONNECT when msg.dPort != null:
                acceptQ.Get(msg.dPort.Value)?.Also(q =>
                {
                    Log.Info($"+ Added to Accept queue, there are {q.Count + 1} messages in queue");
                })?.Enqueue(msg);
                break;
            
            // TCP connection accept
            case Cmd.CTL_TCP_ACCEPT when msg is { sid: not null, dPort: not null }:
                acceptCallbacks.Get(msg.sid.Value + msg.dPort.Value)?.Invoke(msg);
                break;
        }
    }

    private void Send(Msg msg)
    {
        _writer.WriteLine(msg);
        if (msg.cmd != Cmd.CTL_HEARTBEAT)
            Log.Info($"{FutariExt.KeychipToStubIp(keychip).ToIP()} >>> {msg.ToReadableString()}");
    }
}
