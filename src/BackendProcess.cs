using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace AutoElectiveOrb
{
    internal sealed class BackendProcess : IDisposable
    {
        private readonly object gate = new object();
        private readonly Queue<string> recentLines = new Queue<string>();
        private Process process;
        private bool requestedStop;
        private bool scheduledStart;

        public EngineState State { get; private set; }
        public DateTime? ScheduledTarget { get; private set; }
        public event Action<string> LineReceived;
        public event Action<EngineState> StateChanged;
        public event Action<string, string> Notification;

        public BackendProcess() { State = EngineState.Idle; }

        public bool IsRunning
        {
            get { lock (gate) return process != null && !process.HasExited; }
        }

        public string[] RecentLines
        {
            get { lock (gate) return recentLines.ToArray(); }
        }

        public void Start(AppSettings settings, string password, string ttPassword, SettingsStore store)
        {
            lock (gate)
            {
                if (process != null && !process.HasExited) return;
                var python = ResolvePython();
                var engineDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "engine");
                var main = Path.Combine(engineDirectory, "main.py");
                if (!File.Exists(main)) throw new FileNotFoundException("缺少本地选课引擎", main);
                var config = store.WriteEngineConfig(settings);
                requestedStop = false;
                scheduledStart = settings.ScheduledStart;
                recentLines.Clear();

                var start = new ProcessStartInfo
                {
                    FileName = python,
                    Arguments = "-u \"" + main + "\" --config \"" + config + "\"" + (settings.ScheduledStart ? " --start-at " + settings.StartAt : string.Empty),
                    WorkingDirectory = engineDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8,
                    StandardErrorEncoding = System.Text.Encoding.UTF8
                };
                start.EnvironmentVariables["AUTOELECTIVE_IAAA_PASSWORD"] = password;
                start.EnvironmentVariables["AUTOELECTIVE_TT_USERNAME"] = settings.TtUsername;
                start.EnvironmentVariables["AUTOELECTIVE_TT_PASSWORD"] = ttPassword;
                start.EnvironmentVariables["AUTOELECTIVE_DATA_DIR"] = store.DataDirectory;
                start.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
                start.EnvironmentVariables["PYTHONUNBUFFERED"] = "1";

                process = new Process { StartInfo = start, EnableRaisingEvents = true };
                process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs args) { if (args.Data != null) OnLine(args.Data, false); };
                process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs args) { if (args.Data != null) OnLine(args.Data, true); };
                process.Exited += OnExited;
                SetState(EngineState.Starting);
                try
                {
                    process.Start();
                    start.EnvironmentVariables["AUTOELECTIVE_IAAA_PASSWORD"] = string.Empty;
                    start.EnvironmentVariables["AUTOELECTIVE_TT_USERNAME"] = string.Empty;
                    start.EnvironmentVariables["AUTOELECTIVE_TT_PASSWORD"] = string.Empty;
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                }
                catch
                {
                    process.Dispose();
                    process = null;
                    SetState(EngineState.Failed);
                    throw;
                }
            }
        }

        public void Stop()
        {
            lock (gate)
            {
                if (process == null || process.HasExited) return;
                requestedStop = true;
                SetState(EngineState.Stopping);
                try { process.Kill(); }
                catch { }
            }
        }

        private void OnLine(string line, bool error)
        {
            var cleaned = line.TrimEnd();
            var lineIsError = IsErrorLine(cleaned, error);
            lock (gate)
            {
                recentLines.Enqueue((lineIsError ? "[错误] " : string.Empty) + cleaned);
                while (recentLines.Count > 400) recentLines.Dequeue();
            }
            if (cleaned.IndexOf("TT_OCR_READY", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                if (!scheduledStart) SetState(EngineState.Running);
                RaiseNotification("TT 识图配置已就绪", "验证码出现时将通过 HTTPS 发送给 TT 识图。");
            }
            else if (cleaned.IndexOf("SCHEDULE_WAITING=", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                DateTime target;
                var rawTarget = cleaned.Substring(cleaned.IndexOf('=') + 1).Trim();
                ScheduledTarget = DateTime.TryParse(rawTarget, out target) ? (DateTime?)target : null;
                SetState(EngineState.Waiting);
                RaiseNotification("预热与登录检查完成", "程序正在安全等待开放时间，到点前不会扫描或操作课程。");
            }
            else if (cleaned.IndexOf("SCHEDULE_STARTED", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                ScheduledTarget = null;
                SetState(EngineState.Running);
                RaiseNotification("倒计时结束", "已开始监控目标课程。");
            }
            else if (cleaned.IndexOf("SWAP_RECOVERY_WARNING=", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                RaiseNotification("发现未完成的换课记录", "上次换课可能在中途结束，请立即打开换课历史并核对教务系统。");
            }
            else if (cleaned.IndexOf("MANUAL_REVIEW_REQUIRED=", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                RaiseNotification("换课需要人工核对", "目标课程未确认成功；程序已停止继续操作，请立即核对学校课表和换课历史。");
            }
            else if (cleaned.IndexOf("ELECTED", StringComparison.OrdinalIgnoreCase) >= 0)
                RaiseNotification("补选成功", cleaned);
            else if (cleaned.IndexOf("worker failed", StringComparison.OrdinalIgnoreCase) >= 0)
                RaiseNotification("选课任务异常", cleaned);
            var handler = LineReceived;
            if (handler != null) handler((lineIsError ? "[错误] " : string.Empty) + Friendly(cleaned));
        }

        private static bool IsErrorLine(string line, bool fromStandardError)
        {
            if (line.IndexOf("worker failed", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (!fromStandardError || string.IsNullOrWhiteSpace(line)) return false;
            return !(line.StartsWith("[INFO]", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("[DEBUG]", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("[WARNING]", StringComparison.OrdinalIgnoreCase));
        }

        private void OnExited(object sender, EventArgs args)
        {
            ScheduledTarget = null;
            var exitCode = -1;
            try { exitCode = ((Process)sender).ExitCode; } catch { }
            SetState(requestedStop || exitCode == 0 ? EngineState.Idle : EngineState.Failed);
            if (!requestedStop && exitCode != 0) RaiseNotification("任务已停止", "引擎异常退出，请打开面板查看日志。");
            lock (gate)
            {
                if (process != null)
                {
                    process.Dispose();
                    process = null;
                }
            }
        }

        private void SetState(EngineState value)
        {
            State = value;
            var handler = StateChanged;
            if (handler != null) handler(value);
        }

        private void RaiseNotification(string title, string message)
        {
            var handler = Notification;
            if (handler != null) handler(title, message);
        }

        internal static string ResolvePython()
        {
            var candidates = new[]
            {
                Environment.GetEnvironmentVariable("AUTOELECTIVE_PYTHON") ?? string.Empty,
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "runtime", "python.exe"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".venv", "Scripts", "python.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Python", "Python312", "python.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Python", "Python311", "python.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Python", "Python310", "python.exe")
            };
            foreach (var candidate in candidates) if (File.Exists(candidate)) return candidate;
            throw new FileNotFoundException("未找到本地运行环境。请双击 install.cmd 完成一次性安装，或下载带 runtime 的便携版。");
        }

        private static string Friendly(string line)
        {
            return line.Replace("TT_OCR_READY", "TT 识图配置已就绪")
                .Replace("PRESTART_LOGIN_READY", "统一认证登录检查通过")
                .Replace("SCHEDULE_WAITING=", "安全等待至 ")
                .Replace("SCHEDULE_STARTED", "倒计时结束，开始监控")
                .Replace("SWAP_RECOVERY_WARNING=", "警告：发现状态不确定的换课记录 ")
                .Replace("MANUAL_REVIEW_REQUIRED=", "需要人工核对：")
                .Replace("No course available", "当前没有课程余量")
                .Replace("Get available courses", "正在检查课程余量")
                .Replace("Try to login IAAA", "正在登录统一认证")
                .Replace("Validation passed", "验证码校验通过")
                .Replace("Recognition result", "TT 识图结果")
                .Replace("No actionable tasks remain", "当前没有可继续执行的任务");
        }

        public void Dispose() { Stop(); }
    }
}
