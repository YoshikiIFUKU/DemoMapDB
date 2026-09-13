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

        /// <summary>
        /// 結果をCSVにする（1行目は見出し）。列は 順位・店舗名・距離・方角・住所 と、
        /// 「一覧・標準出力に含める」フィールド。full のときは全フィールドと緯度経度も出す。
        /// </summary>
        public static string ToCsv(StoreData data, List<Nearby> list, bool full)
        {
            var fields = full ? data.SortedFields : data.ListFields;
            var head = new List<string> { "順位", data.NameLabel, "距離", "方角", data.AddressLabel };
            head.AddRange(fields.Select(f => f.Label));
            if (full) { head.Add("距離km"); head.Add("緯度"); head.Add("経度"); }

            var sb = new StringBuilder();
            sb.AppendLine(TextUtil.CsvLine(head));
            foreach (var n in list)
            {
                var cells = new List<string>
                {
                    n.Rank.ToString(), n.Store.Name, n.DistanceText, n.Direction, n.Store.Address,
                };
                cells.AddRange(fields.Select(f => n.Store.Get(f.ApiName)));
                if (full)
                {
                    cells.Add(n.DistanceKm.ToString("0.000", CultureInfo.InvariantCulture));
                    cells.Add(n.Store.Lat.ToString("0.#####", CultureInfo.InvariantCulture));
                    cells.Add(n.Store.Lng.ToString("0.#####", CultureInfo.InvariantCulture));
                }
                sb.AppendLine(TextUtil.CsvLine(cells));
            }
            return sb.ToString();
        }

        /// <summary>
        /// 結果をJSONにする（音声認識システム側で読みやすい形）。
        /// status は ok / no_store（該当店舗なし）/ no_address（住所を特定できない）。
        /// </summary>
        public static string ToJson(StoreData data, GeocodeResult geo, List<Nearby> list, string status)
        {
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
                var n = list[i];
                var s = n.Store;
                if (i > 0) sb.Append(',');
                sb.Append("{\"rank\":").Append(n.Rank)
                  .Append(",\"").Append(TextUtil.JsonEscape(data.NameApi)).Append("\":\"")
                  .Append(TextUtil.JsonEscape(s.Name)).Append("\"")
                  .Append(",\"").Append(TextUtil.JsonEscape(data.AddressApi)).Append("\":\"")
                  .Append(TextUtil.JsonEscape(s.Address)).Append("\"")
                  .Append(",\"distance_km\":").Append(n.DistanceKm.ToString("0.000", CultureInfo.InvariantCulture))
                  .Append(",\"distance_text\":\"").Append(n.DistanceText).Append("\"")
                  .Append(",\"direction\":\"").Append(n.Direction).Append("\"")
                  .Append(",\"lat\":").Append(Num(s.Lat)).Append(",\"lng\":").Append(Num(s.Lng));
                foreach (var f in data.SortedFields)
                    sb.Append(",\"").Append(TextUtil.JsonEscape(f.ApiName)).Append("\":\"")
                      .Append(TextUtil.JsonEscape(s.Get(f.ApiName))).Append("\"");
                sb.Append('}');
            }
            sb.Append("]}");
            return sb.ToString();
        }

        static string Num(double d) { return d.ToString("0.#####", CultureInfo.InvariantCulture); }

        /// <summary>オペレータが読み上げやすい形（1店舗1行）</summary>
        public static string ToText(StoreData data, List<Nearby> list)
        {
            var fields = data.ListFields;
            var sb = new StringBuilder();
            foreach (var n in list)
            {
                sb.Append(n.Rank).Append(". ").Append(n.Store.Name)
                  .Append("（").Append(n.DistanceText).Append(" ").Append(n.Direction).Append("）")
                  .Append(" ").Append(n.Store.Address);
                foreach (var f in fields)
                {
                    string v = n.Store.Get(f.ApiName);
                    if (v.Length > 0) sb.Append(" ").Append(v);
                }
                sb.AppendLine();
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
