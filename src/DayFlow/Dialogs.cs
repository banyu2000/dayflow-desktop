using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace DayFlow
{
    public class FormDialog : Window
    {
        protected StackPanel Fields;
        protected StackPanel Buttons;
        protected TextBlock Error;
        public bool Deleted;
        public FormDialog(Window owner, string title, string subtitle, double height)
        {
            Style = (Style)Application.Current.FindResource(typeof(Window));
            Glass.Configure(this);
            Owner = owner; Title = title + " · 日常"; Width = 500; Height = height; MinHeight = 400; MinWidth = 430;
            MaxHeight = SystemParameters.WorkArea.Height; WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.CanResize; ShowInTaskbar = false;
            Grid grid = new Grid { Margin = new Thickness(27, 23, 27, 22) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); grid.RowDefinitions.Add(new RowDefinition()); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            StackPanel heading = new StackPanel(); heading.Children.Add(Theme.Text(title, 23, null, true)); Theme.Gap(heading, 7); TextBlock sub = Theme.Text(subtitle, 12, Theme.Muted); sub.TextWrapping = TextWrapping.Wrap; heading.Children.Add(sub); heading.Margin = new Thickness(0, 0, 25, 20);
            Grid caption = new Grid(); caption.Children.Add(heading); Glass.DragArea(caption, this);
            Button close = Theme.Button("×", delegate { DialogResult = false; }, "Quiet"); close.HorizontalAlignment = HorizontalAlignment.Right; close.VerticalAlignment = VerticalAlignment.Top; close.FontSize = 18; close.Padding = new Thickness(6, 1, 6, 1); close.ToolTip = "取消并关闭"; caption.Children.Add(close); Theme.Put(grid, caption);
            Fields = new StackPanel(); Theme.Put(grid, new ScrollViewer { Content = Fields }, 0, 1);
            Error = Theme.Text("", 12, Theme.Brush("#B15A4C")); Error.TextWrapping = TextWrapping.Wrap; Error.Margin = new Thickness(0, 10, 0, 8); Theme.Put(grid, Error, 0, 2);
            Buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right }; Theme.Put(grid, Buttons, 0, 3); Content = Glass.Wrap(this, grid, true);
        }
        protected void Label(string title) { TextBlock l = Theme.Text(title, 12, Theme.Muted); l.Margin = new Thickness(0, 0, 0, 6); Fields.Children.Add(l); }
        protected TextBox Input(string title, string value, int max, bool multiline = false)
        {
            Label(title); TextBox box = new TextBox { Text = value, MaxLength = max, AcceptsReturn = multiline, TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap, MinHeight = multiline ? 80 : 38, VerticalScrollBarVisibility = multiline ? ScrollBarVisibility.Auto : ScrollBarVisibility.Hidden, Margin = new Thickness(0, 0, 0, 16) };
            box.SetValue(System.Windows.Automation.AutomationProperties.NameProperty, title); Fields.Children.Add(box); return box;
        }
        protected DatePicker DateField(DateTime date)
        {
            DatePicker p = new DatePicker { SelectedDate = date, DisplayDateStart = new DateTime(1900, 1, 1), DisplayDateEnd = new DateTime(2100, 12, 31), SelectedDateFormat = DatePickerFormat.Short };
            return p;
        }
        protected Button Button(string title, Action action, bool primary = false)
        {
            Button b = Theme.Button(title, action, primary ? "Primary" : null); b.Margin = new Thickness(8, 0, 0, 0); Buttons.Children.Add(b); return b;
        }
        protected void DeleteButton(Action action)
        {
            Button button = Button("删除", action); button.Foreground = Theme.Brush("#AE6253");
        }
        protected bool ConfirmDelete(string text)
        {
            if (MessageBox.Show(this, "确定删除“" + text + "”？", "删除确认", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return false;
            Deleted = true; DialogResult = true; return true;
        }
    }
    public class EventDialog : FormDialog
    {
        public CalendarEvent Result;
        readonly CalendarEvent original;
        TextBox title, location, notes, startTime, endTime;
        DatePicker startDate, endDate;
        CheckBox allDay;
        ComboBox category, reminder;
        readonly int[] reminderValues = { -1, 0, 5, 15, 30, 60, 1440 };
        public EventDialog(Window owner, DateTime date, CalendarEvent item) : base(owner, item == null ? "安排一件重要的事" : "编辑日程", "确定的时间，让生活更从容。", 735)
        {
            original = item;
            DateTime initial = date.Date.AddHours(DateTime.Now.Hour < 22 ? DateTime.Now.Hour + 1 : 9);
            title = Input("日程名称 *", item == null ? "" : item.Title, 120);
            allDay = new CheckBox { Content = "全天日程", IsChecked = item != null && item.AllDay, Margin = new Thickness(0, 0, 0, 15) }; Fields.Children.Add(allDay);
            Label("开始日期与时间");
            Grid start = Theme.Grid(2); start.ColumnDefinitions[0].Width = new GridLength(1.7, GridUnitType.Star); start.Margin = new Thickness(0, 0, 0, 14);
            startDate = DateField(item == null ? initial : item.StartAt); startDate.Margin = new Thickness(0, 0, 10, 0); Theme.Put(start, startDate);
            startTime = new TextBox { Text = (item == null ? initial : item.StartAt).ToString("HH:mm"), MaxLength = 5, ToolTip = "24 小时制，例如 09:30" }; Theme.Put(start, startTime, 1); Fields.Children.Add(start);
            Label("结束日期与时间（全天日程包含该日期）");
            Grid end = Theme.Grid(2); end.ColumnDefinitions[0].Width = new GridLength(1.7, GridUnitType.Star); end.Margin = new Thickness(0, 0, 0, 16);
            endDate = DateField(item == null ? initial.AddHours(1) : (item.AllDay ? item.LastDay : item.EndAt)); endDate.Margin = new Thickness(0, 0, 10, 0); Theme.Put(end, endDate);
            endTime = new TextBox { Text = (item == null ? initial.AddHours(1) : item.EndAt).ToString("HH:mm"), MaxLength = 5, ToolTip = "24 小时制，例如 10:30" }; Theme.Put(end, endTime, 1); Fields.Children.Add(end);
            startDate.SetValue(System.Windows.Automation.AutomationProperties.NameProperty, "开始日期"); startTime.SetValue(System.Windows.Automation.AutomationProperties.NameProperty, "开始时间"); endDate.SetValue(System.Windows.Automation.AutomationProperties.NameProperty, "结束日期"); endTime.SetValue(System.Windows.Automation.AutomationProperties.NameProperty, "结束时间");
            startDate.SelectedDateChanged += delegate { if (startDate.SelectedDate.HasValue && endDate.SelectedDate.HasValue && startDate.SelectedDate.Value > endDate.SelectedDate.Value) endDate.SelectedDate = startDate.SelectedDate; };
            allDay.Checked += delegate { startTime.IsEnabled = false; endTime.IsEnabled = false; };
            allDay.Unchecked += delegate { startTime.IsEnabled = true; endTime.IsEnabled = true; };
            startTime.IsEnabled = endTime.IsEnabled = allDay.IsChecked != true;
            Label("分类 / 提醒");
            Grid options = Theme.Grid(2); options.Margin = new Thickness(0, 0, 0, 16);
            category = new ComboBox { ItemsSource = new[] { "生活", "工作", "个人" }, SelectedItem = item == null ? "生活" : item.Category, Margin = new Thickness(0, 0, 10, 0) }; Theme.Put(options, category);
            reminder = new ComboBox { ItemsSource = new[] { "不提醒", "开始时", "提前 5 分钟", "提前 15 分钟", "提前 30 分钟", "提前 1 小时", "提前 1 天" }, SelectedIndex = item == null ? 3 : Array.IndexOf(reminderValues, item.Reminder) }; Theme.Put(options, reminder, 1); Fields.Children.Add(options);
            location = Input("地点（可选）", item == null ? "" : item.Location, 240);
            notes = Input("备注（可选）", item == null ? "" : item.Notes, 4000, true);
            TextBlock hint = Theme.Text("桌面提醒需要程序运行；全天提醒仅写入导出文件。", 10, Theme.Muted); hint.TextWrapping = TextWrapping.Wrap; Fields.Children.Add(hint);
            if (item != null) DeleteButton(delegate { ConfirmDelete(item.Title); });
            Button cancel = Button("取消", delegate { DialogResult = false; }); cancel.IsCancel = true;
            Button save = Button("保存日程", Save, true); save.IsDefault = true;
            Loaded += delegate { title.Focus(); title.CaretIndex = title.Text.Length; };
        }
        void Save()
        {
            Error.Text = "";
            if (String.IsNullOrWhiteSpace(title.Text)) { Error.Text = "请填写日程名称。"; title.Focus(); return; }
            if (!startDate.SelectedDate.HasValue || !endDate.SelectedDate.HasValue) { Error.Text = "请选择有效的开始和结束日期。"; return; }
            DateTime first = startDate.SelectedDate.Value.Date, last = endDate.SelectedDate.Value.Date;
            bool allday = allDay.IsChecked == true;
            if (allday) last = last.AddDays(1);
            else
            {
                DateTime from, to;
                if (!DateTime.TryParseExact(startTime.Text.Trim(), new[] { "HH:mm", "H:mm" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out from) || !DateTime.TryParseExact(endTime.Text.Trim(), new[] { "HH:mm", "H:mm" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out to))
                { Error.Text = "时间请使用 24 小时制，例如 09:30。"; return; }
                first = first.Add(from.TimeOfDay); last = last.Add(to.TimeOfDay);
                if (TimeZoneInfo.Local.IsInvalidTime(first) || TimeZoneInfo.Local.IsInvalidTime(last)) { Error.Text = "该时间处于夏令时跳过的时段，请选择其他时间。"; return; }
            }
            if (last <= first) { Error.Text = "结束时间必须晚于开始时间；跨天请调整结束日期。"; return; }
            if (first.Year < 1900 || last.Year > 2100) { Error.Text = "日期须在 1900—2100 年之间。"; return; }
            Result = new CalendarEvent { Id = original == null ? Guid.NewGuid().ToString("N") : original.Id, Title = title.Text.Trim(), Start = Dates.Format(first), End = Dates.Format(last), AllDay = allday, Location = location.Text.Trim(), Notes = notes.Text.Trim(), Category = (string)category.SelectedItem, Reminder = reminderValues[Math.Max(0, reminder.SelectedIndex)] };
            DialogResult = true;
        }
    }
    public class TodoDialog : FormDialog
    {
        public Todo Result;
        Todo original;
        TextBox title, notes;
        DatePicker due;
        CheckBox dated, important;
        ComboBox goal;
        public TodoDialog(Window owner, DateTime date, Todo item, IEnumerable<LongTermGoal> goals = null, string defaultGoalId = null) : base(owner, item == null ? "记下一件小事" : "编辑待办", "一步一步，把想做的事变成已完成。", 640)
        {
            original = item;
            title = Input("待办内容 *", item == null ? "" : item.Title, 120);
            Label("所属长期目标（可选）");
            goal = new ComboBox { Margin = new Thickness(0, 0, 0, 16) };
            goal.SetValue(System.Windows.Automation.AutomationProperties.NameProperty, "所属长期目标");
            goal.Items.Add(new ComboBoxItem { Content = "独立待办（不属于目标）", Tag = "" });
            foreach (LongTermGoal g in goals ?? Enumerable.Empty<LongTermGoal>()) goal.Items.Add(new ComboBoxItem { Content = g.Title, Tag = g.Id });
            string selectedGoal = item == null ? defaultGoalId : item.GoalId;
            goal.SelectedItem = goal.Items.Cast<ComboBoxItem>().FirstOrDefault(x => (string)x.Tag == selectedGoal) ?? goal.Items[0];
            Fields.Children.Add(goal);
            dated = new CheckBox { Content = "设置截止日期", IsChecked = item == null ? String.IsNullOrEmpty(defaultGoalId) : item.Due.Length > 0 }; Fields.Children.Add(dated);
            due = DateField(item == null || item.Due.Length == 0 ? date : Dates.ParseDay(item.Due)); due.IsEnabled = dated.IsChecked == true; due.Margin = new Thickness(0, 3, 0, 15); Fields.Children.Add(due);
            dated.Checked += delegate { due.IsEnabled = true; }; dated.Unchecked += delegate { due.IsEnabled = false; };
            important = new CheckBox { Content = "★  标记为重要", IsChecked = item != null && item.Important, Margin = new Thickness(0, 0, 0, 16) }; Fields.Children.Add(important);
            notes = Input("备注（可选）", item == null ? "" : item.Notes, 4000, true);
            if (item != null) DeleteButton(delegate { ConfirmDelete(item.Title); });
            Button cancel = Button("取消", delegate { DialogResult = false; }); cancel.IsCancel = true;
            Button save = Button("保存待办", Save, true); save.IsDefault = true;
            Loaded += delegate { title.Focus(); title.CaretIndex = title.Text.Length; };
        }
        void Save()
        {
            if (String.IsNullOrWhiteSpace(title.Text)) { Error.Text = "请填写待办内容。"; title.Focus(); return; }
            if (dated.IsChecked == true && !due.SelectedDate.HasValue) { Error.Text = "请选择有效的截止日期。"; return; }
            if (dated.IsChecked == true && (due.SelectedDate.Value.Year < 1900 || due.SelectedDate.Value.Year > 2100)) { Error.Text = "日期须在 1900—2100 年之间。"; return; }
            Result = new Todo { Id = original == null ? Guid.NewGuid().ToString("N") : original.Id, Title = title.Text.Trim(), Due = dated.IsChecked == true ? Dates.Day(due.SelectedDate.Value) : "", Important = important.IsChecked == true, Done = original != null && original.Done, Notes = notes.Text.Trim(), GoalId = (string)((ComboBoxItem)goal.SelectedItem).Tag };
            DialogResult = true;
        }
    }
    public class ExportDialog : FormDialog
    {
        AppData data;
        DateTime selected;
        ComboBox range;
        CheckBox includeTodos;
        TextBlock count;
        Button export;
        public ExportDialog(Window owner, AppData data, DateTime selected) : base(owner, "让日程，出现在 iPhone 上", "生成苹果日历支持的 .ics 文件，无需登录账号。", 620)
        {
            this.data = data; this.selected = selected;
            Label("导出范围");
            range = new ComboBox { ItemsSource = new[] { "未来 90 天（含今天）", "全部日程", "当前选中日期 · " + selected.ToString("yyyy/M/d") }, SelectedIndex = 0, Margin = new Thickness(0, 0, 0, 16) }; Fields.Children.Add(range);
            includeTodos = new CheckBox { Content = "同时导出有日期的未完成待办", IsChecked = false }; Fields.Children.Add(includeTodos);
            TextBlock note = Theme.Text("待办会变成全天日程，完成状态不会同步到手机。", 11, Theme.Muted); note.TextWrapping = TextWrapping.Wrap; note.Margin = new Thickness(0, 4, 0, 16); Fields.Children.Add(note);
            count = Theme.Text("", 14, Theme.Green, true); Fields.Children.Add(count); Theme.Gap(Fields, 20);
            StackPanel help = new StackPanel(); help.Children.Add(Theme.Text("只需三步", 14, null, true)); Theme.Gap(help, 13);
            foreach (string step in new[] { "1   导出 .ics 文件，作为邮件附件发给自己。", "2   在 iPhone 系统「邮件」App 打开该附件。", "3   选择「添加全部」或「添加到日历」。" }) { TextBlock line = Theme.Text(step, 12); line.TextWrapping = TextWrapping.Wrap; line.LineHeight = 21; line.Margin = new Thickness(0, 0, 0, 9); help.Children.Add(line); }
            Theme.Gap(help, 6); TextBlock limit = Theme.Text("这是一次性导入。以后修改日程需重新导出；避免重复导入同一批日程。若“文件”预览没有添加按钮，请通过系统“邮件”打开。", 11, Theme.Muted); limit.TextWrapping = TextWrapping.Wrap; limit.LineHeight = 20; help.Children.Add(limit);
            Fields.Children.Add(Theme.Card(help, 16));
            Button cancel = Button("取消", delegate { DialogResult = false; }); cancel.IsCancel = true;
            export = Button("导出 .ics 文件", Save, true);
            range.SelectionChanged += delegate { UpdateCount(); }; includeTodos.Checked += delegate { UpdateCount(); }; includeTodos.Unchecked += delegate { UpdateCount(); }; UpdateCount();
        }
        bool InRange(DateTime first, DateTime end)
        {
            if (range.SelectedIndex == 1) return true;
            DateTime from = range.SelectedIndex == 2 ? selected.Date : DateTime.Today;
            DateTime to = range.SelectedIndex == 2 ? from.AddDays(1) : from.AddDays(90);
            return first < to && end > from;
        }
        List<CalendarEvent> Events() { return data.Events.Where(x => InRange(x.StartAt, x.EndAt)).ToList(); }
        List<Todo> Todos() { return includeTodos.IsChecked == true ? data.Todos.Where(x => !x.Done && x.Due.Length > 0 && InRange(Dates.ParseDay(x.Due), Dates.ParseDay(x.Due).AddDays(1))).ToList() : new List<Todo>(); }
        void UpdateCount()
        {
            int events = Events().Count, tasks = Todos().Count;
            count.Text = "将导出 " + events + " 条日程" + (tasks > 0 ? " + " + tasks + " 条待办" : ""); export.IsEnabled = events + tasks > 0;
            if (events + tasks == 0) Error.Text = "此范围内暂无内容，可选择“全部日程”或先添加一条日程。"; else Error.Text = "";
        }
        void Save()
        {
            SaveFileDialog dialog = new SaveFileDialog { Title = "导出至苹果日历", Filter = "苹果日历文件 (*.ics)|*.ics", FileName = "日常日程-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".ics", DefaultExt = ".ics", AddExtension = true };
            if (dialog.ShowDialog(this) != true) return;
            try
            {
                string calendar = ICalendar.Build(Events(), Todos());
                File.WriteAllText(dialog.FileName, calendar, new UTF8Encoding(false));
                MessageBox.Show(this, "导出成功！\n\n" + dialog.FileName + "\n\n把这个文件作为邮件附件发给自己，然后在 iPhone 系统“邮件”App 中打开，选择添加到日历。", "日程已准备好", MessageBoxButton.OK, MessageBoxImage.Information);
                DialogResult = true;
            }
            catch (Exception e) { Error.Text = "导出失败：" + e.Message; }
        }
    }
}
