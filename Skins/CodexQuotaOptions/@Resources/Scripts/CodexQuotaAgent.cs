using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace CodexQuota
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                Dictionary<string, string> options = ParseArguments(args);
                DynamicQuotaAgent agent = new DynamicQuotaAgent(
                    Required(options, "state"),
                    Required(options, "stop"),
                    Required(options, "sync"),
                    Required(options, "quiet"),
                    Value(options, "sessions", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "sessions")),
                    Integer(options, "interval", 20),
                    Integer(options, "timeout", 12000),
                    Integer(options, "rainmeter-pid", 0),
                    Value(options, "codex", null));
                agent.Run();
                return 0;
            }
            catch
            {
                return 1;
            }
        }

        private static Dictionary<string, string> ParseArguments(string[] args)
        {
            Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < args.Length; index++)
            {
                string current = args[index];
                if (!current.StartsWith("--", StringComparison.Ordinal) || index + 1 >= args.Length)
                {
                    continue;
                }
                result[current.Substring(2)] = args[++index];
            }
            return result;
        }

        private static string Required(Dictionary<string, string> options, string key)
        {
            string value;
            if (!options.TryGetValue(key, out value) || String.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Missing required option --" + key);
            }
            return Path.GetFullPath(value);
        }

        private static string Value(Dictionary<string, string> options, string key, string fallback)
        {
            string value;
            return options.TryGetValue(key, out value) && !String.IsNullOrWhiteSpace(value) ? value : fallback;
        }

        private static int Integer(Dictionary<string, string> options, string key, int fallback)
        {
            string value;
            int parsed;
            return options.TryGetValue(key, out value) && Int32.TryParse(value, out parsed) ? parsed : fallback;
        }
    }

    internal sealed class DynamicQuotaAgent
    {
        private static readonly DateTime UnixEpochUtc = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        private static readonly string[] StateKeys =
        {
            "Schema", "AgentPid", "AgentHeartbeat", "Mode", "StatusText", "StatusDetail",
            "ActiveCount", "NextSyncAt", "LastSyncAt", "LastUpdated", "Remaining", "Used",
            "Window", "Reset", "ResetEpoch", "Plan", "Credits", "LimitId", "Aux",
            "QuotaState", "LimitError", "LastError", "ConsecutiveErrors", "ManualSilent",
            "LastSyncRequestToken", "LastQuietRequestToken"
        };

        private readonly string statePath;
        private readonly string stopTokenPath;
        private readonly string syncTokenPath;
        private readonly string quietTokenPath;
        private readonly string sessionsRoot;
        private readonly int intervalSeconds;
        private readonly int timeoutMs;
        private readonly int rainmeterPid;
        private readonly string codexPath;
        private readonly Dictionary<string, string> state = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, long> fileOffsets = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> activeTurns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private AppServerClient appServer;
        private bool pendingImmediateSync;
        private bool pendingFinalSync;
        private bool manualSilent;
        private bool baselineScan;
        private readonly long bootTimeUnix;

        public DynamicQuotaAgent(
            string statePath,
            string stopTokenPath,
            string syncTokenPath,
            string quietTokenPath,
            string sessionsRoot,
            int intervalSeconds,
            int timeoutMs,
            int rainmeterPid,
            string codexPath)
        {
            this.statePath = statePath;
            this.stopTokenPath = stopTokenPath;
            this.syncTokenPath = syncTokenPath;
            this.quietTokenPath = quietTokenPath;
            this.sessionsRoot = Path.GetFullPath(sessionsRoot);
            this.intervalSeconds = Math.Max(10, Math.Min(300, intervalSeconds));
            this.timeoutMs = Math.Max(3000, Math.Min(60000, timeoutMs));
            this.rainmeterPid = rainmeterPid;
            this.codexPath = codexPath;
            bootTimeUnix = UnixNow() - (long)(NativeMethods.GetTickCount64() / 1000UL);
            InitializeState();
        }

        public void Run()
        {
            bool ownsMutex = false;
            using (Mutex mutex = new Mutex(false, "Local\\CodexQuotaOptions.DynamicAgent.Native.v1"))
            {
                try
                {
                    try
                    {
                        ownsMutex = mutex.WaitOne(TimeSpan.FromSeconds(8));
                    }
                    catch (AbandonedMutexException)
                    {
                        ownsMutex = true;
                    }
                    if (!ownsMutex)
                    {
                        return;
                    }

                    string initialStopToken = ReadToken(stopTokenPath);
                    string lastSyncToken = Get("LastSyncRequestToken", "--");
                    string lastQuietToken = Get("LastQuietRequestToken", "--");
                    Set("Mode", "STARTING");
                    Set("StatusText", "STARTING");
                    Set("StatusDetail", "SCANNING TASKS");
                    WriteState(false);

                    baselineScan = true;
                    ScanRecentSessions(true);
                    baselineScan = false;
                    SetModeFromActivity();
                    if (activeTurns.Count > 0)
                    {
                        pendingImmediateSync = true;
                    }
                    WriteState(false);

                    Directory.CreateDirectory(sessionsRoot);
                    using (FileSystemWatcher watcher = CreateWatcher())
                    {
                        DateTime nextHeartbeat = DateTime.UtcNow;
                        DateTime nextCatchUp = DateTime.UtcNow.AddSeconds(5);
                        WatcherChangeTypes changeTypes = WatcherChangeTypes.Changed | WatcherChangeTypes.Created | WatcherChangeTypes.Renamed;

                        while (true)
                        {
                            if (rainmeterPid > 0 && !ProcessExists(rainmeterPid))
                            {
                                break;
                            }
                            if (!String.Equals(ReadToken(stopTokenPath), initialStopToken, StringComparison.Ordinal))
                            {
                                break;
                            }

                            WaitForChangedResult change = watcher.WaitForChanged(changeTypes, 250);
                            if (!change.TimedOut && !String.IsNullOrWhiteSpace(change.Name))
                            {
                                ReadSessionDelta(Path.Combine(sessionsRoot, change.Name));
                            }

                            DateTime nowUtc = DateTime.UtcNow;
                            if (nowUtc >= nextCatchUp)
                            {
                                ScanRecentSessions(false);
                                nextCatchUp = nowUtc.AddSeconds(5);
                            }

                            string currentSyncToken = ReadToken(syncTokenPath);
                            bool manualSync = !String.Equals(currentSyncToken, lastSyncToken, StringComparison.Ordinal);
                            if (manualSync)
                            {
                                lastSyncToken = currentSyncToken;
                                Set("LastSyncRequestToken", currentSyncToken);
                            }

                            string currentQuietToken = ReadToken(quietTokenPath);
                            if (!String.Equals(currentQuietToken, lastQuietToken, StringComparison.Ordinal))
                            {
                                lastQuietToken = currentQuietToken;
                                Set("LastQuietRequestToken", currentQuietToken);
                                ApplyQuietRequest(currentQuietToken);
                            }

                            if (manualSilent && activeTurns.Count == 0)
                            {
                                manualSilent = false;
                                Set("ManualSilent", "0");
                                SetModeFromActivity();
                            }

                            if (pendingFinalSync && activeTurns.Count == 0)
                            {
                                pendingFinalSync = false;
                                pendingImmediateSync = false;
                                SyncQuota("FINAL SNAPSHOT");
                            }
                            else if (pendingImmediateSync && !manualSilent)
                            {
                                pendingImmediateSync = false;
                                SyncQuota("TASK START");
                            }
                            else if (pendingFinalSync && !manualSilent)
                            {
                                pendingFinalSync = false;
                                SyncQuota("FINAL SNAPSHOT");
                            }
                            else if (manualSync)
                            {
                                SyncQuota("MANUAL REQUEST");
                            }
                            else if (!manualSilent && activeTurns.Count > 0 && Long("NextSyncAt", 0) > 0 && UnixNow() >= Long("NextSyncAt", 0))
                            {
                                SyncQuota("20S ACTIVE CYCLE");
                            }

                            if (manualSilent)
                            {
                                pendingImmediateSync = false;
                                if (activeTurns.Count > 0)
                                {
                                    pendingFinalSync = false;
                                }
                            }

                            if (nowUtc >= nextHeartbeat)
                            {
                                string mode = Get("Mode", "STARTING");
                                if (mode != "SYNCING" && mode != "ERROR")
                                {
                                    SetModeFromActivity();
                                }
                                WriteState(false);
                                nextHeartbeat = nowUtc.AddSeconds(10);
                            }
                        }
                    }
                }
                finally
                {
                    StopAppServer();
                    if (ownsMutex)
                    {
                        try
                        {
                            Set("AgentPid", "0");
                            Set("AgentHeartbeat", "0");
                            Set("ActiveCount", "0");
                            Set("Mode", "SILENT");
                            Set("StatusText", "SILENT");
                            Set("StatusDetail", "MONITOR STOPPED");
                            Set("NextSyncAt", "0");
                            WriteState(true);
                        }
                        catch
                        {
                            // Shutdown is best effort.
                        }
                        try
                        {
                            mutex.ReleaseMutex();
                        }
                        catch
                        {
                            // Ignore shutdown races.
                        }
                    }
                }
            }
        }

        private FileSystemWatcher CreateWatcher()
        {
            FileSystemWatcher watcher = new FileSystemWatcher(sessionsRoot, "*.jsonl");
            watcher.IncludeSubdirectories = true;
            watcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size;
            watcher.EnableRaisingEvents = true;
            return watcher;
        }

        private void InitializeState()
        {
            Dictionary<string, string> existing = ReadKeyValueFile(statePath);
            string currentQuietToken = ReadToken(quietTokenPath);
            string previousQuietToken;
            string previousManualSilent;
            bool quietRequested;
            bool hasQuietRequest = TryParseQuietRequest(currentQuietToken, out quietRequested);
            bool quietRequestPending = !existing.TryGetValue("LastQuietRequestToken", out previousQuietToken) ||
                                       !String.Equals(currentQuietToken, previousQuietToken, StringComparison.Ordinal);
            bool wasManualSilent = existing.TryGetValue("ManualSilent", out previousManualSilent) &&
                                   String.Equals(previousManualSilent, "1", StringComparison.Ordinal);
            manualSilent = hasQuietRequest && quietRequested && (quietRequestPending || wasManualSilent);

            Set("Schema", "4");
            Set("AgentPid", Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture));
            Set("AgentHeartbeat", UnixNow().ToString(CultureInfo.InvariantCulture));
            Set("Mode", "STARTING");
            Set("StatusText", "STARTING");
            Set("StatusDetail", "SCANNING TASKS");
            Set("ActiveCount", "0");
            Set("NextSyncAt", "0");
            Set("LastSyncAt", "0");
            Set("LastUpdated", "--:--:--");
            Set("Remaining", "--");
            Set("Used", "--");
            Set("Window", "--");
            Set("Reset", "--");
            Set("ResetEpoch", "0");
            Set("Plan", "--");
            Set("Credits", "--");
            Set("LimitId", "CODEX");
            Set("Aux", "PRIMARY LIMIT");
            Set("QuotaState", "UNKNOWN");
            Set("LimitError", "--");
            Set("LastError", "--");
            Set("ConsecutiveErrors", "0");
            Set("ManualSilent", manualSilent ? "1" : "0");
            Set("LastSyncRequestToken", "--");
            Set("LastQuietRequestToken", currentQuietToken);

            string[] cachedKeys =
            {
                "LastSyncAt", "LastUpdated", "Remaining", "Used", "Window", "Reset", "ResetEpoch",
                "Plan", "Credits", "LimitId", "Aux", "QuotaState", "LimitError", "LastSyncRequestToken"
            };
            foreach (string key in cachedKeys)
            {
                string value;
                if (existing.TryGetValue(key, out value))
                {
                    Set(key, value);
                }
            }
        }

        private void SetModeFromActivity()
        {
            if (activeTurns.Count > 0)
            {
                if (manualSilent)
                {
                    Set("Mode", "PAUSED");
                    Set("StatusText", "QUIET");
                    Set("StatusDetail", String.Format(CultureInfo.InvariantCulture, "{0} LIVE TURN{1} / MUTED", activeTurns.Count, activeTurns.Count == 1 ? "" : "S"));
                    Set("NextSyncAt", "0");
                    Set("ManualSilent", "1");
                }
                else
                {
                    Set("Mode", "ACTIVE");
                    Set("StatusText", "TASK ACTIVE");
                    Set("StatusDetail", String.Format(CultureInfo.InvariantCulture, "{0} LIVE TURN{1}", activeTurns.Count, activeTurns.Count == 1 ? "" : "S"));
                    Set("ManualSilent", "0");
                }
            }
            else
            {
                manualSilent = false;
                Set("Mode", "SILENT");
                Set("StatusText", "SILENT");
                Set("StatusDetail", "NO ACTIVE TASK");
                Set("NextSyncAt", "0");
                Set("ManualSilent", "0");
            }
        }

        private void ApplyQuietRequest(string token)
        {
            bool requested;
            if (!TryParseQuietRequest(token, out requested))
            {
                return;
            }

            if (requested && activeTurns.Count > 0)
            {
                manualSilent = true;
                pendingImmediateSync = false;
                pendingFinalSync = false;
                StopAppServer();
            }
            else if (!requested && manualSilent)
            {
                manualSilent = false;
                if (activeTurns.Count > 0)
                {
                    pendingImmediateSync = true;
                }
            }

            SetModeFromActivity();
            WriteState(false);
        }

        private void SyncQuota(string reason)
        {
            Set("Mode", "SYNCING");
            Set("StatusText", "SYNCING");
            Set("StatusDetail", reason);
            Set("NextSyncAt", "0");
            WriteState(false);

            try
            {
                if (appServer == null)
                {
                    appServer = new AppServerClient(ResolveCodexPath(), timeoutMs);
                }
                Dictionary<string, object> result = appServer.ReadRateLimits();
                QuotaSnapshot quota = QuotaSnapshot.FromResult(result);
                ApplyQuota(quota);
                long now = UnixNow();
                Set("LastSyncAt", now.ToString(CultureInfo.InvariantCulture));
                Set("LastUpdated", DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture));
                Set("LastError", "--");
                Set("ConsecutiveErrors", "0");
                SetModeFromActivity();
                if (activeTurns.Count > 0 && !manualSilent)
                {
                    Set("NextSyncAt", (now + intervalSeconds).ToString(CultureInfo.InvariantCulture));
                }
            }
            catch (Exception error)
            {
                StopAppServer();
                int errors = Integer("ConsecutiveErrors", 0) + 1;
                Set("ConsecutiveErrors", errors.ToString(CultureInfo.InvariantCulture));
                Set("Mode", "ERROR");
                Set("StatusText", activeTurns.Count > 0 ? "RETRYING" : "OFFLINE");
                Set("LastError", SafeValue(error.Message, 240));

                if (activeTurns.Count > 0 && !manualSilent)
                {
                    int[] delays = { 60, 120, 300 };
                    int backoff = delays[Math.Min(errors - 1, delays.Length - 1)];
                    string message = error.Message ?? String.Empty;
                    if (message.IndexOf("429", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        message.IndexOf("too many", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        backoff = 300;
                    }
                    Set("StatusDetail", "BACKOFF " + backoff.ToString(CultureInfo.InvariantCulture) + "S");
                    Set("NextSyncAt", (UnixNow() + backoff).ToString(CultureInfo.InvariantCulture));
                }
                else if (manualSilent && activeTurns.Count > 0)
                {
                    Set("Mode", "PAUSED");
                    Set("StatusText", "QUIET");
                    Set("StatusDetail", "MANUAL SYNC FAILED");
                    Set("NextSyncAt", "0");
                }
                else
                {
                    Set("StatusDetail", "MANUAL SYNC FAILED");
                    Set("NextSyncAt", "0");
                }
            }

            WriteState(false);
            if (activeTurns.Count == 0 || manualSilent)
            {
                StopAppServer();
            }
        }

        private void ApplyQuota(QuotaSnapshot quota)
        {
            Set("Remaining", quota.Remaining.ToString(CultureInfo.InvariantCulture));
            Set("Used", quota.Used.ToString(CultureInfo.InvariantCulture));
            Set("Window", quota.Window);
            Set("Reset", quota.Reset);
            Set("ResetEpoch", quota.ResetEpoch.ToString(CultureInfo.InvariantCulture));
            Set("Plan", quota.Plan);
            Set("Credits", quota.Credits);
            Set("LimitId", quota.LimitId);
            Set("Aux", quota.Aux);
            Set("QuotaState", quota.QuotaState);
            Set("LimitError", quota.LimitError);
        }

        private void StopAppServer()
        {
            if (appServer == null)
            {
                return;
            }
            try
            {
                appServer.Dispose();
            }
            catch
            {
                // Cleanup is best effort.
            }
            appServer = null;
        }

        private void ScanRecentSessions(bool initial)
        {
            if (!Directory.Exists(sessionsRoot))
            {
                return;
            }

            // Revisit every session touched since this Windows boot. Known files
            // resume from their byte offsets, so this remains cheap and also
            // recovers cleanly after sleep, display-driver resets, or missed
            // FileSystemWatcher notifications.
            DateTime cutoff = UnixEpochUtc.AddSeconds(bootTimeUnix).AddMinutes(-2);

            try
            {
                foreach (string path in Directory.EnumerateFiles(sessionsRoot, "*.jsonl", SearchOption.AllDirectories))
                {
                    try
                    {
                        if (File.GetLastWriteTimeUtc(path) >= cutoff)
                        {
                            ReadSessionDelta(path);
                        }
                    }
                    catch
                    {
                        // A rotating session is retried on the next scan.
                    }
                }
            }
            catch
            {
                // The watcher remains available even if a catch-up scan races a directory change.
            }
        }

        private void ReadSessionDelta(string path)
        {
            if (!File.Exists(path))
            {
                return;
            }
            try
            {
                long offset;
                if (!fileOffsets.TryGetValue(path, out offset))
                {
                    offset = 0;
                }
                SessionScanResult result = SessionEventScanner.Scan(path, offset);
                fileOffsets[path] = result.Offset;
                foreach (SessionEvent sessionEvent in result.Events)
                {
                    ProcessSessionEvent(sessionEvent);
                }
            }
            catch
            {
                // A locked or rotated file is retried by the catch-up scan.
            }
        }

        private void ProcessSessionEvent(SessionEvent sessionEvent)
        {
            if (sessionEvent.Type == "task_started")
            {
                if (sessionEvent.TimestampUnix > 0 && sessionEvent.TimestampUnix < bootTimeUnix)
                {
                    return;
                }
                if (activeTurns.Add(sessionEvent.TurnId) && !baselineScan)
                {
                    pendingImmediateSync = true;
                }
                return;
            }

            if (activeTurns.Remove(sessionEvent.TurnId) && !baselineScan)
            {
                pendingFinalSync = true;
            }
        }

        private string ResolveCodexPath()
        {
            if (!String.IsNullOrWhiteSpace(codexPath) && File.Exists(codexPath))
            {
                return codexPath;
            }

            string localCodex = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenAI", "Codex", "bin", "codex.exe");
            if (File.Exists(localCodex))
            {
                return localCodex;
            }

            string pathValue = Environment.GetEnvironmentVariable("PATH") ?? String.Empty;
            foreach (string directory in pathValue.Split(Path.PathSeparator))
            {
                try
                {
                    string candidate = Path.Combine(directory.Trim(), "codex.exe");
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
                catch
                {
                    // Ignore malformed PATH entries.
                }
            }
            throw new FileNotFoundException("Codex executable was not found.");
        }

        private void WriteState(bool preserveAgentFields)
        {
            if (!preserveAgentFields)
            {
                Set("AgentPid", Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture));
                Set("AgentHeartbeat", UnixNow().ToString(CultureInfo.InvariantCulture));
                Set("ActiveCount", activeTurns.Count.ToString(CultureInfo.InvariantCulture));
            }

            string directory = Path.GetDirectoryName(statePath);
            if (!String.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            List<string> lines = new List<string>(StateKeys.Length);
            foreach (string key in StateKeys)
            {
                lines.Add(key + "=" + SafeValue(Get(key, "--"), 320));
            }

            string temporaryPath = statePath + "." + Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture) + ".tmp";
            File.WriteAllLines(temporaryPath, lines.ToArray(), new UTF8Encoding(false));
            try
            {
                if (File.Exists(statePath))
                {
                    File.Replace(temporaryPath, statePath, null);
                }
                else
                {
                    File.Move(temporaryPath, statePath);
                }
            }
            catch
            {
                File.Copy(temporaryPath, statePath, true);
                File.Delete(temporaryPath);
            }
        }

        private static Dictionary<string, string> ReadKeyValueFile(string path)
        {
            Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(path))
            {
                return values;
            }
            try
            {
                foreach (string line in File.ReadAllLines(path, new UTF8Encoding(false)))
                {
                    int index = line.IndexOf('=');
                    if (index > 0)
                    {
                        values[line.Substring(0, index)] = line.Substring(index + 1);
                    }
                }
            }
            catch
            {
                // The next pass recovers from an atomic replacement race.
            }
            return values;
        }

        private static string ReadToken(string path)
        {
            if (!File.Exists(path))
            {
                return "--";
            }
            try
            {
                string token = File.ReadAllText(path, new UTF8Encoding(false)).Trim();
                return String.IsNullOrWhiteSpace(token) ? "--" : token;
            }
            catch
            {
                return "--";
            }
        }

        private static bool TryParseQuietRequest(string token, out bool requested)
        {
            requested = false;
            if (String.IsNullOrWhiteSpace(token))
            {
                return false;
            }
            if (token.StartsWith("QUIET=1|", StringComparison.OrdinalIgnoreCase))
            {
                requested = true;
                return true;
            }
            if (token.StartsWith("QUIET=0|", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            return false;
        }

        private static bool ProcessExists(int processId)
        {
            try
            {
                using (Process process = Process.GetProcessById(processId))
                {
                    return !process.HasExited;
                }
            }
            catch
            {
                return false;
            }
        }

        private string Get(string key, string fallback)
        {
            string value;
            return state.TryGetValue(key, out value) ? value : fallback;
        }

        private void Set(string key, object value)
        {
            state[key] = value == null ? "--" : Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        private long Long(string key, long fallback)
        {
            long result;
            return Int64.TryParse(Get(key, String.Empty), NumberStyles.Integer, CultureInfo.InvariantCulture, out result) ? result : fallback;
        }

        private int Integer(string key, int fallback)
        {
            int result;
            return Int32.TryParse(Get(key, String.Empty), NumberStyles.Integer, CultureInfo.InvariantCulture, out result) ? result : fallback;
        }

        private static string SafeValue(string value, int maximumLength)
        {
            if (String.IsNullOrWhiteSpace(value))
            {
                return "--";
            }
            string safe = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
            return safe.Length > maximumLength ? safe.Substring(0, maximumLength) : safe;
        }

        private static long UnixNow()
        {
            return (long)(DateTime.UtcNow - UnixEpochUtc).TotalSeconds;
        }
    }

    internal sealed class AppServerClient : IDisposable
    {
        private readonly string executablePath;
        private readonly int timeoutMs;
        private readonly JavaScriptSerializer serializer = new JavaScriptSerializer();
        private Process process;
        private Task<string> stderrTask;
        private int requestId = 10;

        public AppServerClient(string executablePath, int timeoutMs)
        {
            this.executablePath = executablePath;
            this.timeoutMs = timeoutMs;
            serializer.MaxJsonLength = Int32.MaxValue;
            Start();
        }

        public Dictionary<string, object> ReadRateLimits()
        {
            EnsureRunning();
            int currentId = ++requestId;
            WriteLine("{\"method\":\"account/rateLimits/read\",\"id\":" + currentId.ToString(CultureInfo.InvariantCulture) + "}");
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);

            while (DateTime.UtcNow < deadline)
            {
                Dictionary<string, object> message = ReadMessage(deadline);
                long id;
                if (!TryGetId(message, out id) || id != currentId)
                {
                    continue;
                }

                object error;
                if (message.TryGetValue("error", out error) && error != null)
                {
                    throw new InvalidOperationException("Quota request failed: " + ErrorMessage(error));
                }

                object result;
                if (!message.TryGetValue("result", out result))
                {
                    throw new InvalidOperationException("Codex App Server returned no quota data.");
                }
                Dictionary<string, object> dictionary = AsDictionary(result);
                if (dictionary == null)
                {
                    throw new InvalidOperationException("Codex App Server returned an invalid quota response.");
                }
                return dictionary;
            }
            throw new TimeoutException("Codex App Server returned no quota response.");
        }

        private void Start()
        {
            DisposeProcess();
            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.FileName = executablePath;
            startInfo.Arguments = "app-server";
            startInfo.UseShellExecute = false;
            startInfo.CreateNoWindow = true;
            startInfo.RedirectStandardInput = true;
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;
            startInfo.StandardOutputEncoding = new UTF8Encoding(false);
            startInfo.StandardErrorEncoding = new UTF8Encoding(false);

            process = new Process();
            process.StartInfo = startInfo;
            if (!process.Start())
            {
                throw new InvalidOperationException("Unable to start Codex App Server.");
            }
            stderrTask = process.StandardError.ReadToEndAsync();
            WriteLine("{\"method\":\"initialize\",\"id\":0,\"params\":{\"clientInfo\":{\"name\":\"rainmeter_dynamic_quota_monitor\",\"title\":\"Rainmeter Dynamic Codex Quota Monitor\",\"version\":\"1.5.0\"}}}");

            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                Dictionary<string, object> message = ReadMessage(deadline);
                long id;
                if (!TryGetId(message, out id) || id != 0)
                {
                    continue;
                }
                object error;
                if (message.TryGetValue("error", out error) && error != null)
                {
                    throw new InvalidOperationException("Codex initialization failed: " + ErrorMessage(error));
                }
                WriteLine("{\"method\":\"initialized\",\"params\":{}}");
                return;
            }
            throw new TimeoutException("Codex App Server returned no initialization response.");
        }

        private void EnsureRunning()
        {
            if (process == null || process.HasExited)
            {
                Start();
            }
        }

        private void WriteLine(string line)
        {
            process.StandardInput.WriteLine(line);
            process.StandardInput.Flush();
        }

        private Dictionary<string, object> ReadMessage(DateTime deadline)
        {
            while (DateTime.UtcNow < deadline)
            {
                if (process == null || process.HasExited)
                {
                    throw new InvalidOperationException("Codex App Server exited unexpectedly.");
                }
                int remaining = Math.Max(1, (int)(deadline - DateTime.UtcNow).TotalMilliseconds);
                Task<string> readTask = process.StandardOutput.ReadLineAsync();
                if (!readTask.Wait(remaining))
                {
                    throw new TimeoutException("Codex App Server timed out after " + timeoutMs.ToString(CultureInfo.InvariantCulture) + " ms.");
                }
                string line = readTask.Result;
                if (line == null)
                {
                    throw new InvalidOperationException("Codex App Server closed its output stream.");
                }
                if (String.IsNullOrWhiteSpace(line))
                {
                    continue;
                }
                try
                {
                    Dictionary<string, object> message = serializer.DeserializeObject(line) as Dictionary<string, object>;
                    if (message != null)
                    {
                        return message;
                    }
                }
                catch
                {
                    // Ignore non-protocol diagnostics.
                }
            }
            throw new TimeoutException("Codex App Server timed out.");
        }

        private static bool TryGetId(Dictionary<string, object> message, out long id)
        {
            id = -1;
            object value;
            if (!message.TryGetValue("id", out value) || value == null)
            {
                return false;
            }
            try
            {
                id = Convert.ToInt64(value, CultureInfo.InvariantCulture);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string ErrorMessage(object error)
        {
            Dictionary<string, object> dictionary = AsDictionary(error);
            object message;
            return dictionary != null && dictionary.TryGetValue("message", out message) ? Convert.ToString(message, CultureInfo.InvariantCulture) : Convert.ToString(error, CultureInfo.InvariantCulture);
        }

        internal static Dictionary<string, object> AsDictionary(object value)
        {
            return value as Dictionary<string, object>;
        }

        public void Dispose()
        {
            DisposeProcess();
        }

        private void DisposeProcess()
        {
            if (process == null)
            {
                return;
            }
            try
            {
                process.StandardInput.Close();
            }
            catch
            {
                // The process may already be gone.
            }
            try
            {
                if (!process.WaitForExit(1000))
                {
                    process.Kill();
                    process.WaitForExit(1000);
                }
            }
            catch
            {
                // Cleanup is best effort.
            }
            process.Dispose();
            process = null;
            stderrTask = null;
        }
    }

    internal sealed class QuotaSnapshot
    {
        private static readonly DateTime UnixEpochUtc = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        public int Remaining { get; private set; }
        public int Used { get; private set; }
        public string Window { get; private set; }
        public string Reset { get; private set; }
        public long ResetEpoch { get; private set; }
        public string Plan { get; private set; }
        public string Credits { get; private set; }
        public string LimitId { get; private set; }
        public string Aux { get; private set; }
        public string QuotaState { get; private set; }
        public string LimitError { get; private set; }

        public static QuotaSnapshot FromResult(Dictionary<string, object> result)
        {
            Dictionary<string, object> snapshot = null;
            object value;
            if (result.TryGetValue("rateLimitsByLimitId", out value))
            {
                Dictionary<string, object> byId = AppServerClient.AsDictionary(value);
                object codex;
                if (byId != null && byId.TryGetValue("codex", out codex))
                {
                    snapshot = AppServerClient.AsDictionary(codex);
                }
            }
            if (snapshot == null && result.TryGetValue("rateLimits", out value))
            {
                snapshot = AppServerClient.AsDictionary(value);
            }
            if (snapshot == null)
            {
                throw new InvalidOperationException("The Codex quota bucket is not available for this account.");
            }

            List<WindowInfo> windows = new List<WindowInfo>();
            object windowValue;
            if (snapshot.TryGetValue("primary", out windowValue) && windowValue != null)
            {
                windows.Add(WindowInfo.FromDictionary("PRIMARY", AppServerClient.AsDictionary(windowValue)));
            }
            if (snapshot.TryGetValue("secondary", out windowValue) && windowValue != null)
            {
                windows.Add(WindowInfo.FromDictionary("SECONDARY", AppServerClient.AsDictionary(windowValue)));
            }
            if (windows.Count == 0)
            {
                throw new InvalidOperationException("The Codex quota response did not contain a rate-limit window.");
            }

            windows.Sort(delegate(WindowInfo left, WindowInfo right)
            {
                int remaining = left.Remaining.CompareTo(right.Remaining);
                return remaining != 0 ? remaining : right.DurationMinutes.CompareTo(left.DurationMinutes);
            });
            WindowInfo selected = windows[0];
            WindowInfo other = windows.Count > 1 ? windows[1] : null;

            string plan = StringValue(snapshot, "planType", "UNKNOWN").ToUpperInvariant();
            string limitId = StringValue(snapshot, "limitId", "CODEX").ToUpperInvariant();
            string credits = "--";
            object creditsValue;
            if (snapshot.TryGetValue("credits", out creditsValue) && creditsValue != null)
            {
                Dictionary<string, object> creditInfo = AppServerClient.AsDictionary(creditsValue);
                if (BooleanValue(creditInfo, "unlimited"))
                {
                    credits = "UNLIMITED";
                }
                else if (creditInfo != null && creditInfo.TryGetValue("balance", out value) && value != null)
                {
                    credits = Convert.ToString(value, CultureInfo.InvariantCulture);
                }
                else if (BooleanValue(creditInfo, "hasCredits"))
                {
                    credits = "AVAILABLE";
                }
                else
                {
                    credits = "0";
                }
            }

            object reachedValue;
            string reached = snapshot.TryGetValue("rateLimitReachedType", out reachedValue) && reachedValue != null
                ? Convert.ToString(reachedValue, CultureInfo.InvariantCulture)
                : null;

            return new QuotaSnapshot
            {
                Remaining = selected.Remaining,
                Used = selected.Used,
                Window = WindowLabel(selected.DurationMinutes),
                Reset = selected.ResetEpoch > 0 ? UnixEpochUtc.AddSeconds(selected.ResetEpoch).ToLocalTime().ToString("MM-dd HH:mm", CultureInfo.InvariantCulture) : "--",
                ResetEpoch = selected.ResetEpoch,
                Plan = plan,
                Credits = credits,
                LimitId = limitId,
                Aux = other != null
                    ? String.Format(CultureInfo.InvariantCulture, "{0} {1}% // {2}", other.Name, other.Remaining, WindowLabel(other.DurationMinutes))
                    : selected.Name + " // " + limitId,
                QuotaState = reached == null ? "ONLINE" : "LIMITED",
                LimitError = reached ?? "--"
            };
        }

        private static string WindowLabel(long minutes)
        {
            if (minutes <= 0) return "RATE WINDOW";
            if (minutes % 10080 == 0) return (minutes / 10080).ToString(CultureInfo.InvariantCulture) + "W WINDOW";
            if (minutes % 1440 == 0) return (minutes / 1440).ToString(CultureInfo.InvariantCulture) + "D WINDOW";
            if (minutes % 60 == 0) return (minutes / 60).ToString(CultureInfo.InvariantCulture) + "H WINDOW";
            return minutes.ToString(CultureInfo.InvariantCulture) + "M WINDOW";
        }

        private static string StringValue(Dictionary<string, object> dictionary, string key, string fallback)
        {
            object value;
            if (dictionary == null || !dictionary.TryGetValue(key, out value) || value == null || String.IsNullOrWhiteSpace(Convert.ToString(value, CultureInfo.InvariantCulture)))
            {
                return fallback;
            }
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        private static bool BooleanValue(Dictionary<string, object> dictionary, string key)
        {
            object value;
            if (dictionary == null || !dictionary.TryGetValue(key, out value) || value == null)
            {
                return false;
            }
            try
            {
                return Convert.ToBoolean(value, CultureInfo.InvariantCulture);
            }
            catch
            {
                return false;
            }
        }

        private sealed class WindowInfo
        {
            public string Name;
            public int Used;
            public int Remaining;
            public long DurationMinutes;
            public long ResetEpoch;

            public static WindowInfo FromDictionary(string name, Dictionary<string, object> dictionary)
            {
                if (dictionary == null)
                {
                    throw new InvalidOperationException("Invalid quota window.");
                }
                int used = Math.Max(0, Math.Min(100, IntegerValue(dictionary, "usedPercent")));
                return new WindowInfo
                {
                    Name = name,
                    Used = used,
                    Remaining = 100 - used,
                    DurationMinutes = LongValue(dictionary, "windowDurationMins"),
                    ResetEpoch = LongValue(dictionary, "resetsAt")
                };
            }

            private static int IntegerValue(Dictionary<string, object> dictionary, string key)
            {
                object value;
                return dictionary.TryGetValue(key, out value) && value != null ? Convert.ToInt32(value, CultureInfo.InvariantCulture) : 0;
            }

            private static long LongValue(Dictionary<string, object> dictionary, string key)
            {
                object value;
                return dictionary.TryGetValue(key, out value) && value != null ? Convert.ToInt64(value, CultureInfo.InvariantCulture) : 0L;
            }
        }
    }

    internal static class NativeMethods
    {
        [DllImport("kernel32.dll")]
        internal static extern ulong GetTickCount64();
    }
}
