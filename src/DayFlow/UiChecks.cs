using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Effects;
using System.Windows.Media.Animation;
using System.Windows.Input;
using System.Windows.Threading;

namespace DayFlow
{
    public static class UiChecks
    {
        static List<string> log = new List<string>();
        static IEnumerable<DependencyObject> Tree(DependencyObject root)
        {
            yield return root;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
                foreach (DependencyObject child in Tree(VisualTreeHelper.GetChild(root, i))) yield return child;
        }
        static T Find<T>(DependencyObject root, Func<T, bool> predicate) where T : DependencyObject
        {
            FrameworkElement element = root as FrameworkElement; if (element != null) element.UpdateLayout();
            return Tree(root).OfType<T>().First(predicate);
        }
        static Button Button(DependencyObject root, string name) { return Find<Button>(root, b => b.Content is string && (string)b.Content == name); }
        static void Click(Button button) { button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)); if (Application.Current.MainWindow != null) Application.Current.MainWindow.UpdateLayout(); }
        static TextBox Input(DependencyObject root, string name) { return Find<TextBox>(root, t => AutomationProperties.GetName(t) == name); }
        static void Settle(int milliseconds = 300)
        {
            DispatcherFrame frame = new DispatcherFrame();
            DispatcherTimer timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(milliseconds) };
            timer.Tick += delegate { timer.Stop(); frame.Continue = false; };
            timer.Start(); Dispatcher.PushFrame(frame);
        }
        static void Check(bool value, string message) { if (!value) throw new Exception("FAIL: " + message); log.Add("PASS " + message); }
        static void Capture(Window window, string path)
        {
            Settle();
            window.UpdateLayout();
            FrameworkElement content = (FrameworkElement)window.Content;
            double logicalWidth = content.ActualWidth + content.Margin.Left + content.Margin.Right, logicalHeight = content.ActualHeight + content.Margin.Top + content.Margin.Bottom;
            PresentationSource source = PresentationSource.FromVisual(window);
            double scale = source == null || source.CompositionTarget == null ? 1 : source.CompositionTarget.TransformToDevice.M11;
            int width = (int)Math.Ceiling(logicalWidth * scale), height = (int)Math.Ceiling(logicalHeight * scale);
            RenderTargetBitmap bitmap = new RenderTargetBitmap(width, height, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            DrawingVisual visual = new DrawingVisual();
            using (DrawingContext drawing = visual.RenderOpen())
            {
                drawing.DrawRectangle(window.Background, null, new Rect(0, 0, logicalWidth, logicalHeight));
                drawing.DrawRectangle(new VisualBrush(content), null, new Rect(content.Margin.Left, content.Margin.Top, content.ActualWidth, content.ActualHeight));
            }
            bitmap.Render(visual);
            PngBitmapEncoder encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (FileStream output = File.Create(path)) encoder.Save(output);
        }
        static void Modal(MainWindow owner, Action open, Action<Window> interact)
        {
            Exception failure = null;
            owner.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(delegate
            {
                Window dialog = Application.Current.Windows.Cast<Window>().Last(x => x != owner && x.IsVisible);
                try { dialog.UpdateLayout(); interact(dialog); }
                catch (Exception e) { failure = e; dialog.Close(); }
            }));
            open();
            if (failure != null) throw failure;
        }
        public static void Run(MainWindow main, Store store)
        {
            string folder = Path.GetDirectoryName(store.Folder);
            string report = Path.Combine(folder, "ui-result.txt");
            try
            {
                Check(main.WindowStyle == WindowStyle.None && main.Background is SolidColorBrush && ((SolidColorBrush)main.Background).Color.A == 0, "glass uses transparent window surface with custom chrome");
                Check(Glass.Backend(main) != "Pending" && Glass.Backend(main) != "Unconfigured", "native backdrop initialized: " + Glass.Backend(main));
                Capture(main, Path.Combine(folder, "empty.png"));
                Modal(main, delegate { Click(Button(main, "＋ 日程")); }, delegate(Window dialog)
                {
                    Click(Button(dialog, "保存日程"));
                    Check(dialog.IsVisible && Tree(dialog).OfType<TextBlock>().Any(t => t.Text == "请填写日程名称。"), "empty event rejected in editor");
                    Input(dialog, "日程名称 *").Text = "产品设计讨论";
                    Input(dialog, "开始时间").Text = "23:00"; Input(dialog, "结束时间").Text = "01:00";
                    Click(Button(dialog, "保存日程"));
                    Check(dialog.IsVisible && Tree(dialog).OfType<TextBlock>().Any(t => t.Text.StartsWith("结束时间必须")), "invalid end rejected in editor");
                    Input(dialog, "开始时间").Text = "10:00"; Input(dialog, "结束时间").Text = "11:00";
                    Input(dialog, "地点（可选）").Text = "会议室 A"; Input(dialog, "备注（可选）").Text = "确认本周的设计方向";
                    Find<ComboBox>(dialog, x => x.Items.Contains("工作")).SelectedItem = "工作";
                    Capture(dialog, Path.Combine(folder, "event-editor.png"));
                    Click(Button(dialog, "保存日程"));
                });
                main.UpdateLayout();
                Check(store.Data.Events.Count == 1 && store.Data.Events[0].Title == "产品设计讨论", "event created through controls");
                Check(Tree(main).OfType<TextBlock>().Any(t => t.Text == "产品设计讨论"), "created event rendered in agenda");
                string originalId = store.Data.Events[0].Id;
                Modal(main, delegate
                {
                    Click(Find<Button>(main, b => b.Content is DependencyObject && Tree((DependencyObject)b.Content).OfType<TextBlock>().Any(t => t.Text == "产品设计讨论")));
                }, delegate(Window dialog)
                {
                    Input(dialog, "备注（可选）").Text = "确认本周的设计方向，带好讨论资料";
                    Click(Button(dialog, "保存日程"));
                });
                Check(store.Data.Events.Count == 1 && store.Data.Events[0].Id == originalId && store.Data.Events[0].Notes.Contains("讨论资料"), "editing updates existing event and preserves identity");
                Modal(main, delegate { Click(Button(main, "＋ 待办")); }, delegate(Window dialog)
                {
                    Input(dialog, "待办内容 *").Text = "整理本周的工作清单";
                    Find<CheckBox>(dialog, x => (string)x.Content == "★  标记为重要").IsChecked = true;
                    Click(Button(dialog, "保存待办"));
                });
                Check(store.Data.Todos.Count == 1 && store.Data.Todos[0].Important, "important todo created through controls");
                CheckBox done = Find<CheckBox>(main, x => AutomationProperties.GetName(x) == "完成待办 整理本周的工作清单");
                int feedbackBefore = Glass.Confirmations;
                done.IsChecked = true; done.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));
                main.UpdateLayout();
                Check(store.Data.Todos[0].Done, "todo completion click saved");
                Check(!SystemParameters.ClientAreaAnimation || Glass.Confirmations > feedbackBefore, "task completion acknowledges the original row position");
                ComboBox filter = Find<ComboBox>(main, x => x.Items.Contains("未完成")); filter.SelectedItem = "已完成";
                main.UpdateLayout();
                Check(Tree(main).OfType<TextBlock>().Any(t => t.Text == "整理本周的工作清单"), "completed filter shows completed task");
                done = Find<CheckBox>(main, x => AutomationProperties.GetName(x) == "完成待办 整理本周的工作清单");
                done.IsChecked = false; done.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));
                filter.SelectedItem = "未完成";
                Input(main, "搜索待办").Text = "不存在的内容";
                main.UpdateLayout();
                Check(Tree(main).OfType<TextBlock>().Any(t => t.Text == "没有找到匹配的待办"), "todo search filters immediately");
                Input(main, "搜索待办").Text = "";
                Modal(main, delegate { Click(Button(main, "↗  导入苹果日历")); }, delegate(Window dialog)
                {
                    Check(Tree(dialog).OfType<TextBlock>().Any(t => t.Text == "将导出 1 条日程"), "export counts selected range");
                    Find<CheckBox>(dialog, x => (string)x.Content == "同时导出有日期的未完成待办").IsChecked = true;
                    Check(Tree(dialog).OfType<TextBlock>().Any(t => t.Text == "将导出 1 条日程 + 1 条待办"), "export optionally includes todos");
                    Capture(dialog, Path.Combine(folder, "export.png")); Click(Button(dialog, "取消"));
                });
                store.Change(delegate
                {
                    store.Data.Events.Add(new CalendarEvent { Title = "给自己一点阅读时间", Start = Dates.Format(DateTime.Today.AddHours(20)), End = Dates.Format(DateTime.Today.AddHours(20.5)), Category = "个人", Reminder = -1 });
                    store.Data.Events.Add(new CalendarEvent { Title = "周末去公园走走", Start = Dates.Format(DateTime.Today.AddDays(5)), End = Dates.Format(DateTime.Today.AddDays(6)), AllDay = true, Reminder = -1 });
                    store.Data.Todos.Add(new Todo { Title = "给阳台的绿植浇水", Due = Dates.Day(DateTime.Today) });
                    store.Data.Todos.Add(new Todo { Title = "预约下周的体检", Due = Dates.Day(DateTime.Today.AddDays(2)) });
                });
                Click(Button(main, "置顶")); Check(main.Topmost, "pin keeps window on top"); Click(Button(main, "已置顶"));
                Capture(main, Path.Combine(folder, "full.png"));
                Click(Button(main, "桌面模式")); main.UpdateLayout();
                Check(main.Width == 460 && main.MinWidth == 400, "compact mode changes real window size");
                Check(Tree(main).OfType<TabItem>().Count() == 3, "compact mode exposes agenda, tasks and goals");
                Capture(main, Path.Combine(folder, "compact.png"));
                Find<TabControl>(main, x => true).SelectedIndex = 1; main.UpdateLayout();
                Check(Tree(main).OfType<TextBlock>().Any(t => t.Text == "待办清单"), "compact task tab renders");
                Capture(main, Path.Combine(folder, "compact-todos.png"));
                Click(Button(main, "展开")); main.UpdateLayout(); Check(main.Width >= 980, "full mode restored");
                Check(new Store(store.Folder).Data.Events.Count == 3 && new Store(store.Folder).Data.Todos.Count == 3, "UI edits survive reopening data store");
                Find<TabControl>(main, x => true).SelectedIndex = 1; main.UpdateLayout();
                Check(Tree(main).OfType<TextBlock>().Any(t => t.Text == "给未来，定一个方向"), "goal page has actionable empty state");
                Modal(main, delegate { Click(Button(main, "＋ 目标")); }, delegate(Window dialog)
                {
                    Click(Button(dialog, "保存目标"));
                    Check(dialog.IsVisible && Tree(dialog).OfType<TextBlock>().Any(t => t.Text == "请填写目标名称。"), "empty goal title rejected");
                    Input(dialog, "目标名称 *").Text = "一年读完 12 本书";
                    Input(dialog, "目标说明（可选）").Text = "每天留出半小时，阅读并记录自己的收获。";
                    Find<CheckBox>(dialog, x => (string)x.Content == "设置目标日期").IsChecked = true;
                    Click(Button(dialog, "保存目标"));
                });
                main.UpdateLayout();
                Check(store.Data.Goals.Count == 1, "goal created through editor");
                LongTermGoal goal = store.Data.Goals[0];
                Check(Find<ProgressBar>(main, x => AutomationProperties.GetName(x) == "目标进度 " + goal.Id).Value == 0, "empty goal shows zero progress");
                foreach (string taskTitle in new[] { "列出今年想读的书单", "阅读第一本书", "写一篇读书笔记" })
                {
                    Modal(main, delegate { Click(Find<Button>(main, b => AutomationProperties.GetName(b) == "添加目标待办 " + goal.Title)); }, delegate(Window dialog)
                    {
                        ComboBox assigned = Find<ComboBox>(dialog, x => AutomationProperties.GetName(x) == "所属长期目标");
                        Check((string)((ComboBoxItem)assigned.SelectedItem).Tag == goal.Id, "new subtask defaults to its parent goal");
                        Input(dialog, "待办内容 *").Text = taskTitle;
                        Find<CheckBox>(dialog, x => (string)x.Content == "设置截止日期").IsChecked = false;
                        Click(Button(dialog, "保存待办"));
                    });
                }
                Check(GoalProgress.For(store.Data, goal.Id).Total == 3, "goal contains multiple tasks");
                CheckBox member = Find<CheckBox>(main, x => AutomationProperties.GetName(x) == "目标待办 列出今年想读的书单");
                member.IsChecked = true; member.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent)); main.UpdateLayout();
                Check(GoalProgress.For(store.Data, goal.Id).Percent == 33 && Find<ProgressBar>(main, x => true).Value == 1, "checking a goal task updates percentage and bar");
                Find<TabControl>(main, x => true).SelectedIndex = 0; main.UpdateLayout();
                CheckBox shared = Find<CheckBox>(main, x => AutomationProperties.GetName(x) == "完成待办 阅读第一本书");
                shared.IsChecked = true; shared.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent)); main.UpdateLayout();
                Check(GoalProgress.For(store.Data, goal.Id).Percent == 67, "completing a task in general list updates goal");
                Modal(main, delegate
                {
                    Click(Find<Button>(main, b => b.Content is DependencyObject && Tree((DependencyObject)b.Content).OfType<TextBlock>().Any(t => t.Text == "预约下周的体检")));
                }, delegate(Window dialog)
                {
                    ComboBox assigned = Find<ComboBox>(dialog, x => AutomationProperties.GetName(x) == "所属长期目标");
                    assigned.SelectedItem = assigned.Items.Cast<ComboBoxItem>().First(x => (string)x.Tag == goal.Id);
                    Click(Button(dialog, "保存待办"));
                });
                Check(GoalProgress.For(store.Data, goal.Id).Total == 4 && GoalProgress.For(store.Data, goal.Id).Percent == 50, "existing todo can join a goal and update denominator");
                Modal(main, delegate
                {
                    Click(Find<Button>(main, b => b.Content is DependencyObject && Tree((DependencyObject)b.Content).OfType<TextBlock>().Any(t => t.Text == "预约下周的体检")));
                }, delegate(Window dialog)
                {
                    Find<ComboBox>(dialog, x => AutomationProperties.GetName(x) == "所属长期目标").SelectedIndex = 0;
                    Click(Button(dialog, "保存待办"));
                });
                Check(GoalProgress.For(store.Data, goal.Id).Total == 3 && GoalProgress.For(store.Data, goal.Id).Percent == 67, "existing task can detach without losing completion");
                Find<TabControl>(main, x => true).SelectedIndex = 1; main.UpdateLayout();
                Modal(main, delegate { Click(Find<Button>(main, b => AutomationProperties.GetName(b) == "编辑目标 " + goal.Title)); }, delegate(Window dialog)
                {
                    Input(dialog, "目标说明（可选）").Text = "每天留出半小时，阅读并记录自己的收获。坚持比速度更重要。";
                    Click(Button(dialog, "保存目标"));
                });
                Check(store.Data.Goals.Count == 1 && store.Data.Goals[0].Id == goal.Id && GoalProgress.For(store.Data, goal.Id).Done == 2, "editing goal preserves membership and progress");
                Capture(main, Path.Combine(folder, "goals-full.png"));
                Click(Button(main, "桌面模式")); main.UpdateLayout();
                Check(Find<TabControl>(main, x => true).SelectedIndex == 2, "desktop mode preserves selected goal page");
                Check(Find<ProgressBar>(main, x => true).Value == 2, "desktop goal shows live progress");
                Capture(main, Path.Combine(folder, "goals-compact.png"));
                member = Find<CheckBox>(main, x => AutomationProperties.GetName(x) == "目标待办 写一篇读书笔记");
                member.IsChecked = true; member.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent)); main.UpdateLayout();
                Check(GoalProgress.For(store.Data, goal.Id).Complete && Tree(main).OfType<TextBlock>().Any(x => x.Text.Contains("3 / 3 项已完成  ·  已达成")), "completing all tasks shows goal achieved");
                member = Find<CheckBox>(main, x => AutomationProperties.GetName(x) == "目标待办 写一篇读书笔记");
                member.IsChecked = false; member.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent)); main.UpdateLayout();
                Check(GoalProgress.For(store.Data, goal.Id).Percent == 67, "unchecking in desktop mode reopens goal");
                Capture(main, Path.Combine(folder, "goals-compact.png"));
                Store restored = new Store(store.Folder);
                Check(restored.Data.Goals.Count == 1 && GoalProgress.For(restored.Data, goal.Id).Done == 2 && restored.Data.Todos.Count == 6, "goals and completion survive reopening data");
                if (SystemParameters.ClientAreaAnimation)
                {
                    Button touch = Button(main, "＋ 目标");
                    touch.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = UIElement.MouseEnterEvent });
                    Settle(250);
                    TransformGroup transforms = (TransformGroup)touch.RenderTransform;
                    Check(Math.Abs(((TranslateTransform)transforms.Children[1]).Y + 1.6) < .01, "hover lifts only the triggering button");
                    Border buttonSurface = (Border)touch.Template.FindName("bg", touch);
                    Check(Math.Abs(((DropShadowEffect)buttonSurface.Effect).Opacity - .15) < .01, "hover raises the local shadow");
                    touch.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent });
                    Settle(180);
                    Check(Math.Abs(((ScaleTransform)transforms.Children[0]).ScaleX - .982) < .001, "press gives a small tactile compression");
                    touch.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseLeftButtonUpEvent });
                    touch.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = UIElement.MouseLeaveEvent });
                    Settle(250);
                    Check(Math.Abs(((ScaleTransform)transforms.Children[0]).ScaleX - 1) < .001 && Math.Abs(((TranslateTransform)transforms.Children[1]).Y) < .01, "interrupted press and leave settle without residual transforms");
                    Check(Motion.Durations.Count > 0 && Motion.Durations.All(ms => ms >= 150 && ms <= 280), "runtime feedback durations remain between 150 and 280 ms");
                    KeySpline curve = Motion.Curve();
                    Check(curve.ControlPoint1 == new Point(.22, 1) && curve.ControlPoint2 == new Point(.36, 1), "runtime feedback uses the specified cubic bezier");
                }
                Check(main.Opacity == 1, "transparency keeps text and controls at full window opacity");
                Todo floatingTask = new Todo { Title = "悬浮条测试任务", Due = Dates.Day(DateTime.Today), GoalId = goal.Id };
                store.Change(delegate { store.Data.Todos.Add(floatingTask); store.Data.Todos.Add(new Todo { Title = "未来不应显示", Due = Dates.Day(DateTime.Today.AddDays(1)) }); });
                int previousGoalDone = GoalProgress.For(store.Data, goal.Id).Done;
                Click(Find<Button>(main, b => AutomationProperties.GetName(b) == "极简悬浮条")); Settle();
                FloatingBar bar = main.CurrentFloatingBar;
                Check(bar != null && bar.IsVisible && !main.IsVisible && bar.Topmost && !bar.ShowInTaskbar, "floating mode hides main window and shows topmost minimal bar");
                Capture(bar, Path.Combine(folder, "floating-initial.png"));
                Check(Math.Abs(bar.Width - 324) < 1 && Math.Abs(bar.Height - 84) < 1 && bar.Opacity == 1 && Glass.Backend(bar).Contains("Acrylic"), "floating bar is compact with native glass and crisp foreground: " + bar.Width + " x " + bar.Height + ", opacity " + bar.Opacity + ", " + Glass.Backend(bar));
                Func<int> dotCount = delegate { return Tree(bar).OfType<Button>().Count(b => AutomationProperties.GetName(b).StartsWith("今日待办 ")); };
                Check(dotCount() == TodayTasks.For(store.Data, DateTime.Today).Count, "one dot per today task including goal members");
                Check(!Tree(bar).OfType<Button>().Any(b => AutomationProperties.GetName(b).Contains("未来不应显示")), "future tasks do not appear in today bar");
                Button taskDot = Find<Button>(bar, b => AutomationProperties.GetName(b) == "今日待办 悬浮条测试任务");
                Check(AutomationProperties.GetItemStatus(taskDot) == "未完成", "unlit dot exposes incomplete status and task title");
                Click(taskDot); Settle();
                Check(store.Data.Todos.Single(t => t.Id == floatingTask.Id).Done && AutomationProperties.GetItemStatus(taskDot) == "已完成", "click lights the original dot and completes the task");
                Check(Object.ReferenceEquals(taskDot, Find<Button>(bar, b => AutomationProperties.GetName(b) == "今日待办 悬浮条测试任务")), "completion preserves control identity and pointer position");
                Check(GoalProgress.For(store.Data, goal.Id).Done == previousGoalDone + 1 && new Store(store.Folder).Data.Todos.Single(t => t.Id == floatingTask.Id).Done, "floating completion updates goal progress and persists to disk");
                Capture(bar, Path.Combine(folder, "floating.png"));
                Click(taskDot); Settle();
                Check(!store.Data.Todos.Single(t => t.Id == floatingTask.Id).Done && GoalProgress.For(store.Data, goal.Id).Done == previousGoalDone, "click again reopens task and decreases goal progress");
                Check(new Store(store.Folder).Data.Settings.Floating, "floating display mode survives restarting the app");
                Click(Find<Button>(bar, b => AutomationProperties.GetName(b) == "展开日历")); Settle();
                Check(main.IsVisible && !bar.IsVisible && !new Store(store.Folder).Data.Settings.Floating, "expand restores calendar and saves normal display mode");
                main.EnterFloating(); Settle();
                Check(Object.ReferenceEquals(bar, main.CurrentFloatingBar), "reentering floating mode reuses a single bar");
                Modal(main, delegate { Click(Find<Button>(bar, b => AutomationProperties.GetName(b) == "添加今天的待办")); }, delegate(Window dialog)
                {
                    Input(dialog, "待办内容 *").Text = "从悬浮条添加";
                    Click(Button(dialog, "保存待办"));
                });
                Check(store.Data.Todos.Any(t => t.Title == "从悬浮条添加" && t.Due == Dates.Day(DateTime.Today)), "floating add action creates a task dated today");
                store.Change(delegate { for (int i = 0; i < 55; i++) store.Data.Todos.Add(new Todo { Title = "大量待办 " + i, Due = Dates.Day(DateTime.Today) }); });
                main.EnterFloating(); Settle();
                Check(dotCount() == TodayTasks.For(store.Data, DateTime.Today).Count && bar.Height <= 157, "many tasks retain all dots within a bounded bar");
                Check(Tree(bar).OfType<ScrollViewer>().Any(s => s.ExtentHeight > s.ViewportHeight), "overflow dots remain accessible by scrolling");
                Capture(bar, Path.Combine(folder, "floating-many.png"));
                bar.Left = 50000; bar.Top = 50000; bar.Constrain();
                Check(bar.Left < 50000 && bar.Top < 50000, "offscreen floating window is brought back into the work area");
                bar.Close(); Settle();
                Check(main.IsVisible && !bar.IsVisible, "closing the floating bar safely expands the calendar");
                File.WriteAllText(report, String.Join("\r\n", log.ToArray()) + "\r\n", Encoding.UTF8);
            }
            catch (Exception e) { File.WriteAllText(report, String.Join("\r\n", log.ToArray()) + "\r\n" + e.ToString(), Encoding.UTF8); Environment.ExitCode = 1; }
            finally { main.Close(); }
        }
    }
}
