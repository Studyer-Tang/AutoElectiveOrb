using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace AutoElectiveOrbUninstaller
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new UninstallForm());
        }
    }

    internal sealed class UninstallForm : Form
    {
        private readonly CheckBox removeCredentials;
        private readonly CheckBox removeData;
        private readonly CheckBox removeProgramFiles;
        private readonly Button uninstall;
        private readonly string installDirectory = AppDomain.CurrentDomain.BaseDirectory;
        private readonly bool isPortablePackage;

        public UninstallForm()
        {
            isPortablePackage = File.Exists(Path.Combine(installDirectory, "AutoElectiveOrb.exe"))
                && Directory.Exists(Path.Combine(installDirectory, "engine"))
                && Directory.Exists(Path.Combine(installDirectory, "runtime"))
                && !Directory.Exists(Path.Combine(installDirectory, ".git"));

            Text = "卸载 AutoElective Orb";
            ClientSize = new Size(510, 330);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(10, 16, 28);
            ForeColor = Color.FromArgb(240, 245, 255);
            Font = new Font("Microsoft YaHei UI", 9f);

            var title = new Label
            {
                Text = "卸载 AutoElective Orb",
                ForeColor = Color.White,
                Font = new Font("Microsoft YaHei UI", 17f, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(28, 24)
            };
            Controls.Add(title);

            var description = new Label
            {
                Text = "程序会先关闭悬浮球并移除开机启动。请选择需要同时清理的内容：",
                ForeColor = Color.FromArgb(164, 181, 207),
                AutoSize = true,
                Location = new Point(30, 70)
            };
            Controls.Add(description);

            removeCredentials = Option("删除 Windows 凭据管理器中保存的统一认证凭据", 106, true);
            removeData = Option("删除本地设置、缓存、日志和换课历史", 146, true);
            removeProgramFiles = Option("删除便携包中的程序文件（卸载器自身除外）", 186, isPortablePackage);
            removeProgramFiles.Enabled = isPortablePackage;
            Controls.Add(removeCredentials);
            Controls.Add(removeData);
            Controls.Add(removeProgramFiles);

            if (!isPortablePackage)
            {
                var notice = new Label
                {
                    Text = "当前不是完整便携包或位于源码仓库中，为安全起见不会删除程序文件。",
                    ForeColor = Color.FromArgb(251, 191, 36),
                    AutoSize = true,
                    Location = new Point(50, 217)
                };
                Controls.Add(notice);
            }

            var cancel = StyledButton("取消", false);
            cancel.SetBounds(278, 270, 92, 36);
            cancel.Click += delegate { Close(); };
            Controls.Add(cancel);

            uninstall = StyledButton("开始卸载", true);
            uninstall.SetBounds(382, 270, 100, 36);
            uninstall.Click += Uninstall;
            Controls.Add(uninstall);
        }

        private CheckBox Option(string text, int y, bool value)
        {
            return new CheckBox
            {
                Text = text,
                Checked = value,
                AutoSize = true,
                Location = new Point(32, y),
                ForeColor = Color.FromArgb(218, 228, 245),
                FlatStyle = FlatStyle.Flat
            };
        }

        private static Button StyledButton(string text, bool primary)
        {
            var button = new Button
            {
                Text = text,
                BackColor = primary ? Color.FromArgb(37, 99, 235) : Color.FromArgb(30, 42, 64),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            button.FlatAppearance.BorderColor = primary ? Color.FromArgb(96, 165, 250) : Color.FromArgb(65, 82, 110);
            return button;
        }

        private void Uninstall(object sender, EventArgs args)
        {
            if (MessageBox.Show(this, "确定卸载 AutoElective Orb 吗？选中的本地数据无法恢复。", "确认卸载",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;

            uninstall.Enabled = false;
            var errors = new List<string>();
            Try("关闭正在运行的程序", StopApplication, errors);
            Try("移除开机启动", RemoveStartup, errors);
            if (removeCredentials.Checked) Try("删除保存的凭据", CredentialCleaner.DeleteAll, errors);
            if (removeData.Checked) Try("删除本地数据", RemoveLocalData, errors);
            if (removeProgramFiles.Checked && isPortablePackage) Try("删除程序文件", RemoveKnownProgramFiles, errors);

            if (errors.Count == 0)
            {
                MessageBox.Show(this,
                    removeProgramFiles.Checked && isPortablePackage
                        ? "卸载完成。为避免可疑的自删除行为，卸载器自身被保留；关闭本窗口后即可删除剩余文件夹。"
                        : "卸载完成。",
                    "AutoElective Orb", MessageBoxButtons.OK, MessageBoxIcon.Information);
                Close();
                return;
            }

            uninstall.Enabled = true;
            MessageBox.Show(this, "部分项目未能清理：\n\n" + string.Join("\n", errors.ToArray()),
                "卸载未完全完成", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private static void Try(string name, Action action, List<string> errors)
        {
            try { action(); }
            catch (Exception error) { errors.Add(name + "：" + error.Message); }
        }

        private static void StopApplication()
        {
            foreach (var process in Process.GetProcessesByName("AutoElectiveOrb"))
            {
                try
                {
                    process.Kill();
                    process.WaitForExit(3000);
                }
                finally { process.Dispose(); }
            }
        }

        private static void RemoveStartup()
        {
            using (var key = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run", true))
                if (key != null) key.DeleteValue("AutoElectiveOrb", false);
        }

        private static void RemoveLocalData()
        {
            var data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AutoElectiveOrb");
            if (Directory.Exists(data)) Directory.Delete(data, true);
        }

        private void RemoveKnownProgramFiles()
        {
            foreach (var directory in new[] { "engine", "runtime", "assets" })
            {
                var path = Path.Combine(installDirectory, directory);
                if (Directory.Exists(path)) Directory.Delete(path, true);
            }
            foreach (var file in new[] { "AutoElectiveOrb.exe", "run.cmd", "README.md", "LICENSE", "THIRD_PARTY_NOTICES.md" })
            {
                var path = Path.Combine(installDirectory, file);
                if (File.Exists(path)) File.Delete(path);
            }
        }
    }

    internal static class CredentialCleaner
    {
        private const uint GenericCredential = 1;
        private const int NotFound = 1168;

        public static void DeleteAll()
        {
            uint count;
            IntPtr credentials;
            if (!CredEnumerate("AutoElectiveOrb:IAAA:*", 0, out count, out credentials))
            {
                var error = Marshal.GetLastWin32Error();
                if (error == NotFound) return;
                throw new System.ComponentModel.Win32Exception(error);
            }

            try
            {
                for (var index = 0; index < count; index++)
                {
                    var pointer = Marshal.ReadIntPtr(credentials, index * IntPtr.Size);
                    var credential = (NativeCredential)Marshal.PtrToStructure(pointer, typeof(NativeCredential));
                    if (!CredDelete(credential.TargetName, GenericCredential, 0))
                    {
                        var error = Marshal.GetLastWin32Error();
                        if (error != NotFound) throw new System.ComponentModel.Win32Exception(error);
                    }
                }
            }
            finally { CredFree(credentials); }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NativeCredential
        {
            public uint Flags;
            public uint Type;
            [MarshalAs(UnmanagedType.LPWStr)] public string TargetName;
            [MarshalAs(UnmanagedType.LPWStr)] public string Comment;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
            public uint CredentialBlobSize;
            public IntPtr CredentialBlob;
            public uint Persist;
            public uint AttributeCount;
            public IntPtr Attributes;
            [MarshalAs(UnmanagedType.LPWStr)] public string TargetAlias;
            [MarshalAs(UnmanagedType.LPWStr)] public string UserName;
        }

        [DllImport("advapi32.dll", EntryPoint = "CredEnumerateW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredEnumerate(string filter, uint flags, out uint count, out IntPtr credentials);
        [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredDelete(string target, uint type, uint flags);
        [DllImport("advapi32.dll")] private static extern void CredFree(IntPtr buffer);
    }
}
