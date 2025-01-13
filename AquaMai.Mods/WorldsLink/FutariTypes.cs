using System.Net.Sockets;

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


public class Msg
{
    public Cmd cmd { get; set; }
    public ProtocolType? proto { get; set; }
    public int? sid { get; set; }
    public uint? src { get; set; }
    public int? sPort { get; set; }
    public uint? dst { get; set; }
    public int? dPort { get; set; }
    public object? data { get; set; }
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
    
    public static void Warn(string msg)
    {
        System.Console.WriteLine(YELLOW + "WARN " + msg + RESET);
    }

    public static void Debug(string msg)
    {
        System.Console.WriteLine(BLUE + "DEBUG " + msg + RESET);
    }

    public static void WriteLine(string msg)
    {
        if (msg.StartsWith("A001")) msg = MAGENTA + msg;
        if (msg.StartsWith("A002")) msg = CYAN + msg;
        msg = msg.Replace("Error", RED + "Error");
        System.Console.WriteLine(msg + RESET);
    }
}
