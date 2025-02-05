using System.Reflection;
using System.Runtime.InteropServices;

namespace AquaMai.ErrorReport;

public class Main
{
    [DllImport("SHCore.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetProcessDpiAwareness(DPI_AWARENESS awareness);

    // 定义 DPI_AWARENESS 枚举
    public enum DPI_AWARENESS
    {
        DPI_AWARENESS_INVALID = -1,
        DPI_UNAWARE = 0,
        SYSTEM_AWARE = 1,
        PER_MONITOR_AWARE = 2
    }

    public static void Start()
    {
        AppDomain.CurrentDomain.AssemblyResolve += CurrentDomain_AssemblyResolve;
        SetProcessDpiAwareness(DPI_AWARENESS.SYSTEM_AWARE);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new CrashForm());
    }

    // 这是魔法
    private static Assembly CurrentDomain_AssemblyResolve(object sender, ResolveEventArgs args)
    {
        return Assembly.GetExecutingAssembly();
    }
}