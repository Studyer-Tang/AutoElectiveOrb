using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace AutoElectiveOrbUpdater
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new UpdateForm());
        }
    }

    internal sealed class UpdateForm : Form
    {
        private const string ApiUrl = "https://api.github.com/repos/Studyer-Tang/AutoElectiveOrb/releases/latest";
        private const string PackageName = "AutoElectiveOrb-windows-x64.zip";
        private readonly Label status;
        private readonly ProgressBar progress;
        private readonly Button action;
        private bool busy;
        private bool updateComplete;

        public UpdateForm()
        {
            Text = "AutoElective Orb 更新器";
            ClientSize = new Size(520, 250);
            MinimumSize = MaximumSize = Size;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(12, 18, 30);
            ForeColor = Color.FromArgb(230, 237, 248);
            Font = new Font("Microsoft YaHei UI", 9);

            var title = new Label { Text = "一键检查并更新", AutoSize = true, Font = new Font(Font.FontFamily, 17, FontStyle.Bold), Location = new Point(28, 24) };
            var hint = new Label { Text = "原地升级程序，不会删除账号设置、日志或换课记录。", AutoSize = true, ForeColor = Color.FromArgb(151, 166, 190), Location = new Point(31, 64) };
            status = new Label { Text = "准备检查 GitHub 最新版本…", Size = new Size(458, 48), Location = new Point(31, 103), ForeColor = Color.FromArgb(125, 211, 252) };
            progress = new ProgressBar { Location = new Point(31, 153), Size = new Size(458, 12), Style = ProgressBarStyle.Marquee, MarqueeAnimationSpeed = 28 };
            action = new Button { Text = "检查并更新", Location = new Point(349, 190), Size = new Size(140, 36), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(56, 189, 248), ForeColor = Color.FromArgb(5, 15, 28) };
            action.FlatAppearance.BorderSize = 0;
            action.Click += async delegate
            {
                if (updateComplete)
                {
                    Process.Start(new ProcessStartInfo(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AutoElectiveOrb.exe")) { UseShellExecute = true });
                    Close();
                    return;
                }
                await CheckAndUpdate();
            };
            Controls.AddRange(new Control[] { title, hint, status, progress, action });
            Shown += async delegate { await CheckAndUpdate(); };
        }

        private async Task CheckAndUpdate()
        {
            if (busy) return;
            busy = true;
            action.Enabled = false;
            progress.Style = ProgressBarStyle.Marquee;
            try
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                var install = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
                if (Directory.Exists(Path.Combine(install, ".git")))
                    throw new InvalidOperationException("检测到这是源码仓库。为避免覆盖开发文件，请使用 git pull 更新源码。完整发布包才能使用一键更新。");

                SetStatus("正在查询最新版本…");
                ReleaseInfo release;
                using (var client = CreateClient())
                {
                    var json = await client.DownloadStringTaskAsync(ApiUrl);
                    release = ParseRelease(json);
                }

                var local = ReadLocalVersion(install);
                if (CompareVersions(local, release.Version) >= 0)
                {
                    SetStatus("当前已是最新版 v" + local + "。", false);
                    action.Text = "重新检查";
                    busy = false;
                    action.Enabled = true;
                    return;
                }

                var answer = MessageBox.Show(this,
                    "发现新版本 v" + release.Version + "（当前 v" + local + "）。\n\n现在下载并原地更新吗？",
                    "发现新版本", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (answer != DialogResult.Yes)
                {
                    SetStatus("已取消更新。", false);
                    busy = false;
                    action.Enabled = true;
                    return;
                }

                var temp = Path.Combine(Path.GetTempPath(), "AutoElectiveOrb-update-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(temp);
                try
                {
                    var zip = Path.Combine(temp, PackageName);
                    var checksum = zip + ".sha256";
                    SetStatus("正在下载新版程序…");
                    using (var client = CreateClient())
                    {
                        await client.DownloadFileTaskAsync(release.PackageUrl, zip);
                        await client.DownloadFileTaskAsync(release.ChecksumUrl, checksum);
                    }
                    VerifyHash(zip, checksum);

                    var staging = Path.Combine(temp, "staging");
                    Directory.CreateDirectory(staging);
                    SetStatus("校验通过，正在准备更新…");
                    ExtractSafely(zip, staging);
                    ValidatePackage(staging);
                    StopMainProgram(install);
                    InstallWithRollback(install, staging, Path.Combine(temp, "backup"));
                }
                finally
                {
                    try { Directory.Delete(temp, true); } catch { }
                }

                SetStatus("更新完成：v" + release.Version + "。", false);
                action.Text = "启动程序";
                action.Enabled = true;
                updateComplete = true;
                MessageBox.Show(this, "更新成功，所有本地设置和记录均已保留。", "更新完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
                busy = false;
                return;
            }
            catch (Exception error)
            {
                SetStatus("更新未完成：" + error.Message, false);
                MessageBox.Show(this, error.Message, "无法更新", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            busy = false;
            action.Enabled = true;
            action.Text = "重试";
        }

        private void SetStatus(string text, bool animate = true)
        {
            status.Text = text;
            progress.Style = animate ? ProgressBarStyle.Marquee : ProgressBarStyle.Blocks;
            if (!animate) progress.Value = text.StartsWith("更新完成") || text.Contains("最新版") ? 100 : 0;
        }

        private static WebClient CreateClient()
        {
            var client = new WebClient { Encoding = Encoding.UTF8 };
            client.Headers[HttpRequestHeader.UserAgent] = "AutoElectiveOrb-Updater";
            client.Headers[HttpRequestHeader.Accept] = "application/vnd.github+json";
            return client;
        }

        private static ReleaseInfo ParseRelease(string json)
        {
            var root = new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>;
            if (root == null || !root.ContainsKey("tag_name") || !root.ContainsKey("assets")) throw new InvalidDataException("GitHub 返回的版本信息无效。");
            var version = Convert.ToString(root["tag_name"]).Trim().TrimStart('v', 'V');
            string package = null, checksum = null;
            foreach (var item in (IEnumerable)root["assets"])
            {
                var asset = item as Dictionary<string, object>;
                if (asset == null) continue;
                var name = Convert.ToString(asset["name"]);
                var url = Convert.ToString(asset["browser_download_url"]);
                if (name == PackageName) package = url;
                if (name == PackageName + ".sha256") checksum = url;
            }
            if (package == null || checksum == null) throw new InvalidDataException("最新版缺少程序包或 SHA-256 校验文件，请稍后再试。");
            return new ReleaseInfo { Version = version, PackageUrl = package, ChecksumUrl = checksum };
        }

        private static string ReadLocalVersion(string install)
        {
            var path = Path.Combine(install, "VERSION");
            return File.Exists(path) ? File.ReadAllText(path).Trim().TrimStart('v', 'V') : "0.0.0";
        }

        private static int CompareVersions(string left, string right)
        {
            Version a, b;
            if (!Version.TryParse(left.Split('-')[0], out a)) a = new Version(0, 0);
            if (!Version.TryParse(right.Split('-')[0], out b)) b = new Version(0, 0);
            return a.CompareTo(b);
        }

        private static void VerifyHash(string zip, string checksum)
        {
            var expected = File.ReadAllText(checksum).Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)[0];
            string actual;
            using (var stream = File.OpenRead(zip))
            using (var sha = SHA256.Create())
                actual = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
            if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("下载文件的 SHA-256 校验失败，已停止更新。");
        }

        private static void ExtractSafely(string zip, string staging)
        {
            var root = Path.GetFullPath(staging + Path.DirectorySeparatorChar);
            using (var archive = ZipFile.OpenRead(zip))
            {
                foreach (var entry in archive.Entries)
                {
                    var target = Path.GetFullPath(Path.Combine(staging, entry.FullName));
                    if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("更新包包含不安全路径，已停止更新。");
                    if (string.IsNullOrEmpty(entry.Name)) { Directory.CreateDirectory(target); continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    entry.ExtractToFile(target, true);
                }
            }
        }

        private static void ValidatePackage(string staging)
        {
            foreach (var file in new[] { "AutoElectiveOrb.exe", "UninstallAutoElectiveOrb.exe", "VERSION" })
                if (!File.Exists(Path.Combine(staging, file))) throw new InvalidDataException("更新包不完整，缺少 " + file + "。");
            foreach (var directory in new[] { "engine", "runtime", "assets" })
                if (!Directory.Exists(Path.Combine(staging, directory))) throw new InvalidDataException("更新包不完整，缺少 " + directory + " 目录。");
        }

        private static void StopMainProgram(string install)
        {
            var expected = Path.GetFullPath(Path.Combine(install, "AutoElectiveOrb.exe"));
            foreach (var process in Process.GetProcessesByName("AutoElectiveOrb"))
            {
                try
                {
                    string runningPath;
                    try { runningPath = Path.GetFullPath(process.MainModule.FileName); }
                    catch { continue; }
                    if (!string.Equals(runningPath, expected, StringComparison.OrdinalIgnoreCase)) continue;
                    process.CloseMainWindow();
                    if (!process.WaitForExit(3000)) { process.Kill(); process.WaitForExit(3000); }
                }
                catch (Exception error) { throw new IOException("无法关闭正在运行的主程序，请手动退出后重试。", error); }
                finally { process.Dispose(); }
            }
        }

        private static readonly string[] ManagedItems = {
            "AutoElectiveOrb.exe", "UninstallAutoElectiveOrb.exe", "README.md", "LICENSE", "THIRD_PARTY_NOTICES.md",
            "run.cmd", "VERSION", "UNSIGNED_BUILD.txt", "engine", "runtime", "assets"
        };

        private static void InstallWithRollback(string install, string staging, string backup)
        {
            Directory.CreateDirectory(backup);
            try
            {
                foreach (var name in ManagedItems)
                {
                    var current = Path.Combine(install, name);
                    if (File.Exists(current)) { Directory.CreateDirectory(backup); File.Copy(current, Path.Combine(backup, name), true); }
                    else if (Directory.Exists(current)) CopyDirectory(current, Path.Combine(backup, name));
                }
                foreach (var name in ManagedItems) DeleteItem(Path.Combine(install, name));
                foreach (var name in ManagedItems)
                {
                    var source = Path.Combine(staging, name);
                    if (File.Exists(source)) File.Copy(source, Path.Combine(install, name), true);
                    else if (Directory.Exists(source)) CopyDirectory(source, Path.Combine(install, name));
                }
            }
            catch
            {
                foreach (var name in ManagedItems) DeleteItem(Path.Combine(install, name));
                foreach (var name in ManagedItems)
                {
                    var source = Path.Combine(backup, name);
                    if (File.Exists(source)) File.Copy(source, Path.Combine(install, name), true);
                    else if (Directory.Exists(source)) CopyDirectory(source, Path.Combine(install, name));
                }
                throw;
            }
        }

        private static void CopyDirectory(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (var file in Directory.GetFiles(source)) File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
            foreach (var directory in Directory.GetDirectories(source)) CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
        }

        private static void DeleteItem(string path)
        {
            if (File.Exists(path)) File.Delete(path);
            else if (Directory.Exists(path)) Directory.Delete(path, true);
        }

        private sealed class ReleaseInfo
        {
            public string Version;
            public string PackageUrl;
            public string ChecksumUrl;
        }
    }
}
