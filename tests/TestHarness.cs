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
        Check("A session dir is junction", Engine.IsReparsePoint(dirA));
        Check("B session dir is junction", Engine.IsReparsePoint(dirB));
        Check("shared has A's session", File.Exists(Path.Combine(dirB, "local_a.json")));
        Check("shared has B's session", File.Exists(Path.Combine(dirA, "local_b.json")));
        Write(Path.Combine(dirB, "local_newOnB.json"), "{\"sessionId\":\"local_newOnB\"}");
        Check("session created on B visible on A", File.Exists(Path.Combine(dirA, "local_newOnB.json")));
        Check("original dirs backed up", Directory.GetDirectories(Path.Combine(store, "backups"), "session-index", SearchOption.AllDirectories).Length > 0);

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

        Console.WriteLine();
        Console.WriteLine(fails == 0 ? "ALL PASS" : (fails + " FAILURES"));
        try { Directory.Delete(@"\\?\" + Path.GetFullPath(root), true); } catch { }
        return fails == 0 ? 0 : 1;
    }
}
