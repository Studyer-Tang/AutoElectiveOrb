using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;

namespace AutoElectiveOrb
{
    internal static class UpdateLauncher
    {
        private const string ReleasesUrl = "https://github.com/Studyer-Tang/AutoElectiveOrb/releases/latest";

        public static void Start(IWin32Window owner)
        {
            var updater = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "UpdateAutoElectiveOrb.exe");
            try
            {
                if (File.Exists(updater))
                {
                    Process.Start(new ProcessStartInfo(updater) { UseShellExecute = true });
                    // Exit cleanly so the backend process releases the bundled
                    // runtime before the updater replaces program files.
                    Application.Exit();
                    return;
                }

                var answer = MessageBox.Show(owner,
                    "当前版本还没有内置更新器。将为你打开最新版下载页，请只下载 UpdateAutoElectiveOrb.exe，放到程序目录后双击一次。",
                    "缺少更新器", MessageBoxButtons.OKCancel, MessageBoxIcon.Information);
                if (answer == DialogResult.OK)
                    Process.Start(new ProcessStartInfo(ReleasesUrl) { UseShellExecute = true });
            }
            catch (Exception error)
            {
                MessageBox.Show(owner, "无法启动更新器：" + error.Message, "检查更新", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}
