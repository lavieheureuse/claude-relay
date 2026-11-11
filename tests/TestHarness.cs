using System;
using System.IO;
using System.Text;
using ClaudeSwitch;

public class TestHarness
{
    static int fails = 0;

    static void Check(string name, bool ok)
    {
        Console.WriteLine((ok ? "PASS " : "FAIL ") + name);
        if (!ok) fails++;
    }

    static void Write(string path, string text)
    {
        var dir = Path.GetDirectoryName(path);
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, text, new UTF8Encoding(false));
    }

    static void BuildData(string dataDir, string uuid, string shortId)
    {
        Directory.CreateDirectory(dataDir);
        Directory.CreateDirectory(Path.Combine(dataDir, "Network"));
        Directory.CreateDirectory(Path.Combine(dataDir, "Local Storage"));
        Write(Path.Combine(dataDir, "config.json"), "{\"lastKnownAccountUuid\":\"" + uuid + "\",\"numStartups\":3}");
        Write(Path.Combine(dataDir, "Network", "Cookies"), "cookies-" + shortId);
        Write(Path.Combine(dataDir, "Network", "Cookies-journal"), "");
        Write(Path.Combine(dataDir, "Local Storage", "LOCK"), "");
        Write(Path.Combine(dataDir, "Local Storage", "000001.ldb"), "ls-" + shortId);
        Write(Path.Combine(dataDir, "ant-did"), Convert.ToBase64String(Encoding.UTF8.GetBytes(shortId)));
        Write(Path.Combine(dataDir, "ant-device-registry.json"), "{\"" + uuid + "\":\"pk1:dev\"}");
        Write(Path.Combine(dataDir, "plan-usage-history.json"), "{\"version\":1}");
        Write(Path.Combine(dataDir, "buddy-tokens.json"), "{}");
        var rec = Path.Combine(dataDir, "claude-code-sessions", uuid, "org-1");
        Write(Path.Combine(rec, "local_" + shortId + ".json"), "{\"sessionId\":\"local_" + shortId + "\",\"cwd\":\"D:\\\\proj\"}");
        Write(Path.Combine(rec, "archived-sessions.idx"), "{\"v\":1,\"archived\":[]}");
    }

    static void SetIdentity(string home, string email, string org)
    {
        Write(Path.Combine(home, ".claude.json"),
            "{\"oauthAccount\":{\"emailAddress\":\"" + email + "\",\"organizationUuid\":\"" + org + "\"},\"projects\":{},\"numStartups\":3}");
    }

    static string ReadUuid(string dataDir)
    {
        var t = File.ReadAllText(Path.Combine(dataDir, "config.json"), Encoding.UTF8);
        var i = t.IndexOf("lastKnownAccountUuid", StringComparison.Ordinal);
        var s = t.IndexOf('"', t.IndexOf(':', i)) + 1;
        return t.Substring(s, t.IndexOf('"', s) - s);
    }

    static string ReadEmail(string home)
    {
        var t = File.ReadAllText(Path.Combine(home, ".claude.json"), Encoding.UTF8);
        var i = t.IndexOf("emailAddress", StringComparison.Ordinal);
        var s = t.IndexOf('"', t.IndexOf(':', i)) + 1;
        return t.Substring(s, t.IndexOf('"', s) - s);
    }

    static Account Find(Store s, string uuid)
    {
        foreach (var a in s.Accounts)
            if (string.Equals(a.AccountUuid, uuid, StringComparison.OrdinalIgnoreCase)) return a;
        return null;
    }

    public static int Main()
    {
        try
        {
            return Run();
        }
        catch (Exception ex)
        {
            Console.WriteLine("EXCEPTION " + ex.GetType().FullName + " :: " + ex.Message);
            return 2;
        }
    }

    static int Run()
    {
        Console.WriteLine("step: start");
        var root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "run-" + Guid.NewGuid().ToString("N"));
        var home = Path.Combine(root, "home");
        var store = Path.Combine(root, "store");
        var data = Path.Combine(root, "data");
        var uuidA = "aaaa1111-1111-2222-3333-444444444444";
        var uuidB = "bbbb2222-1111-2222-3333-444444444444";
        var org = "org-1";

        Directory.CreateDirectory(home);
        Directory.CreateDirectory(Path.Combine(home, ".claude", "projects", "D--proj"));

        Environment.SetEnvironmentVariable("CLAUDE_SWITCH_TEST", "1");
        Environment.SetEnvironmentVariable("CLAUDE_SWITCH_CLAUDE_HOME", home);
        Environment.SetEnvironmentVariable("CLAUDE_SWITCH_HOME", store);
        Environment.SetEnvironmentVariable("CLAUDE_SWITCH_DATA_DIR", data);

        // 账号 A 的当前状态
        BuildData(data, uuidA, "a");
        SetIdentity(home, "a@test.com", org);
        Write(Path.Combine(home, ".claude", ".credentials.json"), "{\"tok\":\"A\"}");
        var sessionFile = Path.Combine(home, ".claude", "projects", "D--proj", "s1.jsonl");
        Write(sessionFile, "{\"type\":\"attachment\",\"context\":{\"userEmail\":\"a@test.com\"}}\n");

        Console.WriteLine("step: save A");
        var e1 = new Engine();
        var profA = e1.SaveCurrent("A", "a@test.com");
        Check("save A", profA != null && profA.AccountUuid == uuidA);

        // 账号 B 的当前状态
        BuildData(data, uuidB, "b");
        SetIdentity(home, "b@test.com", org);
        Write(Path.Combine(home, ".claude", ".credentials.json"), "{\"tok\":\"B\"}");
        Console.WriteLine("step: save B");
        var e2 = new Engine();
        var profB = e2.SaveCurrent("B", "b@test.com");
        Check("save B", profB != null && profB.AccountUuid == uuidB);

        // B -> A
        Console.WriteLine("step: switch B->A");
        var e3 = new Engine();
        var r1 = e3.Switch(Find(e3.LoadStore(), uuidA), null);
        Check("switch B->A ok", r1.Ok && !r1.Already);
        Check("desktop uuid now A", ReadUuid(data) == uuidA);
        Check("claude identity now A", ReadEmail(home) == "a@test.com");
        Check("claude credentials now A", File.ReadAllText(Path.Combine(home, ".claude", ".credentials.json")).Contains("A"));
        Check("transcript email A", File.ReadAllText(sessionFile).Contains("a@test.com"));
        Check("session index copied to A", Directory.Exists(Path.Combine(data, "claude-code-sessions", uuidA, org)));
        Check("backup created", Directory.GetDirectories(Path.Combine(store, "backups")).Length > 0);

        // A -> B
        Console.WriteLine("step: switch A->B");
        var e4 = new Engine();
        var r2 = e4.Switch(Find(e4.LoadStore(), uuidB), null);
        Check("switch A->B ok", r2.Ok && !r2.Already);
        Check("desktop uuid now B", ReadUuid(data) == uuidB);
        Check("claude identity now B", ReadEmail(home) == "b@test.com");
        Check("claude credentials now B", File.ReadAllText(Path.Combine(home, ".claude", ".credentials.json")).Contains("B"));
        Check("transcript email B", File.ReadAllText(sessionFile).Contains("b@test.com"));
        Check("no old email left", !File.ReadAllText(sessionFile).Contains("a@test.com"));
        Check("session index copied to B", Directory.Exists(Path.Combine(data, "claude-code-sessions", uuidB, org)));

        // 会话列表共享：两个账号的目录都是联接，且指向同一份数据
        var dirA = Path.Combine(data, "claude-code-sessions", uuidA, org);
        var dirB = Path.Combine(data, "claude-code-sessions", uuidB, org);
        // 桌面端拒绝往联接里写会话，这两个目录必须是真实目录
        Check("A session dir is a real directory", Directory.Exists(dirA) && !Engine.IsReparsePoint(dirA));
        Check("B session dir is a real directory", Directory.Exists(dirB) && !Engine.IsReparsePoint(dirB));
        Check("B has A's session", File.Exists(Path.Combine(dirB, "local_a.json")));
        Check("A has B's session", File.Exists(Path.Combine(dirA, "local_b.json")));
        Check("session index backed up", Directory.GetDirectories(Path.Combine(store, "backups"), "session-index", SearchOption.AllDirectories).Length > 0);

        // 在 B 上新建的会话、改动的会话，同步后 A 能看到；A 上的旧副本不会盖掉 B 的新内容
        var eSync = new Engine();
        var syncStore = eSync.LoadStore();
        Write(Path.Combine(dirB, "local_newOnB.json"), "{\"sessionId\":\"local_newOnB\"}");
        Write(Path.Combine(dirB, "local_a.json"), "{\"edited\":\"onB\"}");
        File.SetLastWriteTimeUtc(Path.Combine(dirB, "local_a.json"), DateTime.UtcNow.AddMinutes(5));
        eSync.ShareSessionIndex(syncStore, null);
        Check("session created on B visible on A", File.Exists(Path.Combine(dirA, "local_newOnB.json")));
        Check("newer edit wins", File.ReadAllText(Path.Combine(dirA, "local_a.json")).Contains("onB"));

        // 在 B 上删除的会话不会被 A 的副本同步回来
        File.Delete(Path.Combine(dirB, "local_newOnB.json"));
        Write(Path.Combine(dirB, "deleted_newOnB"), "1");
        eSync.ShareSessionIndex(syncStore, null);
        Check("deleted session stays deleted on B", !File.Exists(Path.Combine(dirB, "local_newOnB.json")));
        Check("deleted session removed from A", !File.Exists(Path.Combine(dirA, "local_newOnB.json")));

        // 运行状态不同步
        Write(Path.Combine(dirB, "waiting-input", "local_x.json"), "{}");
        eSync.ShareSessionIndex(syncStore, null);
        Check("waiting-input not synced", !File.Exists(Path.Combine(dirA, "waiting-input", "local_x.json")));

        // 从 1.0.0 升级：联接换回真实目录，中转目录里的会话不丢
        var sharedDir = Path.Combine(data, "claude-switch-shared", "claude-code-sessions");
        Directory.Delete(@"\\?\" + Path.GetFullPath(dirA), true);
        var mk = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", "/c mklink /J \"" + dirA + "\" \"" + sharedDir + "\"") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true });
        mk.WaitForExit();
        var shortEnough = Engine.IsReparsePoint(dirA); // 测试目录太深时 mklink 会失败，此时跳过这组检查
        if (shortEnough)
        {
            eSync.ShareSessionIndex(syncStore, null);
            Check("junction converted to real directory", Directory.Exists(dirA) && !Engine.IsReparsePoint(dirA));
            Check("sessions kept after conversion", File.Exists(Path.Combine(dirA, "local_b.json")) && File.Exists(Path.Combine(sharedDir, "local_b.json")));
        }
        else
        {
            Console.WriteLine("SKIP junction conversion (path too long for mklink)");
            eSync.ShareSessionIndex(syncStore, null);
        }

        // 从桌面端日志补回 Remote Control 对应关系，列表才不会多出重复条目
        var localX = "local_11111111-1111-1111-1111-111111111111";
        var cliOld = "22222222-2222-2222-2222-222222222222";
        var cliNew = "33333333-3333-3333-3333-333333333333";
        var localA = "local_44444444-4444-4444-4444-444444444444";
        var localB = "local_55555555-5555-5555-5555-555555555555";
        var imported = Path.Combine(dirB, "local_" + cliNew + ".json");
        var archivedOld = Path.Combine(dirB, "local_" + cliOld + ".json");
        var hasIds = Path.Combine(dirB, localA + ".json");
        var ambiguous = Path.Combine(dirB, localB + ".json");
        Write(imported, "{\"sessionId\":\"local_" + cliNew + "\",\"cliSessionId\":\"" + cliNew + "\",\"lastActivityAt\":5}");
        Write(archivedOld, "{\"sessionId\":\"local_" + cliOld + "\",\"cliSessionId\":\"" + cliOld + "\",\"isArchived\":true,\"lastActivityAt\":9}");
        Write(hasIds, "{\"sessionId\":\"" + localA + "\",\"bridgeSessionIds\":[\"session_01newest\"],\"lastActivityAt\":3}");
        Write(ambiguous, "{\"sessionId\":\"" + localB + "\",\"lastActivityAt\":2}");
        Write(Path.Combine(data, "logs", "main.log"),
            "x [info] Mapping internal session " + localX + " to CLI session " + cliOld + "\n" +
            "x [info] Mapping internal session " + localX + " to CLI session " + cliNew + "\n" +
            "x [info] Enabling remote control for session " + localX + "\n" +
            "x [info] Remote control enabled: \"https://claude.ai/code/session_01first\"\n" +
            "x [info] Enabling remote control for session " + localX + "\n" +
            "x [info] Remote control enabled: \"https://claude.ai/code/session_01second\"\n" +
            "x [info] Enabling remote control for session " + localA + "\n" +
            "x [info] Remote control enabled: \"https://claude.ai/code/session_01older\"\n" +
            "x [info] Enabling remote control for session " + localA + "\n" +
            "x [info] Enabling remote control for session " + localB + "\n" +
            "x [info] Remote control enabled: \"https://claude.ai/code/session_01mixed\"\n");
        eSync.ShareSessionIndex(syncStore, null);
        var repaired = File.ReadAllText(Path.Combine(dirA, "local_" + cliNew + ".json"));
        Check("bridge ids linked to live session via CLI mapping", repaired.Contains("session_01first") && repaired.Contains("session_01second"));
        Check("archived branch left untouched", !File.ReadAllText(Path.Combine(dirA, "local_" + cliOld + ".json")).Contains("session_01first"));
        var withIds = File.ReadAllText(Path.Combine(dirA, localA + ".json"));
        Check("newest bridge id stays last", withIds.IndexOf("session_01older") >= 0 && withIds.IndexOf("session_01older") < withIds.IndexOf("session_01newest"));
        Check("ambiguous log lines not paired", !File.ReadAllText(Path.Combine(dirA, localA + ".json")).Contains("session_01mixed") && !File.ReadAllText(Path.Combine(dirA, localB + ".json")).Contains("session_01mixed"));
        eSync.ShareSessionIndex(syncStore, null);
        var again = File.ReadAllText(Path.Combine(dirA, "local_" + cliNew + ".json"));
        Check("repair is idempotent", again.Split(new[] { "session_01first" }, StringSplitOptions.None).Length == 2);
        // 在一个号上删除/归档的会话，要带到拥有云端记录的账号
        var delLocal = "local_66666666-6666-6666-6666-666666666666";
        var liveLocal = "local_77777777-7777-7777-7777-777777777777";
        var archLocal = "local_88888888-8888-8888-8888-888888888888";
        Write(Path.Combine(dirA, delLocal + ".json"), "{\"sessionId\":\"" + delLocal + "\",\"bridgeSessionIds\":[\"session_01todelete\"]}");
        Write(Path.Combine(dirB, liveLocal + ".json"), "{\"sessionId\":\"" + liveLocal + "\",\"bridgeSessionIds\":[\"cse_01stilllive\"]}");
        Write(Path.Combine(dirB, archLocal + ".json"), "{\"sessionId\":\"" + archLocal + "\",\"isArchived\":true,\"bridgeSessionIds\":[\"session_01archived\"]}");
        Write(Path.Combine(dirB, "deleted_" + delLocal.Substring(6)), "1");
        Write(Path.Combine(data, "logs", "main.log"),
            "x [info] Enabling remote control for session " + delLocal + "\n" +
            "x [info] Remote control enabled: \"https://claude.ai/code/session_01fromlog\"\n" +
            "x [info] Enabling remote control for session " + liveLocal + "\n" +
            "x [info] Remote control enabled: \"https://claude.ai/code/session_01stilllive\"\n");
        eSync.ShareSessionIndex(syncStore, null);
        Func<string, string> owed = id => { using (var h = System.Security.Cryptography.SHA256.Create()) { var sb = new StringBuilder("owed-bridge-delete_"); foreach (var x in h.ComputeHash(Encoding.UTF8.GetBytes(id))) sb.Append(x.ToString("x2")); return sb.ToString(); } };
        var noteA = Path.Combine(dirA, owed("session_01todelete"));
        Check("deleted session's cloud copy queued on other account", File.Exists(noteA) && File.Exists(Path.Combine(dirB, owed("session_01todelete"))));
        Check("id found only in logs is queued too", File.Exists(Path.Combine(dirA, owed("session_01fromlog"))));
        var noteText = File.Exists(noteA) ? File.ReadAllText(noteA) : "";
        Check("note matches desktop format", noteText.Contains("\"version\":3") && noteText.Contains("\"state\":\"owed\"") && noteText.Contains("\"action\":\"delete\"") && noteText.Contains("\"remoteId\":\"session_01todelete\"") && noteText.Contains("\"sessionId\":\"" + delLocal + "\""));
        Check("live session never queued", !File.Exists(Path.Combine(dirA, owed("session_01stilllive"))) && !File.Exists(Path.Combine(dirA, owed("cse_01stilllive"))));
        var archNote = Path.Combine(dirA, owed("session_01archived"));
        Check("archived session queued as archive", File.Exists(archNote) && File.ReadAllText(archNote).Contains("\"action\":\"archive\""));
        Check("notes not copied to transit dir", !File.Exists(Path.Combine(sharedDir, owed("session_01todelete"))));
        File.Delete(noteA);
        eSync.ShareSessionIndex(syncStore, null);
        Check("handled note not queued again", !File.Exists(noteA));
        foreach (var d in new[] { dirA, dirB, sharedDir })
        {
            foreach (var f in Directory.GetFiles(d, "owed-bridge-delete_*")) File.Delete(f);
            foreach (var n in new[] { liveLocal, archLocal, delLocal })
            {
                var p = Path.Combine(d, n + ".json");
                if (File.Exists(p)) File.Delete(p);
                p = Path.Combine(d, "deleted_" + n.Substring(6));
                if (File.Exists(p)) File.Delete(p);
            }
        }
        foreach (var f in new[] { imported, archivedOld, hasIds, ambiguous })
            foreach (var d in new[] { dirA, dirB, sharedDir })
            {
                var p = Path.Combine(d, Path.GetFileName(f));
                if (File.Exists(p)) File.Delete(p);
            }

        // 故障场景 1：Cookies 被占用时，切换应安全失败，不破坏当前状态
        Console.WriteLine("step: failure - cookies locked");
        var lockPath = Path.Combine(data, "Network", "Cookies");
        bool failedOnLock = false;
        using (var fs = new FileStream(lockPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var e6 = new Engine();
            try { e6.Switch(Find(e6.LoadStore(), uuidA), null); }
            catch { failedOnLock = true; }
        }
        Check("switch fails safely when cookies locked", failedOnLock);
        Check("state unchanged after locked failure", ReadUuid(data) == uuidB && ReadEmail(home) == "b@test.com");

        // 故障场景 2：目标快照损坏时，应自动回滚到切换前状态
        Console.WriteLine("step: failure - corrupt snapshot + rollback");
        var e7 = new Engine();
        var targetA = Find(e7.LoadStore(), uuidA);
        var profDir = Path.Combine(store, "profiles", targetA.Id);
        Directory.Delete(Path.Combine(profDir, "desktop"), true);
        bool failedOnCorrupt = false;
        try { e7.Switch(targetA, null); }
        catch { failedOnCorrupt = true; }
        Check("switch fails on corrupt snapshot", failedOnCorrupt);
        Check("rollback kept account B", ReadUuid(data) == uuidB && ReadEmail(home) == "b@test.com");

        // 故障测试后重建 A 快照、回到 B 状态，再做最后一遍切换
        BuildData(data, uuidA, "a");
        SetIdentity(home, "a@test.com", org);
        Write(Path.Combine(home, ".claude", ".credentials.json"), "{\"tok\":\"A\"}");
        var eFix = new Engine();
        eFix.SaveCurrent("A", "a@test.com");
        BuildData(data, uuidB, "b");
        SetIdentity(home, "b@test.com", org);
        Write(Path.Combine(home, ".claude", ".credentials.json"), "{\"tok\":\"B\"}");

        // B -> A 再来一次（可重复性）
        Console.WriteLine("step: switch B->A again");
        var e5 = new Engine();
        var r3 = e5.Switch(Find(e5.LoadStore(), uuidA), null);
        Check("switch B->A again ok", r3.Ok);
        Check("desktop uuid A again", ReadUuid(data) == uuidA);
        Check("identity A again", ReadEmail(home) == "a@test.com");

        // 版本比较和“被挪动”的判断
        Check("newer version detected", Installer.IsNewerVersion("1.0.2", "1.0.1.0"));
        Check("same version is not newer", !Installer.IsNewerVersion("1.0.1", "1.0.1.0"));
        Check("older version is not newer", !Installer.IsNewerVersion("1.0.0", "1.0.1.0"));
        Check("unreadable installed version allows update", Installer.IsNewerVersion("1.0.1", null));
        Check("moved exe detected", Installer.LooksMoved(@"D:\a\ClaudeRelay", false, @"D:\b"));
        Check("normal install not moved", !Installer.LooksMoved(@"D:\a\ClaudeRelay", true, @"D:\b"));
        Check("running from registered dir not moved", !Installer.LooksMoved(@"D:\a\ClaudeRelay", false, @"D:\a\ClaudeRelay\"));
        Check("never installed not moved", !Installer.LooksMoved(null, false, @"D:\b"));

        Console.WriteLine();
        Console.WriteLine(fails == 0 ? "ALL PASS" : (fails + " FAILURES"));
        try { Directory.Delete(@"\\?\" + Path.GetFullPath(root), true); } catch { }
        return fails == 0 ? 0 : 1;
    }
}
