using System;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using L4D2EightPlayer.UI;

namespace L4D2EightPlayer
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            // Headless checks for development and troubleshooting: L4D2-8P-Manager.exe --selftest --out report.txt
            if (args.Contains("--selftest")) { Environment.Exit(SelfTest.Run(args)); return; }

            using (var single = new Mutex(true, "L4D2-8P-Manager-single-instance", out bool first))
            {
                if (!first)
                {
                    MessageBox.Show("The 8-Player Manager is already running.", "L4D2 8-Player Co-op Manager", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.ThreadException += (s, e) =>
                    MessageBox.Show(e.Exception.Message, "Unexpected error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Application.Run(new MainForm());
            }
        }
    }
}
