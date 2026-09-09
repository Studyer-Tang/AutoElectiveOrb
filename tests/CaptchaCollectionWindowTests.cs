// Compile with src/CaptchaCollectionForm.cs. Uses a local sleeping process, never the network.
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using AutoElectiveOrb;

internal static class CaptchaCollectionWindowTests
{
    [STAThread]
    private static int Main()
    {
        var directory = Path.Combine(Path.GetTempPath(), "orb-window-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try {
            using (var form = new CaptchaCollectionForm(directory, delegate(int count, int seconds) {
                return new ProcessStartInfo {
                    FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell\\v1.0\\powershell.exe"),
                    Arguments = "-NoProfile -Command Start-Sleep -Seconds 30",
                    UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true
                };
            })) {
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-2000, -2000);
                form.Show();
                Application.DoEvents();
                if (form.Modal || !form.MinimizeBox || !form.ShowInTaskbar) throw new Exception("Window configuration regression");
                form.Controls.OfType<Button>().Single(b => b.Text == "开始批量采集").PerformClick();
                Application.DoEvents();
                if (!form.IsBatchRunning) throw new Exception("Child did not start");
                var child = (Process)typeof(CaptchaCollectionForm).GetField("process", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(form);
                using (var observed = Process.GetProcessById(child.Id)) {
                    form.WindowState = FormWindowState.Minimized;
                    Application.DoEvents();
                    if (observed.HasExited || !form.IsBatchRunning) throw new Exception("Minimize stopped collection");
                    form.WindowState = FormWindowState.Normal;
                    Application.DoEvents();
                    var clock = Stopwatch.StartNew();
                    form.Close();
                    Application.DoEvents();
                    if (clock.ElapsedMilliseconds > 2000) throw new Exception("Close blocked UI");
                    if (!observed.WaitForExit(3000)) throw new Exception("Close left child running");
                    if (!form.IsDisposed) throw new Exception("Close did not dispose window");
                }
            }
            Console.WriteLine("PASS: modeless, minimize/resume, close responsiveness, child cleanup");
            return 0;
        } finally { Directory.Delete(directory, false); }
    }
}
