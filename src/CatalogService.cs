using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace AutoElectiveOrb
{
    internal static class CatalogService
    {
        public static CatalogResult Load(AppSettings settings, string password, SettingsStore store, Func<bool> cancelled = null)
        {
            var json = Run(settings, password, store, string.Empty, "CATALOG_JSON=", cancelled);
            var result = new JavaScriptSerializer().Deserialize<CatalogResult>(json);
            if (result == null) throw new InvalidOperationException("课程数据格式无效。");
            if (result.Elected == null) result.Elected = new System.Collections.Generic.List<CatalogCourse>();
            if (result.Plans == null) result.Plans = new System.Collections.Generic.List<CatalogCourse>();
            return result;
        }

        public static LotteryResult LoadLotteryResults(AppSettings settings, string password, SettingsStore store, Func<bool> cancelled = null)
        {
            var json = Run(settings, password, store, " --results-only", "LOTTERY_JSON=", cancelled);
            var result = new JavaScriptSerializer().Deserialize<LotteryResult>(json);
            if (result == null) throw new InvalidOperationException("抽签结果数据格式无效。");
            if (result.Results == null) result.Results = new System.Collections.Generic.List<CatalogCourse>();
            return result;
        }

        private static string Run(AppSettings settings, string password, SettingsStore store, string extraArguments, string marker, Func<bool> cancelled)
        {
            var engineDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "engine");
            var script = Path.Combine(engineDirectory, "catalog.py");
            if (!File.Exists(script)) throw new FileNotFoundException("缺少课程读取模块", script);
            var config = store.WriteEngineConfig(settings);
            var start = new ProcessStartInfo
            {
                FileName = BackendProcess.ResolvePython(),
                Arguments = "-u \"" + script + "\" --config \"" + config + "\"" + extraArguments,
                WorkingDirectory = engineDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding = System.Text.Encoding.UTF8
            };
            start.EnvironmentVariables["AUTOELECTIVE_IAAA_PASSWORD"] = password;
            start.EnvironmentVariables["AUTOELECTIVE_DATA_DIR"] = store.DataDirectory;
            start.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";

            using (var process = Process.Start(start))
            {
                start.EnvironmentVariables["AUTOELECTIVE_IAAA_PASSWORD"] = string.Empty;
                var outputBuffer = new StringBuilder();
                var errorBuffer = new StringBuilder();
                var outputGate = new object();
                var errorGate = new object();
                process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs args)
                {
                    if (args.Data != null) lock (outputGate) outputBuffer.AppendLine(args.Data);
                };
                process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs args)
                {
                    if (args.Data != null) lock (errorGate) errorBuffer.AppendLine(args.Data);
                };
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                var deadline = DateTime.UtcNow.AddSeconds(90);
                while (!process.WaitForExit(250))
                {
                    if (cancelled != null && cancelled())
                    {
                        try { process.Kill(); } catch { }
                        throw new OperationCanceledException("课程读取已取消。");
                    }
                    if (DateTime.UtcNow >= deadline)
                    {
                        try { process.Kill(); } catch { }
                        throw new TimeoutException("读取课程超时，请检查网络后重试。");
                    }
                }
                process.WaitForExit();
                string output, error;
                lock (outputGate) output = outputBuffer.ToString();
                lock (errorGate) error = errorBuffer.ToString();
                var position = output.LastIndexOf(marker, StringComparison.Ordinal);
                if (process.ExitCode != 0 || position < 0)
                {
                    const string errorMarker = "CATALOG_ERROR=";
                    var errorPosition = error.LastIndexOf(errorMarker, StringComparison.Ordinal);
                    var message = errorPosition >= 0 ? error.Substring(errorPosition + errorMarker.Length).Trim() : "无法读取课程，请检查账号、网络和选课系统状态。";
                    throw new InvalidOperationException(message);
                }
                return output.Substring(position + marker.Length).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)[0];
            }
        }
    }
}
