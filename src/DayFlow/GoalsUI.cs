using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace DayFlow
{
    public partial class MainWindow
    {
        StackPanel goalList;
        TextBlock goalSummary;
        TabControl sectionTabs;
        string activeSection = "日程";
        readonly HashSet<string> expandedGoals = new HashSet<string>();
        readonly Dictionary<string, double> lastGoalProgress = new Dictionary<string, double>();
        Border BuildGoals(bool small)
        {
            Grid grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition());
            Grid heading = Theme.Grid(2);
            StackPanel title = new StackPanel();
            title.Children.Add(Theme.Text("长期目标", small ? 18 : 23, null, true));
            goalSummary = Theme.Text("", 11, Theme.Muted); goalSummary.Margin = new Thickness(0, 7, 0, 0); title.Children.Add(goalSummary);
            Theme.Put(heading, title);
            Button add = Theme.Button("＋ 目标", delegate { EditGoal(null); }, "Primary"); add.HorizontalAlignment = HorizontalAlignment.Right; add.VerticalAlignment = VerticalAlignment.Top;
            Theme.Put(heading, add, 1); Theme.Put(grid, heading);
            TextBlock hint = Theme.Text("把一个大目标，拆成每天能完成的小事。", 12, Theme.Muted); hint.Margin = new Thickness(0, 15, 0, 18); hint.TextWrapping = TextWrapping.Wrap; Theme.Put(grid, hint, 0, 1);
            goalList = new StackPanel();
            Theme.Put(grid, new ScrollViewer { Content = goalList }, 0, 2);
            return Theme.Card(grid, small ? 16 : 24);
        }
        void ShowGoals()
        {
            sectionTabs.SelectedIndex = compact ? 2 : 1;
        }
        void EditGoal(LongTermGoal original)
        {
            GoalDialog dialog = new GoalDialog(this, original);
            if (dialog.ShowDialog() != true) return;
            if (dialog.Deleted)
            {
                if (Commit(delegate { GoalProgress.RemoveGoal(store.Data, original.Id); }, "目标已删除，所有待办已保留在清单中")) expandedGoals.Remove(original.Id);
                return;
            }
            LongTermGoal result = dialog.Result;
            expandedGoals.Add(result.Id);
            Commit(delegate
            {
                int existing = store.Data.Goals.FindIndex(g => g.Id == result.Id);
                if (existing < 0) store.Data.Goals.Add(result); else store.Data.Goals[existing] = result;
            }, "目标已保存 · 完成待办会自动更新进度");
        }
        void RenderGoals()
        {
            if (goalList == null) return;
            goalList.Children.Clear();
            int complete = store.Data.Goals.Count(g => GoalProgress.For(store.Data, g.Id).Complete);
            goalSummary.Text = store.Data.Goals.Count + " 个目标  ·  " + complete + " 个已完成";
            if (store.Data.Goals.Count == 0)
            {
                Empty(goalList, "给未来，定一个方向", "例如：读完一本书、学会一项技能。\n添加待办，每完成一项，进度就前进一步。", "＋ 创建第一个目标", delegate { EditGoal(null); });
                return;
            }
            foreach (LongTermGoal g in store.Data.Goals)
            {
                GoalProgress progress = GoalProgress.For(store.Data, g.Id);
                bool expanded = expandedGoals.Contains(g.Id);
                StackPanel contents = new StackPanel();
                Grid header = Theme.Grid(2); header.ColumnDefinitions[1].Width = GridLength.Auto;
                Button title = Theme.Button("", delegate { EditGoal(g); }, "Quiet");
                TextBlock name = Theme.Text(g.Title, compact ? 15 : 17, null, true); name.TextWrapping = TextWrapping.Wrap;
                title.Content = name; title.Padding = new Thickness(0, 2, 8, 2); title.HorizontalContentAlignment = HorizontalAlignment.Left; title.ToolTip = "编辑目标名称、说明和目标日期";
                title.SetValue(AutomationProperties.NameProperty, "编辑目标 " + g.Title); Theme.Put(header, title);
                TextBlock percentage = Theme.Text(progress.Percent + "%", compact ? 20 : 25, Theme.Green, true); percentage.HorizontalAlignment = HorizontalAlignment.Right;
                percentage.SetValue(AutomationProperties.NameProperty, "目标百分比 " + g.Id); Theme.Put(header, percentage, 1); contents.Children.Add(header);
                Theme.Gap(contents, 10);
                ProgressBar bar = new ProgressBar { Minimum = 0, Maximum = progress.Total == 0 ? 1 : progress.Total, Value = progress.Done };
                double previous, fraction = progress.Total == 0 ? 0 : (double)progress.Done / progress.Total;
                bool seen = lastGoalProgress.TryGetValue(g.Id, out previous);
                Motion.Progress(bar, seen ? previous : 0, fraction);
                lastGoalProgress[g.Id] = fraction;
                bar.SetValue(AutomationProperties.NameProperty, "目标进度 " + g.Id); contents.Children.Add(bar);
                Theme.Gap(contents, 9);
                Grid detail = Theme.Grid(2); detail.ColumnDefinitions[1].Width = GridLength.Auto;
                string state = progress.Complete ? "已达成" : progress.Total == 0 ? "等待拆分第一步" : "进行中";
                TextBlock count = Theme.Text(progress.Done + " / " + progress.Total + " 项已完成  ·  " + state, 11, Theme.Muted);
                count.SetValue(AutomationProperties.NameProperty, "目标完成数量 " + g.Id); Theme.Put(detail, count);
                Button toggle = Theme.Button(expanded ? "收起 ▴" : "展开 ▾", delegate { if (!expandedGoals.Add(g.Id)) expandedGoals.Remove(g.Id); RenderGoals(); }, "Quiet");
                toggle.Padding = new Thickness(8, 0, 0, 0); toggle.FontSize = 11; toggle.HorizontalAlignment = HorizontalAlignment.Right;
                toggle.SetValue(AutomationProperties.NameProperty, "展开目标 " + g.Title); Theme.Put(detail, toggle, 1); contents.Children.Add(detail);
                if (g.TargetDate.Length > 0)
                {
                    DateTime target = Dates.ParseDay(g.TargetDate); bool overdue = !progress.Complete && target < DateTime.Today;
                    TextBlock due = Theme.Text((overdue ? "已过目标日期 · " : "目标日期 · ") + target.ToString("yyyy/M/d"), 11, overdue ? Theme.Brush("#B36A55") : Theme.Muted); due.Margin = new Thickness(0, 8, 0, 0); contents.Children.Add(due);
                }
                if (expanded)
                {
                    if (g.Notes.Length > 0)
                    {
                        TextBlock notes = Theme.Text(g.Notes, 12, Theme.Muted); notes.TextWrapping = TextWrapping.Wrap; notes.LineHeight = 21; notes.Margin = new Thickness(0, 12, 0, 0); contents.Children.Add(notes);
                    }
                    Theme.Gap(contents, 12);
                    contents.Children.Add(new Border { Height = 1, Background = Theme.Line }); Theme.Gap(contents, 8);
                    List<Todo> items = store.Data.Todos.Where(t => t.GoalId == g.Id).OrderBy(t => t.Done).ThenByDescending(t => t.Important).ToList();
                    if (items.Count == 0)
                    {
                        TextBlock first = Theme.Text("还没有待办，先定下一个小步骤。", 12, Theme.Muted); first.Margin = new Thickness(0, 5, 0, 8); contents.Children.Add(first);
                    }
                    foreach (Todo task in items)
                    {
                        Grid row = Theme.Grid(2); row.ColumnDefinitions[0].Width = new GridLength(28); row.Margin = new Thickness(0, 3, 0, 3);
                        CheckBox check = new CheckBox { IsChecked = task.Done, VerticalAlignment = VerticalAlignment.Center, ToolTip = task.Done ? "恢复待办并更新目标进度" : "完成待办并更新目标进度" };
                        check.SetValue(AutomationProperties.NameProperty, "目标待办 " + task.Title);
                        check.Click += delegate { Motion.Remember(check, true); Commit(delegate { task.Done = check.IsChecked == true; }, "已更新待办和目标进度"); }; Theme.Put(row, check);
                        StackPanel text = new StackPanel(); TextBlock label = Theme.Text((task.Important ? "★  " : "") + task.Title, 13, task.Done ? Theme.Muted : Theme.Ink); label.TextWrapping = TextWrapping.Wrap; if (task.Done) label.TextDecorations = TextDecorations.Strikethrough; text.Children.Add(label);
                        if (task.Due.Length > 0)
                        {
                            DateTime due = Dates.ParseDay(task.Due); bool overdue = !task.Done && due < DateTime.Today;
                            TextBlock date = Theme.Text((overdue ? "已逾期 · " : "截止 · ") + due.ToString("yyyy/M/d"), 10, overdue ? Theme.Brush("#B36A55") : Theme.Muted); date.Margin = new Thickness(0, 4, 0, 0); text.Children.Add(date);
                        }
                        Button edit = Theme.Button("", delegate { EditTodo(task); }, "Quiet"); edit.Content = text; edit.Padding = new Thickness(0, 5, 0, 5); edit.HorizontalContentAlignment = HorizontalAlignment.Left; edit.ToolTip = "编辑待办或调整所属目标";
                        edit.SetValue(AutomationProperties.NameProperty, "编辑目标待办 " + task.Title); Theme.Put(row, edit, 1); contents.Children.Add(row);
                    }
                }
                Theme.Gap(contents, 9);
                Button add = Theme.Button("＋ 添加待办", delegate { EditTodo(null, g.Id); }, "Quiet"); add.HorizontalAlignment = HorizontalAlignment.Left; add.Foreground = Theme.Green; add.Padding = new Thickness(0, 5, 0, 5);
                add.SetValue(AutomationProperties.NameProperty, "添加目标待办 " + g.Title); contents.Children.Add(add);
                Border card = new Border { Background = Glass.Inner, BorderBrush = Theme.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(18), Padding = new Thickness(compact ? 14 : 20), Margin = new Thickness(4, 4, 4, 14), Child = contents };
                Motion.Surface(card, true);
                if (!seen) card.Loaded += delegate { Motion.Enter(card); };
                goalList.Children.Add(card);
            }
        }
    }
    public class GoalDialog : FormDialog
    {
        public LongTermGoal Result;
        readonly LongTermGoal original;
        TextBox title, notes;
        DatePicker target;
        CheckBox dated;
        public GoalDialog(Window owner, LongTermGoal item) : base(owner, item == null ? "给未来，定一个方向" : "编辑长期目标", "拆成小步骤，进度看得见。", 570)
        {
            original = item;
            title = Input("目标名称 *", item == null ? "" : item.Title, 120);
            dated = new CheckBox { Content = "设置目标日期", IsChecked = item != null && item.TargetDate.Length > 0 }; Fields.Children.Add(dated);
            target = DateField(item != null && item.TargetDate.Length > 0 ? Dates.ParseDay(item.TargetDate) : DateTime.Today.AddMonths(3)); target.IsEnabled = dated.IsChecked == true; target.Margin = new Thickness(0, 3, 0, 16); Fields.Children.Add(target);
            dated.Checked += delegate { target.IsEnabled = true; }; dated.Unchecked += delegate { target.IsEnabled = false; };
            notes = Input("目标说明（可选）", item == null ? "" : item.Notes, 4000, true);
            TextBlock hint = Theme.Text("保存后可逐项添加待办，也可以把现有待办归入目标。进度按完成数量自动计算。", 12, Theme.Muted); hint.TextWrapping = TextWrapping.Wrap; hint.LineHeight = 22; Fields.Children.Add(hint);
            if (item != null) DeleteButton(delegate
            {
                if (MessageBox.Show(this, "确定删除目标“" + item.Title + "”？\n\n所有待办都会保留，并转为独立待办。", "删除目标", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes) { Deleted = true; DialogResult = true; }
            });
            Button cancel = Button("取消", delegate { DialogResult = false; }); cancel.IsCancel = true;
            Button save = Button("保存目标", Save, true); save.IsDefault = true;
            Loaded += delegate { title.Focus(); title.CaretIndex = title.Text.Length; };
        }
        void Save()
        {
            Error.Text = "";
            if (String.IsNullOrWhiteSpace(title.Text)) { Error.Text = "请填写目标名称。"; title.Focus(); return; }
            if (dated.IsChecked == true && (!target.SelectedDate.HasValue || target.SelectedDate.Value.Year < 1900 || target.SelectedDate.Value.Year > 2100)) { Error.Text = "请选择 1900—2100 年之间的目标日期。"; return; }
            Result = new LongTermGoal { Id = original == null ? Guid.NewGuid().ToString("N") : original.Id, Title = title.Text.Trim(), Notes = notes.Text.Trim(), TargetDate = dated.IsChecked == true ? Dates.Day(target.SelectedDate.Value) : "" };
            DialogResult = true;
        }
    }
}
