using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

// 声明目标框架，否则 csc 直接编译出来的程序走旧版路径规则：
// LP() 加的 \\?\ 长路径前缀会被判为“路径中具有非法字符”，超过 260 字符的路径也无法处理。
[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.6.2")]

namespace ClaudeSwitch
{
    public class Account
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Email { get; set; }
        public string AccountUuid { get; set; }
        public string OrgUuid { get; set; }
        public string CreatedAt { get; set; }
        public string UpdatedAt { get; set; }
        public string LastUsedAt { get; set; }
    }

    public class Store
    {
        public List<Account> Accounts { get; set; }
        public string ActiveId { get; set; }
        public Store() { Accounts = new List<Account>(); ActiveId = ""; }
    }

    public class SwitchProgress
    {
        public int Step { get; set; }
        public int Total { get; set; }
        public string Message { get; set; }
        public string Error { get; set; }
        public bool Finished { get; set; }
        public bool Ok { get; set; }
    }

    public class SwitchResult
    {
        public bool Ok { get; set; }
        public bool Already { get; set; }
        public string Message { get; set; }
        public string CurrentUuid { get; set; }
        // 切换后自动打开的会话（切换前最近在用的那个）
        public string ResumedSessionId { get; set; }
        public string ResumedSessionTitle { get; set; }
    }

    public class AccountInsight
    {
        public string DesktopOrg { get; set; }
        public string Plan { get; set; }          // Pro / Max / Team / Enterprise / Free，未知为 null
        public bool PlanGuessed { get; set; }      // 计划是根据有没有用量记录推测的
        public bool HasUsageHistory { get; set; }
        public int? FiveHour { get; set; }         // 5 小时窗口用量百分比
        public int? Week { get; set; }             // 7 天窗口用量百分比
        public DateTime? UsageAt { get; set; }
    }

    public class SessionRef
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public long LastActivityAt { get; set; }
    }

    public class Engine
    {
        public static readonly JavaScriptSerializer Js = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };

        public static bool TestMode = Environment.GetEnvironmentVariable("CLAUDE_SWITCH_TEST") == "1";

        public string Home { get; private set; }
        public string ClaudeDir { get; private set; }
        public string ClaudeJson { get; private set; }
        public string CredFile { get; private set; }
        public string LocalApp { get; private set; }
        public string StoreRoot { get; private set; }
        public string ProfilesDir { get; private set; }
        public string BackupsDir { get; private set; }
        public string LogsDir { get; private set; }
        public string DataBase { get; private set; }
        public string DataDir { get; private set; }
        public string Aumid { get; private set; }
        public string ClaudeVersion { get; private set; }

        private static readonly string[] DesktopFiles = {
            "config.json", "ant-did", "ant-device-registry.json", "plan-usage-history.json", "buddy-tokens.json"
        };
        private static readonly string[] DesktopDirs = { "Network", "Local Storage" };

        public Engine()
        {
            Home = Environment.GetEnvironmentVariable("CLAUDE_SWITCH_CLAUDE_HOME");
            if (string.IsNullOrEmpty(Home)) Home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            ClaudeDir = Path.Combine(Home, ".claude");
            ClaudeJson = Path.Combine(Home, ".claude.json");
            CredFile = Path.Combine(ClaudeDir, ".credentials.json");
            LocalApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            StoreRoot = ResolveStoreRoot();
            ProfilesDir = Path.Combine(StoreRoot, "profiles");
            BackupsDir = Path.Combine(StoreRoot, "backups");
            LogsDir = Path.Combine(StoreRoot, "logs");
            DataBase = Path.Combine(LocalApp, "Claude-Data");
            var dataOverride = Environment.GetEnvironmentVariable("CLAUDE_SWITCH_DATA_DIR");
            DataDir = string.IsNullOrEmpty(dataOverride) ? FindDataDir() : (dataOverride == "none" ? null : dataOverride);
            Aumid = BuildAumid();
            EnsureDirs();
        }

        public static string ResolveHome() { return ResolveStoreRoot(); }

        public const string AppId = "ClaudeRelay";
        public const string AppName = "接力 Relay";
        public const string PortableDirName = "ClaudeRelay-data";

        public static string InstalledDataRoot
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppId); }
        }

        public static string ExeDir
        {
            get
            {
                try { return Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location); }
                catch { return null; }
            }
        }

        // 数据目录：
        //   1. 环境变量 CLAUDE_RELAY_HOME（旧名 CLAUDE_SWITCH_HOME 也认）
        //   2. 安装版：%LOCALAPPDATA%\ClaudeRelay
        //   3. 便携版：exe 旁边的 ClaudeRelay-data 文件夹
        private static string ResolveStoreRoot()
        {
            var candidates = new List<string>();
            foreach (var name in new[] { "CLAUDE_RELAY_HOME", "CLAUDE_SWITCH_HOME" })
            {
                var env = Environment.GetEnvironmentVariable(name);
                if (!string.IsNullOrEmpty(env)) candidates.Add(env);
            }
            var exeDir = ExeDir;
            if (Installer.IsRunningInstalled() || string.IsNullOrEmpty(exeDir))
                candidates.Add(InstalledDataRoot);
            else
                candidates.Add(Path.Combine(exeDir, PortableDirName));
            candidates.Add(InstalledDataRoot);
            candidates.Add(Path.Combine(Path.GetTempPath(), AppId));
            foreach (var c in candidates)
            {
                if (CanWriteDir(c)) return c;
            }
            return candidates[candidates.Count - 1];
        }

        private static bool CanWriteDir(string dir)
        {
            try
            {
                Directory.CreateDirectory(dir);
                var probe = Path.Combine(dir, ".write-test-" + Guid.NewGuid().ToString("N"));
                File.WriteAllText(probe, "ok");
                File.Delete(probe);
                return true;
            }
            catch { return false; }
        }

        // Claude 桌面端有两种装法，数据目录不同：
        //   应用商店版（MSIX）：%LOCALAPPDATA%\Claude-Data\claude_<发布者ID>
        //   官网 exe 版：       %APPDATA%\Claude
        // 都存在时取最近用过的那个（看 config.json 的修改时间）
        private string FindDataDir()
        {
            var candidates = new List<string>();
            try
            {
                if (Directory.Exists(DataBase)) candidates.AddRange(Directory.GetDirectories(DataBase, "claude_*"));
            }
            catch { }
            candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Claude"));
            return candidates
                .Where(d => File.Exists(Path.Combine(d, "config.json")))
                .OrderByDescending(d => { try { return File.GetLastWriteTime(Path.Combine(d, "config.json")); } catch { return DateTime.MinValue; } })
                .FirstOrDefault();
        }

        public bool IsStoreApp
        {
            get { return !string.IsNullOrEmpty(DataDir) && DataDir.StartsWith(DataBase, StringComparison.OrdinalIgnoreCase); }
        }

        // 没找到 Claude 时，用户装好后点「重新检测」
        public bool Redetect()
        {
            var dataOverride = Environment.GetEnvironmentVariable("CLAUDE_SWITCH_DATA_DIR");
            DataDir = string.IsNullOrEmpty(dataOverride) ? FindDataDir() : (dataOverride == "none" ? null : dataOverride);
            Aumid = BuildAumid();
            return !string.IsNullOrEmpty(DataDir);
        }

        // 官网 exe 版 Claude 的启动程序
        private static string FindDesktopExe()
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            foreach (var p in new[]
            {
                Path.Combine(local, "AnthropicClaude", "claude.exe"),
                Path.Combine(local, "Programs", "Claude", "Claude.exe"),
                Path.Combine(local, "Programs", "claude", "Claude.exe"),
            })
            {
                if (File.Exists(p)) return p;
            }
            return null;
        }

        private string BuildAumid()
        {
            if (string.IsNullOrEmpty(DataDir) || !IsStoreApp) return null;
            var name = Path.GetFileName(DataDir);
            var suffix = name.StartsWith("claude_", StringComparison.OrdinalIgnoreCase) ? name.Substring(7) : name;
            return "Claude_" + suffix + "!Claude";
        }

        public void EnsureDirs()
        {
            foreach (var d in new[] { StoreRoot, ProfilesDir, BackupsDir, LogsDir })
                if (!Directory.Exists(d)) Directory.CreateDirectory(d);
        }

        // ------------------------------------------------------------ logging

        public void Log(string msg)
        {
            try
            {
                var line = "[" + DateTime.Now.ToString("HH:mm:ss") + "] " + msg + Environment.NewLine;
                File.AppendAllText(Path.Combine(LogsDir, "switch-" + DateTime.Now.ToString("yyyyMMdd") + ".log"), line, new UTF8Encoding(false));
            }
            catch { }
        }

        // ------------------------------------------------------------ processes

        public List<Process> GetClaudeProcesses()
        {
            var list = new List<Process>();
            foreach (var p in Process.GetProcessesByName("claude"))
            {
                bool include;
                try
                {
                    // 只认桌面端的安装位置；命令行版 Claude Code（npm、~/.local/bin 等）也叫 claude.exe，不能误关
                    var path = p.MainModule.FileName;
                    include = path.IndexOf(@"\WindowsApps\Claude_", StringComparison.OrdinalIgnoreCase) >= 0
                           || path.IndexOf(@"\Claude-Data\", StringComparison.OrdinalIgnoreCase) >= 0
                           || path.IndexOf(@"\AnthropicClaude\", StringComparison.OrdinalIgnoreCase) >= 0
                           || path.IndexOf(@"\Programs\Claude\", StringComparison.OrdinalIgnoreCase) >= 0;
                }
                catch
                {
                    // 应用商店版的进程常常读不到路径，按桌面端处理
                    include = true;
                }
                if (include) list.Add(p);
            }
            return list;
        }

        public bool ClaudeRunning { get { return GetClaudeProcesses().Count > 0; } }

        private static bool CanOpenExclusive(string path)
        {
            try
            {
                if (!File.Exists(path)) return true;
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
                return true;
            }
            catch { return false; }
        }

        public bool ClaudeFilesReleased()
        {
            if (string.IsNullOrEmpty(DataDir)) return true;
            return CanOpenExclusive(Path.Combine(DataDir, "Network", "Cookies"))
                && CanOpenExclusive(Path.Combine(DataDir, "Network", "Cookies-journal"))
                && CanOpenExclusive(Path.Combine(DataDir, "Local Storage", "LOCK"));
        }

        public bool WaitClaudeFilesReleased(int seconds)
        {
            var end = DateTime.Now.AddSeconds(seconds);
            while (DateTime.Now < end)
            {
                if (ClaudeFilesReleased()) return true;
                Thread.Sleep(400);
            }
            return ClaudeFilesReleased();
        }

        public static string LP(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            if (path.StartsWith(@"\\?\")) return path;
            return Path.IsPathRooted(path) ? @"\\?\" + path : path;
        }

        private static void CopyFileRetry(string src, string dst, bool overwrite = true)
        {
            for (int i = 0; i < 8; i++)
            {
                try { File.Copy(src, dst, overwrite); return; }
                catch (IOException) { Thread.Sleep(400); }
                catch (UnauthorizedAccessException) { Thread.Sleep(400); }
            }
            File.Copy(src, dst, overwrite);
        }

        private static bool WaitFor(Func<bool> cond, int seconds)
        {
            var end = DateTime.Now.AddSeconds(seconds);
            while (DateTime.Now < end)
            {
                if (cond()) return true;
                Thread.Sleep(300);
            }
            return cond();
        }

        public bool CanWriteDataDir(out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(DataDir))
            {
                error = "找不到 Claude 数据目录";
                return false;
            }
            var stage = "创建或写入探测文件";
            try
            {
                var probe = Path.Combine(DataDir, ".claude-switch-write-test-" + Guid.NewGuid().ToString("N"));
                File.WriteAllText(probe, "ok");
                stage = "删除探测文件";
                File.Delete(probe);
                return true;
            }
            catch (Exception ex) { error = stage + "失败；" + ex.GetType().Name + "：" + ex.Message; return false; }
        }

        public void CloseClaude()
        {
            if (TestMode) { Log("[TEST] 跳过关闭 Claude"); return; }
            Log("关闭 Claude ...");
            for (int attempt = 0; attempt < 3; attempt++)
            {
                var procs = GetClaudeProcesses();
                if (procs.Count > 0)
                {
                    foreach (var p in procs)
                    {
                        try { if (p.MainWindowHandle != IntPtr.Zero) p.CloseMainWindow(); } catch { }
                    }
                    // Claude 关窗口后通常只是缩到托盘，等太久没有意义；给 3 秒让它收尾，然后直接结束
                    WaitFor(() => GetClaudeProcesses().Count == 0, attempt == 0 ? 3 : 2);
                    foreach (var p in GetClaudeProcesses())
                    {
                        try { p.Kill(); } catch { }
                    }
                    WaitFor(() => GetClaudeProcesses().Count == 0, 10);
                }
                if (WaitClaudeFilesReleased(attempt == 0 ? 10 : 5)) return;
                Thread.Sleep(400);
            }
            throw new Exception("Claude 没有完全退出，数据文件仍被占用。\n请打开任务管理器，结束所有 Claude 进程后再试。");
        }

        public void LaunchClaude()
        {
            if (TestMode) { Log("[TEST] 跳过启动 Claude"); return; }
            Log("启动 Claude");
            if (!string.IsNullOrEmpty(Aumid))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", "shell:AppsFolder\\" + Aumid) { UseShellExecute = true });
            }
            else
            {
                var exe = FindDesktopExe();
                if (exe == null) throw new Exception("找不到 Claude 程序，请手动打开 Claude");
                Process.Start(new ProcessStartInfo("explorer.exe", "\"" + exe + "\"") { UseShellExecute = true });
            }
            if (!WaitFor(() => GetClaudeProcesses().Count > 0, 60))
                throw new Exception("Claude 启动超时，请手动打开");
            // 等到主窗口出现就继续，不再固定等 4 秒
            WaitFor(() => GetClaudeProcesses().Any(p => { try { return p.MainWindowHandle != IntPtr.Zero; } catch { return false; } }), 20);
        }

        // ------------------------------------------------------------ resume session

        // 找出最近在用的本地 Claude Code 会话（未归档、lastActivityAt 最大）
        public SessionRef FindLastSession(string accountUuid)
        {
            if (string.IsNullOrEmpty(DataDir) || string.IsNullOrEmpty(accountUuid)) return null;
            SessionRef best = null;
            try
            {
                var root = Path.Combine(DataDir, "claude-code-sessions", accountUuid);
                if (!Directory.Exists(root)) return null;
                foreach (var orgDir in Directory.GetDirectories(root))
                {
                    foreach (var f in Directory.GetFiles(orgDir, "local_*.json"))
                    {
                        try
                        {
                            var text = File.ReadAllText(f, Encoding.UTF8);
                            if (JsonField(text, "isArchived") == "true") continue;
                            long t;
                            if (!long.TryParse(JsonField(text, "lastActivityAt"), out t)) continue;
                            if (best == null || t > best.LastActivityAt)
                                best = new SessionRef { Id = Path.GetFileNameWithoutExtension(f), Title = JsonField(text, "title"), LastActivityAt = t };
                        }
                        catch { }
                    }
                }
            }
            catch { }
            return best;
        }

        // 取 JSON 顶层附近第一个同名字段的值（会话文件可能很大，不做完整解析）
        private static string JsonField(string json, string key)
        {
            var m = System.Text.RegularExpressions.Regex.Match(json, "\"" + key + "\":(\"((?:[^\"\\\\]|\\\\.)*)\"|[0-9]+|true|false)");
            if (!m.Success) return null;
            return m.Groups[2].Success ? System.Text.RegularExpressions.Regex.Unescape(m.Groups[2].Value) : m.Groups[1].Value;
        }

        // 用桌面端自带的跳转链接打开指定会话
        public void OpenSession(string sessionId)
        {
            if (TestMode || string.IsNullOrEmpty(sessionId)) return;
            if (!System.Text.RegularExpressions.Regex.IsMatch(sessionId, "^local_[A-Za-z0-9-]{1,64}$")) return;
            try
            {
                Log("打开会话：" + sessionId);
                Process.Start(new ProcessStartInfo("claude://code/continue?session=" + sessionId + "&source=desktop_action") { UseShellExecute = true });
            }
            catch (Exception ex) { Log("打开会话失败：" + ex.Message); }
        }

        // ------------------------------------------------------------ claude json

        public string GetDesktopUuid()
        {
            try
            {
                if (string.IsNullOrEmpty(DataDir)) return null;
                var cfg = Js.DeserializeObject(File.ReadAllText(Path.Combine(DataDir, "config.json"))) as Dictionary<string, object>;
                if (cfg != null && cfg.ContainsKey("lastKnownAccountUuid"))
                    return Convert.ToString(cfg["lastKnownAccountUuid"]);
            }
            catch { }
            return null;
        }

        public Dictionary<string, object> ReadIdentity()
        {
            try
            {
                if (!File.Exists(ClaudeJson)) return null;
                var cfg = Js.DeserializeObject(File.ReadAllText(ClaudeJson)) as Dictionary<string, object>;
                if (cfg != null && cfg.ContainsKey("oauthAccount"))
                    return cfg["oauthAccount"] as Dictionary<string, object>;
            }
            catch { }
            return null;
        }

        public string IdentityEmail(Dictionary<string, object> id)
        {
            if (id == null) return null;
            object v;
            return id.TryGetValue("emailAddress", out v) ? Convert.ToString(v) : null;
        }

        public string IdentityOrg(Dictionary<string, object> id)
        {
            if (id == null) return null;
            object v;
            return id.TryGetValue("organizationUuid", out v) ? Convert.ToString(v) : null;
        }

        public void SetIdentity(Dictionary<string, object> identity)
        {
            if (identity == null) return;
            var cfg = Js.DeserializeObject(File.ReadAllText(ClaudeJson)) as Dictionary<string, object>;
            cfg["oauthAccount"] = identity;
            if (cfg.ContainsKey("cachedUsageUtilization")) cfg["cachedUsageUtilization"] = null;
            WriteAllTextAtomic(ClaudeJson, Js.Serialize(cfg));
        }

        public static void WriteAllTextAtomic(string path, string text)
        {
            var tmp = path + ".tmp";
            File.WriteAllText(LP(tmp), text, new UTF8Encoding(false));
            File.Copy(LP(tmp), LP(path), true);
            try { File.Delete(LP(tmp)); } catch { }
        }

        // ------------------------------------------------------------ capture / restore

        public void CaptureInto(string destDir)
        {
            if (string.IsNullOrEmpty(DataDir)) throw new Exception("找不到 Claude 数据目录");
            var desktop = Path.Combine(destDir, "desktop");
            Directory.CreateDirectory(desktop);
            foreach (var f in DesktopFiles)
            {
                var src = Path.Combine(DataDir, f);
                if (File.Exists(src)) CopyFileRetry(src, Path.Combine(desktop, f));
            }
            foreach (var d in DesktopDirs)
            {
                var src = Path.Combine(DataDir, d);
                if (!Directory.Exists(src)) continue;
                var dst = Path.Combine(desktop, d);
                if (Directory.Exists(dst)) TryDeleteDir(dst);
                CopyDir(src, dst);
            }
            var claudeOut = Path.Combine(destDir, "claude");
            Directory.CreateDirectory(claudeOut);
            if (File.Exists(CredFile)) CopyFileRetry(CredFile, Path.Combine(claudeOut, "credentials.json"));
            var identity = ReadIdentity();
            if (identity != null) WriteAllTextAtomic(Path.Combine(destDir, "identity.json"), Js.Serialize(identity));
        }

        public void RestoreFrom(string srcDir, string expectedEmail = null)
        {
            if (string.IsNullOrEmpty(DataDir)) throw new Exception("找不到 Claude 数据目录");
            var desktop = Path.Combine(srcDir, "desktop");
            if (!Directory.Exists(desktop)) throw new Exception("账号快照不完整：" + srcDir);
            if (!WaitClaudeFilesReleased(10))
                throw new Exception("Claude 还没有完全退出，数据文件仍被占用。\n请结束所有 Claude 进程后再试。");
            foreach (var d in DesktopDirs)
            {
                var target = Path.Combine(DataDir, d);
                if (Directory.Exists(target)) TryDeleteDir(target);
            }
            foreach (var f in Directory.GetFiles(desktop))
            {
                var dst = Path.Combine(DataDir, Path.GetFileName(f));
                try { if (File.Exists(dst)) File.SetAttributes(dst, FileAttributes.Normal); } catch { }
                CopyFileRetry(f, dst);
            }
            foreach (var d in DesktopDirs)
            {
                var src = Path.Combine(desktop, d);
                if (Directory.Exists(src)) CopyDir(src, Path.Combine(DataDir, d));
            }
            var idf = Path.Combine(srcDir, "identity.json");
            if (File.Exists(idf))
            {
                var identity = Js.DeserializeObject(File.ReadAllText(idf)) as Dictionary<string, object>;
                var idEmail = IdentityEmail(identity);
                if (string.IsNullOrEmpty(expectedEmail) ||
                    string.Equals(idEmail, expectedEmail, StringComparison.OrdinalIgnoreCase))
                {
                    var cred = Path.Combine(srcDir, "claude", "credentials.json");
                    if (File.Exists(cred)) CopyFileRetry(cred, CredFile);
                    SetIdentity(identity);
                }
                else
                {
                    Log("CLI 凭证与桌面账号不一致（" + idEmail + " != " + expectedEmail + "），跳过不动 ~/.claude");
                }
            }
        }

        public void EnsureDataWritable()
        {
            if (string.IsNullOrEmpty(DataDir)) throw new Exception("找不到 Claude 数据目录");
            string error;
            if (!CanWriteDataDir(out error))
            {
                throw new Exception("无法写入 Claude 数据目录：\n" + DataDir + "\n\n系统错误：" + error +
                    "\n\n请将此错误信息提供给开发者，以便排查。");
            }
        }

        public static void CopyDir(string src, string dst)
        {
            Directory.CreateDirectory(dst);
            foreach (var f in Directory.GetFiles(src))
                CopyFileRetry(f, Path.Combine(dst, Path.GetFileName(f)));
            foreach (var d in Directory.GetDirectories(src))
            {
                try
                {
                    var attrs = File.GetAttributes(d);
                    if ((attrs & FileAttributes.ReparsePoint) != 0) continue;
                }
                catch { continue; }
                CopyDir(d, Path.Combine(dst, Path.GetFileName(d)));
            }
        }

        public static void TryDeleteDir(string dir)
        {
            for (int i = 0; i < 5; i++)
            {
                try { Directory.Delete(dir, true); return; }
                catch { Thread.Sleep(300); }
            }
        }

        // ------------------------------------------------------------ store

        public Store LoadStore()
        {
            try
            {
                var p = Path.Combine(StoreRoot, "config.json");
                if (!File.Exists(p)) return new Store();
                var s = Js.Deserialize<Store>(File.ReadAllText(p));
                if (s == null) return new Store();
                if (s.Accounts == null) s.Accounts = new List<Account>();
                return s;
            }
            catch { return new Store(); }
        }

        public void SaveStore(Store s)
        {
            WriteAllTextAtomic(Path.Combine(StoreRoot, "config.json"), Js.Serialize(s));
        }

        public string ProfileDir(Account a) { return Path.Combine(ProfilesDir, a.Id); }

        public Account FindByUuid(Store s, string uuid)
        {
            if (string.IsNullOrEmpty(uuid)) return null;
            return s.Accounts.FirstOrDefault(a => string.Equals(a.AccountUuid, uuid, StringComparison.OrdinalIgnoreCase));
        }

        // ------------------------------------------------------------ actions

        // 登录另一个号，但不在 Claude 里“退出登录”。
        // 退出登录会让服务器把当前号的登录作废，已存的快照也跟着失效；
        // 这里只把本机的登录状态（Cookies、Local Storage）挪开，Claude 重开后就是登录页。
        public void PrepareFreshLogin()
        {
            if (string.IsNullOrEmpty(DataDir)) throw new Exception("找不到 Claude 数据目录");
            EnsureDataWritable();
            var store = LoadStore();
            var current = FindByUuid(store, GetDesktopUuid());

            Log("准备登录另一个号");
            CloseClaude();

            var backupDir = Path.Combine(BackupsDir, DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-fresh");
            Directory.CreateDirectory(backupDir);
            CaptureInto(backupDir);
            if (File.Exists(ClaudeJson)) File.Copy(ClaudeJson, Path.Combine(backupDir, "claude.json"), true);

            // 当前号如果已存过，先把它最新的登录状态存好
            if (current != null)
            {
                Log("刷新当前账号快照：" + current.Name);
                CaptureInto(ProfileDir(current));
            }

            try
            {
                if (!WaitClaudeFilesReleased(10))
                    throw new Exception("Claude 还没有完全退出，数据文件仍被占用。");
                foreach (var d in DesktopDirs)
                {
                    var target = Path.Combine(DataDir, d);
                    if (Directory.Exists(target)) TryDeleteDir(target);
                    if (Directory.Exists(target)) throw new Exception("没能清掉 " + d + "，可能被占用");
                }
                LaunchClaude();
            }
            catch (Exception ex)
            {
                Log("准备登录失败，回滚：" + ex.Message);
                try { if (WaitClaudeFilesReleased(3)) RestoreFrom(backupDir); if (!ClaudeRunning) LaunchClaude(); } catch { }
                throw new Exception(ex.Message + "\n\n已恢复到之前的登录状态。");
            }
        }

        public Account SaveCurrent(string name, string email)
        {
            var uuid = GetDesktopUuid();
            var identity = ReadIdentity();
            var org = IdentityOrg(identity);
            var store = LoadStore();
            var acc = FindByUuid(store, uuid);
            bool isNew = acc == null;
            if (isNew)
            {
                acc = new Account
                {
                    Id = "acc-" + DateTime.Now.ToString("yyyyMMddHHmmssfff") + "-" + Guid.NewGuid().ToString("N").Substring(0, 4),
                    CreatedAt = DateTime.Now.ToString("s")
                };
            }
            if (string.IsNullOrWhiteSpace(name)) name = "账号 " + DateTime.Now.ToString("MMdd-HHmm");
            acc.Name = name;
            acc.Email = !string.IsNullOrWhiteSpace(email) ? email : IdentityEmail(identity);
            acc.AccountUuid = uuid;
            acc.OrgUuid = org;
            acc.UpdatedAt = DateTime.Now.ToString("s");
            acc.LastUsedAt = acc.UpdatedAt;

            Log("保存账号：" + acc.Name);
            EnsureDataWritable();
            bool wasRunning = !TestMode && ClaudeRunning;
            CloseClaude();
            var dir = ProfileDir(acc);
            Directory.CreateDirectory(dir);
            CaptureInto(dir);
            WriteAllTextAtomic(Path.Combine(dir, "meta.json"), Js.Serialize(acc));

            if (isNew) store.Accounts.Add(acc);
            store.ActiveId = acc.Id;
            SaveStore(store);

            // 新保存的账号也接入共享会话列表
            ShareSessionIndex(store, Path.Combine(BackupsDir, DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-share"));

            if (wasRunning) LaunchClaude();
            return acc;
        }

        public SwitchResult Switch(Account target, IProgress<SwitchProgress> progress)
        {
            var total = 6;
            var currentUuid = GetDesktopUuid();
            var identity = ReadIdentity();

            if (!string.IsNullOrEmpty(currentUuid) && string.Equals(currentUuid, target.AccountUuid, StringComparison.OrdinalIgnoreCase))
                return new SwitchResult { Ok = true, Already = true, Message = "当前已经是「" + target.Name + "」", CurrentUuid = currentUuid };

            var store = LoadStore();
            var currentProfile = FindByUuid(store, currentUuid);
            // 记下切换前正在用的会话，切完自动打开
            var resume = FindLastSession(currentUuid);
            var oldEmail = currentProfile != null && !string.IsNullOrEmpty(currentProfile.Email)
                ? currentProfile.Email
                : IdentityEmail(identity);

            Report(progress, 1, total, "关闭 Claude");
            EnsureDataWritable();
            CloseClaude();

            Report(progress, 2, total, "备份当前状态");
            var backupDir = Path.Combine(BackupsDir, DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            Directory.CreateDirectory(backupDir);
            CaptureInto(backupDir);
            if (File.Exists(ClaudeJson)) File.Copy(ClaudeJson, Path.Combine(backupDir, "claude.json"), true);

            var active = FindByUuid(store, currentUuid);
            if (active != null && active.Id != target.Id)
            {
                Log("刷新当前账号快照：" + active.Name);
                CaptureInto(ProfileDir(active));
                var idNow = ReadIdentity();
                if (idNow != null) WriteAllTextAtomic(Path.Combine(ProfileDir(active), "identity.json"), Js.Serialize(idNow));
            }

            try
            {
                Report(progress, 3, total, "还原账号数据");
                RestoreFrom(ProfileDir(target), target.Email);

                Report(progress, 4, total, "共享会话列表并清理旧痕迹");
                ShareSessionIndex(store, backupDir);
                if (!string.IsNullOrEmpty(oldEmail) && !string.IsNullOrEmpty(target.Email) &&
                    !string.Equals(oldEmail, target.Email, StringComparison.OrdinalIgnoreCase))
                {
                    ScrubEmails(oldEmail, target.Email, backupDir);
                }

                Report(progress, 5, total, "启动 Claude");
                LaunchClaude();
            }
            catch (Exception ex)
            {
                Log("切换失败，尝试回滚：" + ex.Message);
                try
                {
                    if (WaitClaudeFilesReleased(3))
                        RestoreFrom(backupDir);
                    var cj = Path.Combine(backupDir, "claude.json");
                    if (File.Exists(cj)) File.Copy(cj, ClaudeJson, true);
                    if (!ClaudeRunning) LaunchClaude();
                }
                catch (Exception rb)
                {
                    Log("回滚失败：" + rb.Message);
                }
                throw new Exception(ex.Message + "\n\n已尝试回滚到切换前的状态；如仍异常，请点「恢复备份」。");
            }

            Report(progress, 6, total, "回到原来的会话");
            if (resume != null) OpenSession(resume.Id);
            var nowUuid = GetDesktopUuid();

            target.LastUsedAt = DateTime.Now.ToString("s");
            store.ActiveId = target.Id;
            SaveStore(store);

            var ok = string.IsNullOrEmpty(nowUuid) || string.Equals(nowUuid, target.AccountUuid, StringComparison.OrdinalIgnoreCase);
            Log(ok ? "切换完成：" + target.Name : "切换完成但 UUID 不一致：" + nowUuid + " vs " + target.AccountUuid);
            return new SwitchResult
            {
                Ok = ok,
                Already = false,
                CurrentUuid = nowUuid,
                ResumedSessionId = resume != null ? resume.Id : null,
                ResumedSessionTitle = resume != null ? resume.Title : null,
                Message = ok ? "已切换到「" + target.Name + "」" : "已切换，但账号 UUID 校验不一致，请打开 Claude 确认"
            };
        }

        private static void Report(IProgress<SwitchProgress> p, int step, int total, string msg)
        {
            if (p != null) p.Report(new SwitchProgress { Step = step, Total = total, Message = msg });
        }

        public void RestoreLatestBackup()
        {
            var dirs = Directory.GetDirectories(BackupsDir).OrderByDescending(d => d).ToList();
            if (dirs.Count == 0) throw new Exception("没有可用的备份");
            var bk = dirs[0];
            Log("恢复备份：" + bk);
            CloseClaude();
            RestoreFrom(bk);
            var cj = Path.Combine(bk, "claude.json");
            if (File.Exists(cj)) File.Copy(cj, ClaudeJson, true);
            var sess = Path.Combine(bk, "sessions");
            if (Directory.Exists(sess)) RestoreSessions(sess);
            LaunchClaude();
        }

        private void RestoreSessions(string backupSessionsRoot)
        {
            var projects = Path.Combine(ClaudeDir, "projects");
            if (!Directory.Exists(projects)) return;
            foreach (var f in SafeFiles(backupSessionsRoot))
            {
                var rel = f.Substring(backupSessionsRoot.Length).TrimStart('\\');
                var dst = Path.Combine(projects, rel);
                try
                {
                    var dir = Path.GetDirectoryName(dst);
                    if (!Directory.Exists(dir)) Directory.CreateDirectory(LP(dir));
                    File.Copy(LP(f), LP(dst), true);
                }
                catch { }
            }
        }

        // ------------------------------------------------------------ session index

        // 桌面端把左侧会话列表存在 <DataDir>\<root>\<账号UUID>\<组织UUID>\ 下，按账号隔离。
        // 做法：切号时（Claude 已关闭）在各账号的真实目录之间同步：
        //   先把各账号目录合并进中转目录 claude-switch-shared（同名文件保留较新的），再分发回每个账号目录。
        // 这些目录必须是真实目录：桌面端保存会话前会检查，遇到符号链接或目录联接会拒绝写入
        // （PlantDetectedError），会话就再也存不下来。1.0.0 用的是联接，这里会自动换回真实目录。
        // （会话正文 ~/.claude/projects 本来就与账号无关，所以同步列表之后可以直接接着聊。）
        private static readonly string[] SessionRoots = { "claude-code-sessions", "local-agent-mode-sessions" };

        // waiting-input 记录的是运行中的会话状态，同步过去只会留下过期提示
        private const string TransientSessionDir = "waiting-input";

        public string SharedSessionDir(string root)
        {
            return Path.Combine(DataDir, "claude-switch-shared", root);
        }

        // 必须在 Claude 关闭时调用
        public void ShareSessionIndex(Store store, string backupDir)
        {
            if (string.IsNullOrEmpty(DataDir) || store == null) return;
            foreach (var root in SessionRoots)
            {
                var shared = SharedSessionDir(root);
                Directory.CreateDirectory(LP(shared));
                var orgDirs = SessionOrgDirs(store, root);

                try
                {
                    if (!string.IsNullOrEmpty(backupDir)) MergeSessions(shared, Path.Combine(backupDir, "session-index", root));
                }
                catch (Exception ex) { Log("备份会话列表失败：" + ex.Message); }

                // 1. 收集：把各账号目录合并进中转目录；遇到旧版留下的联接，只移除联接本身
                foreach (var orgDir in orgDirs)
                {
                    try
                    {
                        if (IsReparsePoint(orgDir))
                        {
                            Directory.Delete(LP(orgDir), false);
                            Log("已把目录联接换回真实目录：" + orgDir);
                        }
                        else if (Directory.Exists(LP(orgDir)))
                        {
                            MergeSessions(orgDir, shared);
                        }
                    }
                    catch (Exception ex) { Log("收集会话列表失败：" + orgDir + " : " + ex.Message); }
                }
                ApplyDeleteMarkers(shared, shared);

                // 2. 分发：每个账号目录都拿到完整的一份
                foreach (var orgDir in orgDirs)
                {
                    try
                    {
                        if (IsReparsePoint(orgDir)) { Log("目录联接没能移除，跳过：" + orgDir); continue; }
                        MergeSessions(shared, orgDir);
                        ApplyDeleteMarkers(shared, orgDir);
                    }
                    catch (Exception ex) { Log("同步会话列表失败：" + orgDir + " : " + ex.Message); }
                }
            }
        }

        private List<string> SessionOrgDirs(Store store, string root)
        {
            var result = new List<string>();
            foreach (var acc in store.Accounts)
            {
                if (string.IsNullOrEmpty(acc.AccountUuid)) continue;
                var accRoot = Path.Combine(DataDir, root, acc.AccountUuid);
                try { if (Directory.Exists(LP(accRoot))) result.AddRange(Directory.GetDirectories(accRoot)); }
                catch { }
                // 账号还没写过会话时目录不存在，也提前建好，第一次登录就能看到会话
                foreach (var org in new[] { acc.OrgUuid, GetInsight(acc, false).DesktopOrg })
                {
                    if (string.IsNullOrEmpty(org)) continue;
                    var known = Path.Combine(accRoot, org);
                    if (!result.Any(d => string.Equals(d, known, StringComparison.OrdinalIgnoreCase))) result.Add(known);
                }
            }
            return result;
        }

        private static void MergeSessions(string src, string dst)
        {
            Directory.CreateDirectory(LP(dst));
            foreach (var f in Directory.GetFiles(LP(src)))
            {
                var target = LP(Path.Combine(dst, Path.GetFileName(f)));
                if (!File.Exists(target) || File.GetLastWriteTimeUtc(f) > File.GetLastWriteTimeUtc(target))
                    CopyFileRetry(f, target);
            }
            foreach (var d in Directory.GetDirectories(LP(src)))
            {
                var name = Path.GetFileName(d);
                if (IsReparsePoint(d) || string.Equals(name, TransientSessionDir, StringComparison.OrdinalIgnoreCase)) continue;
                CopyTree(Path.Combine(src, name), Path.Combine(dst, name), true);
            }
        }

        // 在一个号上删掉的会话，不能被另一个号的副本同步回来
        private static void ApplyDeleteMarkers(string markersDir, string dir)
        {
            foreach (var marker in Directory.GetFiles(LP(markersDir), "deleted_*"))
            {
                var id = Path.GetFileName(marker).Substring("deleted_".Length);
                var session = LP(Path.Combine(dir, "local_" + id + ".json"));
                try { if (File.Exists(session)) File.Delete(session); } catch { }
            }
        }

        // 复制目录树（支持超过 260 字符的路径）。
        // onlyNewer=true 时为合并：dst 没有的直接拷入；两边都有的保留修改时间较新的那份。
        public static void CopyTree(string src, string dst, bool onlyNewer)
        {
            Directory.CreateDirectory(LP(dst));
            foreach (var f in Directory.GetFiles(LP(src)))
            {
                var target = LP(Path.Combine(dst, Path.GetFileName(f)));
                if (!onlyNewer || !File.Exists(target) || File.GetLastWriteTimeUtc(f) > File.GetLastWriteTimeUtc(target))
                    CopyFileRetry(f, target);
            }
            foreach (var d in Directory.GetDirectories(LP(src)))
            {
                if (IsReparsePoint(d)) continue;
                var name = Path.GetFileName(d);
                CopyTree(Path.Combine(src, name), Path.Combine(dst, name), onlyNewer);
            }
        }

        public static bool IsReparsePoint(string dir)
        {
            try
            {
                if (!Directory.Exists(LP(dir))) return false;
                return (File.GetAttributes(LP(dir)) & FileAttributes.ReparsePoint) != 0;
            }
            catch { return false; }
        }

        // ------------------------------------------------------------ email scrub

        public void ScrubEmails(string oldEmail, string newEmail, string backupDir)
        {
            var projects = Path.Combine(ClaudeDir, "projects");
            if (!Directory.Exists(projects)) return;
            var backupSessions = Path.Combine(backupDir, "sessions");
            int changed = 0;
            foreach (var f in SafeFiles(projects))
            {
                try
                {
                    var text = File.ReadAllText(LP(f), new UTF8Encoding(false));
                    if (text.IndexOf(oldEmail, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    var rel = f.Substring(projects.Length).TrimStart('\\');
                    var bk = Path.Combine(backupSessions, rel);
                    var bkDir = Path.GetDirectoryName(bk);
                    if (!Directory.Exists(bkDir)) Directory.CreateDirectory(LP(bkDir));
                    if (!File.Exists(bk)) File.Copy(LP(f), LP(bk), true);
                    var replaced = text.Replace(oldEmail, newEmail);
                    WriteAllTextAtomic(f, replaced);
                    changed++;
                }
                catch (Exception ex) { Log("清理会话失败：" + Path.GetFileName(f) + " : " + ex.Message); }
            }
            Log("清理旧账号邮箱：" + changed + " 个文件");
        }

        public static IEnumerable<string> SafeFiles(string dir)
        {
            return SafeFilesInner(dir, 0);
        }

        private static IEnumerable<string> SafeFilesInner(string dir, int depth)
        {
            if (depth > 16) yield break;
            string[] files;
            try { files = Directory.GetFiles(dir, "*.jsonl"); }
            catch { files = new string[0]; }
            foreach (var f in files) yield return f;
            string[] sub;
            try { sub = Directory.GetDirectories(dir); }
            catch { sub = new string[0]; }
            foreach (var d in sub)
            {
                try
                {
                    var attrs = File.GetAttributes(d);
                    if ((attrs & FileAttributes.ReparsePoint) != 0) continue;
                }
                catch { continue; }
                foreach (var f in SafeFilesInner(d, depth + 1)) yield return f;
            }
        }

        public static void Trace(string msg)
        {
            var line = DateTime.Now.ToString("HH:mm:ss.fff") + " " + msg + Environment.NewLine;
            try
            {
                var dir = Environment.GetEnvironmentVariable("CLAUDE_SWITCH_HOME");
                if (string.IsNullOrEmpty(dir))
                    dir = InstalledDataRoot;
                try
                {
                    Directory.CreateDirectory(dir);
                    File.AppendAllText(Path.Combine(dir, "trace.log"), line);
                }
                catch
                {
                    File.AppendAllText(Path.Combine(Path.GetTempPath(), "claude-relay-trace.log"), line);
                }
            }
            catch { }
        }

        // ------------------------------------------------------------ account insight

        // 账号卡片上显示的计划类型和用量
        public AccountInsight GetInsight(Account a, bool isCurrent)
        {
            var info = new AccountInsight();
            if (a == null) return info;
            var profile = ProfileDir(a);

            // 桌面端实际使用的组织：写在桌面端 config.json 里（accountUuid 与 orgUuid 成对出现）
            try
            {
                var cfgPath = isCurrent && !string.IsNullOrEmpty(DataDir)
                    ? Path.Combine(DataDir, "config.json")
                    : Path.Combine(profile, "desktop", "config.json");
                if (File.Exists(cfgPath) && !string.IsNullOrEmpty(a.AccountUuid))
                {
                    var m = System.Text.RegularExpressions.Regex.Match(File.ReadAllText(cfgPath, Encoding.UTF8),
                        "\"accountUuid\":\\s*\"" + System.Text.RegularExpressions.Regex.Escape(a.AccountUuid) + "\",\\s*\"orgUuid\":\\s*\"([^\"]+)\"");
                    if (m.Success) info.DesktopOrg = m.Groups[1].Value;
                }
            }
            catch { }

            // 计划类型：只有 CLI 身份和这个账号是同一个邮箱时才可信
            try
            {
                Dictionary<string, object> identity = null;
                if (isCurrent) identity = ReadIdentity();
                var idf = Path.Combine(profile, "identity.json");
                if ((identity == null || !SameEmail(IdentityEmail(identity), a.Email)) && File.Exists(idf))
                    identity = Js.DeserializeObject(File.ReadAllText(idf)) as Dictionary<string, object>;
                if (identity != null && SameEmail(IdentityEmail(identity), a.Email))
                {
                    object v;
                    if (identity.TryGetValue("organizationType", out v)) info.Plan = PlanName(Convert.ToString(v));
                }
            }
            catch { }

            // 用量：桌面端约每 15 分钟记一次，按组织区分；各账号快照里也各有一份，合在一起取最新
            if (!string.IsNullOrEmpty(info.DesktopOrg))
            {
                var files = new List<string>();
                if (!string.IsNullOrEmpty(DataDir)) files.Add(Path.Combine(DataDir, "plan-usage-history.json"));
                try
                {
                    if (Directory.Exists(ProfilesDir))
                        files.AddRange(Directory.GetDirectories(ProfilesDir).Select(d => Path.Combine(d, "desktop", "plan-usage-history.json")));
                }
                catch { }
                long bestT = -1;
                foreach (var f in files)
                {
                    try
                    {
                        if (!File.Exists(f)) continue;
                        var root = Js.DeserializeObject(File.ReadAllText(f)) as Dictionary<string, object>;
                        object samplesObj;
                        if (root == null || !root.TryGetValue("samples", out samplesObj)) continue;
                        var samples = samplesObj as object[];
                        if (samples == null) continue;
                        foreach (var so in samples)
                        {
                            var s = so as Dictionary<string, object>;
                            object org, t, u;
                            if (s == null || !s.TryGetValue("org", out org) || Convert.ToString(org) != info.DesktopOrg) continue;
                            if (!s.TryGetValue("u", out u)) continue;
                            var ud = u as Dictionary<string, object>;
                            if (ud == null || ud.Count == 0) continue;
                            info.HasUsageHistory = true;
                            if (!s.TryGetValue("t", out t)) continue;
                            var tv = Convert.ToInt64(t);
                            if (tv <= bestT) continue;
                            bestT = tv;
                            object fh, sd;
                            info.FiveHour = ud.TryGetValue("fh", out fh) ? (int?)Convert.ToInt32(fh) : null;
                            info.Week = ud.TryGetValue("sd", out sd) ? (int?)Convert.ToInt32(sd) : null;
                            info.UsageAt = DateTimeOffset.FromUnixTimeMilliseconds(tv).LocalDateTime;
                        }
                    }
                    catch { }
                }
            }

            // 计划未知时推测：有过用量记录说明是付费计划，从没有过则多半是 Free
            if (info.Plan == null && !string.IsNullOrEmpty(info.DesktopOrg))
            {
                info.Plan = info.HasUsageHistory ? "Pro" : "Free";
                info.PlanGuessed = true;
            }
            return info;
        }

        private static bool SameEmail(string a, string b)
        {
            return !string.IsNullOrEmpty(a) && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        private static string PlanName(string organizationType)
        {
            if (string.IsNullOrEmpty(organizationType)) return null;
            var t = organizationType.ToLowerInvariant();
            if (t.Contains("max")) return "Max";
            if (t.Contains("pro")) return "Pro";
            if (t.Contains("team")) return "Team";
            if (t.Contains("enterprise")) return "Enterprise";
            if (t.Contains("free")) return "Free";
            return null;
        }

        public int CountSharedSessions()
        {
            try
            {
                if (string.IsNullOrEmpty(DataDir)) return 0;
                var dir = SharedSessionDir("claude-code-sessions");
                return Directory.Exists(dir) ? Directory.GetFiles(dir, "local_*.json").Length : 0;
            }
            catch { return 0; }
        }

        // ------------------------------------------------------------ stats

        public int CountSessions()
        {
            var projects = Path.Combine(ClaudeDir, "projects");
            if (!Directory.Exists(projects)) return 0;
            int n = 0;
            foreach (var f in SafeFiles(projects)) n++;
            return n;
        }

        public int CountProjects()
        {
            try
            {
                var projects = Path.Combine(ClaudeDir, "projects");
                return Directory.Exists(projects) ? Directory.GetDirectories(projects).Length : 0;
            }
            catch { return 0; }
        }

        public int CountBackups()
        {
            try { return Directory.Exists(BackupsDir) ? Directory.GetDirectories(BackupsDir).Length : 0; }
            catch { return 0; }
        }
    }
}
