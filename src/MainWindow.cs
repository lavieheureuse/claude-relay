using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using WinForms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace ClaudeSwitch
{
    // 浅色主题，对应 design/relay-ui.html
    public static class UI
    {
        public static SolidColorBrush B(string hex)
        {
            var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            b.Freeze();
            return b;
        }

        public static readonly FontFamily Font = new FontFamily("Segoe UI Variable Text, Segoe UI, Microsoft YaHei UI");
        public static readonly FontFamily Icons = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");

        public static readonly SolidColorBrush Win = B("#FBFAF7");
        public static readonly SolidColorBrush Card = B("#FFFFFF");
        public static readonly SolidColorBrush Line = B("#E7E4DC");
        public static readonly SolidColorBrush Line2 = B("#D9D5CB");
        public static readonly SolidColorBrush Soft = B("#F7F6F2");
        public static readonly SolidColorBrush Track = B("#E9E6DF");
        public static readonly SolidColorBrush Hover = B("#F1EFEA");

        public static readonly SolidColorBrush Ink = B("#1D1C1A");
        public static readonly SolidColorBrush Ink2 = B("#6B675F");
        public static readonly SolidColorBrush Ink3 = B("#A29D93");

        public static readonly SolidColorBrush Clay = B("#D9734E");
        public static readonly SolidColorBrush ClaySoft = B("#FBEDE6");
        public static readonly SolidColorBrush ClayInk = B("#9A4424");
        public static readonly SolidColorBrush Sage = B("#3F8F6B");
        public static readonly SolidColorBrush SageSoft = B("#E7F3EC");
        public static readonly SolidColorBrush AmberSoft = B("#FBF1DC");
        public static readonly SolidColorBrush AmberInk = B("#8A5A0B");
        public static readonly SolidColorBrush VioletSoft = B("#EEEAFB");
        public static readonly SolidColorBrush VioletInk = B("#4B3C99");
        public static readonly SolidColorBrush Danger = B("#B8442F");
        public static readonly SolidColorBrush DangerSoft = B("#FBE9E5");

        // 账号头像的配色，按顺序轮换
        public static readonly SolidColorBrush[][] AvatarColors =
        {
            new[] { ClaySoft, ClayInk },
            new[] { B("#E6F0FA"), B("#1F5689") },
            new[] { SageSoft, B("#2A6A4D") },
            new[] { VioletSoft, VioletInk },
            new[] { AmberSoft, AmberInk },
        };

        public static DropShadowEffect Shadow(double blur, double depth, double opacity)
        {
            return new DropShadowEffect { BlurRadius = blur, ShadowDepth = depth, Direction = 270, Opacity = opacity, Color = Color.FromRgb(30, 25, 15) };
        }

        // 内嵌的多尺寸图标（build.ps1 用 /resource 放进去）
        public static Drawing.Icon AppIcon(int size)
        {
            try
            {
                using (var s = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("ClaudeRelay.app.ico"))
                    return s == null ? null : new Drawing.Icon(s, size, size);
            }
            catch { return null; }
        }

        // 窗口图标：任务栏和 Alt+Tab 用，带全部尺寸让系统自己挑
        public static ImageSource WindowIcon()
        {
            try
            {
                using (var s = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("ClaudeRelay.app.ico"))
                {
                    if (s == null) return null;
                    var d = new System.Windows.Media.Imaging.IconBitmapDecoder(s, System.Windows.Media.Imaging.BitmapCreateOptions.None, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
                    return d.Frames.OrderByDescending(f => f.PixelWidth).FirstOrDefault();
                }
            }
            catch { return null; }
        }

        // 窗口刚打开时提到最前面：从别的程序里点开时，新窗口容易被压在后面看不见
        public static void BringToFront(Window w)
        {
            try
            {
                if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
                w.Topmost = true;
                w.Activate();
                w.Topmost = false;
                w.Focus();
            }
            catch { }
        }

        public static TextBlock Text(string text, double size, Brush fg, bool bold = false)
        {
            return new TextBlock
            {
                Text = text, FontFamily = Font, FontSize = size, Foreground = fg,
                FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        public static TextBlock Icon(string glyph, double size, Brush fg)
        {
            return new TextBlock
            {
                Text = glyph, FontFamily = Icons, FontSize = size, Foreground = fg,
                VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center
            };
        }
    }

    public class MainWindow : Window
    {
        private readonly Engine eng;
        private Store store;
        private StackPanel content;
        private Grid overlayHost;
        private Grid rootHost;
        private bool busy;
        private WinForms.NotifyIcon tray;
        private string previousAccountId;
        private DispatcherTimer toastTimer;

        public MainWindow() : this(new Engine()) { }

        public MainWindow(Engine engine)
        {
            eng = engine;
            Engine.Trace("MainWindow ctor");
            store = eng.LoadStore();
            BuildWindow();
            RefreshAll();
            InitTray();
            // 用量由 Claude 约每 15 分钟记一次；每分钟看一眼，有新记录就刷新
            var usageTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
            usageTimer.Tick += (s, e) => { if (!busy && IsVisible && overlayHost.Visibility != Visibility.Visible) RefreshAll(); };
            usageTimer.Start();
            Engine.Trace("window ready");
            if (Environment.GetEnvironmentVariable("CLAUDE_SWITCH_DEMO") == "1" && store.Accounts.Count > 0)
                Dispatcher.BeginInvoke(new Action(() => RunDemoSwitch(store.Accounts[store.Accounts.Count - 1])));
        }

        private static readonly string[] SwitchSteps = { "关闭 Claude", "备份当前状态", "换上账号登录", "同步会话列表", "启动 Claude", "回到原来的会话" };

        private void RunDemoSwitch(Account a)
        {
            ShowSwitchOverlay(a, null);
            var n = 0;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
            timer.Tick += (s, e) =>
            {
                n++;
                UpdateSwitchProgress(new SwitchProgress { Step = Math.Min(n, SwitchSteps.Length), Total = SwitchSteps.Length });
                if (n > SwitchSteps.Length)
                {
                    timer.Stop();
                    HideOverlay();
                    ShowToast("已换到「" + a.Name + "」，回到了上次的会话", "按 Ctrl V 发送“继续”，它会接着做", "切回", () => { });
                }
            };
            timer.Start();
        }

        // ------------------------------------------------------------ window chrome

        private void BuildWindow()
        {
            Title = Engine.AppName;
            var winIcon = UI.WindowIcon();
            if (winIcon != null) Icon = winIcon;
            Width = 568;
            SizeToContent = SizeToContent.Height;
            MaxHeight = SystemParameters.WorkArea.Height - 40;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            UseLayoutRounding = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);

            rootHost = new Grid();
            var frame = new Border
            {
                Margin = new Thickness(24),
                CornerRadius = new CornerRadius(14),
                Background = UI.Win,
                BorderBrush = new SolidColorBrush(Color.FromArgb(14, 0, 0, 0)),
                BorderThickness = new Thickness(1),
                Effect = UI.Shadow(40, 12, 0.16)
            };
            var inner = new Grid { ClipToBounds = true };
            var dock = new DockPanel { LastChildFill = true };

            var bar = BuildTitleBar();
            DockPanel.SetDock(bar, Dock.Top);
            dock.Children.Add(bar);

            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            content = new StackPanel { Margin = new Thickness(28, 8, 28, 28) };
            scroll.Content = content;
            dock.Children.Add(scroll);
            inner.Children.Add(dock);

            overlayHost = new Grid
            {
                Background = new SolidColorBrush(Color.FromArgb(0xE6, 0xFB, 0xFA, 0xF7)),
                Visibility = Visibility.Collapsed,
                Margin = new Thickness(0, 56, 0, 0)
            };
            inner.Children.Add(overlayHost);

            frame.Child = inner;
            rootHost.Children.Add(frame);
            Content = rootHost;

            KeyDown += (s, e) => { if (e.Key == Key.Escape && overlayHost.Visibility != Visibility.Visible) Close(); };
            ContentRendered += (s, e) => UI.BringToFront(this);
        }

        private FrameworkElement BuildTitleBar()
        {
            var bar = new Grid { Height = 56, Background = Brushes.Transparent };
            var left = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(26, 0, 0, 0), IsHitTestVisible = false };
            left.Children.Add(Logo(24));
            var name = UI.Text("接力", 15, UI.Ink, true);
            name.Margin = new Thickness(10, 0, 0, 0);
            left.Children.Add(name);
            var en = UI.Text("Relay", 12, UI.Ink3);
            en.Margin = new Thickness(5, 1, 0, 0);
            left.Children.Add(en);
            var ver = UI.Text("v" + Installer.Version, 11, UI.Ink3);
            ver.Margin = new Thickness(6, 2, 0, 0);
            left.Children.Add(ver);
            bar.Children.Add(left);

            var ctl = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 8, 0) };
            ctl.Children.Add(ChromeButton("", "最小化", () => WindowState = WindowState.Minimized));
            ctl.Children.Add(ChromeButton("", "关闭", () => Close()));
            bar.Children.Add(ctl);

            bar.MouseLeftButtonDown += (s, e) => { if (!ctl.IsMouseOver) { try { DragMove(); } catch { } } };
            return bar;
        }

        // 品牌标：陶土色圆角方块，里面两道“接力棒”
        internal static FrameworkElement Logo(double size)
        {
            var g = new Grid { Width = size, Height = size };
            g.Children.Add(new Border { CornerRadius = new CornerRadius(size * 0.32), Background = UI.Clay });
            var k = size / 22.0;
            g.Children.Add(new Border
            {
                Width = 12 * k, Height = 3 * k, CornerRadius = new CornerRadius(1.5 * k), Background = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(5 * k, 7 * k, 0, 0)
            });
            g.Children.Add(new Border
            {
                Width = 10 * k, Height = 3 * k, CornerRadius = new CornerRadius(1.5 * k),
                Background = new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)),
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(6 * k, 12 * k, 0, 0)
            });
            g.VerticalAlignment = VerticalAlignment.Center;
            return g;
        }

        private FrameworkElement ChromeButton(string glyph, string label, Action action)
        {
            var bd = new Border { Width = 34, Height = 30, CornerRadius = new CornerRadius(7), Background = Brushes.Transparent, Cursor = Cursors.Hand };
            bd.Child = UI.Icon(glyph, 10, UI.Ink2);
            bd.MouseEnter += (s, e) => bd.Background = UI.Hover;
            bd.MouseLeave += (s, e) => bd.Background = Brushes.Transparent;
            bd.MouseLeftButtonUp += (s, e) => action();
            System.Windows.Automation.AutomationProperties.SetName(bd, label);
            return bd;
        }

        // ------------------------------------------------------------ main content

        private void RefreshAll()
        {
            store = eng.LoadStore();
            content.Children.Clear();

            if (string.IsNullOrEmpty(eng.DataDir) && !eng.Redetect())
            {
                content.Children.Add(BuildNoClaude());
                RefreshTrayMenu();
                return;
            }

            var currentUuid = eng.GetDesktopUuid();
            var current = eng.FindByUuid(store, currentUuid);

            // 存的号不到两个，或者当前登录的号还没存且只存了一个：显示引导
            if (store.Accounts.Count == 0 || (store.Accounts.Count == 1 && (current == null || current.Id == store.Accounts[0].Id)))
            {
                content.Children.Add(BuildGuide(current));
                RefreshTrayMenu();
                return;
            }

            content.Children.Add(current != null ? BuildHero(current) : BuildUnsavedHero());

            var others = store.Accounts.Where(a => current == null || a.Id != current.Id).ToList();
            var sec = new Grid { Margin = new Thickness(4, 30, 4, 12) };
            sec.Children.Add(UI.Text("切换到", 13, UI.Ink2));
            content.Children.Add(sec);

            var list = new Border { Background = UI.Card, BorderBrush = UI.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14) };
            var rows = new StackPanel();
            foreach (var a in others) rows.Children.Add(BuildAccountRow(a));
            rows.Children.Add(BuildAddRow(others.Count > 0));
            list.Child = rows;
            content.Children.Add(list);

            content.Children.Add(BuildFooter());
            RefreshTrayMenu();
        }

        private int ColorIndex(Account a)
        {
            var i = store.Accounts.FindIndex(x => x.Id == a.Id);
            return Math.Max(0, i) % UI.AvatarColors.Length;
        }

        private FrameworkElement Avatar(Account a, double size = 48)
        {
            var c = UI.AvatarColors[ColorIndex(a)];
            return new Border
            {
                Width = size, Height = size, CornerRadius = new CornerRadius(size * 0.3), Background = c[0],
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = Initial(a), FontFamily = UI.Font, FontSize = size * 0.38, FontWeight = FontWeights.SemiBold,
                    Foreground = c[1], HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                }
            };
        }

        private static string Initial(Account a)
        {
            var s = !string.IsNullOrEmpty(a.Name) ? a.Name : (a.Email ?? "?");
            return s.Substring(0, 1).ToUpperInvariant();
        }

        private static FrameworkElement Pill(string plan)
        {
            if (string.IsNullOrEmpty(plan)) return null;
            var free = plan == "Free";
            return new Border
            {
                CornerRadius = new CornerRadius(9), Padding = new Thickness(7, 1, 7, 2), Margin = new Thickness(7, 1, 0, 0),
                Background = free ? UI.Hover : UI.VioletSoft, VerticalAlignment = VerticalAlignment.Center,
                Child = UI.Text(plan, 11, free ? UI.Ink2 : UI.VioletInk)
            };
        }

        private FrameworkElement NameLine(Account a, string plan)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(UI.Text(a.Name, 17, UI.Ink, true));
            var p = Pill(plan);
            if (p != null) row.Children.Add(p);
            return row;
        }

        private FrameworkElement BuildHero(Account a)
        {
            var info = eng.GetInsight(a, true);
            var card = new Border
            {
                Background = UI.Card, BorderBrush = UI.Line, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14), Padding = new Thickness(22), Effect = UI.Shadow(18, 2, 0.06)
            };
            var stack = new StackPanel();

            var who = new Grid();
            who.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            who.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            who.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            who.Children.Add(Avatar(a));
            var meta = new StackPanel { Margin = new Thickness(16, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
            meta.Children.Add(NameLine(a, info.Plan));
            var mail = UI.Text(string.IsNullOrEmpty(a.Email) ? "未记录邮箱" : a.Email, 13, UI.Ink2);
            mail.Margin = new Thickness(0, 4, 0, 0);
            mail.TextTrimming = TextTrimming.CharacterEllipsis;
            meta.Children.Add(mail);
            Grid.SetColumn(meta, 1);
            who.Children.Add(meta);

            var running = eng.ClaudeRunning;
            var live = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var dotHost = new Grid { Width = 13, Height = 13, Margin = new Thickness(0, 0, 6, 0) };
            dotHost.Children.Add(new Ellipse { Width = 13, Height = 13, Fill = running ? UI.SageSoft : UI.Hover });
            dotHost.Children.Add(new Ellipse { Width = 7, Height = 7, Fill = running ? UI.Sage : UI.Ink3 });
            live.Children.Add(dotHost);
            live.Children.Add(UI.Text(running ? "使用中" : "Claude 未运行", 13, running ? UI.Sage : UI.Ink3));
            Grid.SetColumn(live, 2);
            who.Children.Add(live);
            stack.Children.Add(who);

            if (info.FiveHour.HasValue || info.Week.HasValue)
            {
                var meters = new Grid { Margin = new Thickness(0, 22, 0, 0) };
                meters.ColumnDefinitions.Add(new ColumnDefinition());
                meters.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
                meters.ColumnDefinitions.Add(new ColumnDefinition());
                var at = info.UsageAt.HasValue ? info.UsageAt.Value.ToString(info.UsageAt.Value.Date == DateTime.Today ? "HH:mm" : "MM-dd HH:mm") : "";
                var m1 = Meter("5 小时", info.FiveHour, at);
                meters.Children.Add(m1);
                var m2 = Meter("本周", info.Week, at);
                Grid.SetColumn(m2, 2);
                meters.Children.Add(m2);
                stack.Children.Add(meters);
            }
            else if (info.Plan == "Free")
            {
                stack.Children.Add(Warn("这个账号没有用量记录，可能是 Free 计划，用不了 Claude Code", new Thickness(0, 20, 0, 0)));
            }

            var shared = eng.CountSharedSessions();
            var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 20, 0, 0) };
            line.Children.Add(UI.Icon("", 13, shared > 0 ? UI.Sage : UI.Ink3));
            var tb = new TextBlock { FontFamily = UI.Font, FontSize = 13, Foreground = UI.Ink2, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            if (shared > 0)
            {
                tb.Inlines.Add(new Run(shared + " 个会话") { Foreground = UI.Ink, FontWeight = FontWeights.SemiBold });
                tb.Inlines.Add(new Run(" 在所有账号间共用，换号后接着做"));
            }
            else tb.Text = "第一次切换后，所有账号会共用同一份会话列表";
            line.Children.Add(tb);
            stack.Children.Add(line);

            card.Child = stack;
            return card;
        }

        private FrameworkElement Meter(string label, int? pct, string at)
        {
            var box = new Border { Background = UI.Soft, CornerRadius = new CornerRadius(10), Padding = new Thickness(16, 14, 16, 16) };
            var s = new StackPanel();
            var head = new Grid();
            head.Children.Add(UI.Text(label, 12, UI.Ink2));
            var t = UI.Text(at, 12, UI.Ink3);
            t.HorizontalAlignment = HorizontalAlignment.Right;
            head.Children.Add(t);
            s.Children.Add(head);
            var v = UI.Text(pct.HasValue ? pct.Value + "%" : "—", 24, UI.Ink, true);
            v.Margin = new Thickness(0, 4, 0, 10);
            s.Children.Add(v);
            var track = new Grid { Height = 5 };
            track.Children.Add(new Border { CornerRadius = new CornerRadius(2), Background = UI.Track });
            var value = Math.Max(0, Math.Min(100, pct ?? 0));
            var fill = new Grid();
            fill.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(value, GridUnitType.Star) });
            fill.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100 - value, GridUnitType.Star) });
            fill.Children.Add(new Border { CornerRadius = new CornerRadius(2), Background = value >= 90 ? UI.Danger : UI.Clay });
            track.Children.Add(fill);
            s.Children.Add(track);
            box.Child = s;
            return box;
        }

        private static FrameworkElement Warn(string text, Thickness margin)
        {
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var icon = UI.Icon("", 12, UI.AmberInk);
            icon.VerticalAlignment = VerticalAlignment.Top;
            icon.Margin = new Thickness(0, 2, 7, 0);
            row.Children.Add(icon);
            var t = UI.Text(text, 13, UI.AmberInk);
            t.TextWrapping = TextWrapping.Wrap;
            t.LineHeight = 18;
            Grid.SetColumn(t, 1);
            row.Children.Add(t);
            return new Border { Background = UI.AmberSoft, CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 10, 12, 10), Margin = margin, Child = row };
        }

        private FrameworkElement BuildUnsavedHero()
        {
            var email = eng.IdentityEmail(eng.ReadIdentity());
            var card = new Border
            {
                Background = UI.Card, BorderBrush = UI.Line, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14), Padding = new Thickness(22), Effect = UI.Shadow(18, 2, 0.06)
            };
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var s = new StackPanel();
            s.Children.Add(UI.Text("当前登录的账号还没保存", 14, UI.Ink, true));
            var sub = UI.Text(string.IsNullOrEmpty(email) ? "保存后就能在账号之间一键切换" : email, 12, UI.Ink2);
            sub.Margin = new Thickness(0, 3, 0, 0);
            s.Children.Add(sub);
            g.Children.Add(s);
            var btn = PrimaryButton("保存");
            btn.MouseLeftButtonUp += (o, e) => OpenSaveDialog();
            Grid.SetColumn(btn, 1);
            g.Children.Add(btn);
            card.Child = g;
            return card;
        }

        private FrameworkElement BuildAccountRow(Account a)
        {
            var info = eng.GetInsight(a, false);
            var outer = new StackPanel();
            var row = new Grid { Margin = new Thickness(20, 18, 20, 18) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(Avatar(a));
            var meta = new StackPanel { Margin = new Thickness(16, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
            meta.Children.Add(NameLine(a, info.Plan));
            var sub = UI.Text(SubLine(a, info), 13, UI.Ink2);
            sub.Margin = new Thickness(0, 4, 0, 0);
            sub.TextTrimming = TextTrimming.CharacterEllipsis;
            meta.Children.Add(sub);
            Grid.SetColumn(meta, 1);
            row.Children.Add(meta);
            var btn = PrimaryButton("切换");
            var acct = a;
            btn.MouseLeftButtonUp += (s, e) => StartSwitch(acct);
            Grid.SetColumn(btn, 2);
            row.Children.Add(btn);
            outer.Children.Add(row);

            if (info.Plan == "Free")
                outer.Children.Add(Warn("Free 计划用不了 Claude Code，切过去看不到这些会话", new Thickness(84, 0, 20, 18)));

            return new Border { BorderBrush = UI.Line, BorderThickness = new Thickness(0, 0, 0, 1), Child = outer };
        }

        private static string SubLine(Account a, AccountInsight info)
        {
            var parts = new List<string>();
            parts.Add(string.IsNullOrEmpty(a.Email) ? "未记录邮箱" : a.Email);
            if (info.Week.HasValue) parts.Add("本周 " + info.Week.Value + "%");
            return string.Join("  ·  ", parts);
        }

        private FrameworkElement BuildAddRow(bool hasOthers)
        {
            var row = new Border { Background = Brushes.Transparent, CornerRadius = new CornerRadius(0, 0, 14, 14), Cursor = Cursors.Hand, Padding = new Thickness(20, 16, 20, 16) };
            var g = new StackPanel { Orientation = Orientation.Horizontal };
            var plus = new Grid { Width = 48, Height = 48 };
            plus.Children.Add(new Rectangle { RadiusX = 14, RadiusY = 14, Stroke = UI.Line2, StrokeThickness = 1.5, StrokeDashArray = new DoubleCollection { 3, 2 } });
            plus.Children.Add(UI.Icon("", 13, UI.Ink3));
            g.Children.Add(plus);
            var s = new StackPanel { Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            s.Children.Add(UI.Text("添加另一个号", 14, UI.Ink2));
            if (!hasOthers)
            {
                var hint = UI.Text("Claude 会打开登录页，当前的号不会退出", 11.5, UI.Ink3);
                hint.Margin = new Thickness(0, 2, 0, 0);
                s.Children.Add(hint);
            }
            g.Children.Add(s);
            row.Child = g;
            row.MouseEnter += (o, e) => row.Background = UI.Soft;
            row.MouseLeave += (o, e) => row.Background = Brushes.Transparent;
            row.MouseLeftButtonUp += (o, e) => StartFreshLogin();
            return row;
        }

        private FrameworkElement BuildFooter()
        {
            var g = new Grid { Margin = new Thickness(4, 24, 0, 0) };
            g.Children.Add(UI.Text("切换约 6 秒，会重启 Claude", 13, UI.Ink3));
            var acts = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            acts.Children.Add(GhostButton("恢复备份", () => ConfirmRestore()));
            acts.Children.Add(GhostButton("日志", () => OpenLogs()));
            g.Children.Add(acts);
            return g;
        }

        // 引导页（对应 design/relay-empty.html）
        //   一个号都没存：第 1 步；已存一个号且当前登录就是它：第 2 步，等换号登录
        //   已存一个号、当前登录的是另一个没存的号：也是第 2 步，但直接给出「保存这个号」
        private FrameworkElement BuildGuide(Account current)
        {
            var saved = store.Accounts.FirstOrDefault();
            var stage = saved == null ? 1 : 2;
            var desktopUuid = eng.GetDesktopUuid();
            var unsavedLogin = !string.IsNullOrEmpty(desktopUuid) && current == null;
            var detectedEmail = DetectedEmail(desktopUuid);

            var s = new StackPanel { Margin = new Thickness(0, 4, 0, 4) };

            // 示意图：A → 接力棒 → B
            var art = new Border
            {
                Height = 176, CornerRadius = new CornerRadius(16), BorderBrush = UI.Line, BorderThickness = new Thickness(1),
                Background = new LinearGradientBrush(Color.FromRgb(0xF4, 0xF1, 0xEA), Color.FromRgb(0xF9, 0xF7, 0xF2), 90)
            };
            var artGrid = new Grid();
            var flow = new StackPanel
            {
                Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 0, stage == 1 ? 28 : 0)
            };
            if (stage == 1 || saved == null) flow.Children.Add(ArtAvatar("A", UI.ClaySoft, UI.ClayInk, false));
            else flow.Children.Add(ArtAvatar(Initial(saved), UI.ClaySoft, UI.ClayInk, false));
            var track = new Grid { Width = 160, Height = 64 };
            track.Children.Add(new Line
            {
                X1 = 6, X2 = 154, Y1 = 32, Y2 = 32, Stroke = UI.Line2, StrokeThickness = 1.5, StrokeDashArray = new DoubleCollection { 2, 2.6 }
            });
            var baton = Logo(36);
            baton.HorizontalAlignment = HorizontalAlignment.Center;
            baton.Effect = new DropShadowEffect { BlurRadius = 14, ShadowDepth = 4, Direction = 270, Opacity = 0.35, Color = Color.FromRgb(0xD9, 0x73, 0x4E) };
            track.Children.Add(baton);
            flow.Children.Add(track);
            flow.Children.Add(ArtAvatar(stage == 1 ? "B" : "?", UI.Card, UI.Ink3, true));
            artGrid.Children.Add(flow);
            if (stage == 1)
            {
                var chips = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 16) };
                foreach (var c in new[] { "登录状态", "会话列表", "回到原会话" })
                {
                    chips.Children.Add(new Border
                    {
                        Background = UI.Card, BorderBrush = UI.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10),
                        Padding = new Thickness(10, 2, 10, 3), Margin = new Thickness(4, 0, 4, 0), Child = UI.Text(c, 12, UI.Ink2)
                    });
                }
                artGrid.Children.Add(chips);
            }
            art.Child = artGrid;
            s.Children.Add(art);

            var h = UI.Text(stage == 1 ? "一个号用完，换个号接着干" : "再存一个号就能接力了", 24, UI.Ink, true);
            h.Margin = new Thickness(0, 28, 0, 10);
            s.Children.Add(h);
            var lead = UI.Text(stage == 1
                    ? "把你的几个 Claude 账号存下来，额度用完时点一下就换号。会话列表跟着走，切过去自动回到刚才那个会话。"
                    : "「" + saved.Name + "」已经存好。点下面的「登录另一个号」，Claude 会打开登录页，登录后回来点保存。", 14, UI.Ink2);
            lead.TextWrapping = TextWrapping.Wrap;
            lead.LineHeight = 25;
            s.Children.Add(lead);

            // 当前登录：能存就给按钮，否则给提示
            var canSave = stage == 1 ? !string.IsNullOrEmpty(desktopUuid) : unsavedLogin;
            if (canSave) s.Children.Add(DetectCard(detectedEmail));
            else s.Children.Add(WaitBox(stage == 1 ? null : (detectedEmail ?? saved.Email)));

            // 三步
            var steps = new Grid { Margin = new Thickness(0, 22, 0, 0) };
            for (int i = 0; i < 5; i++)
                steps.ColumnDefinitions.Add(i % 2 == 0 ? new ColumnDefinition() : new ColumnDefinition { Width = new GridLength(12) });
            var data = new[]
            {
                new[] { "存下当前号", stage == 1 ? "约 5 秒，Claude 会重启" : saved.Name },
                new[] { "换号再存", "打开登录页换号" },
                new[] { "一键切换", "之后随时互切" },
            };
            for (int i = 0; i < 3; i++)
            {
                var state = i + 1 < stage ? 2 : (i + 1 == stage ? 1 : 0); // 2 完成 1 当前 0 未到
                var card = GuideStep(i + 1, data[i][0], data[i][1], state);
                Grid.SetColumn(card, i * 2);
                steps.Children.Add(card);
            }
            s.Children.Add(steps);

            var note = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 22, 0, 0) };
            note.Children.Add(UI.Icon("", 11, UI.Ink3));
            var nt = UI.Text("只在这台电脑上保存登录状态，不存密码，不联网", 12.5, UI.Ink3);
            nt.Margin = new Thickness(6, 0, 0, 0);
            note.Children.Add(nt);
            s.Children.Add(note);
            return s;
        }

        // 这台电脑上没找到 Claude 桌面端
        private FrameworkElement BuildNoClaude()
        {
            var s = new StackPanel { Margin = new Thickness(0, 4, 0, 4) };
            var art = new Border
            {
                Height = 176, CornerRadius = new CornerRadius(16), BorderBrush = UI.Line, BorderThickness = new Thickness(1),
                Background = new LinearGradientBrush(Color.FromRgb(0xF4, 0xF1, 0xEA), Color.FromRgb(0xF9, 0xF7, 0xF2), 90)
            };
            var flow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var baton = Logo(56);
            flow.Children.Add(baton);
            var track = new Grid { Width = 110, Height = 64 };
            track.Children.Add(new Line { X1 = 8, X2 = 102, Y1 = 32, Y2 = 32, Stroke = UI.Line2, StrokeThickness = 1.5, StrokeDashArray = new DoubleCollection { 2, 2.6 } });
            flow.Children.Add(track);
            flow.Children.Add(ArtAvatar("?", UI.Card, UI.Ink3, true));
            art.Child = flow;
            s.Children.Add(art);

            var h = UI.Text("没找到 Claude 桌面端", 24, UI.Ink, true);
            h.Margin = new Thickness(0, 28, 0, 10);
            s.Children.Add(h);
            var lead = UI.Text("接力是给 Claude 桌面版（Windows）用的换号工具。先安装 Claude 并登录一个账号，再回来点「重新检测」。", 14, UI.Ink2);
            lead.TextWrapping = TextWrapping.Wrap;
            lead.LineHeight = 25;
            s.Children.Add(lead);

            var btns = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 28, 0, 0) };
            var dl = PrimaryButton("下载 Claude");
            dl.Height = 40;
            dl.MouseLeftButtonUp += (o, e) => { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://claude.ai/download") { UseShellExecute = true }); } catch { } };
            btns.Children.Add(dl);
            var again = SecondaryButton("重新检测");
            again.Height = 40;
            again.Margin = new Thickness(10, 0, 0, 0);
            again.MouseLeftButtonUp += (o, e) =>
            {
                if (eng.Redetect()) RefreshAll();
                else ShowToast("还是没找到 Claude", "装好并打开过一次 Claude 后再试", null, null);
            };
            btns.Children.Add(again);
            s.Children.Add(btns);

            var note = UI.Text("支持应用商店版和官网安装版的 Claude", 12.5, UI.Ink3);
            note.Margin = new Thickness(0, 22, 0, 0);
            s.Children.Add(note);
            return s;
        }

        // 桌面端当前登录账号的邮箱：只有 CLI 身份是同一个账号时才能拿到
        private string DetectedEmail(string desktopUuid)
        {
            var id = eng.ReadIdentity();
            if (id == null || string.IsNullOrEmpty(desktopUuid)) return null;
            object v;
            if (id.TryGetValue("accountUuid", out v) && string.Equals(Convert.ToString(v), desktopUuid, StringComparison.OrdinalIgnoreCase))
                return eng.IdentityEmail(id);
            return null;
        }

        private static FrameworkElement ArtAvatar(string text, Brush bg, Brush fg, bool ghost)
        {
            var g = new Grid { Width = 64, Height = 64 };
            if (ghost)
            {
                g.Children.Add(new Rectangle { RadiusX = 19, RadiusY = 19, Fill = bg, Stroke = UI.Line2, StrokeThickness = 1.5, StrokeDashArray = new DoubleCollection { 3, 2 } });
            }
            else
            {
                g.Children.Add(new Border
                {
                    CornerRadius = new CornerRadius(19), Background = bg,
                    Effect = new DropShadowEffect { BlurRadius = 16, ShadowDepth = 6, Direction = 270, Opacity = 0.08, Color = Color.FromRgb(30, 25, 15) }
                });
            }
            var t = UI.Text(text, 23, fg, true);
            t.HorizontalAlignment = HorizontalAlignment.Center;
            g.Children.Add(t);
            return g;
        }

        private FrameworkElement DetectCard(string email)
        {
            var card = new Border
            {
                Background = UI.Card, BorderBrush = UI.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12),
                Padding = new Thickness(20), Margin = new Thickness(0, 26, 0, 0), Effect = UI.Shadow(18, 2, 0.06)
            };
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var initial = string.IsNullOrEmpty(email) ? "?" : email.Substring(0, 1).ToUpperInvariant();
            var av = new Border { Width = 48, Height = 48, CornerRadius = new CornerRadius(14), Background = UI.ClaySoft };
            var at = UI.Text(initial, 15, UI.ClayInk, true);
            at.HorizontalAlignment = HorizontalAlignment.Center;
            av.Child = at;
            g.Children.Add(av);
            var meta = new StackPanel { Margin = new Thickness(16, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
            var k = new StackPanel { Orientation = Orientation.Horizontal };
            var dot = new Grid { Width = 12, Height = 12, Margin = new Thickness(0, 0, 5, 0) };
            dot.Children.Add(new Ellipse { Fill = UI.SageSoft });
            dot.Children.Add(new Ellipse { Width = 6, Height = 6, Fill = UI.Sage });
            k.Children.Add(dot);
            k.Children.Add(UI.Text("Claude 当前登录", 11, UI.Sage));
            meta.Children.Add(k);
            var v = UI.Text(string.IsNullOrEmpty(email) ? "还没保存的账号" : email, 15, UI.Ink);
            v.FontWeight = FontWeights.Medium;
            v.Margin = new Thickness(0, 2, 0, 0);
            v.TextTrimming = TextTrimming.CharacterEllipsis;
            meta.Children.Add(v);
            Grid.SetColumn(meta, 1);
            g.Children.Add(meta);
            var btn = PrimaryButton("保存这个号");
            btn.Height = 40;
            btn.MouseLeftButtonUp += (o, e) => OpenSaveDialog();
            Grid.SetColumn(btn, 2);
            g.Children.Add(btn);
            card.Child = g;
            return card;
        }

        private FrameworkElement WaitBox(string loggedInEmail)
        {
            var g = new Grid { Margin = new Thickness(0, 24, 0, 0) };
            g.Children.Add(new Rectangle { RadiusX = 12, RadiusY = 12, Stroke = UI.Line2, StrokeThickness = 1.5, StrokeDashArray = new DoubleCollection { 3, 2 } });
            var s = new StackPanel { Margin = new Thickness(22) };
            var tb = new TextBlock
            {
                FontFamily = UI.Font, FontSize = 14, Foreground = UI.Ink2, TextAlignment = TextAlignment.Center,
                LineHeight = 25, TextWrapping = TextWrapping.Wrap
            };
            s.Children.Add(tb);
            if (string.IsNullOrEmpty(loggedInEmail))
            {
                tb.Text = "先在 Claude 里登录一个账号\n登录后，这里会出现「保存这个号」";
            }
            else
            {
                tb.Inlines.Add(new Run("Claude 现在登录的还是 "));
                tb.Inlines.Add(new Run(loggedInEmail) { Foreground = UI.Ink, FontWeight = FontWeights.Medium });
                var btn = PrimaryButton("登录另一个号");
                btn.Height = 40;
                btn.HorizontalAlignment = HorizontalAlignment.Center;
                btn.Margin = new Thickness(0, 16, 0, 0);
                btn.MouseLeftButtonUp += (o, e) => StartFreshLogin();
                s.Children.Add(btn);
                var warn = UI.Text("别在 Claude 里点“退出登录”，那样已存的号也会失效", 12, UI.Ink3);
                warn.HorizontalAlignment = HorizontalAlignment.Center;
                warn.Margin = new Thickness(0, 10, 0, 0);
                s.Children.Add(warn);
            }
            g.Children.Add(s);
            return g;
        }

        // 登录另一个号：确认后关闭 Claude、挪开本机登录状态、重开到登录页
        private void StartFreshLogin()
        {
            if (busy) return;
            var s = new StackPanel();
            s.Children.Add(UI.Text("登录另一个号", 17, UI.Ink, true));
            var d = UI.Text("Claude 会关闭再打开，打开后是登录页，用另一个账号登录，然后回到这里点「保存这个号」。\n\n当前的号已经存好，不会退出，之后随时能切回来。", 13.5, UI.Ink2);
            d.TextWrapping = TextWrapping.Wrap;
            d.LineHeight = 22;
            d.Margin = new Thickness(0, 10, 0, 0);
            s.Children.Add(d);
            var cancel = GhostButtonBorder("取消");
            cancel.MouseLeftButtonUp += (o, e) => HideOverlay();
            var go = PrimaryButton("继续");
            go.MouseLeftButtonUp += async (o, e) =>
            {
                busy = true;
                ShowBusyOverlay("正在打开登录页", "Claude 会关闭再打开，稍等几秒。");
                Exception error = null;
                await Task.Run(() => { try { eng.PrepareFreshLogin(); } catch (Exception ex) { error = ex; } });
                busy = false;
                HideOverlay();
                RefreshAll();
                if (error != null) ShowErrorOverlay("没能打开登录页", error.Message, StartFreshLogin, () => { HideOverlay(); ConfirmRestore(); });
                else ShowToast("Claude 已打开登录页", "用另一个账号登录，然后回来点「保存这个号」", null, null);
            };
            s.Children.Add(ButtonRow(cancel, go));
            ShowOverlay(ModalCard(s));
        }

        private static FrameworkElement GuideStep(int num, string title, string desc, int state)
        {
            var card = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 14, 14, 16), Background = state == 1 ? UI.ClaySoft : UI.Soft };
            var s = new StackPanel();
            var n = new Grid { Width = 22, Height = 22, HorizontalAlignment = HorizontalAlignment.Left };
            if (state == 2)
            {
                n.Children.Add(new Ellipse { Fill = UI.Sage });
                n.Children.Add(new Path
                {
                    Data = Geometry.Parse("M6.4,11.4 L9.4,14.4 L15.6,7.8"), Stroke = Brushes.White, StrokeThickness = 1.8,
                    StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round
                });
            }
            else
            {
                n.Children.Add(new Ellipse { Fill = state == 1 ? UI.Clay : UI.Card, Stroke = state == 1 ? UI.Clay : UI.Line2, StrokeThickness = 1 });
                var nt = UI.Text(num.ToString(), 11, state == 1 ? Brushes.White : UI.Ink2, true);
                nt.HorizontalAlignment = HorizontalAlignment.Center;
                n.Children.Add(nt);
            }
            s.Children.Add(n);
            var t = UI.Text(title, 13.5, state == 1 ? UI.ClayInk : UI.Ink, true);
            t.Margin = new Thickness(0, 10, 0, 0);
            s.Children.Add(t);
            var d = UI.Text(desc, 12, UI.Ink2);
            d.TextWrapping = TextWrapping.Wrap;
            d.LineHeight = 18;
            d.Margin = new Thickness(0, 2, 0, 0);
            s.Children.Add(d);
            card.Child = s;
            return card;
        }

        // ------------------------------------------------------------ buttons

        internal static Border PrimaryButton(string text)
        {
            var b = new Border
            {
                Background = UI.Ink, CornerRadius = new CornerRadius(9), Height = 38, Padding = new Thickness(20, 0, 20, 0),
                Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center
            };
            var t = UI.Text(text, 14, Brushes.White, true);
            t.HorizontalAlignment = HorizontalAlignment.Center;
            b.Child = t;
            b.MouseEnter += (s, e) => b.Background = UI.B("#3A3835");
            b.MouseLeave += (s, e) => b.Background = UI.Ink;
            return b;
        }

        internal static Border SecondaryButton(string text)
        {
            var b = new Border
            {
                Background = UI.Card, BorderBrush = UI.Line2, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
                Height = 38, Padding = new Thickness(18, 0, 18, 0), Cursor = Cursors.Hand
            };
            var t = UI.Text(text, 13, UI.Ink);
            t.HorizontalAlignment = HorizontalAlignment.Center;
            b.Child = t;
            b.MouseEnter += (s, e) => b.Background = UI.Soft;
            b.MouseLeave += (s, e) => b.Background = UI.Card;
            return b;
        }

        private static FrameworkElement GhostButton(string text, Action action)
        {
            var b = new Border { CornerRadius = new CornerRadius(8), Height = 32, Padding = new Thickness(12, 0, 12, 0), Cursor = Cursors.Hand, Background = Brushes.Transparent };
            b.Child = UI.Text(text, 13, UI.Ink2);
            b.MouseEnter += (s, e) => b.Background = UI.Hover;
            b.MouseLeave += (s, e) => b.Background = Brushes.Transparent;
            b.MouseLeftButtonUp += (s, e) => action();
            return b;
        }

        // ------------------------------------------------------------ overlay helpers

        private void ShowOverlay(UIElement inner)
        {
            overlayHost.Children.Clear();
            overlayHost.Children.Add(inner);
            overlayHost.Visibility = Visibility.Visible;
        }

        private void HideOverlay()
        {
            overlayHost.Visibility = Visibility.Collapsed;
            overlayHost.Children.Clear();
        }

        private static Border ModalCard(UIElement inner)
        {
            return new Border
            {
                Margin = new Thickness(28, 8, 28, 28),
                VerticalAlignment = VerticalAlignment.Top,
                Background = UI.Card, BorderBrush = UI.Line, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14), Padding = new Thickness(26),
                Effect = UI.Shadow(24, 4, 0.08),
                Child = inner
            };
        }

        private static FrameworkElement ButtonRow(params FrameworkElement[] buttons)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 24, 0, 0) };
            for (int i = 0; i < buttons.Length; i++)
            {
                if (i > 0) buttons[i].Margin = new Thickness(8, 0, 0, 0);
                row.Children.Add(buttons[i]);
            }
            return row;
        }

        // ------------------------------------------------------------ switch flow

        private async void StartSwitch(Account account)
        {
            if (busy) return;
            var prev = eng.FindByUuid(store, eng.GetDesktopUuid());
            previousAccountId = prev != null && prev.Id != account.Id ? prev.Id : null;
            busy = true;
            ShowSwitchOverlay(account, prev);
            var progress = new Progress<SwitchProgress>(UpdateSwitchProgress);
            SwitchResult result = null;
            Exception error = null;
            await Task.Run(() =>
            {
                try { result = eng.Switch(account, progress); }
                catch (Exception ex) { error = ex; }
            });
            busy = false;
            if (error != null)
            {
                LogError(error);
                ShowErrorOverlay("切换没有完成", error.Message, () => StartSwitch(account), () => { HideOverlay(); ConfirmRestore(); });
                return;
            }
            HideOverlay();
            RefreshAll();
            if (result != null && result.Already)
            {
                ShowToast(result.Message, null, null, null);
                return;
            }
            var backId = previousAccountId;
            Action back = null;
            if (!string.IsNullOrEmpty(backId))
            {
                back = () =>
                {
                    var target = store.Accounts.FirstOrDefault(x => x.Id == backId);
                    if (target != null) StartSwitch(target);
                };
            }
            string title, sub;
            ResumeText(account, result, out title, out sub);
            ShowToast(title, sub, back != null ? "切回" : null, back);
        }

        // 切换完成后的提示：回到了哪个会话；把“继续”放进剪贴板，粘贴发送就能接着做
        private void ResumeText(Account account, SwitchResult result, out string title, out string sub)
        {
            title = result != null ? result.Message : "切换完成";
            sub = null;
            if (result == null || string.IsNullOrEmpty(result.ResumedSessionId)) return;
            var name = string.IsNullOrEmpty(result.ResumedSessionTitle) ? "上次的会话" : "「" + result.ResumedSessionTitle + "」";
            title = "已换到" + account.Name + "，回到了" + name;
            try
            {
                Clipboard.SetText("继续");
                sub = "按 Ctrl V 发送“继续”，它会接着做";
            }
            catch { }
        }

        private List<FrameworkElement> stepMarks;
        private List<TextBlock> stepTexts;
        private ColumnDefinition progDone, progLeft;

        private void ShowSwitchOverlay(Account account, Account from)
        {
            var s = new StackPanel();

            var head = new StackPanel { Orientation = Orientation.Horizontal };
            if (from != null)
            {
                head.Children.Add(Avatar(from));
                var arrow = UI.Icon("", 13, UI.Ink3);
                arrow.Margin = new Thickness(10, 0, 10, 0);
                head.Children.Add(arrow);
            }
            head.Children.Add(Avatar(account));
            var titles = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            titles.Children.Add(UI.Text("正在换到" + account.Name, 17, UI.Ink, true));
            var d = UI.Text("失败会自动回滚，不用担心", 12, UI.Ink2);
            d.Margin = new Thickness(0, 2, 0, 0);
            titles.Children.Add(d);
            head.Children.Add(titles);
            s.Children.Add(head);

            var list = new StackPanel { Margin = new Thickness(0, 22, 0, 0) };
            stepMarks = new List<FrameworkElement>();
            stepTexts = new List<TextBlock>();
            foreach (var step in SwitchSteps)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 8) };
                var mark = new Grid { Width = 18, Height = 18 };
                row.Children.Add(mark);
                var t = UI.Text(step, 14, UI.Ink3);
                t.Margin = new Thickness(10, 0, 0, 0);
                row.Children.Add(t);
                list.Children.Add(row);
                stepMarks.Add(mark);
                stepTexts.Add(t);
            }
            s.Children.Add(list);

            var track = new Grid { Height = 4, Margin = new Thickness(0, 14, 0, 0) };
            track.Children.Add(new Border { CornerRadius = new CornerRadius(2), Background = UI.Track });
            var fill = new Grid();
            progDone = new ColumnDefinition { Width = new GridLength(0, GridUnitType.Star) };
            progLeft = new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) };
            fill.ColumnDefinitions.Add(progDone);
            fill.ColumnDefinitions.Add(progLeft);
            fill.Children.Add(new Border { CornerRadius = new CornerRadius(2), Background = UI.Clay });
            track.Children.Add(fill);
            s.Children.Add(track);

            var hint = UI.Text("这期间别手动打开 Claude。", 12, UI.Ink3);
            hint.Margin = new Thickness(0, 10, 0, 0);
            s.Children.Add(hint);

            ShowOverlay(ModalCard(s));
            UpdateSwitchProgress(new SwitchProgress { Step = 1, Total = SwitchSteps.Length });
        }

        private void UpdateSwitchProgress(SwitchProgress p)
        {
            if (stepMarks == null || p == null) return;
            for (int i = 0; i < stepMarks.Count; i++)
            {
                var mark = (Grid)stepMarks[i];
                mark.Children.Clear();
                var t = stepTexts[i];
                if (i + 1 < p.Step)
                {
                    mark.Children.Add(new Ellipse { Fill = UI.Sage });
                    mark.Children.Add(new Path
                    {
                        Data = Geometry.Parse("M5,9.4 L7.8,12 L13,6.4"), Stroke = Brushes.White, StrokeThickness = 1.7,
                        StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round
                    });
                    t.Foreground = UI.Ink2;
                    t.FontWeight = FontWeights.Normal;
                }
                else if (i + 1 == p.Step)
                {
                    var arc = new Ellipse
                    {
                        Stroke = UI.Clay, StrokeThickness = 1.6, StrokeDashArray = new DoubleCollection { 6, 3 },
                        RenderTransformOrigin = new Point(0.5, 0.5)
                    };
                    var rot = new RotateTransform();
                    arc.RenderTransform = rot;
                    rot.BeginAnimation(RotateTransform.AngleProperty,
                        new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1)) { RepeatBehavior = RepeatBehavior.Forever });
                    mark.Children.Add(arc);
                    t.Foreground = UI.Ink;
                    t.FontWeight = FontWeights.SemiBold;
                }
                else
                {
                    mark.Children.Add(new Ellipse { Stroke = UI.Line2, StrokeThickness = 1.5 });
                    t.Foreground = UI.Ink3;
                    t.FontWeight = FontWeights.Normal;
                }
            }
            if (progDone != null)
            {
                var total = Math.Max(1, p.Total);
                var done = Math.Max(0, Math.Min(total, p.Step - 1)) + 0.5;
                progDone.Width = new GridLength(done, GridUnitType.Star);
                progLeft.Width = new GridLength(Math.Max(0.001, total - done), GridUnitType.Star);
            }
        }

        private void LogError(Exception ex) { eng.Log("错误：" + ex.Message); }

        // ------------------------------------------------------------ error / restore

        private void ShowErrorOverlay(string title, string message, Action retry, Action restore)
        {
            var s = new StackPanel();
            var head = new StackPanel { Orientation = Orientation.Horizontal };
            var badge = new Border { Width = 28, Height = 28, CornerRadius = new CornerRadius(14), Background = UI.DangerSoft };
            badge.Child = UI.Icon("", 13, UI.Danger);
            head.Children.Add(badge);
            var t = UI.Text(title, 15, UI.Ink, true);
            t.Margin = new Thickness(10, 0, 0, 0);
            head.Children.Add(t);
            s.Children.Add(head);
            var m = UI.Text(message, 12.5, UI.Ink2);
            m.TextWrapping = TextWrapping.Wrap;
            m.LineHeight = 20;
            m.Margin = new Thickness(0, 12, 0, 0);
            s.Children.Add(m);

            var close = GhostButtonBorder("关闭");
            close.MouseLeftButtonUp += (o, e) => { HideOverlay(); RefreshAll(); };
            var rb = SecondaryButton("恢复备份");
            rb.MouseLeftButtonUp += (o, e) => restore();
            var again = PrimaryButton("重试");
            again.MouseLeftButtonUp += (o, e) => retry();
            s.Children.Add(ButtonRow(close, rb, again));
            ShowOverlay(ModalCard(s));
        }

        internal static Border GhostButtonBorder(string text)
        {
            var b = new Border { CornerRadius = new CornerRadius(8), Height = 32, Padding = new Thickness(12, 0, 12, 0), Cursor = Cursors.Hand, Background = Brushes.Transparent };
            b.Child = UI.Text(text, 13, UI.Ink2);
            b.MouseEnter += (s, e) => b.Background = UI.Hover;
            b.MouseLeave += (s, e) => b.Background = Brushes.Transparent;
            return b;
        }

        private async void ConfirmRestore()
        {
            if (busy) return;
            var ok = MessageBox.Show("恢复到最近一次切换前的状态？Claude 会重新启动。", Engine.AppName, MessageBoxButton.OKCancel, MessageBoxImage.Question);
            if (ok != MessageBoxResult.OK) return;
            busy = true;
            ShowBusyOverlay("正在恢复备份", "Claude 会重新启动，稍等几秒。");
            Exception error = null;
            await Task.Run(() => { try { eng.RestoreLatestBackup(); } catch (Exception ex) { error = ex; } });
            busy = false;
            HideOverlay();
            RefreshAll();
            if (error != null) ShowErrorOverlay("恢复失败", error.Message, () => { HideOverlay(); ConfirmRestore(); }, () => HideOverlay());
            else ShowToast("已恢复到上次切换前的状态", null, null, null);
        }

        // ------------------------------------------------------------ save flow

        private void OpenSaveDialog()
        {
            if (busy) return;
            var identity = eng.ReadIdentity();
            var detected = eng.IdentityEmail(identity) ?? "";
            var s = new StackPanel();
            s.Children.Add(UI.Text("保存当前账号", 15, UI.Ink, true));
            var d = UI.Text("把 Claude 现在登录的账号存成快照，之后一键切回。保存时 Claude 会重启一下。", 12.5, UI.Ink2);
            d.TextWrapping = TextWrapping.Wrap;
            d.LineHeight = 20;
            d.Margin = new Thickness(0, 6, 0, 0);
            s.Children.Add(d);

            s.Children.Add(FieldLabel("名称"));
            var nameBox = InputBox(store.Accounts.Count == 0 ? "主号" : "账号 " + (store.Accounts.Count + 1));
            s.Children.Add(nameBox);
            s.Children.Add(FieldLabel("邮箱"));
            var emailBox = InputBox(detected);
            s.Children.Add(emailBox);
            var tip = UI.Text("邮箱用来认出这个号，并在切换时清理旧账号留下的痕迹", 11.5, UI.Ink3);
            tip.TextWrapping = TextWrapping.Wrap;
            tip.Margin = new Thickness(0, 6, 0, 0);
            s.Children.Add(tip);

            var cancel = GhostButtonBorder("取消");
            cancel.MouseLeftButtonUp += (o, e) => HideOverlay();
            var ok = PrimaryButton("保存");
            ok.MouseLeftButtonUp += (o, e) =>
            {
                var nm = nameBox.Text.Trim();
                if (string.IsNullOrEmpty(nm)) nm = "账号 " + DateTime.Now.ToString("MMdd-HHmm");
                HideOverlay();
                StartSave(nm, emailBox.Text.Trim());
            };
            s.Children.Add(ButtonRow(cancel, ok));
            ShowOverlay(ModalCard(s));
            nameBox.Focus();
            nameBox.SelectAll();
        }

        private static TextBlock FieldLabel(string text)
        {
            var t = UI.Text(text, 12, UI.Ink2);
            t.Margin = new Thickness(0, 14, 0, 6);
            return t;
        }

        private static TextBox InputBox(string value)
        {
            return new TextBox
            {
                Text = value, Height = 36, FontFamily = UI.Font, FontSize = 13, Foreground = UI.Ink,
                Background = UI.Card, BorderBrush = UI.Line2, BorderThickness = new Thickness(1),
                Padding = new Thickness(8, 0, 8, 0), VerticalContentAlignment = VerticalAlignment.Center, CaretBrush = UI.Ink
            };
        }

        private async void StartSave(string name, string email)
        {
            if (busy) return;
            busy = true;
            ShowBusyOverlay("正在保存「" + name + "」", "关闭 Claude、存下登录状态，然后重新打开。");
            Exception error = null;
            await Task.Run(() => { try { eng.SaveCurrent(name, email); } catch (Exception ex) { error = ex; } });
            busy = false;
            HideOverlay();
            if (error != null)
            {
                ShowErrorOverlay("保存没有完成", error.Message, () => StartSave(name, email), () => { HideOverlay(); RefreshAll(); });
                return;
            }
            RefreshAll();
            ShowToast("已保存「" + name + "」", null, null, null);
        }

        private void ShowBusyOverlay(string title, string desc)
        {
            var s = new StackPanel();
            s.Children.Add(UI.Text(title, 15, UI.Ink, true));
            var d = UI.Text(desc, 12.5, UI.Ink2);
            d.TextWrapping = TextWrapping.Wrap;
            d.Margin = new Thickness(0, 6, 0, 0);
            s.Children.Add(d);
            var bar = new ProgressBar
            {
                Height = 4, Margin = new Thickness(0, 16, 0, 0), IsIndeterminate = true,
                BorderThickness = new Thickness(0), Background = UI.Track, Foreground = UI.Clay
            };
            s.Children.Add(bar);
            ShowOverlay(ModalCard(s));
        }

        // ------------------------------------------------------------ toast

        private void ShowToast(string title, string sub, string actionText, Action action)
        {
            foreach (var child in rootHost.Children.OfType<Border>().Where(b => b.Tag as string == "toast").ToList())
                rootHost.Children.Remove(child);

            Border toast = null;
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var ok = new Grid { Width = 22, Height = 22, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 12, 0) };
            ok.Children.Add(new Ellipse { Fill = UI.Sage });
            ok.Children.Add(new Path
            {
                Data = Geometry.Parse("M6.5,11.4 L9.6,14.4 L15.6,7.8"), Stroke = Brushes.White, StrokeThickness = 1.8,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round
            });
            g.Children.Add(ok);

            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var t1 = UI.Text(title, 14, Brushes.White);
            t1.TextWrapping = TextWrapping.Wrap;
            text.Children.Add(t1);
            if (!string.IsNullOrEmpty(sub))
            {
                var t2 = UI.Text(sub, 12, UI.B("#BDB8AE"));
                t2.TextWrapping = TextWrapping.Wrap;
                t2.Margin = new Thickness(0, 3, 0, 0);
                text.Children.Add(t2);
            }
            Grid.SetColumn(text, 1);
            g.Children.Add(text);

            if (!string.IsNullOrEmpty(actionText) && action != null)
            {
                var link = UI.Text(actionText, 12, UI.B("#F2B59B"));
                link.Cursor = Cursors.Hand;
                link.VerticalAlignment = VerticalAlignment.Top;
                link.Margin = new Thickness(12, 2, 0, 0);
                link.MouseLeftButtonUp += (s, e) => { if (toast != null) rootHost.Children.Remove(toast); action(); };
                Grid.SetColumn(link, 2);
                g.Children.Add(link);
            }

            toast = new Border
            {
                Tag = "toast", Background = UI.Ink, CornerRadius = new CornerRadius(12), Padding = new Thickness(16, 14, 16, 14),
                Margin = new Thickness(52, 0, 52, 56), VerticalAlignment = VerticalAlignment.Bottom,
                Child = g, Effect = UI.Shadow(30, 8, 0.25)
            };
            rootHost.Children.Add(toast);

            if (toastTimer != null) toastTimer.Stop();
            toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(sub != null ? 9 : 5) };
            toastTimer.Tick += (s, e) => { toastTimer.Stop(); rootHost.Children.Remove(toast); };
            toastTimer.Start();
        }

        // ------------------------------------------------------------ tray

        private void InitTray()
        {
            try
            {
                tray = new WinForms.NotifyIcon { Icon = LoadTrayIcon(), Visible = true, Text = Engine.AppName };
                tray.MouseClick += (s, e) => { if (e.Button == WinForms.MouseButtons.Left) ShowFromTray(); };
                RefreshTrayMenu();
            }
            catch { }
        }

        // 托盘用系统小图标尺寸（100% 缩放是 16 像素），从内嵌的多尺寸 ico 里直接取，不从大图缩小
        private static Drawing.Icon LoadTrayIcon()
        {
            var ico = UI.AppIcon(WinForms.SystemInformation.SmallIconSize.Width);
            if (ico != null) return ico;
            try
            {
                ico = Drawing.Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetExecutingAssembly().Location);
                if (ico != null) return ico;
            }
            catch { }
            return Drawing.SystemIcons.Application;
        }

        private void RefreshTrayMenu()
        {
            if (tray == null) return;
            try
            {
                var menu = new WinForms.ContextMenuStrip();
                menu.Items.Add("打开窗口", null, (s, e) => ShowFromTray());
                menu.Items.Add(new WinForms.ToolStripSeparator());
                foreach (var a in store.Accounts)
                {
                    var acct = a;
                    var item = new WinForms.ToolStripMenuItem("切到 " + a.Name + (string.IsNullOrEmpty(a.Email) ? "" : "  ·  " + a.Email));
                    item.Click += async (s, e) => { await TraySwitch(acct); };
                    menu.Items.Add(item);
                }
                menu.Items.Add(new WinForms.ToolStripSeparator());
                menu.Items.Add("退出", null, (s, e) => { try { tray.Visible = false; } catch { } Application.Current.Shutdown(); });
                tray.ContextMenuStrip = menu;
            }
            catch { }
        }

        private async Task TraySwitch(Account account)
        {
            if (busy) return;
            busy = true;
            SwitchResult result = null;
            Exception error = null;
            await Task.Run(() => { try { result = eng.Switch(account, null); } catch (Exception ex) { error = ex; } });
            busy = false;
            RefreshAll();
            try
            {
                string title, sub;
                ResumeText(account, result, out title, out sub);
                if (tray != null)
                    tray.ShowBalloonTip(3000, Engine.AppName,
                        error != null ? "切换失败：" + error.Message : title + (sub != null ? "\n" + sub : ""),
                        WinForms.ToolTipIcon.Info);
            }
            catch { }
        }

        private void ShowFromTray()
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
            RefreshAll();
        }

        private void OpenLogs()
        {
            try { System.Diagnostics.Process.Start("explorer.exe", eng.LogsDir); } catch { }
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            if (!busy && overlayHost != null && overlayHost.Visibility != Visibility.Visible) RefreshAll();
        }

        protected override void OnClosed(EventArgs e)
        {
            try { if (tray != null) { tray.Visible = false; tray.Dispose(); } } catch { }
            base.OnClosed(e);
        }
    }
}
