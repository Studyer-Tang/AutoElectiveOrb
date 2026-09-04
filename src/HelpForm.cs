using System.Drawing;
using System.Windows.Forms;

namespace AutoElectiveOrb
{
    internal sealed class HelpForm : Form
    {
        public HelpForm()
        {
            Text = "功能与用法 · 本地选课助手";
            ClientSize = new Size(590, 610);
            MinimumSize = new Size(540, 520);
            BackColor = Theme.Background;
            ForeColor = Theme.Text;
            Font = new Font("Microsoft YaHei UI", 9);
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;

            var title = new Label
            {
                Text = "功能与用法",
                ForeColor = Theme.Text,
                Font = new Font("Microsoft YaHei UI", 17, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(24, 18)
            };
            Controls.Add(title);

            var subtitle = new Label
            {
                Text = "从配置到监控，一分钟即可上手",
                ForeColor = Theme.Secondary,
                AutoSize = true,
                Location = new Point(27, 52)
            };
            Controls.Add(subtitle);

            var content = new FlowLayoutPanel
            {
                Location = new Point(22, 84),
                Size = new Size(546, 470),
                AutoScroll = true,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                BackColor = Theme.Background,
                Padding = new Padding(0, 0, 6, 0)
            };
            content.Controls.Add(Section("01  快速开始",
                "填写学号和统一认证密码，添加目标课程，点击“开始监控”。密码只保存在 Windows 凭据管理器。"));
            content.Controls.Add(Section("02  自动扫描全部页面",
                "补选阶段会先读取选课计划中的全部课程，再逐页补充余量与教师信息。课程较多时可按课程名、教师、班号或单位搜索。"));
            content.Controls.Add(Section("03  主修与辅双身份",
                "普通本科主修保持未勾选即可。有辅修或双学位身份时勾选开关，再选择本次使用“主修”或“辅修 / 双学位”。"));
            content.Controls.Add(Section("04  课程和余量阈值",
                "课程名、班号和开课单位必须与选课系统一致。阈值为 0 表示出现任意余量就尝试；填 3 表示余量达到 3 时才尝试。"));
            content.Controls.Add(Section("05  智能换课和候选分课",
                "同名课程的多个分课可组成候选组：优先级 1 最先尝试，接着是 2、3；任一分课成功后，同名候选自动停止。不同课程名分别排序。"));
            content.Controls.Add(Section("06  开放倒计时与安全预热",
                "勾选定时启动后，程序先加载本地 OCR 并只读验证登录，再显示倒计时；到点以前不会扫描补退选页面或提交课程操作。"));
            content.Controls.Add(Section("07  永久换课历史",
                "准备、退课、目标提交、成功、失败和回滚都会写入本地记录。可从设置页或托盘打开；若发现上次状态不确定，启动时会告警。"));
            content.Controls.Add(Section("08  悬浮球与托盘",
                "单击悬浮球打开设置；拖动可贴边；中键快速开始或停止；Ctrl + Alt + E 随时唤出；关闭设置窗口后仍会驻留托盘。"));
            content.Controls.Add(Section("09  本地识图与安全",
                "验证码仅在本机使用 ddddocr，连续失败最多重试 5 次。全自动换课会先退旧课，目标可能被抢走，回滚仍无法保证成功。"));
            content.Controls.Add(Section("10  预选抽签结果",
                "点击“抽签结果”只读访问本人账号的官方结果页，逐门显示“已选中、未选中、抽签中”等原始状态；只有“已选中”才算抽中。"));
            content.Controls.Add(Section("11  抽签结果哨兵",
                "启动后程序驻留托盘并复用登录会话，每 60 秒只读检查一次；状态变化时立即通知，并将首次发现时间写入本地变化记录。"));
            Controls.Add(content);

            var close = Theme.Button("知道了", true);
            close.SetBounds(446, 565, 122, 34);
            close.Click += delegate { Close(); };
            Controls.Add(close);
        }

        private static Panel Section(string heading, string body)
        {
            var card = new Panel { BackColor = Theme.Surface, Size = new Size(520, 93), Margin = new Padding(0, 0, 0, 10) };
            var title = new Label
            {
                Text = heading,
                ForeColor = Theme.Cyan,
                Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(14, 12)
            };
            var description = new Label
            {
                Text = body,
                ForeColor = Theme.Secondary,
                Location = new Point(14, 40),
                Size = new Size(490, 42),
                AutoEllipsis = true
            };
            card.Controls.Add(title);
            card.Controls.Add(description);
            return card;
        }
    }
}
