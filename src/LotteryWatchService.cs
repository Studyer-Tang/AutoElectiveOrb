using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace AutoElectiveOrb
{
    internal sealed class LotteryWatchService : IDisposable
    {
        private readonly object gate = new object();
        private Process process;
        private bool requestedStop;
        private string historyPath;

        public LotteryResult LastResult { get; private set; }
        public event Action<bool, string> StateChanged;
        public event Action<LotteryResult> SnapshotChanged;
        public event Action<string, string> Notification;

        public bool IsRunning
        {
            get { lock (gate) return process != null && !process.HasExited; }
        }

        public string HistoryPath { get { return historyPath; } }

        public void Start(AppSettings settings, string password, SettingsStore store, int intervalSeconds = 60)
        {
            lock (gate)
            {
                if (process != null && !process.HasExited) return;
                var engineDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "engine");
                var script = Path.Combine(engineDirectory, "catalog.py");
                if (!File.Exists(script)) throw new FileNotFoundException("缺少抽签结果读取模块", script);
                historyPath = Path.Combine(store.DataDirectory, "lottery-history.log");
                var config = store.WriteEngineConfig(settings);
                var start = new ProcessStartInfo
                {
                    FileName = BackendProcess.ResolvePython(),
                    Arguments = "-u \"" + script + "\" --config \"" + config + "\" --watch-results --watch-interval " + Math.Max(60, intervalSeconds),
                    WorkingDirectory = engineDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };
                start.EnvironmentVariables["AUTOELECTIVE_IAAA_PASSWORD"] = password;
                start.EnvironmentVariables["AUTOELECTIVE_DATA_DIR"] = store.DataDirectory;
                start.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
                start.EnvironmentVariables["PYTHONUNBUFFERED"] = "1";
                requestedStop = false;
                process = new Process { StartInfo = start, EnableRaisingEvents = true };
                process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs args) { if (args.Data != null) OnOutput(args.Data); };
                process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs args) { if (args.Data != null) OnError(args.Data); };
                process.Exited += OnExited;
                try
                {
                    process.Start();
                    start.EnvironmentVariables["AUTOELECTIVE_IAAA_PASSWORD"] = string.Empty;
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                    RaiseState(true, "正在建立抽签结果基线…");
                }
                catch
                {
                    process.Dispose();
                    process = null;
                    RaiseState(false, "抽签哨兵启动失败。");
                    throw;
                }
            }
        }

        public void Stop()
        {
            lock (gate)
            {
                requestedStop = true;
                if (process != null && !process.HasExited)
                    try { process.Kill(); } catch { }
            }
            RaiseState(false, "抽签结果哨兵已停止。");
        }

        public void OpenHistory()
        {
            if (string.IsNullOrEmpty(historyPath))
                historyPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AutoElectiveOrb", "lottery-history.log");
            Directory.CreateDirectory(Path.GetDirectoryName(historyPath));
            if (!File.Exists(historyPath)) File.WriteAllText(historyPath, "暂时还没有抽签状态变化记录。\r\n", Encoding.UTF8);
            Process.Start(new ProcessStartInfo(historyPath) { UseShellExecute = true });
        }

        private void OnOutput(string line)
        {
            const string marker = "LOTTERY_WATCH_JSON=";
            if (!line.StartsWith(marker, StringComparison.Ordinal)) return;
            try
            {
                var update = new JavaScriptSerializer().Deserialize<LotteryWatchUpdate>(line.Substring(marker.Length));
                if (update == null) return;
                if (update.Results == null) update.Results = new System.Collections.Generic.List<CatalogCourse>();
                if (update.Changes == null) update.Changes = new System.Collections.Generic.List<CatalogCourse>();
                LastResult = update;
                RaiseState(true, "哨兵监控中 · " + update.Message);
                var snapshot = SnapshotChanged;
                if (snapshot != null) snapshot(update);
                if (update.IsBaseline || update.Changes.Count == 0) return;
                AppendHistory(update.Changes);
                var selected = update.Changes.Count(item => item.Selected == true);
                var summary = string.Join("；", update.Changes.Take(3).Select(item =>
                    item.Name + "：" + (item.PreviousOutcome ?? "未知") + " → " + (item.Outcome ?? "未知")));
                if (update.Changes.Count > 3) summary += "；另有 " + (update.Changes.Count - 3) + " 门";
                RaiseNotification(selected > 0 ? "有课程已选中" : "抽签状态已变化", summary);
            }
            catch (Exception error) { RaiseState(true, "哨兵数据解析失败：" + error.Message); }
        }

        private void OnError(string line)
        {
            const string marker = "LOTTERY_WATCH_ERROR=";
            if (line.StartsWith(marker, StringComparison.Ordinal)) RaiseState(true, line.Substring(marker.Length));
        }

        private void AppendHistory(System.Collections.Generic.IEnumerable<CatalogCourse> changes)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(historyPath));
                var lines = changes.Select(item => string.Format("[{0:yyyy-MM-dd HH:mm:ss}] {1}（{2}班，{3}）：{4} -> {5}\r\n",
                    DateTime.Now, item.Name, item.ClassNo, item.School, item.PreviousOutcome ?? "未知", item.Outcome ?? "未知"));
                File.AppendAllText(historyPath, string.Concat(lines), new UTF8Encoding(false));
            }
            catch { }
        }

        private void OnExited(object sender, EventArgs args)
        {
            var unexpected = !requestedStop;
            lock (gate)
            {
                if (process != null) { process.Dispose(); process = null; }
            }
            RaiseState(false, unexpected ? "抽签结果哨兵意外停止，请重新启动。" : "抽签结果哨兵已停止。");
            if (unexpected) RaiseNotification("抽签哨兵已停止", "监控进程异常退出，请打开抽签结果窗口后重新启动。");
        }

        private void RaiseState(bool running, string message)
        {
            var handler = StateChanged;
            if (handler != null) handler(running, message);
        }

        private void RaiseNotification(string title, string message)
        {
            var handler = Notification;
            if (handler != null) handler(title, message);
        }

        public void Dispose() { Stop(); }
    }
}
