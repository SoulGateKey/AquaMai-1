using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using PartyLink;

namespace AquaMai.Mods.WorldsLink;

public class FutariSocket
{
    private int _bindPort = -1;
    private readonly FutariClient _client;
    private readonly ProtocolType _proto;
    private int _streamId = -1;

    // ConnectSocket.Enter_Active (doesn't seem to be actually used)
    public EndPoint LocalEndPoint => new IPEndPoint(_client.StubIP, 0);

    // ConnectSocket.Enter_Active, ListenSocket.acceptClient (TCP)
    // Each client's remote endpoint must be different
    public EndPoint RemoteEndPoint { get; private set; } = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 0);

    public FutariSocket(FutariClient client, ProtocolType proto)
    {
        _client = client;
        _proto = proto;
        RemoteEndPoint = new IPEndPoint(_client.StubIP, 0);
    }

    // Compatibility constructor
    public FutariSocket(AddressFamily addressFamily, SocketType socketType, ProtocolType protocolType, int mockID) :
        this(FutariClient.Instance, protocolType) { }

    // ListenSocket.open (TCP)
    public void Listen(int backlog) { }

    // ListenSocket.open, UdpRecvSocket.open
    public void Bind(EndPoint localEndP)
    {
        if (localEndP is not IPEndPoint ipEndP) return;
        _bindPort = ipEndP.Port;
        _client.Bind(_bindPort, _proto);
        _client.sendQ.Enqueue(new Msg { cmd = Cmd.CTL_BIND, proto = _proto, 
            src = ipEndP.Address.ToNetworkByteOrderU32(), sPort = ipEndP.Port });
    }

    // Only used in BroadcastSocket
    public void SetSocketOption(SocketOptionLevel l, SocketOptionName n, bool o) { }

    // SocketBase.checkRecvEnable, checkSendEnable
    // This is the Select step called before blocking calls (e.g. Accept)
    public static bool Poll(FutariSocket socket, SelectMode mode)
    {
        if (mode == SelectMode.SelectRead)
        {
            return (socket._proto == ProtocolType.Udp)  // Check is UDP
                ? !socket._client.udpRecvQ.Get(socket._bindPort)?.IsEmpty ?? false
                : (socket._streamId == -1)  // Check is TCP stream or TCP server
                    ? !socket._client.acceptQ.Get(socket._bindPort)?.IsEmpty ?? false
                    : !socket._client.tcpRecvQ.Get(socket._streamId)?.IsEmpty ?? false;
        }
        // Write is always ready?
        return mode == SelectMode.SelectWrite;
    }

    // ConnectSocket.Enter_Connect (TCP)
    // The destination address is obtained from a RecruitInfo packet sent by the host.
    // The host should patch their PartyLink.Util.MyIpAddress() to return a mock address instead of the real one
    // Returns true if IO is pending and false if the operation completed synchronously
    // When it's false, e will contain the result of the operation
    public bool ConnectAsync(SocketAsyncEventArgs e, int mockID)
    {
        if (e.RemoteEndPoint is not IPEndPoint ipEndP) return false;
        var addr = ipEndP.Address.ToNetworkByteOrderU32();
        // Localhost
        if (addr is 2130706433 or 16777343) addr = _client.StubIP.ToNetworkByteOrderU32();
        _streamId = new Random().Next();
        _client.tcpRecvQ[_streamId] = new ConcurrentQueue<Msg>();
        Log.Error($"Addr: {addr}");
        _client.sendQ.Enqueue(new Msg
        {
            cmd = Cmd.CTL_TCP_CONNECT, 
            proto = _proto,
            sid = _streamId,
            dst = addr,
            dPort = ipEndP.Port
        });
        // It is very annoying to call Complete event using reflection
        // So we'll just pretend that the client has ACKed
        return false;
    }

    // Accept is blocking
    public FutariSocket Accept()
    {
        // Check if accept queue has any pending connections
        if (!_client.acceptQ.TryGetValue(_bindPort, out var q) ||
            !q.TryDequeue(out var msg) || 
            msg.sid == null ||
            msg.src == null)
        {
            Log.Warn("Accept: No pending connections");
            return null;
        }

        _client.tcpRecvQ[msg.sid.Value] = new ConcurrentQueue<Msg>();
        _client.sendQ.Enqueue(new Msg
        {
            cmd = Cmd.CTL_TCP_ACCEPT, proto = _proto, sid = msg.sid, dst = msg.src
        });
        
        return new FutariSocket(_client, _proto)
        {
            _streamId = msg.sid.Value,
            RemoteEndPoint = new IPEndPoint(msg.src.Value.ToIP(), _bindPort)
        };
    }

    public int Send(byte[] buffer, int offset, int size, SocketFlags socketFlags)
    {
        Log.Debug($"Send: {size} bytes");
        // Remote EP is not relevant here, because the stream is already established,
        // there can only be one remote endpoint
        _client.sendQ.Enqueue(new Msg
        {
            cmd = Cmd.DATA_SEND, proto = _proto, data = buffer.View(offset, size).B64(),
            sid = _streamId == -1 ? null : _streamId
        });
        return size;
    }

    // Only used in BroadcastSocket
    public int SendTo(byte[] buffer, int offset, int size, SocketFlags socketFlags, EndPoint remoteEP)
    {
        Log.Debug($"SendTo: {size} bytes");
        if (remoteEP is not IPEndPoint ipEndP) return 0;
        _client.sendQ.Enqueue(new Msg
        {
            cmd = Cmd.DATA_BROADCAST, proto = _proto, data = buffer.View(offset, size).B64(), dPort = ipEndP.Port
        });
        return size;
    }

    // Only used in TCP ConnectSocket
    public int Receive(byte[] buffer, int offset, int size, SocketFlags socketFlags, out SocketError errorCode)
    {
        Log.Debug("Receive called");
        if (!_client.tcpRecvQ.TryGetValue(_streamId, out var q) || 
            !q.TryDequeue(out var msg))
        {
            Log.Warn("Receive: No data to receive");
            errorCode = SocketError.WouldBlock;
            return 0;
        }
        var data = msg.data!.B64();

        Buffer.BlockCopy(data, 0, buffer, 0, data.Length);
        errorCode = SocketError.Success;
        return data.Length;
    }

    // Only used in UdpRecvSocket to receive from 0 (broadcast)
    public int ReceiveFrom(byte[] buffer, SocketFlags socketFlags, ref EndPoint remoteEP)
    {
        Log.Debug("ReceiveFrom called");
        if (!_client.udpRecvQ.TryGetValue(_bindPort, out var q) || 
            !q.TryDequeue(out var msg))
        {
            Log.Warn("ReceiveFrom: No data to receive");
            return 0;
        }
        var data = msg.data!.B64();

        Buffer.BlockCopy(data, 0, buffer, 0, data.Length);
        return data.Length;
    }

    // Called everywhere, but only relevant for TCP
    public void Close()
    {
        // TCP FIN/RST
        if (_proto == ProtocolType.Tcp) 
            _client.sendQ.Enqueue(new Msg { cmd = Cmd.CTL_TCP_CLOSE, proto = _proto });
    }

    public void Shutdown(SocketShutdown how) => Close();
}