using System;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;

namespace CivilEngi.AcadBridge
{
    /// <summary>
    /// Named pipe \\.\pipe\{Name}. Giao thuc: moi dong = 1 JSON {"id","cmd","args"}; tra 1 dong JSON
    /// {"id","ok","result"} hoac {"id","ok":false,"error","kind"}. Chi user dang dang nhap moi ket noi duoc.
    /// </summary>
    internal class PipeServer
    {
        public readonly string Name;
        public long Served;
        const int Instances = 4;
        volatile bool _stop;

        public PipeServer(string name) { Name = name; }

        public void Start()
        {
            for (int i = 0; i < Instances; i++)
            {
                var t = new Thread(Loop) { IsBackground = true, Name = "AcadBridgePipe" + i };
                t.Start();
            }
        }

        public void Stop()
        {
            _stop = true;
            // danh thuc cac thread dang WaitForConnection
            for (int i = 0; i < Instances; i++)
            {
                try { using (var c = new NamedPipeClientStream(".", Name, PipeDirection.InOut)) c.Connect(200); } catch { }
            }
        }

        static PipeSecurity Security()
        {
            var ps = new PipeSecurity();
            var me = WindowsIdentity.GetCurrent().User;
            ps.AddAccessRule(new PipeAccessRule(me, PipeAccessRights.FullControl, AccessControlType.Allow));
            return ps;
        }

        void Loop()
        {
            while (!_stop)
            {
                try
                {
                    using (var pipe = new NamedPipeServerStream(Name, PipeDirection.InOut, Instances,
                               PipeTransmissionMode.Byte, PipeOptions.None, 1 << 16, 1 << 16, Security()))
                    {
                        pipe.WaitForConnection();
                        if (_stop) return;
                        var enc = new UTF8Encoding(false);
                        var reader = new StreamReader(pipe, enc);
                        var writer = new StreamWriter(pipe, enc) { AutoFlush = true, NewLine = "\n" };
                        string line;
                        while (!_stop && (line = reader.ReadLine()) != null)
                        {
                            if (line.Trim().Length == 0) continue;
                            string resp = Dispatcher.Handle(line);
                            Interlocked.Increment(ref Served);
                            writer.WriteLine(resp);
                        }
                    }
                }
                catch (IOException) { /* client ngat */ }
                catch (Exception ex)
                {
                    Log.Write("Pipe loi: " + ex);
                    Thread.Sleep(500);
                }
            }
        }
    }
}
