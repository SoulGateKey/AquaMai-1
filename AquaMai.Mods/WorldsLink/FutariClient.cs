using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace AquaMai.Mods.WorldsLink;

public class FutariClient
{
    public static FutariClient Instance { get; set; }
    private static readonly JsonSerializerSettings settings = new() 
    {
        NullValueHandling = NullValueHandling.Ignore
    };
    
    private readonly string _keychip;
    private TcpClient _tcpClient;
    private StreamWriter _writer;
    private StreamReader _reader;

    public readonly ConcurrentQueue<Msg> sendQ = new();
    // <Stream ID, Message Queue>
    public readonly ConcurrentDictionary<int, ConcurrentQueue<Msg>> tcpRecvQ = new();
    // <Port, Message Queue>
    public readonly ConcurrentDictionary<int, ConcurrentQueue<Msg>> udpRecvQ = new();
    // <Port, Accept Queue>
    public readonly ConcurrentDictionary<int, ConcurrentQueue<Msg>> acceptQ = new();

    private Thread _sendThread;
    private Thread _recvThread;
    
    public IPAddress StubIP => new IPAddress(FutariExt.KeychipToStubIp(_keychip));

    public FutariClient(string keychip)
    {
        _keychip = keychip;
        Instance = this;
    }

    public void Connect(string host, int port)
    {
        _tcpClient = new TcpClient();
        _tcpClient.Connect(host, port);

        var networkStream = _tcpClient.GetStream();
        _writer = new StreamWriter(networkStream, Encoding.UTF8) { AutoFlush = true };
        _reader = new StreamReader(networkStream, Encoding.UTF8);

        // Register
        Send(new Msg { cmd = Cmd.CTL_START, data = _keychip });
        Log.WriteLine($"Connected to server at {host}:{port}");

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

    private void SendThread()
    {
        try
        {
            long lastHeartbeat = 0;
            while (true)
            {
                var time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                if (time - lastHeartbeat > 1000)
                {
                    Send(new Msg { cmd = Cmd.CTL_HEARTBEAT });
                    lastHeartbeat = time;
                }

                // Send any data in the send queue
                while (sendQ.TryDequeue(out var msg))
                    Send(msg);

                Thread.Sleep(10);
            }
        }
        catch (Exception ex)
        {
            Log.WriteLine($"Error during communication: {ex.Message}");
        }
        finally { _tcpClient.Close(); }
    }

    private void RecvThread()
    {
        try
        {
            while (true)
            {
                var line = _reader.ReadLine();
                if (line == null) break;

                var message = JsonConvert.DeserializeObject<Msg>(line);
                if (message == null) continue;
                HandleIncomingMessage(message);
            }
        }
        catch (Exception ex)
        {
            Log.WriteLine($"Error receiving messages: {ex.Message}");
        }
        finally { _tcpClient.Close(); }
    }

    private void HandleIncomingMessage(Msg msg)
    {
        Log.WriteLine($"{_keychip} <<< {JsonConvert.SerializeObject(msg, settings)}");

        switch (msg.cmd)
        {
            // UDP message
            case Cmd.DATA_SEND or Cmd.DATA_BROADCAST when msg.proto == ProtocolType.Udp && msg.dPort != null:
                udpRecvQ.Get(msg.dPort.Value)?.Enqueue(msg);
                break;
            
            // TCP connection
            case Cmd.DATA_SEND when msg.proto == ProtocolType.Tcp && msg.sid != null:
                tcpRecvQ.Get(msg.sid.Value)?.Enqueue(msg);
                break;
            
            // TCP connection accepted
            case Cmd.CTL_TCP_CONNECT when msg.dPort != null:
                acceptQ.Get(msg.dPort.Value)?.Enqueue(msg);
                break; 
        }
    }

    private void Send(Msg msg)
    {
        var json = JsonConvert.SerializeObject(msg, settings);
        _writer.WriteLine(json);
        Log.WriteLine($"{_keychip} >>> {json}");
    }
}
