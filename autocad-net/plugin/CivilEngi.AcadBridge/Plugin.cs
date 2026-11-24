using System;
using System.IO;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Runtime;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

[assembly: ExtensionApplication(typeof(CivilEngi.AcadBridge.Plugin))]
[assembly: CommandClass(typeof(CivilEngi.AcadBridge.Plugin))]

namespace CivilEngi.AcadBridge
{
    /// <summary>
    /// Cau noi AutoCAD &lt;-&gt; Claude: mo named pipe, nhan lenh JSON (1 dong / yeu cau),
    /// chay tren main thread cua AutoCAD va tra JSON. Thay cho COM (cham, "Call was rejected").
    /// </summary>
    public class Plugin : IExtensionApplication
    {
        public const string Version = "0.2.0";
        public static readonly string BaseName =
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CIVIL_ACAD_PIPE")) ? "CivilEngiAcad" : Environment.GetEnvironmentVariable("CIVIL_ACAD_PIPE");
        internal static PipeServer Server;

        public void Initialize()
        {
            try
            {
                MainThread.Init();
                // Moi phien AutoCAD 1 pipe rieng: CivilEngiAcad-<pid>. Client chon phien theo ten ban ve.
                string name = BaseName + "-" + System.Diagnostics.Process.GetCurrentProcess().Id;
                Server = new PipeServer(name);
                Server.Start();
                Log.Write("Khoi dong, pipe=" + name + ", AutoCAD " + Application.Version);
                WriteLine("\nCivilEngi AcadBridge " + Version + " san sang (pipe \\\\.\\pipe\\" + name + "). Lenh: CEBRIDGE");
            }
            catch (System.Exception ex)
            {
                Log.Write("Initialize loi: " + ex);
                WriteLine("\nCivilEngi AcadBridge loi khoi dong: " + ex.Message);
            }
        }

        public void Terminate()
        {
            try { Server?.Stop(); } catch { }
        }

        internal static void WriteLine(string s)
        {
            try { Application.DocumentManager.MdiActiveDocument?.Editor.WriteMessage(s); } catch { }
        }

        [CommandMethod("CEBRIDGE", CommandFlags.Session)]
        public void StatusCommand()
        {
            var s = Server;
            WriteLine(s == null
                ? "\nAcadBridge chua chay."
                : $"\nAcadBridge {Version} | pipe \\\\.\\pipe\\{s.Name} | da xu ly {s.Served} yeu cau | log: {Log.FilePath}");
        }

        [CommandMethod("CEBRIDGE_RESTART", CommandFlags.Session)]
        public void RestartCommand()
        {
            try { Server?.Stop(); } catch { }
            Server = new PipeServer(Server?.Name ?? (BaseName + "-" + System.Diagnostics.Process.GetCurrentProcess().Id));
            Server.Start();
            WriteLine("\nAcadBridge da khoi dong lai.");
        }
    }

    internal static class Log
    {
        public static readonly string FilePath = Path.Combine(Path.GetTempPath(), "CivilEngiAcadBridge.log");
        static readonly object Lk = new object();
        static readonly int Pid = System.Diagnostics.Process.GetCurrentProcess().Id;
        public static void Write(string s)
        {
            try
            {
                lock (Lk)
                {
                    var fi = new FileInfo(FilePath);
                    if (fi.Exists && fi.Length > 5_000_000) fi.Delete();
                    File.AppendAllText(FilePath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff ") + "[" + Pid + "] " + s + Environment.NewLine);
                }
            }
            catch { }
        }
    }
}
