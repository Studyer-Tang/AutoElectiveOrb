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
        private readonly LotteryWatchService watcher;
        private readonly Label status;
        private readonly DataGridView results;
        private readonly Button refresh;
        private readonly Button watch;
        private BackgroundWorker worker;

        public LotteryResultsForm(AppSettings settings, string password, SettingsStore store, LotteryWatchService watcher)
        {
            this.settings = settings;
            this.password = password;
            this.store = store;
            this.watcher = watcher;
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

            watch = Theme.Button("启动结果哨兵", true);
            watch.SetBounds(410, 22, 150, 34);
            watch.Click += delegate { ToggleWatcher(); };
            Controls.Add(watch);

            refresh = Theme.Button("刷新结果", false);
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
            results.Columns.Add("Outcome", "抽签状态");
            results.Columns[0].FillWeight = 27;
            results.Columns[1].FillWeight = 10;
            results.Columns[2].FillWeight = 31;
            results.Columns[3].FillWeight = 18;
            results.Columns[4].FillWeight = 14;
            results.SetBounds(24, 132, 670, 260);
            Controls.Add(results);

            var explanation = new Label
            {
                Text = "说明：只有明确标为“已选中”的课程才算抽中；“未选中”或“未知”均不算成功。空列表不能证明落选，也可能是学校尚未发布。",
                ForeColor = Color.FromArgb(253, 186, 116),
                Location = new Point(25, 407),
                Size = new Size(520, 44)
            };
            Controls.Add(explanation);
            var history = Theme.Button("查看变化记录", false);
            history.SetBounds(558, 410, 136, 30);
            history.Click += delegate { watcher.OpenHistory(); };
            Controls.Add(history);

            watcher.StateChanged += OnWatcherState;
            watcher.SnapshotChanged += OnWatcherSnapshot;
            Shown += delegate
            {
                UpdateWatchButton();
                if (watcher.LastResult != null) ApplyResult(watcher.LastResult);
                else if (!watcher.IsRunning) LoadResults();
            };
            FormClosing += delegate(object sender, FormClosingEventArgs args)
            {
                if (worker != null && worker.IsBusy) worker.CancelAsync();
            };
            FormClosed += delegate
            {
                watcher.StateChanged -= OnWatcherState;
                watcher.SnapshotChanged -= OnWatcherSnapshot;
            };
        }

        private void ToggleWatcher()
        {
            if (watcher.IsRunning) watcher.Stop();
            else
            {
                if (worker != null) { status.Text = "请等待当前读取完成后再启动哨兵。"; return; }
                try { watcher.Start(settings, password, store, 60); }
                catch (Exception error) { status.Text = "哨兵启动失败：" + error.Message; status.ForeColor = Theme.Red; }
            }
            UpdateWatchButton();
        }

        private void OnWatcherState(bool running, string message)
        {
            if (IsDisposed) return;
            if (InvokeRequired) { BeginInvoke(new Action<bool, string>(OnWatcherState), running, message); return; }
            status.Text = message;
            status.ForeColor = running ? Theme.Cyan : Theme.Secondary;
            UpdateWatchButton();
        }

        private void OnWatcherSnapshot(LotteryResult value)
        {
            if (IsDisposed) return;
            if (InvokeRequired) { BeginInvoke(new Action<LotteryResult>(OnWatcherSnapshot), value); return; }
            ApplyResult(value);
        }

        private void UpdateWatchButton()
        {
            watch.Text = watcher.IsRunning ? "停止结果哨兵" : "启动结果哨兵";
            watch.BackColor = watcher.IsRunning ? Color.FromArgb(127, 29, 29) : Color.FromArgb(36, 99, 160);
            refresh.Enabled = worker == null && !watcher.IsRunning;
        }

        private void LoadResults()
        {
            if (worker != null) { worker.CancelAsync(); return; }
            refresh.Enabled = false;
            watch.Enabled = false;
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
                watch.Enabled = true;
                refresh.Text = "刷新结果";
                if (args.Cancelled) { status.Text = "读取已取消。"; status.ForeColor = Theme.Secondary; return; }
                if (args.Error != null)
                {
                    status.Text = "读取失败：" + (args.Error.InnerException == null ? args.Error.Message : args.Error.InnerException.Message);
                    status.ForeColor = Theme.Red;
                    return;
                }
                ApplyResult((LotteryResult)args.Result);
            };
            current.RunWorkerAsync();
        }

        private void ApplyResult(LotteryResult value)
        {
            results.Rows.Clear();
            foreach (var course in value.Results)
            {
                var index = results.Rows.Add(course.Name, course.ClassNo, course.School, course.Teacher ?? string.Empty, course.Outcome ?? "未知");
                results.Rows[index].DefaultCellStyle.ForeColor = course.Selected == true ? Theme.Green
                    : course.Selected == false ? Theme.Red : Theme.Secondary;
            }
            status.Text = value.Message;
            status.ForeColor = value.Results.Exists(course => course.Selected == true) ? Theme.Green
                : value.Status == "available" || value.Status == "empty" ? Color.FromArgb(253, 186, 116) : Theme.Secondary;
            UpdateWatchButton();
        }
    }
}
