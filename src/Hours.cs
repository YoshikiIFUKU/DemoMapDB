using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace StoreMapDemo
{
    /// <summary>営業しているかどうか</summary>
    public enum OpenState
    {
        Unknown,   // 営業時間が書かれていない・読めない（しぼり込みでは除外しない）
        Open,      // 営業時間内
        Closed,    // 営業時間外
    }

    /// <summary>
    /// 「9:00-21:00」のような営業時間の文字列を読み、その時刻に営業しているかを判定する。
    /// ・区切りは - 〜 ～ ~ / 全角も可。複数の時間帯は , 、 / 空白 で並べられる（例: 11:00-14:00, 17:00-22:00）
    /// ・終わりが始まりより前なら日をまたぐ扱い（例: 22:00-翌5:00 は 22:00-5:00 と書く）
    /// ・「24時間」「終日」は常に営業。「定休」「休業」だけなら営業時間外
    /// ・カッコ書き（L.O. など）は無視する
    /// ※ デモ用のため、曜日ごとの営業時間や祝日は見ていません。
    /// </summary>
    public static class OpeningHours
    {
        public static OpenState Check(string text, DateTime at)
        {
            var ranges = Parse(text);
            if (ranges == null) return OpenState.Unknown;       // 読めない
            if (ranges.Count == 0) return OpenState.Closed;     // 「定休日」など
            if (ranges.Count == 1 && ranges[0].Key == 0 && ranges[0].Value >= 1440) return OpenState.Open;

            int now = at.Hour * 60 + at.Minute;
            foreach (var r in ranges)
                if ((now >= r.Key && now < r.Value) || (now + 1440 >= r.Key && now + 1440 < r.Value))
                    return OpenState.Open;
            return OpenState.Closed;
        }

        public static string Label(OpenState state)
        {
            switch (state)
            {
                case OpenState.Open: return "営業中";
                case OpenState.Closed: return "時間外";
                default: return "";
            }
        }

        /// <summary>時間帯の一覧（分単位）。読めなければ null、「定休日」なら空のリスト。</summary>
        static List<KeyValuePair<int, int>> Parse(string text)
        {
            string s = Normalize(text);
            if (s.Length == 0) return null;

            if (s.Contains("24時間") || s.Contains("24h") || s.Contains("終日") || s.Contains("年中無休"))
                return new List<KeyValuePair<int, int>> { new KeyValuePair<int, int>(0, 1440) };

            var ranges = new List<KeyValuePair<int, int>>();
            foreach (var part in s.Split(',', '、', '/', ' ', '|'))
            {
                string p = part.Trim();
                if (p.Length == 0) continue;
                int sep = p.IndexOf('-');
                if (sep <= 0) continue;
                int from, to;
                if (!TryTime(p.Substring(0, sep), out from) || !TryTime(p.Substring(sep + 1), out to)) continue;
                if (to <= from) to += 1440;     // 日をまたぐ
                ranges.Add(new KeyValuePair<int, int>(from, to));
            }
            if (ranges.Count > 0) return ranges;

            // 時間帯が1つも読めなかった場合、休みと書いてあれば「営業時間外」、そうでなければ「読めない」
            if (s.Contains("定休") || s.Contains("休業") || s.Contains("閉店") || s == "休") return ranges;
            return null;
        }

        /// <summary>全角→半角、区切り記号と時刻表記をそろえ、カッコ書きを落とす</summary>
        static string Normalize(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var sb = new StringBuilder();
            int depth = 0;
            foreach (char raw in text.Normalize(NormalizationForm.FormKC))
            {
                char c = raw;
                if (c == '(' || c == '（' || c == '[' || c == '【') { depth++; continue; }
                if (c == ')' || c == '）' || c == ']' || c == '】') { if (depth > 0) depth--; continue; }
                if (depth > 0) continue;
                if (c == '〜' || c == '～' || c == '~' || c == '－' || c == 'ー' || c == '–' || c == '―' || c == '−') c = '-';
                if (c == '：') c = ':';
                if (c == '時') c = ':';
                if (c == '分' || c == '頃') continue;
                sb.Append(char.ToLowerInvariant(c));
            }
            return sb.ToString().Trim();
        }

        /// <summary>「9:00」「9」「21:30」「25:00」を0時からの分に直す（深夜表記の25時なども許す）</summary>
        static bool TryTime(string text, out int minutes)
        {
            minutes = 0;
            string t = text.Trim().TrimEnd(':');
            if (t.Length == 0) return false;
            int colon = t.IndexOf(':');
            string hh = colon < 0 ? t : t.Substring(0, colon);
            string mm = colon < 0 ? "0" : t.Substring(colon + 1);
            if (mm.Length == 0) mm = "0";
            int h, m;
            if (!int.TryParse(hh, NumberStyles.Integer, CultureInfo.InvariantCulture, out h)) return false;
            if (!int.TryParse(mm, NumberStyles.Integer, CultureInfo.InvariantCulture, out m)) return false;
            if (h < 0 || h > 47 || m < 0 || m > 59) return false;
            minutes = h * 60 + m;
            return true;
        }
    }
}
