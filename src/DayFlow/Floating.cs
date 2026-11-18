using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Forms = System.Windows.Forms;
using Path = System.IO.Path;

namespace DayFlow
{
    public static class StartupRegistration
    {
        public static string ShortcutPath { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "DayFlowCalendar.lnk"); } }
        static string Executable { get { return Assembly.GetExecutingAssembly().Location; } }
        static void ValidatePath(string path)
        {
            if (String.IsNullOrWhiteSpace(path) || path.Contains('"')) throw new ArgumentException("程序路径或数据路径无效。");
        }
        static string Quote(string path)
        {
            int trailing = path.Length - path.TrimEnd('\\').Length;
            return "\"" + path + new string('\\', trailing) + "\"";
        }
        public static string Arguments(string folder)
        {
            ValidatePath(folder);
            string full = Path.GetFullPath(folder);
            if (full != Path.GetPathRoot(full)) full = full.TrimEnd('\\');
            return "--startup --data-dir " + Quote(full);
        }
        public static string Command(string executable, string dataFolder)
        {
            ValidatePath(executable);
            return Quote(Path.GetFullPath(executable)) + " " + Arguments(dataFolder);
        }
        static object Property(object target, string name) { return target.GetType().InvokeMember(name, BindingFlags.GetProperty, null, target, null); }
        static void Property(object target, string name, object value) { target.GetType().InvokeMember(name, BindingFlags.SetProperty, null, target, new[] { value }); }
        static object OpenShortcut(string path, out object shell)
        {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell", true));
            return shell.GetType().InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { path });
        }
        static void Release(object item) { if (item != null && Marshal.IsComObject(item)) Marshal.FinalReleaseComObject(item); }
        public static void SaveShortcut(string path, string executable, string folder)
        {
            ValidatePath(executable); string args = Arguments(folder);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            object shell = null, link = null;
            try
            {
                link = OpenShortcut(path, out shell);
                Property(link, "TargetPath", Path.GetFullPath(executable)); Property(link, "Arguments", args);
                Property(link, "WorkingDirectory", Path.GetDirectoryName(Path.GetFullPath(executable)));
                Property(link, "Description", "日常日历 · 登录后恢复日历或极简悬浮条");
                Property(link, "IconLocation", Path.GetFullPath(executable) + ",0");
                link.GetType().InvokeMember("Save", BindingFlags.InvokeMethod, null, link, null);
            }
            finally { Release(link); Release(shell); }
        }
        public static bool MatchesShortcut(string path, string executable, string folder)
        {
            if (!File.Exists(path)) return false;
            object shell = null, link = null;
            try
            {
                link = OpenShortcut(path, out shell);
                return String.Equals((string)Property(link, "TargetPath"), Path.GetFullPath(executable), StringComparison.OrdinalIgnoreCase)
                    && String.Equals((string)Property(link, "Arguments"), Arguments(folder), StringComparison.OrdinalIgnoreCase);
            }
            catch (COMException) { return false; }
            finally { Release(link); Release(shell); }
        }
        public static bool IsEnabled(string folder) { return MatchesShortcut(ShortcutPath, Executable, folder); }
        public static void SetEnabled(bool enabled, string folder)
        {
            if (enabled) SaveShortcut(ShortcutPath, Executable, folder);
            else if (File.Exists(ShortcutPath)) File.Delete(ShortcutPath);
            if (IsEnabled(folder) != enabled) throw new IOException("开机自启动设置未能保存。");
        }
    }

    public static class TodayTasks
    {
        // Keep data order: completing a dot must not move another task under the pointer.
        public static List<Todo> For(AppData data, DateTime day)
        {
            string date = Dates.Day(day);
            return data.Todos.Where(t => t.Due == date).ToList();
        }
    }

    public static class FloatingPlacement
    {
        public static Rect Clamp(Rect desired, IEnumerable<Rect> workAreas)
        {
            List<Rect> areas = workAreas.Where(a => a.Width > 0 && a.Height > 0).ToList();
            if (areas.Count == 0) areas.Add(new Rect(0, 0, 1024, 768));
            Rect area = areas.OrderByDescending(a => { Rect intersection = Rect.Intersect(a, desired); return intersection.IsEmpty ? 0 : intersection.Width * intersection.Height; })
                .ThenBy(a => Math.Pow(a.Left + a.Width / 2 - desired.Left - desired.Width / 2, 2) + Math.Pow(a.Top + a.Height / 2 - desired.Top - desired.Height / 2, 2)).First();
            double width = Math.Min(desired.Width, area.Width), height = Math.Min(desired.Height, area.Height);
            return new Rect(Math.Max(area.Left, Math.Min(desired.Left, area.Right - width)), Math.Max(area.Top, Math.Min(desired.Top, area.Bottom - height)), width, height);
        }
    }

    public sealed class FloatingBar : Window
    {
        readonly Store store;
        readonly Action restore;
        readonly Func<string, bool> toggle;
        readonly TextBlock summary, caption;
        readonly WrapPanel dots;
        readonly ScrollViewer scroll;
        readonly Action<double, double> savePosition;
        readonly Dictionary<string, Button> buttons = new Dictionary<string, Button>();
        bool positioning, shuttingDown;
        public FloatingBar(Store store, Action restore, Action add, Action menu, Func<string, bool> toggle, Action<double, double> savePosition)
        {
            this.store = store; this.restore = restore; this.toggle = toggle; this.savePosition = savePosition;
            Style = (Style)Application.Current.FindResource(typeof(Window)); Glass.Configure(this);
            Title = "日常 · 今日悬浮条"; Width = 324; Height = 84; MinWidth = 324; MinHeight = 84;
            ResizeMode = ResizeMode.NoResize; Topmost = true; ShowInTaskbar = false; WindowStartupLocation = WindowStartupLocation.Manual;
            System.Windows.Shell.WindowChrome chrome = System.Windows.Shell.WindowChrome.GetWindowChrome(this); chrome.ResizeBorderThickness = new Thickness(0);
            Grid root = new Grid { Margin = new Thickness(17, 11, 17, 10) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid head = Theme.Grid(2); head.ColumnDefinitions[1].Width = GridLength.Auto; head.Background = Brushes.Transparent;
            StackPanel title = new StackPanel { Orientation = Orientation.Horizontal };
            TextBlock grip = Theme.Text("⠿", 16, Theme.Muted); grip.Margin = new Thickness(0, 0, 8, 0); title.Children.Add(grip);
            title.Children.Add(Theme.Text("今天", 13, null, true));
            summary = Theme.Text("", 11, Theme.Muted); summary.Margin = new Thickness(9, 0, 0, 0); title.Children.Add(summary); Theme.Put(head, title);
            StackPanel actions = new StackPanel { Orientation = Orientation.Horizontal };
            actions.Children.Add(Small("＋", "添加今天的待办", add));
            actions.Children.Add(Small("↗", "展开日历", restore));
            actions.Children.Add(Small("···", "悬浮条设置", menu)); Theme.Put(head, actions, 1);
            head.ToolTip = "拖动移动 · 双击展开日历";
            head.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                DependencyObject node = e.OriginalSource as DependencyObject;
                while (node != null && node != head)
                {
                    if (node is System.Windows.Controls.Primitives.ButtonBase) return;
                    node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
                }
                if (e.ClickCount == 2) restore();
                else if (e.LeftButton == MouseButtonState.Pressed) { DragMove(); Constrain(); savePosition(Left, Top); }
                e.Handled = true;
            };
            Theme.Put(root, head);
            Grid line = new Grid { Margin = new Thickness(2, 7, 0, 0) };
            dots = new WrapPanel { Width = 264 };
            scroll = new ScrollViewer { Content = dots, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 96 };
            line.Children.Add(scroll);
            caption = Theme.Text("今天暂无待办", 11, Theme.Muted); caption.Margin = new Thickness(0, 3, 0, 3); line.Children.Add(caption);
            Theme.Put(root, line, 0, 1); Content = Glass.Wrap(this, root, false);
            Loaded += delegate { Constrain(); };
            Closing += delegate(object sender, System.ComponentModel.CancelEventArgs e) { if (!shuttingDown) { e.Cancel = true; restore(); } };
            Refresh();
        }
        Button Small(string text, string name, Action click)
        {
            Button button = Theme.Button(text, click, "Quiet"); button.Width = 26; button.Height = 25; button.Padding = new Thickness(0); button.Margin = new Thickness(1, 0, 0, 0); button.FontSize = 16; button.ToolTip = name;
            AutomationProperties.SetName(button, name); return button;
        }
        public void Place()
        {
            Preferences settings = store.Data.Settings;
            Rect area = SystemParameters.WorkArea;
            Left = settings.HasFloatingPosition && Valid(settings.FloatingLeft) ? settings.FloatingLeft : area.Right - Width - 26;
            Top = settings.HasFloatingPosition && Valid(settings.FloatingTop) ? settings.FloatingTop : area.Top + 40;
            Constrain();
        }
        static bool Valid(double n) { return !Double.IsNaN(n) && !Double.IsInfinity(n) && Math.Abs(n) < 1000000; }
        public void Constrain()
        {
            if (positioning || Double.IsNaN(Left) || Double.IsNaN(Top)) return;
            positioning = true;
            try
            {
                PresentationSource source = PresentationSource.FromVisual(this);
                double scale = source == null || source.CompositionTarget == null ? 1 : source.CompositionTarget.TransformToDevice.M11;
                IEnumerable<Rect> areas = Forms.Screen.AllScreens.Select(s => new Rect(s.WorkingArea.Left / scale, s.WorkingArea.Top / scale, s.WorkingArea.Width / scale, s.WorkingArea.Height / scale));
                Rect fit = FloatingPlacement.Clamp(new Rect(Left, Top, Width, Height), areas); Left = fit.Left; Top = fit.Top;
            }
            finally { positioning = false; }
        }
        public void Refresh()
        {
            List<Todo> tasks = TodayTasks.For(store.Data, DateTime.Today);
            summary.Text = tasks.Count == 0 ? "0 项待办" : tasks.Count(t => t.Done) + " / " + tasks.Count + " 已完成";
            summary.ToolTip = "仅统计日期为今天的待办（含长期目标子任务）";
            HashSet<string> current = new HashSet<string>(tasks.Select(t => t.Id));
            foreach (string id in buttons.Keys.Where(id => !current.Contains(id)).ToList()) { dots.Children.Remove(buttons[id]); buttons.Remove(id); }
            for (int i = 0; i < tasks.Count; i++)
            {
                Todo task = tasks[i]; Button button;
                if (!buttons.TryGetValue(task.Id, out button))
                {
                    string id = task.Id;
                    button = Theme.Button("", delegate { toggle(id); }, "Quiet");
                    button.Width = 24; button.Height = 24; button.Margin = new Thickness(0); button.Padding = new Thickness(0);
                    button.Content = new Ellipse { Width = 9, Height = 9, StrokeThickness = 1.3 };
                    buttons.Add(id, button); dots.Children.Insert(Math.Min(i, dots.Children.Count), button);
                }
                Ellipse dot = (Ellipse)button.Content;
                dot.Stroke = task.Done ? Theme.Brush("#46A996") : Theme.Brush("#8193A8");
                dot.Fill = task.Done ? Glass.Gradient("#7BDFC2", "#55AEDD") : Theme.Brush("#1C65768B");
                dot.Effect = task.Done ? new System.Windows.Media.Effects.DropShadowEffect { Color = Color.FromRgb(72, 190, 166), BlurRadius = 7, ShadowDepth = 0, Opacity = .65 } : null;
                button.ToolTip = task.Title + (task.Done ? "\n已完成 · 点击恢复为未完成" : "\n未完成 · 点击标记完成");
                AutomationProperties.SetName(button, "今日待办 " + task.Title);
                AutomationProperties.SetItemStatus(button, task.Done ? "已完成" : "未完成");
            }
            caption.Visibility = tasks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            scroll.Visibility = tasks.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            Height = 60 + Math.Min(4, Math.Max(1, (tasks.Count + 10) / 11)) * 24;
            Constrain();
        }
        public void Confirm(string id)
        {
            Button button; if (!buttons.TryGetValue(id, out button)) return;
            Ellipse dot = (Ellipse)button.Content;
            ScaleTransform scale = new ScaleTransform(1, 1); dot.RenderTransformOrigin = new Point(.5, .5); dot.RenderTransform = scale;
            Motion.To(scale, ScaleTransform.ScaleXProperty, 1, 220, .7); Motion.To(scale, ScaleTransform.ScaleYProperty, 1, 220, .7);
        }
        public void Shutdown() { shuttingDown = true; Close(); }
    }

    public partial class MainWindow
    {
        FloatingBar floatingBar;
        public FloatingBar CurrentFloatingBar { get { return floatingBar; } }
        public void EnterFloating()
        {
            if (!Commit(delegate { store.Data.Settings.Floating = true; }, "已切换极简悬浮条")) return;
            ShowFloating();
        }
        void ShowFloating()
        {
            if (floatingBar == null)
                floatingBar = new FloatingBar(store, Restore, delegate { Restore(); selected = DateTime.Today; month = new DateTime(selected.Year, selected.Month, 1); Refresh(); EditTodo(null); }, FloatingMenu, ToggleFloatingTodo, SaveFloatingPosition);
            floatingBar.Refresh(); Hide(); floatingBar.Show(); floatingBar.Place(); floatingBar.Activate();
        }
        bool ToggleFloatingTodo(string id)
        {
            // Re-resolve after rollback, deletion or data recovery; do not retain a stale Todo reference.
            Todo item = TodayTasks.For(store.Data, DateTime.Today).FirstOrDefault(t => t.Id == id);
            if (item == null) { floatingBar.Refresh(); return false; }
            bool saved = Commit(delegate { item.Done = !item.Done; }, item.Done ? "待办已恢复" : "待办已完成");
            if (saved) floatingBar.Confirm(id); return saved;
        }
        void SaveFloatingPosition(double left, double top)
        {
            try { store.Change(delegate { store.Data.Settings.HasFloatingPosition = true; store.Data.Settings.FloatingLeft = left; store.Data.Settings.FloatingTop = top; }); }
            catch (Exception e) { MessageBox.Show(floatingBar, "悬浮条位置未能保存：\n" + e.Message, "日常"); }
        }
        bool StartupEnabled()
        {
            try { return StartupRegistration.IsEnabled(store.Folder); } catch { return false; }
        }
        void ToggleStartup()
        {
            try { StartupRegistration.SetEnabled(!StartupRegistration.IsEnabled(store.Folder), store.Folder); Say(StartupEnabled() ? "已开启开机自启动" : "已关闭开机自启动"); }
            catch (Exception e) { MessageBox.Show(floatingBar != null && floatingBar.IsVisible ? (Window)floatingBar : this, "无法更改开机自启动：\n" + e.Message, "日常", MessageBoxButton.OK, MessageBoxImage.Warning); }
        }
        void FloatingMenu()
        {
            ContextMenu menu = new ContextMenu();
            AddMenu(menu, "展开日历", Restore);
            MenuItem startup = new MenuItem { Header = "开机自启动", IsCheckable = true, IsChecked = StartupEnabled() }; startup.Click += delegate { ToggleStartup(); }; menu.Items.Add(startup);
            AddMenu(menu, "隐藏到托盘", delegate { floatingBar.Hide(); });
            menu.Items.Add(new Separator()); AddMenu(menu, "退出日常", Close);
            menu.PlacementTarget = floatingBar; menu.IsOpen = true;
        }
    }
}
