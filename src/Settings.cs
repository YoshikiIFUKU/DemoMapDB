using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace StoreMapDemo
{
    /// <summary>PCごとの設定（テーマ、既定の表示件数、オンライン住所検索の可否）。データファイルと同じフォルダの demomapdb_settings.ini に保存する。</summary>
    public static class Settings
    {
        public const string FileName = "demomapdb_settings.ini";
        static readonly Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        static string path;

        public static void Load(string dataFilePath)
        {
            try
            {
                path = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(dataFilePath)), FileName);
                values.Clear();
                if (!File.Exists(path)) return;
                foreach (var line in File.ReadAllLines(path, Encoding.UTF8))
                {
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    values[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
                }
            }
            catch { }
        }

        public static string Get(string key, string fallback = "")
        {
            string v;
            return values.TryGetValue(key, out v) ? v : fallback;
        }

        public static int GetInt(string key, int fallback)
        {
            int n;
            return int.TryParse(Get(key, ""), out n) ? n : fallback;
        }

        public static bool GetBool(string key, bool fallback)
        {
            string v = Get(key, "");
            if (v.Length == 0) return fallback;
            return v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase);
        }

        public static void Set(string key, string value)
        {
            values[key] = value ?? "";
            Save();
        }

        static void Save()
        {
            if (path == null) return;
            try
            {
                var sb = new StringBuilder();
                foreach (var kv in values) sb.AppendLine(kv.Key + "=" + kv.Value);
                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            }
            catch { }
        }
    }
}
