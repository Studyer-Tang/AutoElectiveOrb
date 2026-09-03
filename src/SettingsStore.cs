using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace AutoElectiveOrb
{
    internal sealed class SettingsStore
    {
        private readonly JavaScriptSerializer serializer = new JavaScriptSerializer();
        public string DataDirectory { get; private set; }
        public string SettingsPath { get; private set; }

        public SettingsStore()
        {
            DataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AutoElectiveOrb");
            SettingsPath = Path.Combine(DataDirectory, "settings.json");
        }

        public AppSettings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var value = serializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath, Encoding.UTF8));
                    if (value != null)
                    {
                        if (value.Courses == null) value.Courses = new System.Collections.Generic.List<CourseSetting>();
                        return value;
                    }
                }
            }
            catch { }
            return new AppSettings();
        }

        public void Save(AppSettings settings)
        {
            Directory.CreateDirectory(DataDirectory);
            var temporary = SettingsPath + ".tmp";
            File.WriteAllText(temporary, serializer.Serialize(settings), new UTF8Encoding(false));
            if (File.Exists(SettingsPath)) File.Replace(temporary, SettingsPath, null);
            else File.Move(temporary, SettingsPath);
        }

        public string WriteEngineConfig(AppSettings settings)
        {
            Directory.CreateDirectory(DataDirectory);
            var path = Path.Combine(DataDirectory, "engine.ini");
            var builder = new StringBuilder();
            builder.AppendLine("[user]");
            builder.AppendLine("student_id = " + Ini(settings.StudentId));
            builder.AppendLine("dual_degree = " + (settings.DualDegree ? "true" : "false"));
            builder.AppendLine("identity = " + Ini(settings.Identity));
            builder.AppendLine();
            builder.AppendLine("[client]");
            builder.AppendLine("refresh_interval = " + settings.RefreshInterval.ToString(System.Globalization.CultureInfo.InvariantCulture));
            builder.AppendLine("random_deviation = 0.15");
            builder.AppendLine("iaaa_client_timeout = 20");
            builder.AppendLine("elective_client_timeout = 35");
            builder.AppendLine("elective_client_pool_size = 1");
            builder.AppendLine("elective_client_max_life = 600");
            builder.AppendLine("login_loop_interval = 4");
            builder.AppendLine("print_mutex_rules = false");
            builder.AppendLine("debug_print_request = false");
            builder.AppendLine("debug_dump_request = false");
            builder.AppendLine();
            builder.AppendLine("[captcha]");
            builder.AppendLine("provider = local");
            builder.AppendLine();
            builder.AppendLine("[safety]");
            var hasSwap = settings.Courses.Exists(course => course.IsSwap);
            builder.AppendLine("enable_unsafe_auto_swap = " + (hasSwap ? "true" : "false"));
            var choiceGroups = new Dictionary<string, List<int>>(StringComparer.Ordinal);
            for (var index = 0; index < settings.Courses.Count; index++)
            {
                var course = settings.Courses[index];
                var id = index + 1;
                builder.AppendLine();
                builder.AppendLine("[course:" + id + "]");
                builder.AppendLine("name = " + Ini(course.Name));
                builder.AppendLine("class = " + course.ClassNo);
                builder.AppendLine("school = " + Ini(course.School));
                if (!string.IsNullOrWhiteSpace(course.SwapGroup))
                {
                    List<int> members;
                    if (!choiceGroups.TryGetValue(course.SwapGroup, out members))
                    {
                        members = new List<int>();
                        choiceGroups[course.SwapGroup] = members;
                    }
                    members.Add(id);
                }
                if (course.IsSwap)
                {
                    builder.AppendLine("drop_name = " + Ini(course.DropName));
                    builder.AppendLine("drop_class = " + course.DropClassNo);
                    builder.AppendLine("drop_school = " + Ini(course.DropSchool));
                }
                if (course.Threshold > 0)
                {
                    builder.AppendLine();
                    builder.AppendLine("[delay:" + id + "]");
                    builder.AppendLine("course = " + id);
                    builder.AppendLine("threshold = " + course.Threshold);
                }
            }
            var groupNumber = 0;
            foreach (var group in choiceGroups.Values)
            {
                if (group.Count < 2) continue;
                groupNumber++;
                builder.AppendLine();
                builder.AppendLine("[mutex:choice_group_" + groupNumber + "]");
                builder.AppendLine("courses = " + string.Join(",", group));
            }
            File.WriteAllText(path, builder.ToString(), new UTF8Encoding(false));
            return path;
        }

        private static string Ini(string value)
        {
            return (value ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
        }
    }
}
