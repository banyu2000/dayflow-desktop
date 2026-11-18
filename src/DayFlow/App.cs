using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace DayFlow
{
    public static class Program
    {
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
        public static string BaseFolder = AppDomain.CurrentDomain.BaseDirectory;
        [STAThread]
        public static void Main(string[] args)
        {
            if (args.Contains("--self-test")) { SelfTests.Run(args); return; }
            bool preview = args.Contains("--preview");
            string dataFolder = Path.Combine(BaseFolder, "data");
            int location = Array.IndexOf(args, "--data-dir");
            if (location >= 0 && location + 1 < args.Length) dataFolder = Path.GetFullPath(args[location + 1]);
            if (args.Contains("--enable-startup"))
            {
                try { StartupRegistration.SetEnabled(true, dataFolder); }
                catch (Exception e) { Console.Error.WriteLine(e.Message); Environment.ExitCode = 1; }
                return;
            }
            bool created;
            string hash = String.Join("", System.Security.Cryptography.SHA256.Create().ComputeHash(Encoding.UTF8.GetBytes(dataFolder.ToLowerInvariant())).Take(12).Select(x => x.ToString("x2")).ToArray());
            using (Mutex mutex = new Mutex(true, "Local\\DayFlow-" + hash, out created))
            {
                if (!created)
                {
                    try { using (EventWaitHandle activate = EventWaitHandle.OpenExisting("Local\\DayFlow-Activate-" + hash)) activate.Set(); } catch { }
                    return;
                }
                try
                {
                    SetProcessDPIAware();
                    Store store = new Store(dataFolder);
                    if (args.Contains("--floating")) store.Change(delegate { store.Data.Settings.Floating = true; });
                    if (preview && store.Data.Events.Count == 0 && store.Data.Todos.Count == 0) SampleData(store);
                    Application app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
                    app.Resources = Theme.Resources();
                    app.DispatcherUnhandledException += delegate(object sender, DispatcherUnhandledExceptionEventArgs e)
                    {
                        MessageBox.Show("操作未完成：\n" + e.Exception.Message, "日常", MessageBoxButton.OK, MessageBoxImage.Warning); e.Handled = true;
                    };
                    using (EventWaitHandle activate = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\DayFlow-Activate-" + hash))
                    {
                        MainWindow main = new MainWindow(store, preview);
                        if (args.Contains("--ui-check"))
                            main.Loaded += delegate { main.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(delegate { UiChecks.Run(main, store); })); };
                        RegisteredWaitHandle registration = ThreadPool.RegisterWaitForSingleObject(activate, delegate { main.Dispatcher.BeginInvoke(new Action(main.Restore)); }, null, -1, false);
                        try { app.Run(main); } finally { registration.Unregister(null); }
                    }
                }
                catch (Exception e) { MessageBox.Show(e.Message, "日常无法启动", MessageBoxButton.OK, MessageBoxImage.Error); }
            }
        }
        static void SampleData(Store store)
        {
            DateTime today = DateTime.Today;
            store.Data.Events.Add(new CalendarEvent { Title = "产品设计讨论", Start = Dates.Format(today.AddHours(10)), End = Dates.Format(today.AddHours(11)), Category = "工作", Location = "会议室 A", Notes = "确认本周的设计方向", Reminder = -1 });
            store.Data.Events.Add(new CalendarEvent { Title = "给自己一点阅读时间", Start = Dates.Format(today.AddHours(20)), End = Dates.Format(today.AddHours(20.5)), Category = "个人", Reminder = -1 });
            store.Data.Events.Add(new CalendarEvent { Title = "周末去公园走走", Start = Dates.Format(today.AddDays(5)), End = Dates.Format(today.AddDays(6)), AllDay = true, Category = "生活", Reminder = -1 });
            store.Data.Todos.Add(new Todo { Title = "整理本周的工作清单", Due = Dates.Day(today), Important = true });
            store.Data.Todos.Add(new Todo { Title = "给阳台的绿植浇水", Due = Dates.Day(today) });
            store.Data.Todos.Add(new Todo { Title = "预约下周的体检", Due = Dates.Day(today.AddDays(2)) });
            store.Data.Todos.Add(new Todo { Title = "喝一杯水，休息一下", Due = Dates.Day(today), Done = true });
            store.Save();
        }
    }
    public static class Theme
    {
        public static Brush Ink = Brush("#263B56"), Muted = Brush("#5D7189"), Green = Brush("#507EA9"), Line = Brush("#A0FFFFFF"), Canvas = Glass.Inner, Pale = Brush("#6387BDD7");
        public static Brush Brush(string hex) { return (SolidColorBrush)new BrushConverter().ConvertFromString(hex); }
        public static ResourceDictionary Resources()
        {
            using (Stream stream = typeof(Theme).Assembly.GetManifestResourceStream("DayFlow.Theme.xaml"))
                return (ResourceDictionary)XamlReader.Load(stream);
        }
        public static TextBlock Text(string text, double size, Brush color = null, bool bold = false)
        {
            return new TextBlock { Text = text, FontSize = size, Foreground = color ?? Ink, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, VerticalAlignment = VerticalAlignment.Center };
        }
        public static Button Button(string text, Action action, string style = null)
        {
            Button b = new Button { Content = text };
            if (style != null) b.SetResourceReference(FrameworkElement.StyleProperty, style);
            Motion.WireButton(b);
            b.Click += delegate { Motion.Remember(b, false); action(); };
            return b;
        }
        public static Border Card(UIElement child, double padding = 20)
        {
            Border card = new Border { Background = Glass.Surface, BorderBrush = Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(21), Padding = new Thickness(padding), Child = child };
            Motion.Surface(card, false);
            return card;
        }
        public static Grid Grid(int columns)
        {
            Grid grid = new Grid();
            for (int i = 0; i < columns; i++) grid.ColumnDefinitions.Add(new ColumnDefinition());
            return grid;
        }
        public static void Put(Grid grid, UIElement element, int col = 0, int row = 0)
        {
            System.Windows.Controls.Grid.SetColumn(element, col); System.Windows.Controls.Grid.SetRow(element, row); grid.Children.Add(element);
        }
        public static void Gap(Panel panel, double height = 12) { panel.Children.Add(new Border { Height = height }); }
    }
    public partial class MainWindow : Window
    {
        readonly Store store;
        readonly bool preview;
        DateTime selected = DateTime.Today, month = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        bool compact, pinned;
        string todoFilter = "未完成", search = "";
        Grid calendar, body;
        StackPanel dayList, todoList;
        TextBlock monthLabel, dayLabel, daySubtitle, status, todoCount, greeting;
        Button pinButton, modeButton;
        Forms.NotifyIcon tray;
        DispatcherTimer timer;
        readonly HashSet<string> notified = new HashSet<string>();
        DateTime lastTick = DateTime.Now, lastDate = DateTime.Today;
        double fullWidth = 1160, fullHeight = 810;
        public MainWindow(Store store, bool preview)
        {
            this.store = store; this.preview = preview;
            Style = (Style)Application.Current.FindResource(typeof(Window));
            Glass.Configure(this);
            compact = store.Data.Settings.Compact; pinned = store.Data.Settings.Topmost;
            Title = preview ? "日常 · 界面预览（示例数据）" : "日常 · 桌面日历与待办";
            Width = compact ? 460 : fullWidth; Height = compact ? 790 : fullHeight;
            MinWidth = compact ? 400 : 980; MinHeight = 660;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            MaxHeight = SystemParameters.WorkArea.Height; MaxWidth = SystemParameters.WorkArea.Width;
            Topmost = pinned;
            try { Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri(Path.Combine(Program.BaseFolder, "DayFlow.ico"))); } catch { }
            Build(); SetupTray();
            timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
            timer.Tick += Tick; timer.Start();
            Closed += delegate { timer.Stop(); if (floatingBar != null) floatingBar.Shutdown(); tray.Visible = false; tray.Dispose(); };
            Loaded += delegate { if (store.StartupNotice.Length > 0) MessageBox.Show(this, store.StartupNotice, "已恢复数据"); };
            Loaded += delegate { if (store.Data.Settings.Floating && !preview) Dispatcher.BeginInvoke(new Action(ShowFloating)); };
            PreviewKeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.N) { EditEvent(null); e.Handled = true; }
                else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.T) { EditTodo(null); e.Handled = true; }
                else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.E) { Export(); e.Handled = true; }
                else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.G) { ShowGoals(); EditGoal(null); e.Handled = true; }
            };
        }
        void SetupTray()
        {
            tray = new Forms.NotifyIcon { Text = "日常 · 日历与待办", Visible = true };
            try { tray.Icon = new System.Drawing.Icon(Path.Combine(Program.BaseFolder, "DayFlow.ico")); } catch { tray.Icon = System.Drawing.SystemIcons.Application; }
            Forms.ContextMenuStrip menu = new Forms.ContextMenuStrip();
            menu.Items.Add("显示日常", null, delegate { Dispatcher.Invoke(new Action(Restore)); });
            menu.Items.Add("极简悬浮条", null, delegate { Dispatcher.Invoke(new Action(EnterFloating)); });
            menu.Items.Add("新建日程", null, delegate { Dispatcher.Invoke(new Action(delegate { Restore(); EditEvent(null); })); });
            Forms.ToolStripMenuItem startup = new Forms.ToolStripMenuItem("开机自启动");
            startup.Click += delegate { Dispatcher.Invoke(new Action(ToggleStartup)); };
            menu.Items.Add(startup);
            menu.Opening += delegate { startup.Checked = StartupEnabled(); };
            menu.Items.Add("退出", null, delegate { Dispatcher.Invoke(new Action(Close)); });
            tray.ContextMenuStrip = menu;
            tray.DoubleClick += delegate { Dispatcher.Invoke(new Action(Restore)); };
        }
        public void Restore()
        {
            if (store.Data.Settings.Floating && !Commit(delegate { store.Data.Settings.Floating = false; }, "已展开日历")) return;
            if (floatingBar != null) floatingBar.Hide();
            Show(); if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal; Activate();
        }
        bool Commit(Action action, string message)
        {
            try { store.Change(action); Refresh(); Say(message); Motion.Confirm(this); return true; }
            catch (Exception e) { MessageBox.Show(this, "保存未完成，原有数据已保留。\n" + e.Message, "无法保存", MessageBoxButton.OK, MessageBoxImage.Warning); Refresh(); return false; }
        }
        void Say(string message) { status.Text = message; }
        void Build()
        {
            Grid root = new Grid { Margin = new Thickness(compact ? 16 : 28, compact ? 14 : 23, compact ? 16 : 28, 12) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition());
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid header = Theme.Grid(2);
            header.ColumnDefinitions[1].Width = GridLength.Auto;
            Glass.DragArea(header, this);
            StackPanel brand = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            Border logo = new Border { Width = 38, Height = 38, Background = Glass.Accent, CornerRadius = new CornerRadius(13), Child = Theme.Text("日", 20, Brushes.White, true), Margin = new Thickness(0, 0, 11, 0), Effect = Glass.Shadow(.15, 16, 4) };
            ((TextBlock)logo.Child).HorizontalAlignment = HorizontalAlignment.Center;
            brand.Children.Add(logo);
            StackPanel brandText = new StackPanel();
            brandText.Children.Add(Theme.Text("日常", compact ? 21 : 23, null, true));
            brandText.Children.Add(Theme.Text("DAYFLOW  /  把日子安排得刚刚好", compact ? 9 : 10, Theme.Muted));
            brand.Children.Add(brandText); Theme.Put(header, brand);
            StackPanel controls = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            pinButton = Theme.Button(pinned ? "已置顶" : "置顶", TogglePin, "Quiet"); pinButton.ToolTip = "让组件始终显示在其他窗口上方";
            modeButton = Theme.Button(compact ? "展开" : "桌面模式", ToggleMode, "Quiet");
            Button more = Theme.Button("···", More, "Quiet"); more.FontSize = 20; more.ToolTip = "数据备份、使用说明与托盘";
            controls.Children.Add(pinButton); controls.Children.Add(modeButton); controls.Children.Add(more);
            Button floating = Theme.Button(compact ? "◦" : "悬浮条", EnterFloating, "Quiet"); floating.ToolTip = "极简悬浮条 · 圆点显示今天的待办";
            System.Windows.Automation.AutomationProperties.SetName(floating, "极简悬浮条");
            controls.Children.Insert(2, floating);
            Button minimize = Theme.Button("−", delegate { WindowState = WindowState.Minimized; }, "Quiet"); minimize.ToolTip = "最小化"; minimize.Width = 27; minimize.FontSize = 18; minimize.Padding = new Thickness(3, 4, 3, 4);
            Button close = Theme.Button("×", Close, "Quiet"); close.ToolTip = "关闭日常"; close.Width = 27; close.FontSize = 18; close.Padding = new Thickness(3, 4, 3, 4);
            controls.Children.Add(minimize); controls.Children.Add(close);
            Theme.Put(header, controls, 1); Theme.Put(root, header);
            Grid intro = Theme.Grid(2); intro.Margin = new Thickness(0, compact ? 16 : 25, 0, compact ? 14 : 20);
            StackPanel introText = new StackPanel();
            greeting = Theme.Text(compact ? "今天，也从容一点。" : "留一点时间，给重要的事。", compact ? 17 : 26, null, true);
            introText.Children.Add(greeting);
            if (!compact) { Theme.Gap(introText, 7); introText.Children.Add(Theme.Text(DateTime.Today.ToString("yyyy 年 M 月 d 日") + "  ·  " + Dates.Weekday(DateTime.Today), 13, Theme.Muted)); }
            Theme.Put(intro, introText);
            Button export = Theme.Button(compact ? "导入 iPhone" : "↗  导入苹果日历", Export); export.VerticalAlignment = VerticalAlignment.Center; export.HorizontalAlignment = HorizontalAlignment.Right; export.ToolTip = "导出 .ics 文件，通过邮件在 iPhone 的日历中添加";
            Theme.Put(intro, export, 1); Theme.Put(root, intro, 0, 1);
            body = new Grid();
            if (compact) BuildCompact(); else BuildFull();
            Theme.Put(root, body, 0, 2);
            Grid footer = Theme.Grid(2); footer.Margin = new Thickness(2, 11, 2, 0);
            status = Theme.Text(preview ? "示例数据预览 · 不影响你的日历" : "●  本机保存 · 无需账号", 11, Theme.Muted); status.TextTrimming = TextTrimming.CharacterEllipsis;
            Theme.Put(footer, status);
            TextBlock hint = Theme.Text(compact ? "" : "Ctrl+N 新日程   ·   Ctrl+T 新待办", 11, Theme.Muted); hint.HorizontalAlignment = HorizontalAlignment.Right; Theme.Put(footer, hint, 1);
            Theme.Put(root, footer, 0, 3);
            Content = Glass.Wrap(this, root, false); Refresh();
        }
        StackPanel MonthHeader()
        {
            StackPanel p = new StackPanel();
            Grid top = Theme.Grid(2);
            monthLabel = Theme.Text("", compact ? 18 : 21, null, true); Theme.Put(top, monthLabel);
            StackPanel nav = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            nav.Children.Add(Theme.Button("‹", delegate { MoveMonth(-1); }, "Quiet"));
            nav.Children.Add(Theme.Button("今天", delegate { selected = DateTime.Today; month = new DateTime(selected.Year, selected.Month, 1); Refresh(); }, "Quiet"));
            nav.Children.Add(Theme.Button("›", delegate { MoveMonth(1); }, "Quiet")); Theme.Put(top, nav, 1);
            p.Children.Add(top); Theme.Gap(p, compact ? 10 : 12);
            Grid weekdays = Theme.Grid(7);
            for (int i = 0; i < 7; i++) { TextBlock label = Theme.Text(new[] { "一", "二", "三", "四", "五", "六", "日" }[i], 11, Theme.Muted); label.HorizontalAlignment = HorizontalAlignment.Center; Theme.Put(weekdays, label, i); }
            p.Children.Add(weekdays); Theme.Gap(p, 9); return p;
        }
        void BuildFull()
        {
            body.ColumnDefinitions.Add(new ColumnDefinition()); body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) }); body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(350) });
            Grid left = new Grid();
            left.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.25, GridUnitType.Star) }); left.RowDefinitions.Add(new RowDefinition { Height = new GridLength(16) }); left.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            Grid cal = new Grid(); cal.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); cal.RowDefinitions.Add(new RowDefinition()); cal.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Theme.Put(cal, MonthHeader()); calendar = new Grid(); Theme.Put(cal, calendar, 0, 1);
            StackPanel legend = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(3, 14, 0, 0) };
            foreach (string category in new[] { "生活", "工作", "个人" })
            {
                TextBlock t = Theme.Text("●  " + category, 11, CategoryColor(category)); t.Margin = new Thickness(0, 0, 22, 0); legend.Children.Add(t);
            }
            legend.Children.Add(Theme.Text("○ 待办  ·  双击日期新建日程", 10, Theme.Muted)); Theme.Put(cal, legend, 0, 2);
            Theme.Put(left, Theme.Card(cal)); Theme.Put(left, BuildTodos(false), 0, 2); Theme.Put(body, left);
            Theme.Put(body, BuildDay(false), 2);
            Grid workspace = body;
            body = new Grid();
            sectionTabs = new TabControl { Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0) };
            sectionTabs.Items.Add(new TabItem { Header = "日历与待办", Content = workspace });
            sectionTabs.Items.Add(new TabItem { Header = "长期目标", Content = BuildGoals(false) });
            sectionTabs.SelectedIndex = activeSection == "目标" ? 1 : 0;
            sectionTabs.SelectionChanged += delegate(object sender, SelectionChangedEventArgs e)
            {
                if (e.Source != sectionTabs) return;
                activeSection = sectionTabs.SelectedIndex == 1 ? "目标" : "日程";
                FrameworkElement page = sectionTabs.SelectedContent as FrameworkElement; if (page != null) Motion.Enter(page);
            };
            body.Children.Add(sectionTabs);
        }
        void BuildCompact()
        {
            body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(12) }); body.RowDefinitions.Add(new RowDefinition());
            StackPanel cal = MonthHeader(); calendar = new Grid { Height = 198 }; cal.Children.Add(calendar);
            Border monthCard = Theme.Card(cal, 15); Theme.Put(body, monthCard);
            TabControl tabs = new TabControl { Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0) };
            sectionTabs = tabs;
            TabItem agenda = new TabItem { Header = "  日程  ", Content = BuildDay(true), FontSize = 13 };
            TabItem tasks = new TabItem { Header = "  待办  ", Content = BuildTodos(true), FontSize = 13 };
            tabs.Items.Add(agenda); tabs.Items.Add(tasks); tabs.Items.Add(new TabItem { Header = "  长期目标  ", Content = BuildGoals(true), FontSize = 13 });
            tabs.SelectedIndex = activeSection == "目标" ? 2 : activeSection == "待办" ? 1 : 0;
            monthCard.Visibility = tabs.SelectedIndex == 2 ? Visibility.Collapsed : Visibility.Visible;
            body.RowDefinitions[1].Height = new GridLength(tabs.SelectedIndex == 2 ? 0 : 12);
            tabs.SelectionChanged += delegate(object sender, SelectionChangedEventArgs e)
            {
                if (e.Source != tabs) return;
                activeSection = tabs.SelectedIndex == 2 ? "目标" : tabs.SelectedIndex == 1 ? "待办" : "日程";
                monthCard.Visibility = tabs.SelectedIndex == 2 ? Visibility.Collapsed : Visibility.Visible;
                body.RowDefinitions[1].Height = new GridLength(tabs.SelectedIndex == 2 ? 0 : 12);
                FrameworkElement page = tabs.SelectedContent as FrameworkElement; if (page != null) Motion.Enter(page);
            };
            Theme.Put(body, tabs, 0, 2);
        }
        Border BuildDay(bool small)
        {
            Grid grid = new Grid(); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); grid.RowDefinitions.Add(new RowDefinition()); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid head = Theme.Grid(2); dayLabel = Theme.Text("", small ? 17 : 21, null, true); Theme.Put(head, dayLabel);
            Button add = Theme.Button("＋ 日程", delegate { EditEvent(null); }, "Primary"); add.Padding = new Thickness(12, 7, 12, 7); add.HorizontalAlignment = HorizontalAlignment.Right; Theme.Put(head, add, 1); Theme.Put(grid, head);
            daySubtitle = Theme.Text("", 11, Theme.Muted); daySubtitle.Margin = new Thickness(0, 9, 0, 17); Theme.Put(grid, daySubtitle, 0, 1);
            dayList = new StackPanel(); Theme.Put(grid, new ScrollViewer { Content = dayList }, 0, 2);
            if (!small)
            {
                StackPanel tip = new StackPanel();
                tip.Children.Add(Theme.Text("让安排，随身而行。", 14, Theme.Green, true)); Theme.Gap(tip, 7);
                TextBlock copy = Theme.Text("导出日程后，用 iPhone 的邮件 App\n打开附件，即可添加到苹果日历。", 12, Theme.Muted); copy.TextWrapping = TextWrapping.Wrap; copy.LineHeight = 21; tip.Children.Add(copy);
                Button b = Theme.Button("导出我的日程  ↗", Export, "Quiet"); b.HorizontalAlignment = HorizontalAlignment.Left; b.Margin = new Thickness(-8, 8, 0, 0); tip.Children.Add(b);
                Border note = new Border { Background = Theme.Canvas, CornerRadius = new CornerRadius(12), Padding = new Thickness(16), Margin = new Thickness(0, 14, 0, 0), Child = tip };
                Theme.Put(grid, note, 0, 3);
            }
            return Theme.Card(grid, small ? 15 : 22);
        }
        Border BuildTodos(bool small)
        {
            Grid grid = new Grid(); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); grid.RowDefinitions.Add(new RowDefinition());
            Grid top = Theme.Grid(2);
            StackPanel title = new StackPanel { Orientation = Orientation.Horizontal };
            title.Children.Add(Theme.Text("待办清单", 17, null, true)); todoCount = Theme.Text("", 11, Theme.Muted); todoCount.Margin = new Thickness(10, 0, 0, 0); title.Children.Add(todoCount); Theme.Put(top, title);
            Button add = Theme.Button("＋ 待办", delegate { EditTodo(null); }, "Quiet"); add.HorizontalAlignment = HorizontalAlignment.Right; Theme.Put(top, add, 1); Theme.Put(grid, top);
            Grid filters = Theme.Grid(2); filters.ColumnDefinitions[0].Width = new GridLength(110); filters.Margin = new Thickness(0, 10, 0, 10);
            ComboBox filter = new ComboBox { ItemsSource = new[] { "未完成", "今天", "已完成", "全部" }, SelectedItem = todoFilter, MinHeight = 29, FontSize = 11, Padding = new Thickness(5) };
            filter.SelectionChanged += delegate { todoFilter = (string)filter.SelectedItem; RenderTodos(); }; Theme.Put(filters, filter);
            TextBox find = new TextBox { Text = search, Margin = new Thickness(10, 0, 0, 0), FontSize = 12, Padding = new Thickness(8, 4, 8, 4), ToolTip = "搜索待办标题或备注" };
            find.SetValue(System.Windows.Automation.AutomationProperties.NameProperty, "搜索待办");
            Grid searchBox = new Grid { Margin = new Thickness(0) };
            TextBlock placeholder = Theme.Text("搜索待办…", 12, Theme.Muted); placeholder.Margin = new Thickness(19, 0, 0, 0); placeholder.IsHitTestVisible = false; placeholder.Visibility = search.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            searchBox.Children.Add(find); searchBox.Children.Add(placeholder);
            find.TextChanged += delegate { search = find.Text; placeholder.Visibility = search.Length == 0 ? Visibility.Visible : Visibility.Collapsed; RenderTodos(); }; Theme.Put(filters, searchBox, 1);
            Theme.Put(grid, filters, 0, 1); todoList = new StackPanel(); Theme.Put(grid, new ScrollViewer { Content = todoList }, 0, 2);
            return Theme.Card(grid, small ? 15 : 20);
        }
        void Refresh()
        {
            monthLabel.Text = month.ToString("yyyy 年 M 月");
            dayLabel.Text = selected.ToString("M 月 d 日");
            List<CalendarEvent> events = store.Data.Events.Where(x => x.OnDay(selected)).OrderBy(x => !x.AllDay).ThenBy(x => x.StartAt).ToList();
            daySubtitle.Text = Dates.Weekday(selected) + (selected.Date == DateTime.Today ? "  ·  今天" : "") + "  /  " + events.Count + " 项日程";
            RenderCalendar(); RenderDay(events); RenderTodos(); RenderGoals();
            if (floatingBar != null) floatingBar.Refresh();
        }
        void MoveMonth(int direction)
        {
            DateTime next = month.AddMonths(direction); if (next.Year < 1900 || next.Year > 2100) return;
            month = next; selected = month.AddDays(Math.Min(selected.Day, DateTime.DaysInMonth(month.Year, month.Month)) - 1); Refresh();
        }
        Brush CategoryColor(string category) { return category == "工作" ? Theme.Brush("#6685B0") : category == "个人" ? Theme.Brush("#B08C62") : Theme.Green; }
        void RenderCalendar()
        {
            calendar.Children.Clear(); calendar.RowDefinitions.Clear(); calendar.ColumnDefinitions.Clear();
            for (int c = 0; c < 7; c++) calendar.ColumnDefinitions.Add(new ColumnDefinition());
            for (int r = 0; r < 6; r++) calendar.RowDefinitions.Add(new RowDefinition());
            DateTime start = month.AddDays(-(((int)month.DayOfWeek + 6) % 7));
            for (int i = 0; i < 42; i++)
            {
                DateTime date = start.AddDays(i); bool chosen = date == selected, today = date == DateTime.Today;
                Grid content = new Grid(); content.RowDefinitions.Add(new RowDefinition()); content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(compact ? 8 : 6) });
                TextBlock number = Theme.Text(date.Day.ToString(), compact ? 12 : 14, chosen ? Brushes.White : (date.Month != month.Month ? Theme.Brush("#BAC4BF") : (today ? Theme.Green : Theme.Ink)), chosen || today); number.HorizontalAlignment = HorizontalAlignment.Center; Theme.Put(content, number);
                StackPanel dots = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top };
                foreach (string category in store.Data.Events.Where(x => x.OnDay(date)).Select(x => x.Category).Distinct().Take(3))
                    dots.Children.Add(new Border { Width = 4, Height = 4, CornerRadius = new CornerRadius(2), Background = chosen ? Brushes.White : CategoryColor(category), Margin = new Thickness(1) });
                if (store.Data.Todos.Any(t => !t.Done && t.Due == Dates.Day(date)))
                    dots.Children.Add(new Border { Width = 4, Height = 4, CornerRadius = new CornerRadius(2), BorderBrush = chosen ? Brushes.White : Theme.Muted, BorderThickness = new Thickness(1), Margin = new Thickness(1) });
                Theme.Put(content, dots, 0, 1);
                Button cell = Theme.Button("", delegate { selected = date; if (date.Month != month.Month || date.Year != month.Year) month = new DateTime(date.Year, date.Month, 1); Refresh(); });
                cell.Content = content; cell.Padding = new Thickness(0); cell.Margin = new Thickness(compact ? 2 : 1); cell.Background = chosen ? Glass.Accent : (today ? Theme.Pale : Brushes.Transparent); cell.BorderThickness = new Thickness(0); cell.HorizontalContentAlignment = HorizontalAlignment.Stretch; cell.VerticalContentAlignment = VerticalAlignment.Stretch;
                cell.ToolTip = date.ToString("yyyy 年 M 月 d 日") + " · " + Dates.Weekday(date);
                cell.SetValue(System.Windows.Automation.AutomationProperties.NameProperty, Dates.Day(date));
                cell.MouseDoubleClick += delegate { selected = date; EditEvent(null); };
                Theme.Put(calendar, cell, i % 7, i / 7);
            }
        }
        void Empty(Panel list, string title, string detail, string action, Action click)
        {
            StackPanel p = new StackPanel { Margin = new Thickness(6, compact ? 12 : 20, 6, 8), HorizontalAlignment = HorizontalAlignment.Center };
            TextBlock heading = Theme.Text(title, 14, Theme.Muted); heading.HorizontalAlignment = HorizontalAlignment.Center; p.Children.Add(heading); Theme.Gap(p, 8);
            TextBlock note = Theme.Text(detail, 11, Theme.Muted); note.TextAlignment = TextAlignment.Center; note.TextWrapping = TextWrapping.Wrap; p.Children.Add(note);
            if (action.Length > 0) { Button b = Theme.Button(action, click, "Quiet"); b.Margin = new Thickness(0, 10, 0, 0); b.Foreground = Theme.Green; p.Children.Add(b); }
            list.Children.Add(p);
        }
        string EventTime(CalendarEvent e)
        {
            if (e.AllDay) return e.StartAt.Date == e.LastDay ? "全天" : "全天 · " + e.StartAt.ToString("M/d") + "—" + e.LastDay.ToString("M/d");
            return e.StartAt.Date == e.LastDay ? e.StartAt.ToString("HH:mm") + " — " + e.EndAt.ToString("HH:mm") : e.StartAt.ToString("M/d HH:mm") + " — " + e.EndAt.ToString("M/d HH:mm");
        }
        void RenderDay(List<CalendarEvent> events)
        {
            dayList.Children.Clear();
            if (events.Count == 0) { Empty(dayList, "今天的时间，由你安排", "给重要的事留一个位置。", "＋ 添加第一条日程", delegate { EditEvent(null); }); return; }
            foreach (CalendarEvent e in events)
            {
                StackPanel text = new StackPanel();
                Grid line = Theme.Grid(2); line.ColumnDefinitions[1].Width = GridLength.Auto;
                Theme.Put(line, Theme.Text(EventTime(e), 11, CategoryColor(e.Category), true)); TextBlock category = Theme.Text(e.Category, 10, Theme.Muted); category.HorizontalAlignment = HorizontalAlignment.Right; Theme.Put(line, category, 1); text.Children.Add(line); Theme.Gap(text, 8);
                TextBlock title = Theme.Text(e.Title, 14, null, true); title.TextWrapping = TextWrapping.Wrap; text.Children.Add(title);
                if (e.Location.Length > 0) { Theme.Gap(text, 7); TextBlock location = Theme.Text("地点 · " + e.Location, 11, Theme.Muted); location.TextWrapping = TextWrapping.Wrap; text.Children.Add(location); }
                if (e.Notes.Length > 0 && !compact) { Theme.Gap(text, 7); TextBlock note = Theme.Text(e.Notes.Replace("\n", " "), 11, Theme.Muted); note.TextTrimming = TextTrimming.CharacterEllipsis; text.Children.Add(note); }
                if (!compact) { Theme.Gap(text, 9); text.Children.Add(Theme.Text(e.Reminder < 0 ? "无提醒" : (e.Reminder == 0 ? "开始时提醒" : "提前 " + (e.Reminder == 1440 ? "1 天" : e.Reminder + " 分钟") + "提醒"), 10, Theme.Muted)); }
                Border stripe = new Border { BorderBrush = CategoryColor(e.Category), BorderThickness = new Thickness(3, 0, 0, 0), Padding = new Thickness(12, 1, 0, 1), Child = text };
                Button row = Theme.Button("", delegate { EditEvent(e); }); row.Content = stripe; row.HorizontalContentAlignment = HorizontalAlignment.Stretch; row.Padding = new Thickness(12); row.Margin = new Thickness(0, 0, 0, 11); row.Background = Theme.Canvas; row.BorderThickness = new Thickness(0); row.ToolTip = "点击编辑日程或删除"; dayList.Children.Add(row);
            }
        }
        void RenderTodos()
        {
            if (todoList == null) return;
            todoList.Children.Clear(); int open = store.Data.Todos.Count(x => !x.Done);
            todoCount.Text = open + " 项未完成";
            IEnumerable<Todo> list = store.Data.Todos;
            if (todoFilter == "未完成") list = list.Where(x => !x.Done);
            else if (todoFilter == "已完成") list = list.Where(x => x.Done);
            else if (todoFilter == "今天") list = list.Where(x => !x.Done && x.Due == Dates.Day(DateTime.Today));
            if (search.Length > 0) list = list.Where(x => x.Title.IndexOf(search, StringComparison.CurrentCultureIgnoreCase) >= 0 || x.Notes.IndexOf(search, StringComparison.CurrentCultureIgnoreCase) >= 0);
            List<Todo> sorted = list.OrderBy(x => x.Done).ThenByDescending(x => x.Important).ThenBy(x => String.IsNullOrEmpty(x.Due) ? "9999-12-31" : x.Due).ToList();
            if (sorted.Count == 0)
            {
                Empty(todoList, search.Length > 0 ? "没有找到匹配的待办" : (todoFilter == "已完成" ? "完成的小事，会留在这里" : "清单很轻，心情也是"), search.Length > 0 ? "试试其他关键词。" : "写下一件想完成的小事。", search.Length > 0 ? "" : "＋ 添加待办", delegate { EditTodo(null); }); return;
            }
            foreach (Todo t in sorted)
            {
                Grid row = Theme.Grid(3); row.ColumnDefinitions[0].Width = new GridLength(28); row.ColumnDefinitions[2].Width = GridLength.Auto; row.Margin = new Thickness(0, 3, 0, 3);
                CheckBox check = new CheckBox { IsChecked = t.Done, VerticalAlignment = VerticalAlignment.Center, ToolTip = t.Done ? "恢复为未完成" : "标记为完成" };
                check.SetValue(System.Windows.Automation.AutomationProperties.NameProperty, "完成待办 " + t.Title);
                check.Click += delegate { Motion.Remember(check, true); Commit(delegate { t.Done = check.IsChecked == true; }, "待办已保存"); }; Theme.Put(row, check);
                StackPanel text = new StackPanel(); TextBlock title = Theme.Text(t.Title, 13, t.Done ? Theme.Muted : Theme.Ink); title.TextWrapping = TextWrapping.Wrap; if (t.Done) title.TextDecorations = TextDecorations.Strikethrough; text.Children.Add(title);
                LongTermGoal goal = store.Data.Goals.FirstOrDefault(g => g.Id == t.GoalId);
                if (goal != null) { TextBlock goalLabel = Theme.Text("目标 · " + goal.Title, 10, Theme.Green); goalLabel.TextTrimming = TextTrimming.CharacterEllipsis; goalLabel.Margin = new Thickness(0, 3, 0, 0); text.Children.Add(goalLabel); }
                if (t.Due.Length > 0)
                {
                    DateTime due = Dates.ParseDay(t.Due); bool overdue = !t.Done && due < DateTime.Today;
                    string dateText = due == DateTime.Today ? "今天" : due == DateTime.Today.AddDays(1) ? "明天" : due.ToString("M 月 d 日");
                    if (due.Year != DateTime.Today.Year) dateText = due.ToString("yyyy/M/d");
                    TextBlock date = Theme.Text((overdue ? "已逾期 · " : "") + dateText, 10, overdue ? Theme.Brush("#B36A55") : Theme.Muted); date.Margin = new Thickness(0, 4, 0, 0); text.Children.Add(date);
                }
                Button edit = Theme.Button("", delegate { EditTodo(t); }, "Quiet"); edit.Content = text; edit.HorizontalContentAlignment = HorizontalAlignment.Left; edit.Padding = new Thickness(0, 4, 8, 4); edit.ToolTip = "编辑待办"; Theme.Put(row, edit, 1);
                Button star = Theme.Button(t.Important ? "★" : "☆", delegate { Commit(delegate { t.Important = !t.Important; }, "已更新重要标记"); }, "Quiet"); star.Foreground = t.Important ? Theme.Brush("#B58A4C") : Theme.Brush("#B3BEB7"); star.FontSize = 17; star.ToolTip = "标记为重要"; Theme.Put(row, star, 2);
                todoList.Children.Add(row);
            }
        }
        void TogglePin()
        {
            if (Commit(delegate { store.Data.Settings.Topmost = !pinned; }, "已保存置顶设置")) { pinned = store.Data.Settings.Topmost; Topmost = pinned; pinButton.Content = pinned ? "已置顶" : "置顶"; }
        }
        void ToggleMode()
        {
            bool next = !compact;
            if (!Commit(delegate { store.Data.Settings.Compact = next; }, "已保存桌面模式设置")) return;
            if (!compact) { fullWidth = ActualWidth; fullHeight = ActualHeight; }
            compact = next; WindowState = WindowState.Normal; MinWidth = compact ? 400 : 980;
            Width = compact ? 460 : Math.Min(fullWidth, SystemParameters.WorkArea.Width); Height = compact ? Math.Min(790, SystemParameters.WorkArea.Height) : Math.Min(fullHeight, SystemParameters.WorkArea.Height);
            if (Left + Width > SystemParameters.WorkArea.Right) Left = Math.Max(SystemParameters.WorkArea.Left, SystemParameters.WorkArea.Right - Width);
            if (Top + Height > SystemParameters.WorkArea.Bottom) Top = Math.Max(SystemParameters.WorkArea.Top, SystemParameters.WorkArea.Bottom - Height);
            Build();
        }
        public void EditEvent(CalendarEvent item)
        {
            EventDialog dialog = new EventDialog(this, selected, item);
            if (dialog.ShowDialog() != true) return;
            if (dialog.Deleted)
            {
                Commit(delegate { store.Data.Events.RemoveAll(x => x.Id == item.Id); }, "日程已删除"); return;
            }
            CalendarEvent result = dialog.Result;
            Commit(delegate { store.Data.Events.RemoveAll(x => x.Id == result.Id); store.Data.Events.Add(result); }, "日程已保存 · 可随时导入苹果日历");
        }
        public void EditTodo(Todo item)
        {
            EditTodo(item, null);
        }
        void EditTodo(Todo item, string goalId)
        {
            TodoDialog dialog = new TodoDialog(this, selected, item, store.Data.Goals, goalId);
            if (dialog.ShowDialog() != true) return;
            if (dialog.Deleted) { Commit(delegate { store.Data.Todos.RemoveAll(x => x.Id == item.Id); }, "待办已删除"); return; }
            Todo result = dialog.Result;
            Commit(delegate { store.Data.Todos.RemoveAll(x => x.Id == result.Id); store.Data.Todos.Add(result); }, "待办已保存");
        }
        void Export()
        {
            ExportDialog dialog = new ExportDialog(this, store.Data, selected);
            if (dialog.ShowDialog() == true) Say("日程已导出 · 用 iPhone 邮件打开 .ics 附件即可添加");
        }
        void More()
        {
            ContextMenu menu = new ContextMenu();
            AddMenu(menu, "极简悬浮条", EnterFloating);
            MenuItem startup = new MenuItem { Header = "开机自启动", IsCheckable = true, IsChecked = StartupEnabled() }; startup.Click += delegate { ToggleStartup(); }; menu.Items.Add(startup);
            menu.Items.Add(new Separator());
            AddMenu(menu, "备份全部数据…", Backup); AddMenu(menu, "从备份恢复…", Recover); AddMenu(menu, "打开数据文件夹", delegate { Open(store.Folder); });
            menu.Items.Add(new Separator()); AddMenu(menu, "使用说明 / iPhone 导入步骤", Help); AddMenu(menu, "隐藏到系统托盘", delegate { Hide(); }); AddMenu(menu, "退出日常", Close);
            menu.IsOpen = true;
        }
        static void AddMenu(ContextMenu menu, string title, Action click) { MenuItem item = new MenuItem { Header = title }; item.Click += delegate { click(); }; menu.Items.Add(item); }
        void Backup()
        {
            SaveFileDialog dialog = new SaveFileDialog { Title = "备份日程、待办与目标", Filter = "日常备份 (*.json)|*.json", FileName = "日常备份-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".json" };
            if (dialog.ShowDialog(this) != true) return;
            try { store.ExportBackup(dialog.FileName); Say("全部数据已备份"); } catch (Exception e) { MessageBox.Show(this, e.Message, "备份失败"); }
        }
        void Recover()
        {
            OpenFileDialog dialog = new OpenFileDialog { Title = "选择日常备份", Filter = "日常备份 (*.json)|*.json" };
            if (dialog.ShowDialog(this) != true) return;
            try
            {
                AppData replacement = Store.ReadFile(dialog.FileName);
                if (MessageBox.Show(this, "将恢复 " + replacement.Events.Count + " 条日程、" + replacement.Todos.Count + " 条待办和 " + replacement.Goals.Count + " 个目标，并替换当前内容。\n当前数据会先自动备份。是否继续？", "恢复备份", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                store.ExportBackup(Path.Combine(store.Folder, "before-restore-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".json"));
                Preferences currentSettings = store.Data.Settings;
                if (Commit(delegate { store.Data = replacement; store.Data.Settings = currentSettings; }, "数据已恢复")) notified.Clear();
            }
            catch (Exception e) { MessageBox.Show(this, e.Message, "无法恢复备份", MessageBoxButton.OK, MessageBoxImage.Warning); }
        }
        public static void Open(string path) { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        void Help()
        {
            string path = Path.Combine(Program.BaseFolder, "使用说明.txt");
            if (File.Exists(path)) Open(path); else MessageBox.Show(this, "选择日期 → ＋ 日程 / ＋ 待办。\n\n导入苹果日历：点击“导入苹果日历”导出 .ics，发送为邮件附件，在 iPhone 系统“邮件”App 中打开并添加。\n\n“桌面模式”切换紧凑组件；“置顶”保持在其他窗口之上。\n关闭窗口会退出，更多菜单可隐藏到托盘。\n提醒需要日常保持运行。", "日常 · 使用说明");
        }
        void Tick(object sender, EventArgs args)
        {
            DateTime now = DateTime.Now;
            if (lastDate != now.Date) { lastDate = now.Date; if (selected == now.Date.AddDays(-1)) { selected = now.Date; month = new DateTime(now.Year, now.Month, 1); } Refresh(); }
            foreach (CalendarEvent e in store.Data.Events.Where(x => x.Reminder >= 0 && !x.AllDay))
            {
                DateTime alarm = e.StartAt.AddMinutes(-e.Reminder);
                string key = e.Id + "|" + e.Start + "|" + e.Reminder;
                if (alarm <= now && alarm > lastTick && e.EndAt > now && !notified.Contains(key))
                {
                    notified.Add(key); tray.BalloonTipTitle = e.Title; tray.BalloonTipText = e.StartAt.ToString("HH:mm") + (e.Location.Length > 0 ? " · " + e.Location : "") + "\n日常 · 日程提醒";
                    tray.ShowBalloonTip(8000);
                }
            }
            lastTick = now;
        }
    }
}
