using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace AutoElectiveOrb
{
    internal sealed class CoursePickerForm : Form
    {
        private readonly AppSettings settings;
        private readonly string password;
        private readonly SettingsStore store;
        private readonly DataGridView electedGrid;
        private readonly DataGridView planGrid;
        private readonly TextBox search;
        private readonly Label status;
        private readonly Button load;
        private readonly Button create;
        private readonly HashSet<string> selectedKeys = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> selectedPriorities = new Dictionary<string, int>(StringComparer.Ordinal);
        private List<CatalogCourse> allPlans = new List<CatalogCourse>();
        private bool canExecuteSwap;
        private BackgroundWorker catalogWorker;

        public List<CourseSetting> CreatedRules { get; private set; }

        public CoursePickerForm(AppSettings settings, string password, SettingsStore store)
        {
            this.settings = settings;
            this.password = password;
            this.store = store;
            CreatedRules = new List<CourseSetting>();

            Text = "读取课程并创建换课组";
            ClientSize = new Size(1080, 650);
            MinimumSize = new Size(960, 580);
            BackColor = Theme.Background;
            ForeColor = Theme.Text;
            Font = new Font("Microsoft YaHei UI", 9);
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;

            var title = new Label { Text = "智能换课选择器", ForeColor = Theme.Text, Font = new Font("Microsoft YaHei UI", 17, FontStyle.Bold), AutoSize = true, Location = new Point(24, 18) };
            Controls.Add(title);
            var subtitle = new Label { Text = "选择一门准备退掉的课程，再勾选一个或多个可接受的目标分课", ForeColor = Theme.Secondary, AutoSize = true, Location = new Point(27, 52) };
            Controls.Add(subtitle);

            load = Theme.Button("读取我的课程", true);
            load.SetBounds(906, 24, 148, 34);
            load.Click += delegate { LoadCatalog(); };
            Controls.Add(load);

            status = new Label { Text = "尚未读取，仅在点击上方按钮后登录选课系统。", ForeColor = Theme.Secondary, AutoSize = true, Location = new Point(25, 82) };
            Controls.Add(status);

            var left = Card("① 准备退掉的已选课程（单选）", 22, 112, 374, 450);
            Controls.Add(left);
            electedGrid = Grid(false);
            electedGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            electedGrid.MultiSelect = false;
            electedGrid.Columns.Add("Name", "课程名");
            electedGrid.Columns.Add("Class", "班号");
            electedGrid.Columns.Add("School", "开课单位");
            electedGrid.Columns[0].FillWeight = 43;
            electedGrid.Columns[1].FillWeight = 17;
            electedGrid.Columns[2].FillWeight = 40;
            electedGrid.SetBounds(14, 42, 346, 392);
            left.Controls.Add(electedGrid);

            var right = Card("② 可接受的目标分课（可多选，1 为最高优先级）", 410, 112, 648, 450);
            Controls.Add(right);
            search = new TextBox { BackColor = Theme.SurfaceHigh, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle };
            search.SetBounds(14, 39, 620, 28);
            search.TextChanged += delegate { PreserveChecks(); ApplyFilter(); };
            right.Controls.Add(search);
            var placeholder = new Label { Text = "搜索课程名、教师、班号或开课单位", ForeColor = Theme.Secondary, AutoSize = true, BackColor = Color.Transparent, Location = new Point(21, 45) };
            right.Controls.Add(placeholder);
            placeholder.Click += delegate { search.Focus(); };
            search.GotFocus += delegate { placeholder.Visible = false; };
            search.LostFocus += delegate { placeholder.Visible = search.TextLength == 0; };

            planGrid = Grid(true);
            planGrid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Pick", HeaderText = "选", FillWeight = 8 });
            planGrid.Columns.Add("Priority", "优先级");
            planGrid.Columns.Add("Name", "课程名");
            planGrid.Columns.Add("Teacher", "教师");
            planGrid.Columns.Add("Class", "班号");
            planGrid.Columns.Add("School", "开课单位");
            planGrid.Columns.Add("Quota", "余量");
            planGrid.Columns[1].FillWeight = 13;
            planGrid.Columns[2].FillWeight = 28;
            planGrid.Columns[3].FillWeight = 18;
            planGrid.Columns[4].FillWeight = 10;
            planGrid.Columns[5].FillWeight = 25;
            planGrid.Columns[6].FillWeight = 9;
            for (var column = 2; column <= 6; column++) planGrid.Columns[column].ReadOnly = true;
            planGrid.SetBounds(14, 76, 620, 358);
            planGrid.CurrentCellDirtyStateChanged += delegate { if (planGrid.IsCurrentCellDirty) planGrid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
            planGrid.CellValueChanged += delegate(object sender, DataGridViewCellEventArgs args)
            {
                if (args.RowIndex < 0 || args.ColumnIndex != 0) return;
                var course = planGrid.Rows[args.RowIndex].Tag as CatalogCourse;
                if (course == null) return;
                if (Convert.ToBoolean(planGrid.Rows[args.RowIndex].Cells[0].Value))
                {
                    selectedKeys.Add(course.Key);
                    if (!selectedPriorities.ContainsKey(course.Key)) selectedPriorities[course.Key] = selectedPriorities.Values.DefaultIfEmpty(0).Max() + 1;
                    planGrid.Rows[args.RowIndex].Cells[1].Value = selectedPriorities[course.Key];
                }
                else
                {
                    selectedKeys.Remove(course.Key);
                    selectedPriorities.Remove(course.Key);
                    planGrid.Rows[args.RowIndex].Cells[1].Value = string.Empty;
                }
            };
            right.Controls.Add(planGrid);

            var warning = new Label
            {
                Text = "高风险：系统会先退旧课再选目标课。即使目标刚有余量，也可能在中途被抢走，回滚原课程无法保证成功。",
                ForeColor = Color.FromArgb(253, 186, 116),
                Location = new Point(24, 577),
                Size = new Size(760, 44)
            };
            Controls.Add(warning);

            var cancel = Theme.Button("取消", false);
            cancel.SetBounds(820, 588, 104, 36);
            cancel.Click += delegate
            {
                if (catalogWorker != null) { catalogWorker.CancelAsync(); status.Text = "正在取消课程读取…"; return; }
                DialogResult = DialogResult.Cancel;
                Close();
            };
            Controls.Add(cancel);
            create = Theme.Button("创建全自动换课组", true);
            create.SetBounds(936, 588, 122, 36);
            create.Enabled = false;
            create.Click += delegate { CreateRules(); };
            Controls.Add(create);
        }

        private void LoadCatalog()
        {
            if (catalogWorker != null)
            {
                catalogWorker.CancelAsync();
                load.Enabled = false;
                status.Text = "正在取消课程读取…";
                return;
            }
            load.Enabled = true;
            load.Text = "取消读取";
            create.Enabled = false;
            status.Text = "正在登录并扫描全部课程页面…";
            status.ForeColor = Theme.Blue;
            var worker = new BackgroundWorker();
            catalogWorker = worker;
            worker.WorkerSupportsCancellation = true;
            worker.DoWork += delegate(object sender, DoWorkEventArgs args)
            {
                try { args.Result = CatalogService.Load(settings, password, store, delegate { return worker.CancellationPending; }); }
                catch (OperationCanceledException) { args.Cancel = true; }
            };
            worker.RunWorkerCompleted += delegate(object sender, RunWorkerCompletedEventArgs args)
            {
                worker.Dispose();
                catalogWorker = null;
                load.Enabled = true;
                load.Text = "读取我的课程";
                if (args.Cancelled)
                {
                    status.Text = "课程读取已取消。";
                    status.ForeColor = Theme.Secondary;
                    return;
                }
                if (args.Error != null)
                {
                    status.Text = "读取失败";
                    status.ForeColor = Theme.Red;
                    MessageBox.Show(this, args.Error.InnerException == null ? args.Error.Message : args.Error.InnerException.Message, "无法读取课程", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                var result = (CatalogResult)args.Result;
                allPlans = result.Plans;
                canExecuteSwap = result.CanExecuteSwap;
                selectedKeys.Clear();
                selectedPriorities.Clear();
                electedGrid.Rows.Clear();
                foreach (var course in result.Elected)
                {
                    var row = electedGrid.Rows[electedGrid.Rows.Add(course.Name, course.ClassNo, course.School)];
                    row.Tag = course;
                }
                ApplyFilter();
                create.Enabled = result.Elected.Count > 0 && result.Plans.Count > 0;
                status.Text = string.Format("读取完成：{0}；已选 {1} 门，课程 {2} 个分课。{3}",
                    string.IsNullOrWhiteSpace(result.Phase) ? "当前阶段" : result.Phase,
                    result.Elected.Count,
                    result.Plans.Count,
                    result.CanExecuteSwap ? string.Empty : "可提前配置，暂不能执行退补选。");
                status.ForeColor = Theme.Green;
            };
            worker.RunWorkerAsync();
        }

        private void PreserveChecks()
        {
            foreach (DataGridViewRow row in planGrid.Rows)
            {
                var course = row.Tag as CatalogCourse;
                if (course == null) continue;
                if (Convert.ToBoolean(row.Cells[0].Value))
                {
                    selectedKeys.Add(course.Key);
                    int priority;
                    if (!int.TryParse(Convert.ToString(row.Cells[1].Value), out priority) || priority < 1)
                        priority = selectedPriorities.ContainsKey(course.Key) ? selectedPriorities[course.Key] : selectedPriorities.Values.DefaultIfEmpty(0).Max() + 1;
                    selectedPriorities[course.Key] = priority;
                }
                else
                {
                    selectedKeys.Remove(course.Key);
                    selectedPriorities.Remove(course.Key);
                }
            }
        }

        private void ApplyFilter()
        {
            var query = search.Text.Trim();
            planGrid.Rows.Clear();
            foreach (var course in allPlans.Where(item => Matches(item, query)))
            {
                int priority;
                var priorityText = selectedPriorities.TryGetValue(course.Key, out priority) ? priority.ToString() : string.Empty;
                var index = planGrid.Rows.Add(selectedKeys.Contains(course.Key), priorityText, course.Name, course.Teacher, course.ClassNo, course.School, course.QuotaKnown ? course.RemainingQuota.ToString() : "—");
                planGrid.Rows[index].Tag = course;
            }
        }

        private void CreateRules()
        {
            PreserveChecks();
            var drop = electedGrid.CurrentRow == null ? null : electedGrid.CurrentRow.Tag as CatalogCourse;
            var targets = allPlans.Where(course => selectedKeys.Contains(course.Key))
                .OrderBy(course => selectedPriorities.ContainsKey(course.Key) ? selectedPriorities[course.Key] : int.MaxValue)
                .ThenBy(course => course.ClassNo).ToList();
            if (drop == null) { MessageBox.Show(this, "请先在左侧选择一门准备退掉的课程。", "缺少退选课程", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            if (targets.Count == 0) { MessageBox.Show(this, "请在右侧至少勾选一个可接受的目标分课。", "缺少目标课程", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            if (targets.Any(course => course.Key == drop.Key)) { MessageBox.Show(this, "目标分课不能与准备退掉的课程相同。", "换课规则无效", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

            var message = canExecuteSwap
                ? string.Format("将创建全自动换课：\n\n退掉：{0}（{1} 班）\n候选：{2} 个分课\n\n发现任一候选有余量时，程序会自动退掉旧课并立即尝试新课。此操作存在旧课无法恢复的风险。确定继续吗？", drop.Name, drop.ClassNo, targets.Count)
                : string.Format("将提前保存换课规则：\n\n准备退掉：{0}（{1} 班）\n候选：{2} 个分课\n\n当前不在补退选阶段，现在不会执行退选或选课。开放后启动功能时仍会再次要求风险确认。确定保存吗？", drop.Name, drop.ClassNo, targets.Count);
            if (MessageBox.Show(this, message, canExecuteSwap ? "确认高风险全自动换课" : "保存早期换课计划", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;

            var group = Guid.NewGuid().ToString("N");
            CreatedRules = targets.Select((target, index) => new CourseSetting
            {
                Name = target.Name,
                ClassNo = target.ClassNo,
                School = target.School,
                Threshold = 0,
                Priority = index + 1,
                SwapGroup = group,
                DropName = drop.Name,
                DropClassNo = drop.ClassNo,
                DropSchool = drop.School
            }).ToList();
            DialogResult = DialogResult.OK;
            Close();
        }

        private static bool Matches(CatalogCourse course, string query)
        {
            if (query.Length == 0) return true;
            return (course.Name ?? string.Empty).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
                || (course.School ?? string.Empty).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
                || (course.Teacher ?? string.Empty).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
                || course.ClassNo.ToString().Contains(query);
        }

        private static DataGridView Grid(bool editable)
        {
            return new DataGridView
            {
                BackgroundColor = Theme.Surface,
                BorderStyle = BorderStyle.None,
                GridColor = Theme.Border,
                ForeColor = Theme.Text,
                DefaultCellStyle = new DataGridViewCellStyle { BackColor = Theme.SurfaceHigh, ForeColor = Theme.Text, SelectionBackColor = Color.FromArgb(42, 74, 108), SelectionForeColor = Theme.Text },
                ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { BackColor = Theme.Surface, ForeColor = Theme.Cyan, Font = new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold) },
                EnableHeadersVisualStyles = false,
                RowHeadersVisible = false,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                ReadOnly = !editable,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
            };
        }

        private static Panel Card(string title, int x, int y, int width, int height)
        {
            var panel = new Panel { BackColor = Theme.Surface, Location = new Point(x, y), Size = new Size(width, height) };
            var heading = new Label { Text = title, ForeColor = Theme.Cyan, Font = new Font("Microsoft YaHei UI", 9, FontStyle.Bold), AutoSize = true, Location = new Point(14, 11) };
            panel.Controls.Add(heading);
            return panel;
        }
    }
}
