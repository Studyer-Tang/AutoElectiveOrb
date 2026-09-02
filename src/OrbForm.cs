using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace AutoElectiveOrb
{
    internal sealed class OrbForm : Form
    {
        private readonly SettingsStore store;
        private readonly BackendProcess backend;
        private readonly SettingsForm settingsForm;
        private readonly NotifyIcon tray;
        private readonly Icon appIcon;
        private readonly Timer animation;
        private readonly Font orbFont;
        private readonly StringFormat orbTextFormat;
        private bool allowExit;
        private bool moved;
        private Point mouseDown;
        private int pulse;
        private bool hotkeyRegistered;

        private const int HotkeyId = 0xAE01;
        private const int WmHotkey = 0x0312;
        private const uint ModAlt = 0x0001;
        private const uint ModControl = 0x0002;

        [DllImport("user32.dll")] private static extern bool ReleaseCapture();
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr handle, int message, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr handle, int id, uint modifiers, Keys key);
        [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr handle, int id);
        [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr handle);

        public OrbForm()
        {
            store = new SettingsStore();
            backend = new BackendProcess();
            settingsForm = new SettingsForm(store, backend);
            Text = "AutoElective Orb";
            ClientSize = new Size(64, 64);
            FormBorderStyle = FormBorderStyle.None;
            BackColor = Theme.Background;
            TopMost = true;
            ShowInTaskbar = Program.InspectMode;
            StartPosition = FormStartPosition.Manual;
            DoubleBuffered = true;
            Opacity = 0.97;
            appIcon = CreateAppIcon();
            Icon = appIcon;
            orbFont = new Font("Microsoft YaHei UI", 17, FontStyle.Bold);
            orbTextFormat = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            ApplyCircle();
            Location = LoadLocation();

            var menu = new ContextMenuStrip();
            var open = menu.Items.Add("打开设置  Ctrl+Alt+E");
            var toggle = menu.Items.Add("开始监控");
            var data = menu.Items.Add("打开日志目录");
            var history = menu.Items.Add("查看换课历史");
            var startup = new ToolStripMenuItem("开机自动启动") { CheckOnClick = true };
            menu.Items.Add(startup);
            menu.Items.Add(new ToolStripSeparator());
            var hide = menu.Items.Add("隐藏悬浮球");
            var exit = menu.Items.Add("退出");
            open.Click += delegate { settingsForm.ShowPanel(); };
            toggle.Click += delegate { settingsForm.ShowPanel(); settingsForm.ToggleEngine(); };
            data.Click += delegate { OpenDataDirectory(); };
            history.Click += delegate { OpenSwapHistory(); };
            startup.Click += delegate { SetStartup(startup.Checked); };
            hide.Click += delegate { Hide(); };
            exit.Click += delegate { allowExit = true; Close(); };
            menu.Opening += delegate
            {
                toggle.Text = backend.IsRunning ? "停止监控" : "开始监控";
                startup.Checked = IsStartupEnabled();
            };
            ContextMenuStrip = menu;

            var trayMenu = new ContextMenuStrip();
            var showTray = trayMenu.Items.Add("显示悬浮球");
            var openTray = trayMenu.Items.Add("打开设置");
            var toggleTray = trayMenu.Items.Add("开始监控");
            var historyTray = trayMenu.Items.Add("查看换课历史");
            var exitTray = trayMenu.Items.Add("退出");
            showTray.Click += delegate { ShowOrb(); };
            openTray.Click += delegate { settingsForm.ShowPanel(); };
            toggleTray.Click += delegate { settingsForm.ShowPanel(); settingsForm.ToggleEngine(); };
            historyTray.Click += delegate { OpenSwapHistory(); };
            exitTray.Click += delegate { allowExit = true; Close(); };
            trayMenu.Opening += delegate { toggleTray.Text = backend.IsRunning ? "停止监控" : "开始监控"; };
            tray = new NotifyIcon { Icon = appIcon, Text = "本地选课助手", Visible = true, ContextMenuStrip = trayMenu };
            tray.DoubleClick += delegate { ShowOrb(); settingsForm.ShowPanel(); };

            backend.StateChanged += OnStateChanged;
            backend.Notification += OnNotification;
            animation = new Timer { Interval = 120 };
            animation.Tick += delegate { pulse = (pulse + 1) % 30; Invalidate(); };

            MouseDown += OnMouseDown;
            MouseMove += OnMouseMove;
            MouseUp += OnMouseUp;
            FormClosing += OnFormClosing;
            FormClosed += OnFormClosed;
            Shown += delegate
            {
                tray.BalloonTipTitle = "本地选课助手已启动";
                tray.BalloonTipText = "单击悬浮球设置课程，中键可快速开始或停止。";
                tray.ShowBalloonTip(2200);
                if (Program.InspectMode) settingsForm.ShowPanel();
            };
        }

        protected override void OnPaint(PaintEventArgs args)
        {
            base.OnPaint(args);
            var graphics = args.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var outer = new Rectangle(4, 4, 56, 56);
            var state = backend.State;
            var first = state == EngineState.Running ? Color.FromArgb(5, 150, 105) : state == EngineState.Waiting ? Color.FromArgb(124, 58, 237) : state == EngineState.Failed ? Color.FromArgb(225, 29, 72) : Theme.Blue;
            var second = state == EngineState.Running ? Color.FromArgb(13, 148, 136) : state == EngineState.Waiting ? Color.FromArgb(79, 70, 229) : state == EngineState.Failed ? Color.FromArgb(249, 115, 22) : Color.FromArgb(14, 165, 233);
            using (var shadow = new SolidBrush(Color.FromArgb(72, 0, 0, 0))) graphics.FillEllipse(shadow, 5, 7, 54, 54);
            using (var fill = new LinearGradientBrush(outer, first, second, 45f)) graphics.FillEllipse(fill, outer);
            using (var shine = new LinearGradientBrush(new Rectangle(10, 7, 44, 22), Color.FromArgb(95, Color.White), Color.FromArgb(0, Color.White), 90f))
                graphics.FillEllipse(shine, 10, 7, 44, 22);
            using (var border = new Pen(Color.FromArgb(105, Color.White), 1.2f)) graphics.DrawEllipse(border, outer);
            if (state == EngineState.Starting || state == EngineState.Waiting || state == EngineState.Running)
            {
                using (var progress = new Pen(Color.FromArgb(150 + pulse * 3, Color.White), 2.4f))
                    graphics.DrawArc(progress, 1, 1, 62, 62, pulse * 12, state == EngineState.Starting ? 90 : 210);
            }
            graphics.DrawString("选", orbFont, Brushes.White, new Rectangle(4, 1, 56, 52), orbTextFormat);
            var indicator = state == EngineState.Running ? Color.FromArgb(167, 243, 208) : state == EngineState.Failed ? Color.FromArgb(254, 205, 211) : Color.FromArgb(224, 231, 255);
            using (var glow = new SolidBrush(Color.FromArgb(70, indicator))) graphics.FillEllipse(glow, 25, 49, 14, 8);
            using (var dot = new SolidBrush(indicator)) graphics.FillEllipse(dot, 29, 51, 6, 6);
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == WmHotkey && message.WParam.ToInt32() == HotkeyId)
            {
                ShowOrb();
                settingsForm.ShowPanel();
                return;
            }
            base.WndProc(ref message);
            if (message.Msg == 0x0232) { SnapToEdge(); SaveLocation(); }
        }

        protected override void OnHandleCreated(EventArgs args)
        {
            base.OnHandleCreated(args);
            hotkeyRegistered = RegisterHotKey(Handle, HotkeyId, ModControl | ModAlt, Keys.E);
        }

        protected override void OnHandleDestroyed(EventArgs args)
        {
            if (hotkeyRegistered) UnregisterHotKey(Handle, HotkeyId);
            hotkeyRegistered = false;
            base.OnHandleDestroyed(args);
        }

        private void OnMouseDown(object sender, MouseEventArgs args)
        {
            if (args.Button == MouseButtons.Middle) { settingsForm.ShowPanel(); settingsForm.ToggleEngine(); return; }
            if (args.Button != MouseButtons.Left) return;
            mouseDown = args.Location;
            moved = false;
        }

        private void OnMouseMove(object sender, MouseEventArgs args)
        {
            if (args.Button != MouseButtons.Left) return;
            if (!moved && Math.Abs(args.X - mouseDown.X) + Math.Abs(args.Y - mouseDown.Y) < 5) return;
            moved = true;
            ReleaseCapture();
            SendMessage(Handle, 0xA1, new IntPtr(2), IntPtr.Zero);
        }

        private void OnMouseUp(object sender, MouseEventArgs args)
        {
            if (args.Button == MouseButtons.Left && !moved) settingsForm.ShowPanel();
            SnapToEdge();
            SaveLocation();
        }

        private void OnStateChanged(EngineState state)
        {
            if (IsDisposed) return;
            if (InvokeRequired) { BeginInvoke(new Action<EngineState>(OnStateChanged), state); return; }
            tray.Text = "本地选课助手 · " + StateText(state);
            var shouldAnimate = state == EngineState.Starting || state == EngineState.Waiting || state == EngineState.Running;
            if (shouldAnimate && !animation.Enabled) animation.Start();
            else if (!shouldAnimate && animation.Enabled) animation.Stop();
            Invalidate();
        }

        private void OnNotification(string title, string message)
        {
            if (IsDisposed) return;
            if (InvokeRequired) { BeginInvoke(new Action<string, string>(OnNotification), title, message); return; }
            tray.BalloonTipTitle = title;
            tray.BalloonTipText = message.Length > 220 ? message.Substring(0, 220) : message;
            tray.ShowBalloonTip(2600);
        }

        private void ApplyCircle()
        {
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(0, 0, Width, Height);
                Region = new Region(path);
            }
        }

        private Point LoadLocation()
        {
            var value = store.Load();
            if (value.OrbX >= 0 && value.OrbY >= 0)
            {
                var point = new Point(value.OrbX, value.OrbY);
                if (Screen.FromPoint(point).WorkingArea.Contains(point)) return point;
            }
            var work = Screen.PrimaryScreen.WorkingArea;
            return new Point(work.Right - Width - 16, work.Top + work.Height / 3);
        }

        private void SaveLocation()
        {
            try
            {
                var value = store.Load();
                value.OrbX = Left;
                value.OrbY = Top;
                store.Save(value);
            }
            catch { }
        }

        private void SnapToEdge()
        {
            var work = Screen.FromControl(this).WorkingArea;
            var x = Left + Width / 2 < work.Left + work.Width / 2 ? work.Left + 12 : work.Right - Width - 12;
            Location = new Point(x, Math.Max(work.Top + 12, Math.Min(Top, work.Bottom - Height - 12)));
        }

        private void ShowOrb() { Show(); TopMost = true; Activate(); }

        private void OpenDataDirectory()
        {
            Directory.CreateDirectory(store.DataDirectory);
            Process.Start(new ProcessStartInfo(store.DataDirectory) { UseShellExecute = true });
        }

        private void OpenSwapHistory()
        {
            Directory.CreateDirectory(store.DataDirectory);
            var path = Path.Combine(store.DataDirectory, "swap-history.log");
            if (!File.Exists(path)) File.WriteAllText(path, "暂时还没有换课记录。\r\n", System.Text.Encoding.UTF8);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }

        private static bool IsStartupEnabled()
        {
            using (var key = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run", false))
                return key != null && key.GetValue("AutoElectiveOrb") != null;
        }

        private static void SetStartup(bool enabled)
        {
            using (var key = Registry.CurrentUser.CreateSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run"))
            {
                if (enabled) key.SetValue("AutoElectiveOrb", "\"" + Application.ExecutablePath + "\"");
                else key.DeleteValue("AutoElectiveOrb", false);
            }
        }

        private void OnFormClosing(object sender, FormClosingEventArgs args)
        {
            if (allowExit) return;
            args.Cancel = true;
            Hide();
        }

        private void OnFormClosed(object sender, FormClosedEventArgs args)
        {
            SaveLocation();
            animation.Stop();
            animation.Dispose();
            orbFont.Dispose();
            orbTextFormat.Dispose();
            backend.Dispose();
            settingsForm.Dispose();
            tray.Visible = false;
            tray.Dispose();
            appIcon.Dispose();
        }

        private static Icon CreateAppIcon()
        {
            using (var bitmap = new Bitmap(32, 32))
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var fill = new LinearGradientBrush(new Rectangle(2, 2, 28, 28), Theme.Blue, Color.FromArgb(14, 165, 233), 45f))
                    graphics.FillEllipse(fill, 2, 2, 28, 28);
                using (var border = new Pen(Color.FromArgb(185, Color.White), 1.2f))
                    graphics.DrawEllipse(border, 2.5f, 2.5f, 27, 27);
                using (var check = new Pen(Color.White, 3.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                    graphics.DrawLines(check, new[] { new Point(8, 16), new Point(14, 22), new Point(25, 10) });
                var handle = bitmap.GetHicon();
                try { return (Icon)Icon.FromHandle(handle).Clone(); }
                finally { DestroyIcon(handle); }
            }
        }

        private static string StateText(EngineState state)
        {
            if (state == EngineState.Running) return "监控中";
            if (state == EngineState.Waiting) return "等待开放";
            if (state == EngineState.Starting) return "预热中";
            if (state == EngineState.Stopping) return "停止中";
            if (state == EngineState.Failed) return "异常";
            return "待机";
        }
    }
}
