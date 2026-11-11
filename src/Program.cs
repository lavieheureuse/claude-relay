using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;

[assembly: AssemblyTitle("接力 Relay")]
[assembly: AssemblyProduct("接力 Relay")]
[assembly: AssemblyDescription("Claude 多账号切换，会话接着用")]
[assembly: AssemblyVersion("1.0.1.0")]
[assembly: AssemblyFileVersion("1.0.1.0")]

namespace ClaudeSwitch
{
    public class App : Application
    {
        [STAThread]
        public static void Main(string[] args)
        {
            if (args.Length == 3 && string.Equals(args[0], "--finish-uninstall", StringComparison.OrdinalIgnoreCase))
            {
                Installer.FinishUninstall(args[1], args[2]);
                return;
            }

            Engine.Trace("main start");

            if (args.Any(a => string.Equals(a, "--uninstall", StringComparison.OrdinalIgnoreCase)))
            {
                Installer.Uninstall();
                return;
            }

            // 如果是从 Claude 里（例如 Claude Code 的终端）启动的，本进程属于 Claude 的进程树，
            // 切号时关闭 Claude 会把本程序一起结束。这种情况下交给资源管理器重新启动自己，然后退出。
            if (!Engine.TestMode && (Environment.GetEnvironmentVariable("CLAUDECODE") == "1" ||
                !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CLAUDE_CODE_ENTRYPOINT"))))
            {
                try
                {
                    var exe = System.Reflection.Assembly.GetExecutingAssembly().Location;
                    Engine.Trace("launched inside Claude, relaunching via explorer");
                    System.Diagnostics.Process.Start("explorer.exe", "\"" + exe + "\"");
                    return;
                }
                catch (Exception ex) { Engine.Trace("relaunch failed: " + ex.Message); }
            }

            // 第一次双击下载的 exe：问一次要安装还是直接在当前文件夹运行
            if (!Engine.TestMode && !Installer.FirstRunChoice()) return;

            var logDir = Engine.ResolveHome();
            try
            {
                Directory.CreateDirectory(logDir);
                var eng = new Engine();

                string writeError;
                // 没装 Claude 时 DataDir 为空，窗口里会提示去安装；找到了但写不进去才报错
                if (!Engine.TestMode && !string.IsNullOrEmpty(eng.DataDir) && !eng.CanWriteDataDir(out writeError))
                {
                    MessageBox.Show(
                        "无法写入 Claude 数据目录：\n" + eng.DataDir + "\n\n系统错误：" + writeError +
                        "\n\n请将此错误信息提供给开发者，以便排查。",
                        "接力 Relay - 数据目录不可写");
                    return;
                }

                using (var lockFile = new FileStream(Path.Combine(eng.StoreRoot, "instance.lock"),
                    FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
                {
                    var app = new App();
                    app.DispatcherUnhandledException += (s, e) =>
                    {
                        try
                        {
                            File.AppendAllText(Path.Combine(eng.StoreRoot, "crash.log"),
                                "[" + DateTime.Now.ToString("s") + "] " + e.Exception + Environment.NewLine);
                        }
                        catch { }
                        MessageBox.Show(e.Exception.Message, "接力 Relay - 出错");
                        e.Handled = true;
                    };
                    app.ShutdownMode = ShutdownMode.OnMainWindowClose;
                    Engine.Trace("creating window");
                    app.Run(new MainWindow(eng));
                }
            }
            catch (IOException)
            {
                MessageBox.Show("接力 Relay 已经在运行了。", "提示");
            }
            catch (Exception ex)
            {
                try
                {
                    File.AppendAllText(Path.Combine(logDir, "crash.log"),
                        "[" + DateTime.Now.ToString("s") + "] STARTUP " + ex + Environment.NewLine);
                }
                catch { }
                MessageBox.Show(ex.Message, "接力 Relay - 启动失败");
            }
        }
    }
}
