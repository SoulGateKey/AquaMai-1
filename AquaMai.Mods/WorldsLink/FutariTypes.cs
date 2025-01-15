using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using MelonLoader;

namespace AquaMai.Mods.WorldsLink;

public enum Cmd
{
    // Control plane
    CTL_START = 1,
    CTL_BIND = 2,
    CTL_HEARTBEAT = 3,
    CTL_TCP_CONNECT = 4,  // Accept a new multiplexed TCP stream
    CTL_TCP_ACCEPT = 5,
    CTL_TCP_ACCEPT_ACK = 6,
    CTL_TCP_CLOSE = 7,

    // Data plane
    DATA_SEND = 21,
    DATA_BROADCAST = 22,
}


public struct Msg
{
    public Cmd cmd;
    public ProtocolType? proto;
    public int? sid;
    public uint? src;
    public int? sPort;
    public uint? dst;
    public int? dPort;
    public string? data;

    public override string ToString()
    {
        int? proto_ = proto == null ? null : (int) proto;
        var arr = new object[] {
            1, (int) cmd, proto_, sid, src, sPort, dst, dPort, 
            null, null, null, null, null, null, null, null,  // reserved for future use
            data
        };
        
        // Map nulls to empty strings
        return string.Join(",", arr.Select(x => x ?? "")).TrimEnd(',');
    }
    
    private static T? Parse<T>(string[] fields, int i) where T : struct 
        => fields.Length <= i || fields[i] == "" ? null
            : (T) Convert.ChangeType(fields[i], typeof(T));
    

    public static Msg FromString(string str)
    {
        var fields = str.Split(',');
        return new Msg
        {
            cmd = (Cmd) (Parse<int>(fields, 1) ?? throw new InvalidOperationException("cmd is required")),
            proto = Parse<int>(fields, 2)?.Let(it => (ProtocolType) it),
            sid = Parse<int>(fields, 3),
            src = Parse<uint>(fields, 4),
            sPort = Parse<int>(fields, 5),
            dst = Parse<uint>(fields, 6),
            dPort = Parse<int>(fields, 7),
            data = string.Join(",", fields.Skip(16))
        };
    }

    public string ToReadableString()
    {
        var parts = new List<string> { cmd.ToString() };

        if (proto.HasValue) parts.Add(proto.ToString());
        if (sid.HasValue) parts.Add($"Stream: {sid}");
        if (src.HasValue) parts.Add($"Src: {src?.ToIP()}:{sPort}");
        if (dst.HasValue) parts.Add($"Dst: {dst?.ToIP()}:{dPort}");
        if (!string.IsNullOrEmpty(data))
        {
            try { parts.Add(Encoding.UTF8.GetString(data.B64())); }
            catch { parts.Add(data); }
        }
    
        return string.Join(" | ", parts);
    }
}

public abstract class Log
{
    // Text colors
    private const string BLACK = "\u001b[30m";
    private const string RED = "\u001b[31m";
    private const string GREEN = "\u001b[32m";
    private const string YELLOW = "\u001b[33m";
    private const string BLUE = "\u001b[34m";
    private const string MAGENTA = "\u001b[35m";
    private const string CYAN = "\u001b[36m";
    private const string WHITE = "\u001b[37m";

    // Bright text colors
    private const string BRIGHT_BLACK = "\u001b[90m";
    private const string BRIGHT_RED = "\u001b[91m";
    private const string BRIGHT_GREEN = "\u001b[92m";
    private const string BRIGHT_YELLOW = "\u001b[93m";
    private const string BRIGHT_BLUE = "\u001b[94m";
    private const string BRIGHT_MAGENTA = "\u001b[95m";
    private const string BRIGHT_CYAN = "\u001b[96m";
    private const string BRIGHT_WHITE = "\u001b[97m";

    // Reset
    private const string RESET = "\u001b[0m";
    
    public static void Error(string msg)
    {
        MelonLogger.Error($"[FUTARI] {RED}ERROR {RESET}{msg}{RESET}");
    }
    
    public static void Warn(string msg)
    {
        MelonLogger.Warning($"[FUTARI] {YELLOW}WARN  {RESET}{msg}{RESET}");
    }

    public static void Debug(string msg)
    {
        MelonLogger.Msg($"[FUTARI] {CYAN}DEBUG {RESET}{msg}{RESET}");
    }

    public static void Info(string msg)
    {
        if (msg.StartsWith("A001")) msg = MAGENTA + msg;
        if (msg.StartsWith("A002")) msg = CYAN + msg;
        MelonLogger.Msg($"[FUTARI] {GREEN}INFO  {RESET}{msg}{RESET}");
    }
}
