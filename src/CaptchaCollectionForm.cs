using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace AutoElectiveOrb
{
    internal sealed class CaptchaCollectionForm : Form
    {
        private Process process;
        private readonly Timer poll = new Timer { Interval = 300 };
        private Label progress;
        private Button begin;
        private NumericUpDown target;
        private NumericUpDown interval;

        public CaptchaCollectionForm(string dataDirectory, Func<int, int, ProcessStartInfo> createProcess)
        {
            Text = "收集验证码";
            ClientSize = new Size(520, 400);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Font = new Font("Microsoft YaHei UI", 9);
            var marker = Path.Combine(dataDirectory, "collect-captcha.enabled");
            var folder = Path.Combine(dataDirectory, "captcha-collection");
            var toggle = new CheckBox {
                Text = "开启收集：同时保存正常识图流程中的验证码图片",
                AutoSize = true, Location = new Point(18, 18), Checked = File.Exists(marker)
            };
            Controls.Add(toggle);
            bool updating = false;
            toggle.CheckedChanged += delegate {
                if (updating) return;
                try {
                    Directory.CreateDirectory(dataDirectory);
                    if (toggle.Checked) File.WriteAllText(marker, "enabled");
                    else if (File.Exists(marker)) File.Delete(marker);
                } catch (Exception error) {
                    updating = true;
                    toggle.Checked = File.Exists(marker);
                    updating = false;
                    MessageBox.Show(this, error.Message, "无法更新收集开关");
                }
            };
            var help = new Label {
                Text = "运行中切换即可生效；不会额外请求网站，也不会单独启动选课。\n"
                    + "original：网站原图；uploaded：实际发送给 TT 的 JPEG 图片。\n"
                    + "按图片内容去重，不保存账号、Cookie 或识别标签。\n"
                    + "最多约 5000 组 / 500 MB，达到上限后不再保存。\n"
                    + "停止收集不会删除已保存图片；原图可能含元数据，请勿直接公开。",
                Location = new Point(18, 55), Size = new Size(488, 112)
            };
            Controls.Add(help);
            var open = new Button { Text = "打开图片文件夹", Location = new Point(18, 174), Size = new Size(160, 30) };
            open.Click += delegate {
                try {
                    Directory.CreateDirectory(folder);
                    Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
                } catch (Exception error) {
                    MessageBox.Show(this, error.Message, "无法打开目录");
                }
            };
            Controls.Add(open);
            Controls.Add(new Label { Text = "独立批量采集（需停止其他监控，不上传 TT、不选退课）", AutoSize = true, Location = new Point(18, 223) });
            Controls.Add(new Label { Text = "新增张数", AutoSize = true, Location = new Point(18, 256) });
            target = new NumericUpDown { Minimum = 1, Maximum = 1000, Value = 300, Location = new Point(90, 252), Size = new Size(80, 28) };
            Controls.Add(target);
            Controls.Add(new Label { Text = "间隔秒数", AutoSize = true, Location = new Point(190, 256) });
            interval = new NumericUpDown { Minimum = 1, Maximum = 3600, Value = 2, Location = new Point(265, 252), Size = new Size(80, 28) };
            Controls.Add(interval);
            begin = new Button { Text = "开始批量采集", Location = new Point(18, 290), Size = new Size(145, 30) };
            var stop = new Button { Text = "停止采集", Location = new Point(180, 290), Size = new Size(120, 30) };
            Controls.Add(begin);
            Controls.Add(stop);
            progress = new Label { Text = "仅单请求顺序获取；最多尝试目标张数的三倍。", Location = new Point(18, 332), Size = new Size(485, 58) };
            Controls.Add(progress);
            begin.Click += delegate {
                if (process != null) return;
                ProcessStartInfo start = null;
                try {
                    start = createProcess((int)target.Value, (int)interval.Value);
                    process = new Process { StartInfo = start };
                    process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs args) {
                        if (args.Data == null || !args.Data.StartsWith("COLLECT=", StringComparison.Ordinal)) return;
                        var message = args.Data.Substring(8);
                        try { BeginInvoke(new Action(delegate { progress.Text = message; })); } catch (InvalidOperationException) { }
                    };
                    process.ErrorDataReceived += delegate { }; // Drain, never display response bodies or credentials.
                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                    begin.Enabled = target.Enabled = interval.Enabled = false;
                    progress.Text = "正在启动批量采集…";
                    poll.Start();
                } catch (Exception error) {
                    StopBatch();
                    MessageBox.Show(this, error.Message, "无法开始采集");
                } finally {
                    if (start != null) start.EnvironmentVariables["AUTOELECTIVE_IAAA_PASSWORD"] = string.Empty;
                }
            };
            stop.Click += delegate { StopBatch(); progress.Text = "已停止；已保存图片保留。"; };
            poll.Tick += delegate {
                if (process != null && process.HasExited) {
                    process.WaitForExit();
                    if (process.ExitCode != 0) progress.Text = "采集异常结束，请检查登录、网络和磁盘。";
                    process.Dispose(); process = null;
                    poll.Stop();
                    begin.Enabled = target.Enabled = interval.Enabled = true;
                }
            };
            FormClosing += delegate { StopBatch(); };
            FormClosed += delegate { poll.Dispose(); };
        }

        private void StopBatch()
        {
            poll.Stop();
            if (process != null) {
                try { if (!process.HasExited) { process.Kill(); process.WaitForExit(3000); } } catch { }
                process.Dispose(); process = null;
            }
            if (begin != null) begin.Enabled = target.Enabled = interval.Enabled = true;
        }
    }
}
