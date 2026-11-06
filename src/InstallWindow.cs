using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace ClaudeSwitch
{
    // 安装 / 卸载窗口：和主界面同一套浅色风格
    public class InstallWindow : Window
    {
        public enum Mode { Install, Uninstall }
        public enum Result { None, Installed, Portable, Uninstalled }

        public Result Outcome { get; private set; }

        private readonly Mode mode;
        private StackPanel body;
        private bool working;

        public InstallWindow(Mode mode)
        {
            this.mode = mode;
            Title = mode == Mode.Install ? "安装 " + Engine.AppName : "卸载 " + Engine.AppName;
            Width = 568;
            SizeToContent = SizeToContent.Height;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            UseLayoutRounding = true;
            ShowInTaskbar = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
            var winIcon = UI.WindowIcon();
            if (winIcon != null) Icon = winIcon;

            var frame = new Border
            {
                Margin = new Thickness(24), CornerRadius = new CornerRadius(14), Background = UI.Win,
                BorderBrush = new SolidColorBrush(Color.FromArgb(14, 0, 0, 0)), BorderThickness = new Thickness(1),
                Effect = UI.Shadow(40, 12, 0.16)
            };
            var dock = new DockPanel();
            var bar = TitleBar();
            DockPanel.SetDock(bar, Dock.Top);
            dock.Children.Add(bar);
            body = new StackPanel { Margin = new Thickness(36, 6, 36, 30) };
            dock.Children.Add(body);
            frame.Child = dock;
            Content = frame;

            KeyDown += (s, e) => { if (e.Key == Key.Escape && !working) Close(); };
            if (mode == Mode.Install) ShowInstallPage(); else ShowUninstallPage();
            ContentRendered += (s, e) => UI.BringToFront(this);
        }

        private FrameworkElement TitleBar()
        {
            var bar = new Grid { Height = 52, Background = Brushes.Transparent };
            var left = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(24, 0, 0, 0), IsHitTestVisible = false };
            left.Children.Add(MainWindow.Logo(20));
            var t = UI.Text(mode == Mode.Install ? "安装程序" : "卸载程序", 13, UI.Ink2);
            t.Margin = new Thickness(10, 0, 0, 0);
            left.Children.Add(t);
            bar.Children.Add(left);
            var close = new Border { Width = 34, Height = 30, CornerRadius = new CornerRadius(7), Background = Brushes.Transparent, Cursor = Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 10, 0) };
            close.Child = UI.Icon("", 10, UI.Ink2);
            close.MouseEnter += (s, e) => close.Background = UI.Hover;
            close.MouseLeave += (s, e) => close.Background = Brushes.Transparent;
            close.MouseLeftButtonUp += (s, e) => { if (!working) Close(); };
            bar.Children.Add(close);
            bar.MouseLeftButtonDown += (s, e) => { if (!close.IsMouseOver) { try { DragMove(); } catch { } } };
            return bar;
        }

        // ------------------------------------------------------------ install

        private string installDir = Installer.InstallDir;

        private void ShowInstallPage()
        {
            body.Children.Clear();
            var upgrading = Installer.IsInstalled;
            if (upgrading) installDir = Installer.InstallDir;

            body.Children.Add(Header(upgrading ? "更新 接力 Relay" : "安装 接力 Relay", "Claude 多账号切换，换号后会话接着用",
                upgrading && Installer.InstalledVersion != null ? "版本 " + ShortVer(Installer.InstalledVersion) + " → " + Installer.Version : "版本 " + Installer.Version));

            var feats = new StackPanel { Margin = new Thickness(0, 24, 0, 0) };
            feats.Children.Add(Feature("一键换号，Claude 自动重开，约 6 秒"));
            feats.Children.Add(Feature("会话列表在所有账号之间共用"));
            feats.Children.Add(Feature("切过去自动回到原来的会话，接着做"));
            body.Children.Add(feats);

            // 安装位置：没装过可以改；更新时装回原来的位置
            var loc = new Border { Background = UI.Card, BorderBrush = UI.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12),
                Padding = new Thickness(18, 14, 18, 14), Margin = new Thickness(0, 24, 0, 0) };
            var ls = new StackPanel();
            var lh = new Grid();
            lh.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            lh.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var left = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
            left.Children.Add(UI.Text(upgrading ? "已安装在" : "安装位置", 12, UI.Ink2));
            lh.Children.Add(left);
            var path = UI.Text(installDir, 13.5, UI.Ink);
            path.Margin = new Thickness(0, 6, 0, 0);
            path.TextWrapping = TextWrapping.Wrap;
            path.ToolTip = installDir;
            var err = UI.Text("", 12, UI.Danger);
            err.TextWrapping = TextWrapping.Wrap;
            err.Margin = new Thickness(0, 6, 0, 0);
            err.Visibility = Visibility.Collapsed;
            if (!upgrading)
            {
                var change = MainWindow.SecondaryButton("更改");
                change.Height = 30;
                change.Padding = new Thickness(12, 0, 12, 0);
                change.VerticalAlignment = VerticalAlignment.Top;
                change.MouseLeftButtonUp += (s, e) =>
                {
                    var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                    var picked = FolderPicker.Pick(hwnd, "选择接力的安装位置", System.IO.Path.GetDirectoryName(installDir));
                    if (string.IsNullOrEmpty(picked)) return;
                    installDir = Installer.NormalizeInstallDir(picked);
                    path.Text = installDir;
                    path.ToolTip = installDir;
                    var problem = Installer.CheckInstallDir(installDir);
                    err.Text = problem ?? "";
                    err.Visibility = problem == null ? Visibility.Collapsed : Visibility.Visible;
                };
                Grid.SetColumn(change, 1);
                lh.Children.Add(change);
            }
            left.Children.Add(path);
            ls.Children.Add(lh);
            ls.Children.Add(err);
            var dataNote = UI.Text("不需要管理员权限。账号数据另存在 " + Engine.InstalledDataRoot, 12, UI.Ink3);
            dataNote.TextWrapping = TextWrapping.Wrap;
            dataNote.Margin = new Thickness(0, 10, 0, 0);
            ls.Children.Add(dataNote);
            loc.Child = ls;
            body.Children.Add(loc);

            var legacy = Installer.FindLegacyData(Engine.InstalledDataRoot);
            if (legacy != null)
                body.Children.Add(Note("", "找到旧版的账号数据，会一起搬过来，原文件夹保留不动", UI.SageSoft, UI.B("#2A6A4D")));
            if (Installer.RunningInstalled().Count > 0)
                body.Children.Add(Note("", "已安装的接力正在运行，更新时会先把它关掉", UI.AmberSoft, UI.AmberInk));

            var opts = new StackPanel { Margin = new Thickness(2, 20, 0, 0) };
            Func<bool> desktop;
            opts.Children.Add(Check("在桌面创建快捷方式", true, out desktop));
            body.Children.Add(opts);

            // 底部
            var foot = new Grid { Margin = new Thickness(0, 28, 0, 0) };
            var portable = UI.Text("不安装，直接运行", 13, UI.Ink2);
            portable.Cursor = Cursors.Hand;
            portable.ToolTip = "在当前文件夹运行，数据放在旁边的 " + Engine.PortableDirName + " 文件夹";
            portable.MouseEnter += (s, e) => portable.TextDecorations = TextDecorations.Underline;
            portable.MouseLeave += (s, e) => portable.TextDecorations = null;
            portable.MouseLeftButtonUp += (s, e) => { Outcome = Result.Portable; Close(); };
            foot.Children.Add(portable);
            var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var cancel = MainWindow.GhostButtonBorder("取消");
            cancel.Height = 40;
            cancel.MouseLeftButtonUp += (s, e) => Close();
            btns.Children.Add(cancel);
            var go = MainWindow.PrimaryButton(upgrading ? "更新" : "安装");
            go.Height = 40;
            go.MinWidth = 96;
            go.Margin = new Thickness(8, 0, 0, 0);
            go.MouseLeftButtonUp += (s, e) =>
            {
                var problem = Installer.CheckInstallDir(installDir);
                if (problem != null) { err.Text = problem; err.Visibility = Visibility.Visible; return; }
                RunInstall(desktop());
            };
            btns.Children.Add(go);
            foot.Children.Add(btns);
            body.Children.Add(foot);
        }

        private void RunInstall(bool desktopShortcut)
        {
            working = true;
            body.Children.Clear();
            body.Children.Add(Header(Installer.IsInstalled ? "正在更新" : "正在安装", "马上就好", null));
            var steps = new Steps(new[] { "复制程序", "搬运账号数据", "创建快捷方式", "登记到“设置 → 应用”" });
            steps.Element.Margin = new Thickness(0, 24, 0, 8);
            body.Children.Add(steps.Element);

            Task.Run(() =>
            {
                Exception error = null;
                try
                {
                    Installer.Install(installDir, desktopShortcut, n =>
                    {
                        Dispatcher.Invoke(new Action(() => steps.Set(n)));
                        Thread.Sleep(220);
                    });
                }
                catch (Exception ex) { error = ex; }
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    working = false;
                    if (error != null) ShowError("安装没有完成", error.Message, ShowInstallPage);
                    else ShowInstallDone(desktopShortcut);
                }));
            });
        }

        private void ShowInstallDone(bool desktopShortcut)
        {
            Outcome = Result.Installed;
            body.Children.Clear();
            body.Children.Add(BigCheck());
            var h = UI.Text("安装完成", 24, UI.Ink, true);
            h.HorizontalAlignment = HorizontalAlignment.Center;
            h.Margin = new Thickness(0, 18, 0, 0);
            body.Children.Add(h);
            var d = UI.Text(desktopShortcut ? "已添加到开始菜单和桌面" : "已添加到开始菜单", 14, UI.Ink2);
            d.HorizontalAlignment = HorizontalAlignment.Center;
            d.Margin = new Thickness(0, 8, 0, 0);
            body.Children.Add(d);

            var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 30, 0, 6) };
            var done = MainWindow.SecondaryButton("完成");
            done.Height = 40;
            done.MinWidth = 96;
            done.MouseLeftButtonUp += (s, e) => Close();
            btns.Children.Add(done);
            var open = MainWindow.PrimaryButton("打开接力");
            open.Height = 40;
            open.MinWidth = 120;
            open.Margin = new Thickness(10, 0, 0, 0);
            open.MouseLeftButtonUp += (s, e) => { Installer.LaunchInstalled(); Close(); };
            btns.Children.Add(open);
            body.Children.Add(btns);
        }

        // ------------------------------------------------------------ uninstall

        private void ShowUninstallPage()
        {
            body.Children.Clear();
            body.Children.Add(Header("卸载 接力 Relay", "会移除程序，以及开始菜单和桌面上的快捷方式", null));

            var opts = new StackPanel { Margin = new Thickness(2, 24, 0, 0) };
            Func<bool> deleteData;
            opts.Children.Add(Check("同时删除保存的账号和备份", false, out deleteData));
            var sub = UI.Text("里面有各账号的登录凭证。不勾选就保留在 " + Engine.InstalledDataRoot, 12, UI.Ink3);
            sub.TextWrapping = TextWrapping.Wrap;
            sub.Margin = new Thickness(32, 4, 0, 0);
            opts.Children.Add(sub);
            body.Children.Add(opts);

            body.Children.Add(Note("", "各账号共用的会话列表在 Claude 自己的数据目录里，卸载不影响", UI.Soft, UI.Ink2));

            var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 28, 0, 0) };
            var cancel = MainWindow.GhostButtonBorder("取消");
            cancel.Height = 40;
            cancel.MouseLeftButtonUp += (s, e) => Close();
            btns.Children.Add(cancel);
            var go = MainWindow.PrimaryButton("卸载");
            go.Background = UI.Danger;
            go.Height = 40;
            go.MinWidth = 96;
            go.Margin = new Thickness(8, 0, 0, 0);
            go.MouseEnter += (s, e) => go.Background = UI.B("#9E3826");
            go.MouseLeave += (s, e) => go.Background = UI.Danger;
            go.MouseLeftButtonUp += (s, e) => RunUninstall(deleteData());
            btns.Children.Add(go);
            body.Children.Add(btns);
        }

        private void RunUninstall(bool deleteData)
        {
            working = true;
            body.Children.Clear();
            body.Children.Add(Header("正在卸载", "马上就好", null));
            Task.Run(() =>
            {
                Exception error = null;
                try { Installer.UninstallCore(deleteData); Thread.Sleep(400); }
                catch (Exception ex) { error = ex; }
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    working = false;
                    if (error != null) { ShowError("卸载没有完成", error.Message, ShowUninstallPage); return; }
                    Outcome = Result.Uninstalled;
                    body.Children.Clear();
                    body.Children.Add(BigCheck());
                    var h = UI.Text("已卸载", 24, UI.Ink, true);
                    h.HorizontalAlignment = HorizontalAlignment.Center;
                    h.Margin = new Thickness(0, 18, 0, 0);
                    body.Children.Add(h);
                    var d = UI.Text(deleteData ? "程序和保存的账号都已删除" : "账号数据还保留着，重新安装后可以直接用", 14, UI.Ink2);
                    d.HorizontalAlignment = HorizontalAlignment.Center;
                    d.Margin = new Thickness(0, 8, 0, 0);
                    body.Children.Add(d);
                    var close = MainWindow.PrimaryButton("关闭");
                    close.Height = 40;
                    close.MinWidth = 96;
                    close.HorizontalAlignment = HorizontalAlignment.Center;
                    close.Margin = new Thickness(0, 30, 0, 6);
                    close.MouseLeftButtonUp += (s, e) => Close();
                    body.Children.Add(close);
                }));
            });
        }

        // ------------------------------------------------------------ pieces

        private static string ShortVer(string v)
        {
            var parts = v.Split('.');
            return parts.Length >= 3 ? parts[0] + "." + parts[1] + "." + parts[2] : v;
        }

        private static FrameworkElement Header(string title, string sub, string version)
        {
            var g = new Grid { Margin = new Thickness(0, 10, 0, 0) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var logo = MainWindow.Logo(60);
            logo.VerticalAlignment = VerticalAlignment.Top;
            g.Children.Add(logo);
            var s = new StackPanel { Margin = new Thickness(18, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            s.Children.Add(UI.Text(title, 24, UI.Ink, true));
            var d = UI.Text(sub, 14, UI.Ink2);
            d.Margin = new Thickness(0, 6, 0, 0);
            d.TextWrapping = TextWrapping.Wrap;
            s.Children.Add(d);
            if (!string.IsNullOrEmpty(version))
            {
                var v = new Border { Background = UI.Hover, CornerRadius = new CornerRadius(10), Padding = new Thickness(9, 2, 9, 3),
                    Margin = new Thickness(0, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, Child = UI.Text(version, 12, UI.Ink2) };
                s.Children.Add(v);
            }
            Grid.SetColumn(s, 1);
            g.Children.Add(s);
            return g;
        }

        private static FrameworkElement Feature(string text)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 5, 0, 5) };
            var dot = new Grid { Width = 20, Height = 20 };
            dot.Children.Add(new Ellipse { Fill = UI.SageSoft });
            dot.Children.Add(CheckPath(UI.Sage, 20));
            row.Children.Add(dot);
            var t = UI.Text(text, 14, UI.Ink);
            t.Margin = new Thickness(12, 0, 0, 0);
            row.Children.Add(t);
            return row;
        }

        private static Path CheckPath(Brush stroke, double size)
        {
            var k = size / 22.0;
            return new Path
            {
                Data = Geometry.Parse(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "M{0},{1} L{2},{3} L{4},{5}", 6.4 * k, 11.4 * k, 9.4 * k, 14.4 * k, 15.6 * k, 7.8 * k)),
                Stroke = stroke, StrokeThickness = 1.8, StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round
            };
        }

        private static FrameworkElement Note(string glyph, string text, Brush bg, Brush fg)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var i = UI.Icon(glyph, 13, fg);
            i.VerticalAlignment = VerticalAlignment.Top;
            i.Margin = new Thickness(0, 3, 10, 0);
            g.Children.Add(i);
            var t = UI.Text(text, 13, fg);
            t.TextWrapping = TextWrapping.Wrap;
            t.LineHeight = 20;
            Grid.SetColumn(t, 1);
            g.Children.Add(t);
            return new Border { Background = bg, CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 12, 0, 0), Child = g };
        }

        // 自绘复选框：圆角方块 + 白色对勾
        private static FrameworkElement Check(string label, bool initial, out Func<bool> value)
        {
            var on = initial;
            var row = new StackPanel { Orientation = Orientation.Horizontal, Cursor = Cursors.Hand, Background = Brushes.Transparent, HorizontalAlignment = HorizontalAlignment.Left };
            var box = new Grid { Width = 20, Height = 20 };
            var bg = new Border { CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1.5) };
            var tick = CheckPath(Brushes.White, 20);
            box.Children.Add(bg);
            box.Children.Add(tick);
            Action paint = () =>
            {
                bg.Background = on ? UI.Ink : UI.Card;
                bg.BorderBrush = on ? UI.Ink : UI.Line2;
                tick.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            };
            paint();
            row.Children.Add(box);
            var t = UI.Text(label, 14, UI.Ink);
            t.Margin = new Thickness(12, 0, 0, 0);
            row.Children.Add(t);
            row.MouseLeftButtonUp += (s, e) => { on = !on; paint(); };
            value = () => on;
            return row;
        }

        private static FrameworkElement BigCheck()
        {
            var g = new Grid { Width = 64, Height = 64, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 24, 0, 0) };
            g.Children.Add(new Ellipse { Fill = UI.SageSoft });
            var inner = new Grid { Width = 44, Height = 44 };
            inner.Children.Add(new Ellipse { Fill = UI.Sage });
            var p = CheckPath(Brushes.White, 44);
            p.StrokeThickness = 3;
            inner.Children.Add(p);
            g.Children.Add(inner);
            return g;
        }

        private void ShowError(string title, string message, Action back)
        {
            body.Children.Clear();
            var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            var badge = new Border { Width = 36, Height = 36, CornerRadius = new CornerRadius(18), Background = UI.DangerSoft, Child = UI.Icon("", 15, UI.Danger) };
            head.Children.Add(badge);
            var t = UI.Text(title, 20, UI.Ink, true);
            t.Margin = new Thickness(12, 0, 0, 0);
            head.Children.Add(t);
            body.Children.Add(head);
            var m = UI.Text(message, 13.5, UI.Ink2);
            m.TextWrapping = TextWrapping.Wrap;
            m.LineHeight = 22;
            m.Margin = new Thickness(0, 14, 0, 0);
            body.Children.Add(m);
            var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 26, 0, 0) };
            var close = MainWindow.GhostButtonBorder("关闭");
            close.Height = 40;
            close.MouseLeftButtonUp += (s, e) => Close();
            btns.Children.Add(close);
            var retry = MainWindow.PrimaryButton("返回重试");
            retry.Height = 40;
            retry.Margin = new Thickness(8, 0, 0, 0);
            retry.MouseLeftButtonUp += (s, e) => back();
            btns.Children.Add(retry);
            body.Children.Add(btns);
        }

        // 带勾选和转圈的步骤列表
        private class Steps
        {
            public readonly StackPanel Element = new StackPanel();
            private readonly List<Grid> marks = new List<Grid>();
            private readonly List<TextBlock> texts = new List<TextBlock>();

            public Steps(string[] names)
            {
                foreach (var n in names)
                {
                    var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 8) };
                    var m = new Grid { Width = 20, Height = 20 };
                    row.Children.Add(m);
                    var t = UI.Text(n, 14, UI.Ink3);
                    t.Margin = new Thickness(12, 0, 0, 0);
                    row.Children.Add(t);
                    Element.Children.Add(row);
                    marks.Add(m);
                    texts.Add(t);
                }
                Set(0);
            }

            public void Set(int current)
            {
                for (int i = 0; i < marks.Count; i++)
                {
                    var m = marks[i];
                    m.Children.Clear();
                    if (i + 1 < current)
                    {
                        m.Children.Add(new Ellipse { Fill = UI.Sage });
                        m.Children.Add(CheckPath(Brushes.White, 20));
                        texts[i].Foreground = UI.Ink2;
                        texts[i].FontWeight = FontWeights.Normal;
                    }
                    else if (i + 1 == current)
                    {
                        var arc = new Ellipse { Stroke = UI.Clay, StrokeThickness = 1.8, StrokeDashArray = new DoubleCollection { 6, 3 }, RenderTransformOrigin = new Point(0.5, 0.5) };
                        var rot = new RotateTransform();
                        arc.RenderTransform = rot;
                        rot.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1)) { RepeatBehavior = RepeatBehavior.Forever });
                        m.Children.Add(arc);
                        texts[i].Foreground = UI.Ink;
                        texts[i].FontWeight = FontWeights.SemiBold;
                    }
                    else
                    {
                        m.Children.Add(new Ellipse { Stroke = UI.Line2, StrokeThickness = 1.5 });
                        texts[i].Foreground = UI.Ink3;
                        texts[i].FontWeight = FontWeights.Normal;
                    }
                }
            }
        }
    }
}
