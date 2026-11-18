using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;

namespace DayFlow
{
    public static class SelfTests
    {
        static int checks;
        static void Check(bool value, string message) { checks++; if (!value) throw new Exception("FAIL: " + message); }
        public static void Run(string[] args)
        {
            int index = Array.IndexOf(args, "--test-dir");
            string folder = index >= 0 && index + 1 < args.Length ? Path.GetFullPath(args[index + 1]) : Path.Combine(Program.BaseFolder, "self-test");
            Directory.CreateDirectory(folder);
            string report = Path.Combine(folder, "test-result.txt");
            try
            {
                string dataPath = Path.Combine(folder, "store-" + Guid.NewGuid().ToString("N")); Store store = new Store(dataPath);
                Check(store.Data.Events.Count == 0, "clean startup");
                DateTime start = new DateTime(2026, 11, 16, 23, 30, 0);
                CalendarEvent timed = new CalendarEvent { Title = "跨天会议,项目;讨论\\中文\n第二行 😀" + new string('中', 60), Start = Dates.Format(start), End = Dates.Format(start.AddHours(2)), Location = "会议室 A, 二楼", Notes = "第一行\r\n第二行", Category = "工作", Reminder = 15 };
                CalendarEvent allDay = new CalendarEvent { Title = "旅行", Start = "2026-11-18T00:00:00", End = "2026-11-21T00:00:00", AllDay = true, Reminder = -1 };
                Todo task = new Todo { Title = "带上证件", Due = "2026-11-18", Important = true };
                Todo done = new Todo { Title = "完成的待办", Due = "2026-11-18", Done = true };
                Todo undated = new Todo { Title = "没有日期" };
                store.Change(delegate { store.Data.Events.Add(timed); store.Data.Events.Add(allDay); store.Data.Todos.Add(task); store.Data.Todos.Add(done); store.Data.Todos.Add(undated); });
                Check(new Store(dataPath).Data.Events.Count == 2, "save and reload events");
                Check(new Store(dataPath).Data.Events[0].Title == timed.Title, "unicode persisted exactly");
                Check(timed.OnDay(new DateTime(2026, 11, 16)) && timed.OnDay(new DateTime(2026, 11, 17)) && !timed.OnDay(new DateTime(2026, 11, 18)), "cross-midnight display");
                Check(allDay.OnDay(new DateTime(2026, 11, 20)) && !allDay.OnDay(new DateTime(2026, 11, 21)), "all-day exclusive boundary");
                bool invalid = false;
                try { store.Change(delegate { store.Data.Events.Add(new CalendarEvent { Title = "invalid", Start = timed.Start, End = timed.Start }); }); } catch { invalid = true; }
                Check(invalid && store.Data.Events.Count == 2, "failed validation rolls back");
                store.Change(delegate { store.Data.Todos[0].Done = true; });
                Check(new Store(dataPath).Data.Todos[0].Done, "completion survives restart");
                store.Change(delegate { store.Data.Todos[0].Done = false; });
                store.ExportBackup(Path.Combine(folder, "backup.json"));
                Check(Store.ReadFile(Path.Combine(folder, "backup.json")).Todos.Count == 3, "backup round trip");
                File.WriteAllText(store.Pathname, "{broken}");
                Store recovered = new Store(dataPath);
                Check(recovered.StartupNotice.Length > 0 && recovered.Data.Events.Count == 2 && Directory.GetFiles(dataPath, "calendar.damaged-*.json").Length == 1, "recover damaged data without discarding original");
                Check(Store.ReadFile(store.Pathname).Events.Count == 2, "recovered data written back");
                recovered.Data.Events[0].Title = "";
                bool rejected = false; try { Store.Validate(recovered.Data); } catch { rejected = true; }
                Check(rejected, "invalid backup rejected");
                string goalPath = Path.Combine(folder, "goals-" + Guid.NewGuid().ToString("N"));
                Store goals = new Store(goalPath);
                LongTermGoal goal = new LongTermGoal { Title = "读完 12 本书", Notes = "每个月读一本", TargetDate = "2027-11-16" };
                goals.Change(delegate { goals.Data.Goals.Add(goal); });
                Check(GoalProgress.For(goals.Data, goal.Id).Percent == 0 && !GoalProgress.For(goals.Data, goal.Id).Complete, "empty goal starts at zero instead of complete");
                Todo a = new Todo { Title = "列出书单", GoalId = goal.Id, Done = true };
                Todo c = new Todo { Title = "阅读第一本", GoalId = goal.Id };
                Todo d = new Todo { Title = "写一篇读书笔记", GoalId = goal.Id };
                goals.Change(delegate { goals.Data.Todos.Add(a); goals.Data.Todos.Add(c); goals.Data.Todos.Add(d); goals.Data.Todos.Add(new Todo { Title = "独立待办", Done = true }); });
                Check(GoalProgress.For(goals.Data, goal.Id).Percent == 33 && GoalProgress.For(goals.Data, goal.Id).Done == 1, "progress counts goal members and rounds percentage");
                LongTermGoal secondGoal = new LongTermGoal { Title = "第二个目标" };
                goals.Change(delegate { goals.Data.Goals.Add(secondGoal); goals.Data.Todos.Add(new Todo { Title = "另一个目标的步骤", GoalId = secondGoal.Id, Done = true }); });
                Check(GoalProgress.For(goals.Data, goal.Id).Percent == 33 && GoalProgress.For(goals.Data, secondGoal.Id).Percent == 100, "multiple goals calculate progress independently");
                goals.Change(delegate { goals.Data.Todos.RemoveAll(t => t.GoalId == secondGoal.Id); GoalProgress.RemoveGoal(goals.Data, secondGoal.Id); });
                Check(new GoalProgress { Total = 200, Done = 199 }.Percent == 99, "incomplete goal never rounds to 100 percent");
                goals.Change(delegate { c.Done = true; d.Done = true; });
                Check(GoalProgress.For(goals.Data, goal.Id).Percent == 100 && GoalProgress.For(goals.Data, goal.Id).Complete, "all subtasks complete goal");
                goals.Change(delegate { c.Done = false; });
                Check(GoalProgress.For(goals.Data, goal.Id).Percent == 67 && !GoalProgress.For(goals.Data, goal.Id).Complete, "reopening a task lowers goal progress");
                Store goalReload = new Store(goalPath);
                Check(goalReload.Data.Goals.Count == 1 && GoalProgress.For(goalReload.Data, goal.Id).Done == 2, "goal and member links survive restart");
                goals.ExportBackup(Path.Combine(folder, "goals-backup.json"));
                AppData goalBackup = Store.ReadFile(Path.Combine(folder, "goals-backup.json"));
                Check(goalBackup.Goals[0].TargetDate == goal.TargetDate && goalBackup.Todos.Count(t => t.GoalId == goal.Id) == 3, "backup preserves goal metadata and membership");
                bool orphanRejected = false;
                try { goals.Change(delegate { goals.Data.Todos.Add(new Todo { Title = "无效成员", GoalId = Guid.NewGuid().ToString("N") }); }); } catch { orphanRejected = true; }
                Check(orphanRejected && goals.Data.Todos.Count == 4, "dangling membership rejected and rolled back");
                goals.Change(delegate { goals.Data.Todos[0].GoalId = ""; });
                Check(GoalProgress.For(goals.Data, goal.Id).Total == 2 && GoalProgress.For(goals.Data, goal.Id).Percent == 50, "detaching task recalculates denominator");
                goals.Change(delegate { GoalProgress.RemoveGoal(goals.Data, goal.Id); });
                Check(goals.Data.Goals.Count == 0 && goals.Data.Todos.Count == 4 && goals.Data.Todos.All(t => t.GoalId == ""), "removing goal preserves all tasks as independent");
                string legacyPath = Path.Combine(folder, "legacy-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(legacyPath);
                string legacyJson = "{\"Version\":1,\"Events\":[],\"Todos\":[{\"Id\":\"" + Guid.NewGuid().ToString("N") + "\",\"Title\":\"旧版待办\",\"Due\":\"\",\"Done\":true,\"Important\":false,\"Notes\":\"保留备注\"}],\"Settings\":{\"Topmost\":true,\"Compact\":true}}";
                File.WriteAllText(Path.Combine(legacyPath, "calendar.json"), legacyJson, Encoding.UTF8);
                Store legacy = new Store(legacyPath);
                Check(legacy.Data.Version == 2 && legacy.Data.Goals.Count == 0 && legacy.Data.Todos[0].GoalId == "" && legacy.Data.Todos[0].Done && legacy.Data.Settings.Compact, "version 1 migrates without losing tasks or settings");
                Check(File.ReadAllText(legacy.Pathname + ".v1-backup", Encoding.UTF8) == legacyJson, "legacy upgrade preserves original file separately");
                legacy.Change(delegate { legacy.Data.Goals.Add(new LongTermGoal { Title = "新目标" }); });
                Check(new Store(legacyPath).Data.Goals.Count == 1 && new Store(legacyPath).Data.Todos[0].Notes == "保留备注", "migrated data saves and reopens with goals");
                Check(Store.ReadFile(legacy.Pathname + ".v1-backup").Version == 2, "legacy JSON backups can still be restored");
                string ics = ICalendar.Build(new[] { timed, allDay }, new[] { task, done, undated });
                File.WriteAllText(Path.Combine(folder, "compatibility.ics"), ics, new UTF8Encoding(false));
                Check(!ics.Replace("\r\n", "").Contains("\n") && ics.EndsWith("END:VCALENDAR\r\n"), "RFC5545 CRLF and final terminator");
                Check(ics.Split(new[] { "BEGIN:VEVENT" }, StringSplitOptions.None).Length - 1 == 3, "two events plus eligible todo only");
                Check(ics.Contains("DTSTART;VALUE=DATE:20261118\r\nDTEND;VALUE=DATE:20261121"), "all-day date format and exclusive end");
                Check(ics.Contains("DTSTART:" + start.ToUniversalTime().ToString("yyyyMMdd'T'HHmmss'Z'")), "timed events convert to UTC");
                Check(ics.Contains("TRIGGER:-PT15M"), "alarm included");
                foreach (string line in ics.Split(new[] { "\r\n" }, StringSplitOptions.None)) Check(Encoding.UTF8.GetByteCount(line) <= 75, "line folding at 75 octets");
                string unfolded = ics.Replace("\r\n ", "");
                Check(unfolded.Contains("SUMMARY:" + ICalendar.Escape(timed.Title)), "unicode and escaped punctuation round trip");
                Check(!unfolded.Contains("SUMMARY:完成的待办") && !unfolded.Contains("SUMMARY:没有日期"), "completed and undated tasks omitted");
                Check(ICalendar.Escape("a\r\nBEGIN:VEVENT") == "a\\nBEGIN:VEVENT", "newline cannot inject calendar properties");
                Check(new UTF8Encoding(false, true).GetString(new UTF8Encoding(false, true).GetBytes(ics)) == ics, "valid UTF8 including emoji");
                Application app = new Application(); app.Resources = Theme.Resources();
                Check(app.Resources.Count > 0, "WPF theme parses");
                AppData todayData = new AppData();
                DateTime today = DateTime.Today;
                Todo todayOpen = new Todo { Title = "今天未完成", Due = Dates.Day(today) };
                Todo todayDone = new Todo { Title = "今天已完成", Due = Dates.Day(today), Done = true };
                todayData.Todos.AddRange(new[] { todayOpen, todayDone, new Todo { Title = "昨天", Due = Dates.Day(today.AddDays(-1)) }, new Todo { Title = "明天", Due = Dates.Day(today.AddDays(1)) }, new Todo { Title = "无日期" } });
                Check(TodayTasks.For(todayData, today).Count == 2 && TodayTasks.For(todayData, today).Count(t => t.Done) == 1, "floating dots include exactly today's open and completed tasks");
                todayOpen.Done = true;
                Check(TodayTasks.For(todayData, today)[0].Id == todayOpen.Id, "completion preserves dot ordering");
                Check(TodayTasks.For(todayData, today.AddDays(1)).Single().Title == "明天", "day rollover selects the new date without stale dots");
                Check(!new Preferences().Floating && !new Preferences().HasFloatingPosition, "older settings default to normal mode with safe placement");
                Store floatStore = new Store(Path.Combine(folder, "floating-settings"));
                floatStore.Change(delegate { floatStore.Data.Settings.Floating = true; floatStore.Data.Settings.HasFloatingPosition = true; floatStore.Data.Settings.FloatingLeft = -550; floatStore.Data.Settings.FloatingTop = 120; });
                Preferences remembered = new Store(floatStore.Folder).Data.Settings;
                Check(remembered.Floating && remembered.HasFloatingPosition && remembered.FloatingLeft == -550 && remembered.FloatingTop == 120, "floating mode and negative-monitor position persist");
                Rect screen = new Rect(0, 0, 1920, 1040);
                Rect offscreen = FloatingPlacement.Clamp(new Rect(4000, 2000, 324, 154), new[] { screen });
                Check(screen.Contains(offscreen), "disconnected monitor restores bar inside available work area");
                Rect leftScreen = new Rect(-1920, 0, 1920, 1040);
                Check(FloatingPlacement.Clamp(new Rect(-600, 200, 324, 82), new[] { screen, leftScreen }).Left == -600, "valid negative monitor position remains unchanged");
                string command = StartupRegistration.Command(@"C:\Calendar folder\日常日历.exe", @"C:\Calendar folder\data");
                Check(command == "\"C:\\Calendar folder\\日常日历.exe\" --startup --data-dir \"C:\\Calendar folder\\data\"", "startup command quotes Unicode executable and data paths");
                bool badCommand = false; try { StartupRegistration.Command("C:\\bad\".exe", "C:\\data"); } catch (ArgumentException) { badCommand = true; }
                Check(badCommand, "startup command rejects quote injection");
                string linkPath = Path.Combine(folder, "开机启动测试.lnk");
                string actualExe = System.Reflection.Assembly.GetExecutingAssembly().Location;
                StartupRegistration.SaveShortcut(linkPath, actualExe, floatStore.Folder);
                Check(new FileInfo(linkPath).Length > 0 && StartupRegistration.MatchesShortcut(linkPath, actualExe, floatStore.Folder), "real Windows shortcut preserves executable and data arguments");
                Check(!StartupRegistration.MatchesShortcut(linkPath, actualExe, floatStore.Folder + "-other"), "shortcut for a different data folder is not reported enabled");
                Store uiStore = new Store(Path.Combine(folder, "ui-store"));
                MainWindow window = new MainWindow(uiStore, false);
                new System.Windows.Interop.WindowInteropHelper(window).EnsureHandle();
                window.Measure(new Size(1160, 810)); window.Arrange(new Rect(0, 0, 1160, 810));
                Check(window.Content != null, "main window builds");
                EventDialog ed = new EventDialog(window, DateTime.Today, null); Check(ed.Content != null, "event editor builds"); ed.Close();
                TodoDialog td = new TodoDialog(window, DateTime.Today, null); Check(td.Content != null, "todo editor builds"); td.Close();
                GoalDialog gd = new GoalDialog(window, null); Check(gd.Content != null, "goal editor builds"); gd.Close();
                ExportDialog xd = new ExportDialog(window, uiStore.Data, DateTime.Today); Check(xd.Content != null, "export UI builds for empty data"); xd.Close();
                window.Close();
                File.WriteAllText(report, "PASS: " + checks + " assertions\r\nPersistence, rollback, recovery, backup, dates, RFC5545, alarms, UTF8, goal progress/membership/removal, version 1 migration, WPF main/editor/export construction.\r\n", Encoding.UTF8);
            }
            catch (Exception e) { File.WriteAllText(report, "FAIL\r\n" + e.ToString(), Encoding.UTF8); Environment.ExitCode = 1; }
        }
    }
}
