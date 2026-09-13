using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Serialization;

namespace StoreMapDemo
{
    /// <summary>検索結果の1件（店舗と、検索地点からの距離・方角）</summary>
    public class Nearby
    {
        public int Rank;
        public Store Store;
        public double DistanceKm;
        public string Direction = "";

        public string DistanceText { get { return GeoMath.FormatKm(DistanceKm); } }
    }

    /// <summary>検索条件</summary>
    public class SearchOptions
    {
        public int Count = 3;            // 表示件数 N
        public double RadiusKm = 0;      // 0 なら距離の上限なし
        public string Keyword = "";      // 店舗名・住所・自由項目の部分一致
        public DateTime? OpenAt;         // 指定するとその時刻に営業している店舗だけにしぼる

        /// <summary>項目ごとのしぼり込み（変数名 → 部分一致する値）</summary>
        public Dictionary<string, string> FieldFilters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public SearchOptions Clone()
        {
            return new SearchOptions
            {
                Count = Count,
                RadiusKm = RadiusKm,
                Keyword = Keyword,
                OpenAt = OpenAt,
                FieldFilters = new Dictionary<string, string>(FieldFilters, StringComparer.OrdinalIgnoreCase),
            };
        }
    }

    public static class Finder
    {
        /// <summary>検索地点に近い順に並べて上位 N 件を返す</summary>
        public static List<Nearby> Nearest(StoreData data, GeoPoint origin, SearchOptions opt)
        {
            if (opt == null) opt = new SearchOptions();
            string kw = TextUtil.Norm(opt.Keyword);
            var filters = opt.FieldFilters
                .Where(kv => !string.IsNullOrEmpty(kv.Value))
                .ToDictionary(kv => kv.Key, kv => TextUtil.Norm(kv.Value));

            var list = data.Stores
                .Where(s => s.HasLocation)
                .Where(s => !opt.OpenAt.HasValue || data.OpenStateOf(s, opt.OpenAt.Value) != OpenState.Closed)
                .Where(s => filters.All(kv => TextUtil.Norm(s.Get(kv.Key)).Contains(kv.Value)))
                .Where(s => kw.Length == 0 || TextUtil.Norm(data.SearchText(s)).Contains(kw))
                .Select(s => new Nearby
                {
                    Store = s,
                    DistanceKm = GeoMath.DistanceKm(origin, new GeoPoint(s.Lat, s.Lng)),
                    Direction = GeoMath.Direction(origin, new GeoPoint(s.Lat, s.Lng)),
                })
                .Where(n => opt.RadiusKm <= 0 || n.DistanceKm <= opt.RadiusKm)
                .OrderBy(n => n.DistanceKm)
                .Take(Math.Max(1, opt.Count))
                .ToList();

            for (int i = 0; i < list.Count; i++) list[i].Rank = i + 1;
            return list;
        }

        /// <summary>標準出力の1列（CSVの見出し・JSONのキー・値の取り出し方）</summary>
        public class OutputColumn
        {
            public string Key;       // rank / name / distance / direction / address / 項目の変数名 / distance_km / lat / lng
            public string Header;    // CSV の見出し
            public string JsonKey;
            public bool Numeric;     // JSON で数値として出す
            public Func<Nearby, string> Get;
        }

        /// <summary>
        /// 標準出力に出す列。［項目の設定］の「一覧・標準出力」に印を付けたものを、
        /// 順位・店舗名・距離・方角・住所・自由項目・距離km・緯度・経度 の順に並べる。full のときは全部。
        /// </summary>
        public static List<OutputColumn> OutputColumns(StoreData data, bool full)
        {
            var cols = new List<OutputColumn>();
            Action<string, string, string, bool, Func<Nearby, string>> add = (key, header, json, numeric, get) =>
            {
                if (full || data.GetOutput(key))
                    cols.Add(new OutputColumn { Key = key, Header = header, JsonKey = json, Numeric = numeric, Get = get });
            };

            add("rank", "順位", "rank", true, n => n.Rank.ToString(CultureInfo.InvariantCulture));
            add("name", data.NameLabel, data.NameApi, false, n => n.Store.Name);
            add("distance", "距離", "distance_text", false, n => n.DistanceText);
            add("direction", "方角", "direction", false, n => n.Direction);
            add("address", data.AddressLabel, data.AddressApi, false, n => n.Store.Address);
            foreach (var f in full ? data.SortedFields : data.ListFields)
            {
                var field = f;
                cols.Add(new OutputColumn
                {
                    Key = field.ApiName, Header = field.Label, JsonKey = field.ApiName,
                    Get = n => n.Store.Get(field.ApiName),
                });
            }
            add("distance_km", "距離km", "distance_km", true, n => n.DistanceKm.ToString("0.000", CultureInfo.InvariantCulture));
            add("lat", "緯度", "lat", true, n => Num(n.Store.Lat));
            add("lng", "経度", "lng", true, n => Num(n.Store.Lng));
            return cols;
        }

        /// <summary>結果をCSVにする（1行目は見出し）</summary>
        public static string ToCsv(StoreData data, List<Nearby> list, bool full)
        {
            var cols = OutputColumns(data, full);
            var sb = new StringBuilder();
            sb.AppendLine(TextUtil.CsvLine(cols.Select(c => c.Header)));
            foreach (var n in list)
                sb.AppendLine(TextUtil.CsvLine(cols.Select(c => c.Get(n))));
            return sb.ToString();
        }

        /// <summary>
        /// 結果をJSONにする（音声認識システム側で読みやすい形）。
        /// status は ok / no_store（該当店舗なし）/ no_address（住所を特定できない）。
        /// 店舗ごとのキーは CSV と同じく［項目の設定］で選んだものだけ。
        /// </summary>
        public static string ToJson(StoreData data, GeocodeResult geo, List<Nearby> list, string status, bool full = false)
        {
            var cols = OutputColumns(data, full);
            var sb = new StringBuilder();
            sb.Append("{\"status\":\"").Append(status ?? "ok").Append("\",");
            sb.Append("\"count\":").Append(list.Count).Append(',');
            sb.Append("\"query\":\"").Append(TextUtil.JsonEscape(geo.Query)).Append("\",");
            sb.Append("\"matched\":\"").Append(TextUtil.JsonEscape(geo.Matched)).Append("\",");
            sb.Append("\"source\":\"").Append(geo.Source).Append("\",");
            sb.Append("\"origin\":{\"lat\":").Append(Num(geo.Point.Lat))
              .Append(",\"lng\":").Append(Num(geo.Point.Lng)).Append("},");
            sb.Append("\"stores\":[");
            for (int i = 0; i < list.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append('{');
                for (int c = 0; c < cols.Count; c++)
                {
                    if (c > 0) sb.Append(',');
                    sb.Append('"').Append(TextUtil.JsonEscape(cols[c].JsonKey)).Append("\":");
                    string v = cols[c].Get(list[i]);
                    if (cols[c].Numeric) sb.Append(v);
                    else sb.Append('"').Append(TextUtil.JsonEscape(v)).Append('"');
                }
                sb.Append('}');
            }
            sb.Append("]}");
            return sb.ToString();
        }

        static string Num(double d) { return d.ToString("0.#####", CultureInfo.InvariantCulture); }

        /// <summary>オペレータが読み上げやすい形（1店舗1行。［項目の設定］で選んだ項目だけ）</summary>
        public static string ToText(StoreData data, List<Nearby> list, bool full = false)
        {
            var cols = OutputColumns(data, full);
            var sb = new StringBuilder();
            foreach (var n in list)
            {
                var parts = new List<string>();
                string near = string.Join(" ", cols.Where(c => c.Key == "distance" || c.Key == "direction")
                                                   .Select(c => c.Get(n)).ToArray());
                foreach (var c in cols)
                {
                    if (c.Key == "distance" || c.Key == "direction") continue;
                    string v = c.Get(n);
                    if (v.Length == 0) continue;
                    if (c.Key == "rank") { parts.Add(v + "."); continue; }
                    parts.Add(v);
                    if (c.Key == "name" && near.Length > 0) { parts[parts.Count - 1] += "（" + near + "）"; near = ""; }
                }
                if (near.Length > 0) parts.Add("（" + near + "）");
                sb.AppendLine(string.Join(" ", parts.ToArray()));
            }
            return sb.ToString();
        }
    }

    // ---- コマンドラインの検索結果を、開いている画面へ渡すための受け渡しファイル

    public class QueryFilter
    {
        [XmlAttribute("name")]
        public string Name = "";
        [XmlText]
        public string Value = "";

        public QueryFilter() { }
        public QueryFilter(string name, string value) { Name = name; Value = value ?? ""; }
    }

    [XmlRoot("DemoMapDbQuery")]
    public class LastQuery
    {
        public string Query = "";
        public string Matched = "";
        public string Source = "";
        public double Lat, Lng;
        public int Count = 3;
        public double RadiusKm;
        public string Keyword = "";
        public DateTime OpenAt;          // 既定値(0001/01/01)なら営業時間でしぼらない
        public DateTime At = DateTime.Now;
        public List<QueryFilter> Filters = new List<QueryFilter>();

        public const string FileName = "demomapdb_query.xml";

        public SearchOptions ToOptions()
        {
            var o = new SearchOptions
            {
                Count = Count,
                RadiusKm = RadiusKm,
                Keyword = Keyword,
                OpenAt = OpenAt == default(DateTime) ? (DateTime?)null : OpenAt,
            };
            foreach (var f in Filters) o.FieldFilters[f.Name] = f.Value;
            return o;
        }

        public static LastQuery From(GeocodeResult geo, SearchOptions opt)
        {
            var q = new LastQuery
            {
                Query = geo.Query,
                Matched = geo.Matched,
                Source = geo.Source,
                Lat = geo.Point.Lat,
                Lng = geo.Point.Lng,
                Count = opt.Count,
                RadiusKm = opt.RadiusKm,
                Keyword = opt.Keyword,
                OpenAt = opt.OpenAt ?? default(DateTime),
                At = DateTime.Now,
            };
            foreach (var kv in opt.FieldFilters)
                if (!string.IsNullOrEmpty(kv.Value)) q.Filters.Add(new QueryFilter(kv.Key, kv.Value));
            return q;
        }

        public static string PathFor(string dataFilePath)
        {
            return Path.Combine(Path.GetDirectoryName(Path.GetFullPath(dataFilePath)), FileName);
        }

        public void Save(string dataFilePath)
        {
            try
            {
                var ser = new XmlSerializer(typeof(LastQuery));
                using (var fs = new FileStream(PathFor(dataFilePath), FileMode.Create, FileAccess.Write))
                using (var w = new StreamWriter(fs, new UTF8Encoding(false)))
                    ser.Serialize(w, this);
            }
            catch { }   // 画面連携は補助機能なので、失敗してもコマンドは成功させる
        }

        public static LastQuery Load(string dataFilePath)
        {
            try
            {
                string path = PathFor(dataFilePath);
                if (!File.Exists(path)) return null;
                var ser = new XmlSerializer(typeof(LastQuery));
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    return (LastQuery)ser.Deserialize(fs);
            }
            catch { return null; }
        }
    }
}
