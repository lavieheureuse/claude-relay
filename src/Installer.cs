using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using Microsoft.Win32;

namespace ClaudeSwitch
{
    // 自带安装 / 卸载：下载的 exe 双击后可以选择安装到本机，或直接在当前文件夹便携运行。
    // 安装位置：%LOCALAPPDATA%\Programs\ClaudeRelay\ClaudeRelay.exe（不需要管理员权限）
    // 数据位置：安装版 %LOCALAPPDATA%\ClaudeRelay；便携版 exe 旁边的 ClaudeRelay-data
    public static class Installer
    {
        public const string ExeName = "ClaudeRelay.exe";
        private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\ClaudeRelay";

        private static string LocalAppData
        {
            get { return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData); }
        }

        public static string DefaultInstallDir { get { return Path.Combine(LocalAppData, "Programs", Engine.AppId); } }

        // 本次安装选定的位置（安装过程中优先用它）
        private static string installTarget;

        // 已安装的位置记在“设置 → 应用”的卸载信息里（InstallLocation）；没装过时用默认位置
        public static string InstallDir
        {
            get
            {
                if (!string.IsNullOrEmpty(installTarget)) return installTarget;
                try
                {
                    using (var k = Registry.CurrentUser.OpenSubKey(UninstallKey))
                    {
                        var v = k == null ? null : k.GetValue("InstallLocation") as string;
                        if (!string.IsNullOrEmpty(v) && File.Exists(Path.Combine(v, ExeName))) return v;
                    }
                }
                catch { }
                return DefaultInstallDir;
            }
        }

        public static string InstalledExe { get { return Path.Combine(InstallDir, ExeName); } }

        // 用户选的文件夹不叫 ClaudeRelay 时，在里面建一个 ClaudeRelay 子文件夹
        public static string NormalizeInstallDir(string picked)
        {
            if (string.IsNullOrEmpty(picked)) return DefaultInstallDir;
            picked = Path.GetFullPath(picked);
            var root = Path.GetPathRoot(picked);
            if (string.Equals(picked.TrimEnd('\\'), root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                return Path.Combine(root, Engine.AppId); // 选了盘符根目录
            picked = picked.TrimEnd('\\');
            return string.Equals(Path.GetFileName(picked), Engine.AppId, StringComparison.OrdinalIgnoreCase)
                ? picked : Path.Combine(picked, Engine.AppId);
        }

        // 检查能不能装到这里；不能时返回原因
        public static string CheckInstallDir(string dir)
        {
            try
            {
                var probeDir = dir;
                while (!Directory.Exists(probeDir))
                {
                    var parent = Path.GetDirectoryName(probeDir);
                    if (string.IsNullOrEmpty(parent)) return "这个位置不存在";
                    probeDir = parent;
                }
                var probe = Path.Combine(probeDir, ".claude-relay-write-test-" + Guid.NewGuid().ToString("N"));
                File.WriteAllText(probe, "ok");
                File.Delete(probe);
                return null;
            }
            catch (UnauthorizedAccessException) { return "这个位置需要管理员权限，换一个文件夹，比如 D:\\Apps"; }
            catch (Exception ex) { return "没法写入这个位置：" + ex.Message; }
        }
        public static string CurrentExe { get { return Assembly.GetExecutingAssembly().Location; } }

        private static string ShortcutName { get { return Engine.AppName + ".lnk"; } }
        private static string StartMenuShortcut
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), ShortcutName); }
        }
        private static string DesktopShortcut
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), ShortcutName); }
        }

        public static bool IsRunningInstalled()
        {
            try
            {
                return string.Equals(Path.GetFullPath(CurrentExe), Path.GetFullPath(InstalledExe), StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        private static string PortableDir
        {
            get { var d = Engine.ExeDir; return string.IsNullOrEmpty(d) ? null : Path.Combine(d, Engine.PortableDirName); }
        }

        // 返回 false 表示本次启动到此结束（已交给安装好的程序，或用户取消）
        public static bool FirstRunChoice()
        {
            if (IsRunningInstalled()) return true;
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CLAUDE_RELAY_HOME")) ||
                !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CLAUDE_SWITCH_HOME"))) return true;
            var portable = PortableDir;
            if (portable != null && Directory.Exists(portable)) return true;

            var w = new InstallWindow(InstallWindow.Mode.Install);
            w.ShowDialog();
            if (w.Outcome == InstallWindow.Result.Portable)
            {
                Directory.CreateDirectory(portable);
                MigrateLegacy(portable);
                return true;
            }
            return false;
        }

        public static bool IsInstalled { get { return File.Exists(InstalledExe); } }

        public static string Version
        {
            get { var v = Assembly.GetExecutingAssembly().GetName().Version; return v.Major + "." + v.Minor + "." + v.Build; }
        }

        public static string InstalledVersion
        {
            get
            {
                try { return IsInstalled ? FileVersionInfo.GetVersionInfo(InstalledExe).FileVersion : null; }
                catch { return null; }
            }
        }

        // 已安装的那份程序是否在运行（更新时要先关掉，否则 exe 被占用）
        public static List<Process> RunningInstalled()
        {
            return Process.GetProcessesByName(Path.GetFileNameWithoutExtension(ExeName))
                .Where(p => p.Id != Process.GetCurrentProcess().Id && SamePath(SafePath(p), InstalledExe)).ToList();
        }

        // 安装步骤，step 回调参数为 1..4：复制程序、迁移数据、创建快捷方式、登记卸载信息
        public static void Install(string dir, bool desktopShortcut, Action<int> step)
        {
            foreach (var p in RunningInstalled()) { try { p.Kill(); p.WaitForExit(5000); } catch { } }
            installTarget = dir;

            step(1);
            Directory.CreateDirectory(InstallDir);
            CopyExe();

            step(2);
            Directory.CreateDirectory(Engine.InstalledDataRoot);
            MigrateLegacy(Engine.InstalledDataRoot);

            step(3);
            CreateShortcut(StartMenuShortcut);
            if (desktopShortcut) CreateShortcut(DesktopShortcut);
            else TryDelete(DesktopShortcut);

            step(4);
            RegisterUninstall();
        }

        // 覆盖安装位置的 exe。旧 exe 还在运行、被杀毒软件占着或是只读时，直接覆盖会“访问被拒绝”；
        // 这时先把旧文件改名挪开（运行中的 exe 允许改名），再放新的进去，旧文件下次安装时清理
        private static void CopyExe()
        {
            foreach (var old in Directory.GetFiles(InstallDir, ExeName + ".old-*"))
                TryDelete(old);
            if (File.Exists(InstalledExe))
            {
                try { File.SetAttributes(InstalledExe, FileAttributes.Normal); } catch { }
            }
            Exception last = null;
            for (int i = 0; i < 3; i++)
            {
                try { File.Copy(CurrentExe, InstalledExe, true); return; }
                catch (Exception ex)
                {
                    if (!(ex is IOException || ex is UnauthorizedAccessException)) throw;
                    last = ex;
                    try
                    {
                        if (File.Exists(InstalledExe))
                            File.Move(InstalledExe, InstalledExe + ".old-" + DateTime.Now.Ticks);
                    }
                    catch { }
                    System.Threading.Thread.Sleep(400);
                }
            }
            throw new Exception("没能写入 " + InstalledExe + "\n\n可能是接力还在运行，或被杀毒软件拦截了。请在任务管理器里结束 ClaudeRelay 后重试。\n\n系统信息：" + last.Message);
        }

        public static void LaunchInstalled()
        {
            try { Process.Start("explorer.exe", "\"" + InstalledExe + "\""); } catch { }
        }

        // 有没有旧版（ClaudeSwitch）的数据可以搬过来
        public static string FindLegacyData(string target)
        {
            if (File.Exists(Path.Combine(target, "config.json"))) return null;
            var candidates = new[]
            {
                Engine.ExeDir == null ? null : Path.Combine(Engine.ExeDir, Engine.PortableDirName),
                Engine.ExeDir == null ? null : Path.Combine(Engine.ExeDir, "data"),
                Path.Combine(LocalAppData, "ClaudeSwitch"),
            };
            foreach (var c in candidates)
            {
                if (string.IsNullOrEmpty(c) || SamePath(c, target)) continue;
                if (File.Exists(Path.Combine(c, "config.json")) && Directory.Exists(Path.Combine(c, "profiles"))) return c;
            }
            return null;
        }

        // 把旧版（ClaudeSwitch）的数据搬过来：只在新目录还没有账号数据时复制，旧目录保留不动
        public static void MigrateLegacy(string target)
        {
            var c = FindLegacyData(target);
            if (c != null)
            {
                // 逐项复制，跳过旧版运行时锁住的 instance.lock
                foreach (var f in Directory.GetFiles(c))
                {
                    var n = Path.GetFileName(f);
                    if (n.Equals("instance.lock", StringComparison.OrdinalIgnoreCase)) continue;
                    try { File.Copy(f, Path.Combine(target, n), true); } catch { }
                }
                foreach (var d in Directory.GetDirectories(c))
                    Engine.CopyTree(d, Path.Combine(target, Path.GetFileName(d)), false);
                try
                {
                    File.AppendAllText(Path.Combine(target, "migrated-from.txt"),
                        DateTime.Now.ToString("s") + " " + c + Environment.NewLine);
                }
                catch { }
                return;
            }
        }

        public static void Uninstall()
        {
            var dir = InstallDir; // 卸载信息删掉后就查不到了，先记下来
            var w = new InstallWindow(InstallWindow.Mode.Uninstall);
            w.ShowDialog();
            if (w.Outcome == InstallWindow.Result.Uninstalled) ScheduleRemoveInstallDir(dir);
        }

        public static void UninstallCore(bool deleteData)
        {
            foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(ExeName)))
            {
                if (p.Id == Process.GetCurrentProcess().Id) continue;
                try { p.Kill(); p.WaitForExit(5000); } catch { }
            }
            TryDelete(StartMenuShortcut);
            TryDelete(DesktopShortcut);
            try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false); } catch { }
            if (deleteData && Directory.Exists(Engine.InstalledDataRoot)) Engine.TryDeleteDir(Engine.InstalledDataRoot);
        }

        // 程序自己正在运行，退出后再删。只删接力自己的 exe，文件夹空了才删文件夹（不整个递归删除，免得误删用户放在那里的东西）
        private static void ScheduleRemoveInstallDir(string dir)
        {
            if (!Directory.Exists(dir)) return;
            var exe = Path.Combine(dir, ExeName);
            var psi = new ProcessStartInfo("cmd.exe",
                "/c ping 127.0.0.1 -n 3 >nul & del /f /q \"" + exe + "\" \"" + exe + ".old-*\" & rmdir \"" + dir + "\"")
            { CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden };
            try { Process.Start(psi); } catch { }
        }

        private static void RegisterUninstall()
        {
            using (var k = Registry.CurrentUser.CreateSubKey(UninstallKey))
            {

                k.SetValue("DisplayName", Engine.AppName);
                k.SetValue("DisplayVersion", Version);
                k.SetValue("Publisher", Engine.AppName);
                k.SetValue("DisplayIcon", InstalledExe + ",0");
                k.SetValue("InstallLocation", InstallDir);
                k.SetValue("UninstallString", "\"" + InstalledExe + "\" --uninstall");
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                k.SetValue("EstimatedSize", (int)Math.Max(1, new FileInfo(InstalledExe).Length / 1024), RegistryValueKind.DWord);
            }
        }

        private static void CreateShortcut(string lnkPath)
        {
            try
            {
                var shellType = Type.GetTypeFromProgID("WScript.Shell");
                var shell = Activator.CreateInstance(shellType);
                var lnk = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { lnkPath });
                var t = lnk.GetType();
                t.InvokeMember("TargetPath", BindingFlags.SetProperty, null, lnk, new object[] { InstalledExe });
                t.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, lnk, new object[] { InstallDir });
                t.InvokeMember("IconLocation", BindingFlags.SetProperty, null, lnk, new object[] { InstalledExe + ",0" });
                t.InvokeMember("Description", BindingFlags.SetProperty, null, lnk, new object[] { "Claude 多账号切换，会话接着用" });
                t.InvokeMember("Save", BindingFlags.InvokeMethod, null, lnk, null);
            }
            catch (Exception ex) { Engine.Trace("create shortcut failed: " + lnkPath + " : " + ex.Message); }
        }

        private static string SafePath(Process p)
        {
            try { return p.MainModule.FileName; } catch { return null; }
        }

        private static bool SamePath(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            try { return string.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }
}
