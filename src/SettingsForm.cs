using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace AutoElectiveOrb
{
    internal sealed class SettingsForm : Form
    {
        private readonly SettingsStore store;
        private readonly BackendProcess backend;
        private readonly TextBox studentId;
        private readonly TextBox password;
        private readonly NumericUpDown interval;
        private readonly CheckBox dualDegree;
        private readonly ComboBox identity;
        private readonly CheckBox scheduledStart;
        private readonly DateTimePicker startAt;
        private readonly DataGridView courses;
        private readonly TextBox logs;
        private readonly Label status;
        private readonly Button startStop;
        private readonly Timer countdownTimer;

        public SettingsForm(SettingsStore store, BackendProcess backend)
        {
            this.store = store;
            this.backend = backend;
            Text = "AutoElective Orb · 本地选课助手";
            ClientSize = new Size(700, 720);
            MinimumSize = new Size(650, 680);
            BackColor = Theme.Background;
            ForeColor = Theme.Text;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = true;
            Font = new Font("Microsoft YaHei UI", 9);

            var title = new Label { Text = "本地选课助手", ForeColor = Theme.Text, Font = new Font("Microsoft YaHei UI", 17, FontStyle.Bold), AutoSize = true };
            title.Location = new Point(24, 18);
            Controls.Add(title);
            var subtitle = new Label { Text = "小球常驻 · 本地识图 · 不使用第三方验证码服务", ForeColor = Theme.Secondary, AutoSize = true };
            subtitle.Location = new Point(27, 52);
            Controls.Add(subtitle);

            status = new Label { Text = "● 已停止", ForeColor = Theme.Secondary, TextAlign = ContentAlignment.MiddleRight };
            status.SetBounds(470, 22, 200, 28);
            Controls.Add(status);
            var help = Theme.Button("?  功能与用法", false);
            help.SetBounds(548, 52, 122, 28);
            help.Click += delegate { using (var window = new HelpForm()) window.ShowDialog(this); };
            Controls.Add(help);

            var account = Card("账号、刷新与定时预热", 22, 86, 656, 170);
            Controls.Add(account);
            AddLabel(account, "学号", 14, 39, 58);
            studentId = Input(account, 72, 36, 170);
            AddLabel(account, "密码", 260, 39, 48);
            password = Input(account, 312, 36, 170);
            password.UseSystemPasswordChar = true;
            AddLabel(account, "扫描范围", 497, 39, 66);
            var scanScope = new Label { Text = "全部页面 · 自动", ForeColor = Theme.Cyan, Font = new Font("Microsoft YaHei UI", 9, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft };
            scanScope.SetBounds(565, 36, 80, 29);
            account.Controls.Add(scanScope);

            AddLabel(account, "刷新间隔", 14, 78, 72);
            interval = new NumericUpDown { Minimum = 4, Maximum = 120, DecimalPlaces = 1, Increment = 0.5M, BackColor = Theme.SurfaceHigh, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle };
            interval.SetBounds(90, 75, 86, 29);
            account.Controls.Add(interval);
            var seconds = new Label { Text = "秒（最低 4 秒）", ForeColor = Theme.Secondary, AutoSize = true };
            seconds.Location = new Point(182, 81);
            account.Controls.Add(seconds);
            dualDegree = new CheckBox { Text = "有辅修/双学位身份", ForeColor = Theme.Text, AutoSize = true, BackColor = Color.Transparent };
            dualDegree.Location = new Point(310, 79);
            account.Controls.Add(dualDegree);
            identity = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Theme.SurfaceHigh, ForeColor = Theme.Text };
            identity.Items.AddRange(new object[] { "主修", "辅修 / 双学位" });
            identity.SetBounds(468, 75, 167, 28);
            account.Controls.Add(identity);
            dualDegree.CheckedChanged += delegate
            {
                identity.Enabled = dualDegree.Checked;
                if (!dualDegree.Checked) identity.SelectedIndex = 0;
            };
            scheduledStart = new CheckBox { Text = "定时启动", ForeColor = Theme.Text, AutoSize = true, BackColor = Color.Transparent, Location = new Point(14, 113) };
            account.Controls.Add(scheduledStart);
            startAt = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "HH:mm:ss", ShowUpDown = true, BackColor = Theme.SurfaceHigh, ForeColor = Theme.Text };
            startAt.SetBounds(95, 109, 105, 28);
            account.Controls.Add(startAt);
            var scheduleHint = new Label { Text = "先验证账号和本地 OCR，再倒计时；到点前绝不扫描或操作课程。", ForeColor = Theme.Cyan, AutoSize = true, Location = new Point(214, 115) };
            account.Controls.Add(scheduleHint);
            scheduledStart.CheckedChanged += delegate { startAt.Enabled = scheduledStart.Checked; };
            var accountHint = new Label { Text = "密码只保存在 Windows 凭据管理器，不写入配置文件。", ForeColor = Theme.Secondary, AutoSize = true };
            accountHint.Location = new Point(14, 143);
            account.Controls.Add(accountHint);

            var courseCard = Card("目标课程", 22, 268, 656, 218);
            Controls.Add(courseCard);
            courses = new DataGridView
            {
                BackgroundColor = Theme.Surface,
                BorderStyle = BorderStyle.None,
                GridColor = Theme.Border,
                ForeColor = Theme.Text,
                DefaultCellStyle = new DataGridViewCellStyle { BackColor = Theme.SurfaceHigh, ForeColor = Theme.Text, SelectionBackColor = Color.FromArgb(42, 74, 108), SelectionForeColor = Theme.Text },
                ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { BackColor = Theme.Surface, ForeColor = Theme.Cyan, Font = new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold) },
                EnableHeadersVisualStyles = false,
                RowHeadersVisible = false,
                AllowUserToResizeRows = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
            };
            courses.Columns.Add("CourseName", "课程名");
            courses.Columns.Add("ClassNo", "班号");
            courses.Columns.Add("School", "开课单位");
            courses.Columns.Add("Threshold", "余量阈值");
            courses.Columns.Add("Priority", "优先级");
            courses.Columns.Add("Mode", "操作");
            courses.Columns.Add("SwapGroup", "SwapGroup");
            courses.Columns.Add("DropName", "DropName");
            courses.Columns.Add("DropClass", "DropClass");
            courses.Columns.Add("DropSchool", "DropSchool");
            courses.Columns[0].FillWeight = 28;
            courses.Columns[1].FillWeight = 11;
            courses.Columns[2].FillWeight = 28;
            courses.Columns[3].FillWeight = 14;
            courses.Columns[4].FillWeight = 12;
            courses.Columns[5].FillWeight = 19;
            courses.Columns[5].ReadOnly = true;
            for (var hidden = 6; hidden <= 9; hidden++) courses.Columns[hidden].Visible = false;
            courses.SetBounds(14, 38, 628, 124);
            courseCard.Controls.Add(courses);
            var smartSwap = Theme.Button("读取课程 / 智能换课", true);
            smartSwap.SetBounds(350, 180, 160, 28);
            smartSwap.Click += delegate { OpenCoursePicker(); };
            courseCard.Controls.Add(smartSwap);
            var deleteCourse = Theme.Button("删除选中", false);
            deleteCourse.SetBounds(520, 180, 122, 28);
            deleteCourse.Click += delegate
            {
                foreach (DataGridViewRow row in courses.SelectedRows) if (!row.IsNewRow) courses.Rows.Remove(row);
                NormalizeSameNameCandidateGroups();
            };
            courseCard.Controls.Add(deleteCourse);
            var courseHint = new Label
            {
                Text = "同名分课按优先级 1 → 2 → 3 依次尝试；成功一门后，同组其他分课自动停止。",
                ForeColor = Theme.Cyan,
                Size = new Size(325, 40)
            };
            courseHint.Location = new Point(14, 164);
            courseCard.Controls.Add(courseHint);

            var logCard = Card("运行记录", 22, 498, 656, 136);
            Controls.Add(logCard);
            logs = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BackColor = Color.FromArgb(6, 10, 18), ForeColor = Theme.Secondary, BorderStyle = BorderStyle.None, Font = new Font("Consolas", 8.5f) };
            logs.SetBounds(14, 38, 628, 84);
            logCard.Controls.Add(logs);
            var history = Theme.Button("查看换课历史", false);
            history.SetBounds(510, 7, 132, 27);
            history.Click += delegate { OpenSwapHistory(); };
            logCard.Controls.Add(history);

            var warning = new Label { Text = "提示：自动请求可能触发学校限流；本工具保留最低 4 秒刷新限制。", ForeColor = Color.FromArgb(253, 186, 116), AutoSize = true };
            warning.Location = new Point(24, 646);
            Controls.Add(warning);

            var save = Theme.Button("保存设置", false);
            save.SetBounds(430, 675, 112, 34);
            save.Click += delegate { SaveFromUi(true); };
            Controls.Add(save);
            startStop = Theme.Button("开始监控", true);
            startStop.SetBounds(552, 675, 126, 34);
            startStop.Click += delegate { ToggleEngine(); };
            Controls.Add(startStop);

            studentId.Leave += delegate { if (password.TextLength == 0) password.Text = CredentialStore.Read(studentId.Text); };
            FormClosing += delegate(object sender, FormClosingEventArgs args) { if (args.CloseReason == CloseReason.UserClosing) { args.Cancel = true; Hide(); } };
            backend.LineReceived += OnLine;
            backend.StateChanged += OnStateChanged;
            LoadToUi(store.Load());
            foreach (var line in backend.RecentLines) AppendLine(line);
            UpdateState(backend.State);
            countdownTimer = new Timer { Interval = 1000 };
            countdownTimer.Tick += delegate { if (backend.State == EngineState.Waiting) UpdateCountdown(); };
            countdownTimer.Start();
            FormClosed += delegate { countdownTimer.Stop(); countdownTimer.Dispose(); };
        }

        public void ShowPanel()
        {
            if (!Visible) Show();
            WindowState = FormWindowState.Normal;
            Activate();
        }

        public void ToggleEngine()
        {
            if (backend.IsRunning) { backend.Stop(); return; }
            try
            {
                var settings = SaveFromUi(true);
                var secret = password.Text;
                if (string.IsNullOrEmpty(secret)) secret = CredentialStore.Read(settings.StudentId);
                if (string.IsNullOrEmpty(secret)) throw new InvalidOperationException("请填写统一认证密码。");
                var swapCount = settings.Courses.Count(course => course.IsSwap);
                if (swapCount > 0)
                {
                    var warning = string.Format("当前有 {0} 个全自动换课候选。发现余量后，程序会先退掉旧课再尝试新课，旧课可能无法恢复。\n\n确定开始吗？", swapCount);
                    if (MessageBox.Show(this, warning, "确认启动全自动换课", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
                }
                backend.Start(settings, secret, store);
            }
            catch (Exception error)
            {
                MessageBox.Show(this, error.Message, "无法开始监控", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private AppSettings SaveFromUi(bool saveCredential)
        {
            var id = studentId.Text.Trim();
            var currentSettings = store.Load();
            if (!Regex.IsMatch(id, "^[A-Za-z0-9_-]{1,64}$")) throw new InvalidOperationException("学号只能包含字母、数字、下划线和连字符。");
            NormalizeSameNameCandidateGroups();
            var settings = new AppSettings
            {
                StudentId = id,
                DualDegree = dualDegree.Checked,
                Identity = dualDegree.Checked && identity.SelectedIndex == 1 ? "bfx" : "bzx",
                RefreshInterval = (double)interval.Value,
                ScheduledStart = scheduledStart.Checked,
                StartAt = startAt.Value.ToString("HH:mm:ss"),
                OrbX = currentSettings.OrbX,
                OrbY = currentSettings.OrbY
            };
            foreach (DataGridViewRow row in courses.Rows)
            {
                if (row.IsNewRow) continue;
                var name = Cell(row, 0);
                var classText = Cell(row, 1);
                var school = Cell(row, 2);
                var thresholdText = Cell(row, 3);
                var priorityText = Cell(row, 4);
                if (name.Length == 0 && school.Length == 0) continue;
                int classNo, threshold, priority;
                if (name.Length == 0 || school.Length == 0 || !int.TryParse(classText, out classNo) || classNo < 1)
                    throw new InvalidOperationException("每门课程都要填写课程名、正整数班号和开课单位。");
                if (!int.TryParse(thresholdText.Length == 0 ? "0" : thresholdText, out threshold) || threshold < 0)
                    throw new InvalidOperationException("余量阈值必须是 0 或正整数。");
                if (!int.TryParse(priorityText.Length == 0 ? "100" : priorityText, out priority) || priority < 1)
                    throw new InvalidOperationException("优先级必须是正整数，数字越小越优先。");
                var dropName = Cell(row, 7);
                var dropClassText = Cell(row, 8);
                var dropSchool = Cell(row, 9);
                var dropClassNo = 0;
                if (dropName.Length > 0 && (dropSchool.Length == 0 || !int.TryParse(dropClassText, out dropClassNo) || dropClassNo < 1))
                    throw new InvalidOperationException("换课规则中的退选课程信息不完整，请删除后重新创建。");
                settings.Courses.Add(new CourseSetting
                {
                    Name = name,
                    ClassNo = classNo,
                    School = school,
                    Threshold = threshold,
                    Priority = priority,
                    SwapGroup = Cell(row, 6),
                    DropName = dropName,
                    DropClassNo = dropClassNo,
                    DropSchool = dropSchool
                });
            }
            settings.Courses = settings.Courses.OrderBy(course => course.Priority).ToList();
            if (settings.Courses.Count == 0) throw new InvalidOperationException("至少添加一门目标课程。");
            store.Save(settings);
            if (saveCredential && password.TextLength > 0) CredentialStore.Save(id, password.Text);
            return settings;
        }

        private void LoadToUi(AppSettings settings)
        {
            studentId.Text = settings.StudentId;
            interval.Value = (decimal)Math.Max(4, Math.Min(120, settings.RefreshInterval));
            scheduledStart.Checked = settings.ScheduledStart;
            DateTime parsedStart;
            startAt.Value = DateTime.TryParse(settings.StartAt, out parsedStart) ? DateTime.Today.Add(parsedStart.TimeOfDay) : DateTime.Today.AddHours(8);
            startAt.Enabled = scheduledStart.Checked;
            dualDegree.Checked = settings.DualDegree;
            identity.SelectedIndex = settings.DualDegree && settings.Identity == "bfx" ? 1 : 0;
            identity.Enabled = settings.DualDegree;
            password.Text = CredentialStore.Read(settings.StudentId);
            courses.Rows.Clear();
            foreach (var course in settings.Courses) AddCourseRow(course);
        }

        private void OpenCoursePicker()
        {
            if (backend.IsRunning)
            {
                MessageBox.Show(this, "请先停止当前监控，再读取课程并修改换课规则。", "正在监控", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var id = studentId.Text.Trim();
            if (!Regex.IsMatch(id, "^[A-Za-z0-9_-]{1,64}$"))
            {
                MessageBox.Show(this, "请先填写正确的学号。", "无法读取课程", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var secret = password.TextLength > 0 ? password.Text : CredentialStore.Read(id);
            if (string.IsNullOrEmpty(secret))
            {
                MessageBox.Show(this, "请先填写统一认证密码。", "无法读取课程", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var current = store.Load();
            var snapshot = new AppSettings
            {
                StudentId = id,
                DualDegree = dualDegree.Checked,
                Identity = dualDegree.Checked && identity.SelectedIndex == 1 ? "bfx" : "bzx",
                RefreshInterval = (double)interval.Value,
                OrbX = current.OrbX,
                OrbY = current.OrbY
            };
            using (var picker = new CoursePickerForm(snapshot, secret, store))
            {
                if (picker.ShowDialog(this) != DialogResult.OK) return;
                foreach (var rule in picker.CreatedRules)
                {
                    var duplicate = courses.Rows.Cast<DataGridViewRow>().Any(row => !row.IsNewRow
                        && Cell(row, 0) == rule.Name
                        && Cell(row, 1) == rule.ClassNo.ToString()
                        && Cell(row, 2) == rule.School);
                    if (!duplicate) AddCourseRow(rule);
                }
                NormalizeSameNameCandidateGroups();
            }
        }

        private void NormalizeSameNameCandidateGroups()
        {
            var normalRows = courses.Rows.Cast<DataGridViewRow>()
                .Where(row => !row.IsNewRow && Cell(row, 7).Length == 0 && Cell(row, 0).Length > 0)
                .ToList();
            foreach (var group in normalRows.GroupBy(row => Cell(row, 0), StringComparer.OrdinalIgnoreCase))
            {
                if (group.Count() == 1)
                {
                    var row = group.First();
                    row.Cells[6].Value = string.Empty;
                    row.Cells[5].Value = "普通";
                    continue;
                }
                var groupId = group.Select(row => Cell(row, 6)).FirstOrDefault(value => value.Length > 0) ?? Guid.NewGuid().ToString("N");
                var ordered = group.OrderBy(row => ParsedPriority(row)).ThenBy(row => Cell(row, 1)).ToList();
                for (var index = 0; index < ordered.Count; index++)
                {
                    ordered[index].Cells[4].Value = index + 1;
                    ordered[index].Cells[6].Value = groupId;
                    ordered[index].Cells[5].Value = "同名候选组";
                }
            }
        }

        private static int ParsedPriority(DataGridViewRow row)
        {
            int value;
            return int.TryParse(Cell(row, 4), out value) && value > 0 ? value : int.MaxValue;
        }

        private void AddCourseRow(CourseSetting course)
        {
            courses.Rows.Add(
                course.Name,
                course.ClassNo,
                course.School,
                course.Threshold,
                course.Priority > 0 ? course.Priority : 100,
                course.IsSwap ? "换：" + course.DropName : string.IsNullOrWhiteSpace(course.SwapGroup) ? "普通" : "同名候选组",
                course.SwapGroup,
                course.DropName,
                course.DropClassNo > 0 ? course.DropClassNo.ToString() : string.Empty,
                course.DropSchool);
        }

        private void OpenSwapHistory()
        {
            Directory.CreateDirectory(store.DataDirectory);
            var path = Path.Combine(store.DataDirectory, "swap-history.log");
            if (!File.Exists(path)) File.WriteAllText(path, "暂时还没有换课记录。\r\n", System.Text.Encoding.UTF8);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }

        private void OnLine(string line)
        {
            if (IsDisposed) return;
            if (InvokeRequired) { BeginInvoke(new Action<string>(OnLine), line); return; }
            AppendLine(line);
        }

        private void AppendLine(string line)
        {
            logs.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + line + Environment.NewLine);
            if (logs.Lines.Length > 220) logs.Lines = logs.Lines.Skip(logs.Lines.Length - 180).ToArray();
            logs.SelectionStart = logs.TextLength;
            logs.ScrollToCaret();
        }

        private void OnStateChanged(EngineState state)
        {
            if (IsDisposed) return;
            if (InvokeRequired) { BeginInvoke(new Action<EngineState>(OnStateChanged), state); return; }
            UpdateState(state);
        }

        private void UpdateState(EngineState state)
        {
            if (state == EngineState.Starting) { status.Text = "● 正在预热本地识图"; status.ForeColor = Theme.Blue; startStop.Text = "停止"; }
            else if (state == EngineState.Waiting) { UpdateCountdown(); status.ForeColor = Theme.Blue; startStop.Text = "取消倒计时"; }
            else if (state == EngineState.Running) { status.Text = "● 正在监控"; status.ForeColor = Theme.Green; startStop.Text = "停止监控"; }
            else if (state == EngineState.Stopping) { status.Text = "● 正在停止"; status.ForeColor = Theme.Secondary; startStop.Text = "停止中"; }
            else if (state == EngineState.Failed) { status.Text = "● 运行异常"; status.ForeColor = Theme.Red; startStop.Text = "重新开始"; }
            else { status.Text = "● 已停止"; status.ForeColor = Theme.Secondary; startStop.Text = "开始监控"; }
        }

        private void UpdateCountdown()
        {
            if (!backend.ScheduledTarget.HasValue) { status.Text = "● 预热完成，等待开放"; return; }
            var remaining = backend.ScheduledTarget.Value - DateTime.Now;
            if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;
            status.Text = string.Format("● 倒计时 {0:00}:{1:00}:{2:00}", (int)remaining.TotalHours, remaining.Minutes, remaining.Seconds);
        }

        private static Panel Card(string title, int x, int y, int width, int height)
        {
            var panel = new Panel { BackColor = Theme.Surface, Location = new Point(x, y), Size = new Size(width, height) };
            var heading = new Label { Text = title, ForeColor = Theme.Cyan, Font = new Font("Microsoft YaHei UI", 9, FontStyle.Bold), AutoSize = true };
            heading.Location = new Point(14, 10);
            panel.Controls.Add(heading);
            return panel;
        }

        private static TextBox Input(Control parent, int x, int y, int width)
        {
            var box = new TextBox { BackColor = Theme.SurfaceHigh, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle };
            box.SetBounds(x, y, width, 29);
            parent.Controls.Add(box);
            return box;
        }

        private static void AddLabel(Control parent, string text, int x, int y, int width)
        {
            var label = new Label { Text = text, ForeColor = Theme.Secondary, TextAlign = ContentAlignment.MiddleLeft };
            label.SetBounds(x, y, width, 26);
            parent.Controls.Add(label);
        }

        private static string Cell(DataGridViewRow row, int index)
        {
            return row.Cells[index].Value == null ? string.Empty : row.Cells[index].Value.ToString().Trim();
        }
    }
}
