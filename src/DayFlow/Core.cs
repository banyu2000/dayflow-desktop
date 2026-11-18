using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace DayFlow
{
    public class CalendarEvent
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string Title = "";
        public string Start = "";
        public string End = "";
        public bool AllDay;
        public string Location = "";
        public string Notes = "";
        public string Category = "生活";
        public int Reminder = 15;
        [ScriptIgnore] public DateTime StartAt { get { return Dates.Parse(Start); } }
        [ScriptIgnore] public DateTime EndAt { get { return Dates.Parse(End); } }
        [ScriptIgnore] public DateTime LastDay { get { return EndAt.AddTicks(-1).Date; } }
        public bool OnDay(DateTime date) { return StartAt.Date <= date.Date && LastDay >= date.Date; }
    }
    public class Todo
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string Title = "";
        public string Due = "";
        public bool Done;
        public bool Important;
        public string Notes = "";
        public string GoalId = "";
    }
    public class LongTermGoal
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string Title = "";
        public string Notes = "";
        public string TargetDate = "";
    }
    public class GoalProgress
    {
        public int Total;
        public int Done;
        public int Percent { get { return Total == 0 ? 0 : Done == Total ? 100 : Math.Min(99, (int)Math.Round(100.0 * Done / Total, MidpointRounding.AwayFromZero)); } }
        public bool Complete { get { return Total > 0 && Done == Total; } }
        public static GoalProgress For(AppData data, string goalId)
        {
            List<Todo> items = data.Todos.Where(t => t.GoalId == goalId).ToList();
            return new GoalProgress { Total = items.Count, Done = items.Count(t => t.Done) };
        }
        public static void RemoveGoal(AppData data, string goalId)
        {
            foreach (Todo t in data.Todos.Where(t => t.GoalId == goalId)) t.GoalId = "";
            data.Goals.RemoveAll(g => g.Id == goalId);
        }
    }
    public class Preferences
    {
        public bool Topmost;
        public bool Compact;
        public bool Floating;
        public bool HasFloatingPosition;
        public double FloatingLeft;
        public double FloatingTop;
    }
    public class AppData
    {
        public int Version = 2;
        public List<CalendarEvent> Events = new List<CalendarEvent>();
        public List<Todo> Todos = new List<Todo>();
        public List<LongTermGoal> Goals = new List<LongTermGoal>();
        public Preferences Settings = new Preferences();
    }
    public static class Dates
    {
        public static string Format(DateTime date) { return date.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture); }
        public static string Day(DateTime date) { return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); }
        public static DateTime Parse(string value)
        {
            return DateTime.ParseExact(value, "yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None);
        }
        public static DateTime ParseDay(string value)
        {
            return DateTime.ParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None);
        }
        public static string Weekday(DateTime date) { return new string[] { "星期日", "星期一", "星期二", "星期三", "星期四", "星期五", "星期六" }[(int)date.DayOfWeek]; }
    }
    public class Store
    {
        public AppData Data;
        public readonly string Folder;
        public readonly string Pathname;
        public string StartupNotice = "";
        static JavaScriptSerializer Serializer()
        {
            return new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024, RecursionLimit = 30 };
        }
        public Store(string folder)
        {
            Folder = folder;
            Pathname = Path.Combine(folder, "calendar.json");
            Directory.CreateDirectory(folder);
            if (!File.Exists(Pathname)) { Data = new AppData(); return; }
            try
            {
                bool upgraded;
                Data = ReadFile(Pathname, out upgraded);
                if (upgraded && !File.Exists(Pathname + ".v1-backup")) File.Copy(Pathname, Pathname + ".v1-backup");
            }
            catch (Exception original)
            {
                if (File.Exists(Pathname + ".bak"))
                {
                    try
                    {
                        Data = ReadFile(Pathname + ".bak");
                        File.Copy(Pathname, Path.Combine(folder, "calendar.damaged-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".json"));
                        File.Copy(Pathname + ".bak", Pathname, true);
                        StartupNotice = "数据文件损坏，已恢复上一次保存的备份。原文件已保留在 data 文件夹。";
                        return;
                    }
                    catch { }
                }
                throw new InvalidDataException("无法读取日历数据。为保护现有内容，程序没有覆盖原文件。\n" + Pathname + "\n" + original.Message);
            }
        }
        public static AppData ReadFile(string path)
        {
            bool upgraded;
            return ReadFile(path, out upgraded);
        }
        static AppData ReadFile(string path, out bool upgraded)
        {
            if (new FileInfo(path).Length > 16 * 1024 * 1024) throw new InvalidDataException("备份文件过大。上限为 16 MB。");
            AppData value = Serializer().Deserialize<AppData>(File.ReadAllText(path, Encoding.UTF8));
            upgraded = value != null && value.Version == 1;
            if (upgraded)
            {
                value.Version = 2;
                if (value.Goals == null) value.Goals = new List<LongTermGoal>();
                if (value.Todos != null) foreach (Todo t in value.Todos) if (t != null && t.GoalId == null) t.GoalId = "";
            }
            Validate(value);
            return value;
        }
        public static void Validate(AppData value)
        {
            if (value == null || value.Version != 2 || value.Events == null || value.Todos == null || value.Goals == null || value.Settings == null)
                throw new InvalidDataException("这不是受支持的日常日历备份。");
            if (value.Events.Count + value.Todos.Count + value.Goals.Count > 10000) throw new InvalidDataException("最多支持 10000 条记录。");
            HashSet<string> ids = new HashSet<string>();
            HashSet<string> goalIds = new HashSet<string>();
            foreach (LongTermGoal g in value.Goals)
            {
                Guid id;
                if (g == null || !Guid.TryParse(g.Id, out id) || !ids.Add(g.Id)) throw new InvalidDataException("目标编号无效或重复。");
                goalIds.Add(g.Id);
                CheckText(g.Title, 120, true); CheckText(g.Notes, 4000, false);
                if (g.TargetDate == null) throw new InvalidDataException("目标日期格式不正确。");
                if (g.TargetDate.Length > 0)
                {
                    DateTime date = Dates.ParseDay(g.TargetDate);
                    if (date.Year < 1900 || date.Year > 2100) throw new InvalidDataException("目标日期须在 1900—2100 之间。");
                }
            }
            foreach (CalendarEvent e in value.Events)
            {
                Guid id;
                if (e == null || !Guid.TryParse(e.Id, out id) || !ids.Add(e.Id)) throw new InvalidDataException("日程编号无效或重复。");
                CheckText(e.Title, 120, true); CheckText(e.Location, 240, false); CheckText(e.Notes, 4000, false);
                if (!(new[] { "生活", "工作", "个人" }).Contains(e.Category)) throw new InvalidDataException("日程分类无效。");
                if (!(new[] { -1, 0, 5, 15, 30, 60, 1440 }).Contains(e.Reminder)) throw new InvalidDataException("提醒时间无效。");
                DateTime start = e.StartAt, end = e.EndAt;
                if (start.Year < 1900 || end.Year > 2100 || end <= start) throw new InvalidDataException("日程结束时间必须晚于开始时间，年份须在 1900—2100 之间。");
                if (e.AllDay && (start.TimeOfDay != TimeSpan.Zero || end.TimeOfDay != TimeSpan.Zero)) throw new InvalidDataException("全天日程日期无效。");
            }
            foreach (Todo t in value.Todos)
            {
                Guid id;
                if (t == null || !Guid.TryParse(t.Id, out id) || !ids.Add(t.Id)) throw new InvalidDataException("待办编号无效或重复。");
                CheckText(t.Title, 120, true); CheckText(t.Notes, 4000, false);
                if (t.GoalId == null || (t.GoalId.Length > 0 && !goalIds.Contains(t.GoalId))) throw new InvalidDataException("待办所属目标不存在。");
                if (!String.IsNullOrEmpty(t.Due))
                {
                    DateTime date = Dates.ParseDay(t.Due);
                    if (date.Year < 1900 || date.Year > 2100) throw new InvalidDataException("待办日期须在 1900—2100 之间。");
                }
            }
        }
        static void CheckText(string value, int max, bool required)
        {
            if (value == null || value.Length > max || (required && String.IsNullOrWhiteSpace(value))) throw new InvalidDataException("标题、地点或备注格式不正确。");
        }
        public void Save()
        {
            Validate(Data);
            string temporary = Pathname + ".tmp";
            File.WriteAllText(temporary, Serializer().Serialize(Data), new UTF8Encoding(false));
            if (File.Exists(Pathname)) File.Replace(temporary, Pathname, Pathname + ".bak", true);
            else File.Move(temporary, Pathname);
        }
        public void Change(Action action)
        {
            string before = Serializer().Serialize(Data);
            try { action(); Save(); }
            catch { Data = Serializer().Deserialize<AppData>(before); throw; }
        }
        public void ExportBackup(string path)
        {
            Validate(Data);
            File.WriteAllText(path, Serializer().Serialize(Data), new UTF8Encoding(false));
        }
    }
    public static class ICalendar
    {
        public static string Escape(string text)
        {
            return (text ?? "").Replace("\\", "\\\\").Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\\n").Replace(";", "\\;").Replace(",", "\\,");
        }
        static void Add(StringBuilder result, string line)
        {
            int bytes = 0;
            for (int i = 0; i < line.Length; i++)
            {
                string ch = line[i].ToString();
                if (Char.IsHighSurrogate(line[i]) && i + 1 < line.Length && Char.IsLowSurrogate(line[i + 1])) ch += line[++i];
                int length = Encoding.UTF8.GetByteCount(ch);
                if (bytes + length > 75) { result.Append("\r\n "); bytes = 1; }
                result.Append(ch); bytes += length;
            }
            result.Append("\r\n");
        }
        static string Utc(DateTime value) { return value.ToUniversalTime().ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture); }
        static string Date(DateTime value) { return value.ToString("yyyyMMdd", CultureInfo.InvariantCulture); }
        public static string Build(IEnumerable<CalendarEvent> events, IEnumerable<Todo> todos)
        {
            StringBuilder b = new StringBuilder();
            Add(b, "BEGIN:VCALENDAR"); Add(b, "VERSION:2.0"); Add(b, "PRODID:-//DayFlow//Desktop Calendar 1.0//ZH-CN");
            Add(b, "CALSCALE:GREGORIAN"); Add(b, "METHOD:PUBLISH"); Add(b, "X-WR-CALNAME:日常日程");
            string stamp = Utc(DateTime.UtcNow);
            foreach (CalendarEvent e in events.OrderBy(x => x.StartAt))
            {
                Add(b, "BEGIN:VEVENT"); Add(b, "UID:" + e.Id + "@dayflow.local"); Add(b, "DTSTAMP:" + stamp);
                if (e.AllDay) { Add(b, "DTSTART;VALUE=DATE:" + Date(e.StartAt)); Add(b, "DTEND;VALUE=DATE:" + Date(e.EndAt)); }
                else { Add(b, "DTSTART:" + Utc(e.StartAt)); Add(b, "DTEND:" + Utc(e.EndAt)); }
                Add(b, "SUMMARY:" + Escape(e.Title));
                if (e.Location.Length > 0) Add(b, "LOCATION:" + Escape(e.Location));
                if (e.Notes.Length > 0) Add(b, "DESCRIPTION:" + Escape(e.Notes));
                Add(b, "CATEGORIES:" + Escape(e.Category)); Add(b, "TRANSP:OPAQUE");
                if (e.Reminder >= 0)
                {
                    Add(b, "BEGIN:VALARM"); Add(b, "TRIGGER:-PT" + e.Reminder.ToString(CultureInfo.InvariantCulture) + "M");
                    Add(b, "ACTION:DISPLAY"); Add(b, "DESCRIPTION:" + Escape(e.Title)); Add(b, "END:VALARM");
                }
                Add(b, "END:VEVENT");
            }
            foreach (Todo t in todos.Where(x => !x.Done && !String.IsNullOrEmpty(x.Due)))
            {
                DateTime due = Dates.ParseDay(t.Due);
                Add(b, "BEGIN:VEVENT"); Add(b, "UID:" + t.Id + "-todo@dayflow.local"); Add(b, "DTSTAMP:" + stamp);
                Add(b, "DTSTART;VALUE=DATE:" + Date(due)); Add(b, "DTEND;VALUE=DATE:" + Date(due.AddDays(1)));
                Add(b, "SUMMARY:" + Escape("待办 · " + t.Title)); Add(b, "TRANSP:TRANSPARENT");
                Add(b, "DESCRIPTION:" + Escape("由日常待办导出为全天日程；完成状态不会同步。" + (t.Notes.Length > 0 ? "\n" + t.Notes : "")));
                Add(b, "END:VEVENT");
            }
            Add(b, "END:VCALENDAR");
            return b.ToString();
        }
    }
}
