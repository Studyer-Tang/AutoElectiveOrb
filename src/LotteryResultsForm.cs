using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace AutoElectiveOrb
{
    internal sealed class LotteryResultsForm : Form
    {
        private readonly AppSettings settings;
        private readonly string password;
        private readonly SettingsStore store;
        private readonly Label status;
        private readonly DataGridView results;
        private readonly Button refresh;
        private BackgroundWorker worker;

        public LotteryResultsForm(AppSettings settings, string password, SettingsStore store)
        {
            this.settings = settings;
            this.password = password;
            this.store = store;
            Text = "预选抽签结果 · 只读查询";
            ClientSize = new Size(720, 470);
            MinimumSize = new Size(650, 420);
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            BackColor = Theme.Background;
            ForeColor = Theme.Text;
            Font = new Font("Microsoft YaHei UI", 9);

            Controls.Add(new Label { Text = "预选抽签结果", AutoSize = true, ForeColor = Theme.Text, Font = new Font("Microsoft YaHei UI", 17, FontStyle.Bold), Location = new Point(24, 18) });
            Controls.Add(new Label { Text = "只读取本人账号的官方结果页，不会选课、退课或修改任何数据。", AutoSize = true, ForeColor = Theme.Secondary, Location = new Point(27, 53) });

            refresh = Theme.Button("刷新结果", true);
            refresh.SetBounds(568, 22, 126, 34);
            refresh.Click += delegate { LoadResults(); };
            Controls.Add(refresh);

            status = new Label { Text = "准备读取官方结果接口…", ForeColor = Theme.Cyan, Location = new Point(25, 86), Size = new Size(669, 45) };
            Controls.Add(status);

            results = new DataGridView
            {
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                BackgroundColor = Theme.Surface,
                BorderStyle = BorderStyle.None,
                GridColor = Theme.Border,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                DefaultCellStyle = new DataGridViewCellStyle { BackColor = Theme.SurfaceHigh, ForeColor = Theme.Text, SelectionBackColor = Color.FromArgb(42, 74, 108), SelectionForeColor = Theme.Text },
                ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { BackColor = Theme.Surface, ForeColor = Theme.Cyan, Font = new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold) },
                EnableHeadersVisualStyles = false
            };
            results.Columns.Add("Name", "课程名");
            results.Columns.Add("Class", "班号");
            results.Columns.Add("School", "开课单位");
            results.Columns.Add("Teacher", "教师");
            results.Columns[0].FillWeight = 30;
            results.Columns[1].FillWeight = 12;
            results.Columns[2].FillWeight = 35;
            results.Columns[3].FillWeight = 23;
            results.SetBounds(24, 132, 670, 260);
            Controls.Add(results);

            Controls.Add(new Label
            {
                Text = "说明：有课程表示官方接口已向你的账号返回结果；空列表不能证明落选，也可能是学校尚未发布。程序不会尝试绕过服务器权限。",
                ForeColor = Color.FromArgb(253, 186, 116),
                Location = new Point(25, 407),
                Size = new Size(669, 44)
            });

            Shown += delegate { LoadResults(); };
            FormClosing += delegate(object sender, FormClosingEventArgs args)
            {
                if (worker != null && worker.IsBusy) worker.CancelAsync();
            };
        }

        private void LoadResults()
        {
            if (worker != null) { worker.CancelAsync(); return; }
            refresh.Enabled = false;
            refresh.Text = "读取中…";
            status.Text = "正在登录并读取官方选课结果页…";
            status.ForeColor = Theme.Cyan;
            var current = new BackgroundWorker { WorkerSupportsCancellation = true };
            worker = current;
            current.DoWork += delegate(object sender, DoWorkEventArgs args)
            {
                try { args.Result = CatalogService.LoadLotteryResults(settings, password, store, delegate { return current.CancellationPending; }); }
                catch (OperationCanceledException) { args.Cancel = true; }
            };
            current.RunWorkerCompleted += delegate(object sender, RunWorkerCompletedEventArgs args)
            {
                current.Dispose();
                worker = null;
                if (IsDisposed) return;
                refresh.Enabled = true;
                refresh.Text = "刷新结果";
                if (args.Cancelled) { status.Text = "读取已取消。"; status.ForeColor = Theme.Secondary; return; }
                if (args.Error != null)
                {
                    status.Text = "读取失败：" + (args.Error.InnerException == null ? args.Error.Message : args.Error.InnerException.Message);
                    status.ForeColor = Theme.Red;
                    return;
                }
                var value = (LotteryResult)args.Result;
                results.Rows.Clear();
                foreach (var course in value.Results)
                    results.Rows.Add(course.Name, course.ClassNo, course.School, course.Teacher ?? string.Empty);
                status.Text = value.Message;
                status.ForeColor = value.Status == "available" ? Theme.Green : value.Status == "empty" ? Color.FromArgb(253, 186, 116) : Theme.Secondary;
            };
            current.RunWorkerAsync();
        }
    }
}
