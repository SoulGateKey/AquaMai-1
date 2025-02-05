using System;
using System.Reflection;

namespace AquaMai;

public class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            if (args.Length == 0)
            {
                return;
            }

            switch (args[0])
            {
                case "ErrorReport":
                    Console.WriteLine("Starting ErrorReport...");
                    var erAssembly = AssemblyLoader.LoadAssemblyFromResource("AquaMai.ErrorReport.dll");
                    erAssembly.GetType("AquaMai.ErrorReport.Main")
                        .GetMethod("Start", BindingFlags.Public | BindingFlags.Static)
                        .Invoke(null, []);
                    break;
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e.GetType());
            Console.WriteLine(e.Message);
            Console.WriteLine(e);
            throw;
        }
    }
}