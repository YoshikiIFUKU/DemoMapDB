using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;

namespace StoreMapDemo
{
    /// <summary>緯度経度の1点</summary>
    public struct GeoPoint
    {
        public double Lat, Lng;
        public GeoPoint(double lat, double lng) { Lat = lat; Lng = lng; }
        public bool IsEmpty { get { return Math.Abs(Lat) < 0.0001 && Math.Abs(Lng) < 0.0001; } }
        public override string ToString()
        {
            return Lat.ToString("0.#####", CultureInfo.InvariantCulture) + "," +
                   Lng.ToString("0.#####", CultureInfo.InvariantCulture);
        }
    }

    /// <summary>距離・方位の計算</summary>
    public static class GeoMath
    {
        const double EarthRadiusKm = 6371.0;

        /// <summary>2点間の直線距離（km）。球面の大円距離。</summary>
        public static double DistanceKm(GeoPoint a, GeoPoint b)
        {
            double dLat = Rad(b.Lat - a.Lat), dLng = Rad(b.Lng - a.Lng);
            double s = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                       Math.Cos(Rad(a.Lat)) * Math.Cos(Rad(b.Lat)) * Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
            return EarthRadiusKm * 2 * Math.Atan2(Math.Sqrt(s), Math.Sqrt(1 - s));
        }

        static readonly string[] Compass = { "北", "北東", "東", "南東", "南", "南西", "西", "北西" };

        /// <summary>a から見た b の方角（北・北東…）</summary>
        public static string Direction(GeoPoint a, GeoPoint b)
        {
            double y = Math.Sin(Rad(b.Lng - a.Lng)) * Math.Cos(Rad(b.Lat));
            double x = Math.Cos(Rad(a.Lat)) * Math.Sin(Rad(b.Lat)) -
                       Math.Sin(Rad(a.Lat)) * Math.Cos(Rad(b.Lat)) * Math.Cos(Rad(b.Lng - a.Lng));
            double deg = (Math.Atan2(y, x) * 180 / Math.PI + 360) % 360;
            return Compass[(int)Math.Round(deg / 45) % 8];
        }

        /// <summary>「1.2km」「430m」の形にする</summary>
        public static string FormatKm(double km)
        {
            if (km < 1) return Math.Round(km * 1000 / 10) * 10 + "m";
            if (km < 10) return km.ToString("0.0") + "km";
            return km.ToString("0") + "km";
        }

        static double Rad(double deg) { return deg * Math.PI / 180; }
    }

    /// <summary>住所を緯度経度に変換した結果</summary>
    public class GeocodeResult
    {
        public GeoPoint Point;
        public string Query = "";
        public string Matched = "";   // 実際に一致した住所（内蔵表なら「東京都港区」など）
        public string Source = "";    // online / builtin / store / latlon
        public bool Ok { get { return !Point.IsEmpty; } }

        public string SourceLabel
        {
            get
            {
                switch (Source)
                {
                    case "online": return "国土地理院の住所検索";
                    case "builtin": return "内蔵の住所表（市区町村の代表点）";
                    case "store": return "登録店舗の住所";
                    case "latlon": return "緯度経度の直接指定";
                    default: return Source;
                }
            }
        }
    }

    /// <summary>
    /// 住所 → 緯度経度。次の順に試し、最初に決まったものを使う。
    /// 1) 緯度経度の直接指定  2) 国土地理院の住所検索API（オンライン）  3) 内蔵の住所表と登録店舗の住所（最長一致）
    /// </summary>
    public static class Geocoder
    {
        public static int TimeoutMs = 4000;

        public static GeocodeResult Resolve(string address, StoreData data, bool allowOnline)
        {
            var r = new GeocodeResult { Query = (address ?? "").Trim() };
            if (r.Query.Length == 0) return r;

            // 1) 「35.658,139.745」のような直接指定
            GeoPoint p;
            if (TryParseLatLng(r.Query, out p))
            {
                r.Point = p; r.Matched = r.Query; r.Source = "latlon";
                return r;
            }

            // 2) オンライン
            if (allowOnline)
            {
                string matched;
                if (TryOnline(r.Query, out p, out matched))
                {
                    r.Point = p; r.Matched = matched; r.Source = "online";
                    return r;
                }
            }

            // 3) オフライン（内蔵の住所表 + 登録店舗の住所のうち、いちばん長く一致したもの）
            return Offline(r, data);
        }

        public static bool TryParseLatLng(string s, out GeoPoint p)
        {
            p = new GeoPoint();
            var parts = (s ?? "").Replace("、", ",").Split(',', '/', ' ');
            var nums = parts.Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();
            if (nums.Length != 2) return false;
            double lat, lng;
            if (!double.TryParse(nums[0], NumberStyles.Float, CultureInfo.InvariantCulture, out lat)) return false;
            if (!double.TryParse(nums[1], NumberStyles.Float, CultureInfo.InvariantCulture, out lng)) return false;
            if (lat < -90 || lat > 90 || lng < -180 || lng > 180) return false;
            p = new GeoPoint(lat, lng);
            return true;
        }

        // ---- オンライン（国土地理院 住所検索API。キー不要・無料）

        public const string ApiUrl = "https://msearch.gsi.go.jp/address-search/AddressSearch?q=";

        public static bool TryOnline(string address, out GeoPoint p, out string matched)
        {
            p = new GeoPoint();
            matched = "";
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                var req = (HttpWebRequest)WebRequest.Create(ApiUrl + Uri.EscapeDataString(address));
                req.Method = "GET";
                req.Timeout = TimeoutMs;
                req.ReadWriteTimeout = TimeoutMs;
                req.UserAgent = "DemoMapDB/1.0";
                string json;
                using (var res = (HttpWebResponse)req.GetResponse())
                using (var sr = new StreamReader(res.GetResponseStream(), Encoding.UTF8))
                    json = sr.ReadToEnd();
                return ParseGsiJson(json, out p, out matched);
            }
            catch { return false; }
        }

        /// <summary>[{"geometry":{"coordinates":[経度,緯度]},"properties":{"title":"..."}}] の先頭を取り出す</summary>
        internal static bool ParseGsiJson(string json, out GeoPoint p, out string matched)
        {
            p = new GeoPoint();
            matched = "";
            if (string.IsNullOrEmpty(json)) return false;
            int i = json.IndexOf("\"coordinates\"", StringComparison.Ordinal);
            if (i < 0) return false;
            int lb = json.IndexOf('[', i), rb = lb < 0 ? -1 : json.IndexOf(']', lb);
            if (lb < 0 || rb < 0) return false;
            var nums = json.Substring(lb + 1, rb - lb - 1).Split(',');
            double lng, lat;
            if (nums.Length < 2) return false;
            if (!double.TryParse(nums[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out lng)) return false;
            if (!double.TryParse(nums[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out lat)) return false;
            if (Math.Abs(lat) < 0.0001 && Math.Abs(lng) < 0.0001) return false;
            p = new GeoPoint(lat, lng);

            int t = json.IndexOf("\"title\"", StringComparison.Ordinal);
            if (t >= 0)
            {
                int q1 = json.IndexOf('"', json.IndexOf(':', t) + 1);
                int q2 = q1 < 0 ? -1 : json.IndexOf('"', q1 + 1);
                if (q1 >= 0 && q2 > q1) matched = json.Substring(q1 + 1, q2 - q1 - 1);
            }
            return true;
        }

        // ---- オフライン（内蔵の住所表 + 登録店舗の住所）

        static GeocodeResult Offline(GeocodeResult r, StoreData data)
        {
            string q = TextUtil.Norm(r.Query);
            int bestLen = 0;

            foreach (var e in AddressTable.Entries)
            {
                string key = TextUtil.Norm(e.Name);
                if (key.Length > bestLen && q.StartsWith(key, StringComparison.Ordinal))
                {
                    bestLen = key.Length;
                    r.Point = new GeoPoint(e.Lat, e.Lng);
                    r.Matched = e.Name;
                    r.Source = "builtin";
                }
            }

            // 登録済み店舗の住所と、より長く一致するならそちらを使う（町名・番地まで近づく）
            if (data != null)
            {
                foreach (var s in data.Stores)
                {
                    if (!s.HasLocation || string.IsNullOrEmpty(s.Address)) continue;
                    string key = TextUtil.Norm(s.Address);
                    int len = CommonPrefixLen(q, key);
                    // 市区町村より細かいところまで一致した場合だけ採用する
                    if (len > bestLen && len >= 6)
                    {
                        bestLen = len;
                        r.Point = new GeoPoint(s.Lat, s.Lng);
                        r.Matched = s.Address + "（" + s.Name + "）";
                        r.Source = "store";
                    }
                }
            }
            if (bestLen > 0) return r;

            // 「大阪市北区梅田」のように都道府県が省かれた住所は、都道府県を外した形でもう一度探す
            foreach (var e in AddressTable.Entries)
            {
                string key = TextUtil.Norm(StripPrefecture(e.Name));
                if (key.Length > bestLen && q.StartsWith(key, StringComparison.Ordinal))
                {
                    bestLen = key.Length;
                    r.Point = new GeoPoint(e.Lat, e.Lng);
                    r.Matched = e.Name;
                    r.Source = "builtin";
                }
            }
            if (data != null)
            {
                foreach (var s in data.Stores)
                {
                    if (!s.HasLocation || string.IsNullOrEmpty(s.Address)) continue;
                    int len = CommonPrefixLen(q, TextUtil.Norm(StripPrefecture(s.Address)));
                    if (len > bestLen && len >= 6)
                    {
                        bestLen = len;
                        r.Point = new GeoPoint(s.Lat, s.Lng);
                        r.Matched = s.Address + "（" + s.Name + "）";
                        r.Source = "store";
                    }
                }
            }
            return r;
        }

        /// <summary>「東京都港区」→「港区」。都道府県だけの名前なら空を返す（検索対象から外れる）。</summary>
        static string StripPrefecture(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            if (name.StartsWith("北海道", StringComparison.Ordinal)) return name.Substring(3);
            int i = name.IndexOfAny(new[] { '都', '府', '県' });
            return i >= 1 && i <= 3 ? name.Substring(i + 1) : name;
        }

        static int CommonPrefixLen(string a, string b)
        {
            int n = Math.Min(a.Length, b.Length), i = 0;
            while (i < n && a[i] == b[i]) i++;
            return i;
        }
    }

    /// <summary>
    /// オフライン用の住所表。都道府県と主な市区町村の代表点（おおよその中心）を持つ。
    /// デモ用なので番地までの精度は出ない（誤差は数百m〜数km）。正確さが要るときはオンライン検索を使う。
    /// </summary>
    public static class AddressTable
    {
        public class Entry
        {
            public string Name;
            public double Lat, Lng;
            public Entry(string name, double lat, double lng) { Name = name; Lat = lat; Lng = lng; }
        }

        static readonly string[] Rows =
        {
            // 都道府県（県庁所在地の位置）
            "北海道|43.064|141.347", "青森県|40.824|140.740", "岩手県|39.704|141.153", "宮城県|38.269|140.872",
            "秋田県|39.719|140.102", "山形県|38.240|140.364", "福島県|37.750|140.468", "茨城県|36.342|140.447",
            "栃木県|36.566|139.884", "群馬県|36.391|139.060", "埼玉県|35.857|139.649", "千葉県|35.605|140.123",
            "東京都|35.689|139.692", "神奈川県|35.448|139.643", "新潟県|37.902|139.023", "富山県|36.695|137.211",
            "石川県|36.595|136.626", "福井県|36.065|136.222", "山梨県|35.664|138.568", "長野県|36.651|138.181",
            "岐阜県|35.391|136.722", "静岡県|34.977|138.383", "愛知県|35.180|136.907", "三重県|34.730|136.509",
            "滋賀県|35.005|135.869", "京都府|35.021|135.756", "大阪府|34.686|135.520", "兵庫県|34.691|135.183",
            "奈良県|34.685|135.833", "和歌山県|34.226|135.168", "鳥取県|35.504|134.238", "島根県|35.472|133.051",
            "岡山県|34.662|133.935", "広島県|34.396|132.460", "山口県|34.186|131.471", "徳島県|34.066|134.559",
            "香川県|34.340|134.043", "愛媛県|33.842|132.766", "高知県|33.560|133.531", "福岡県|33.607|130.418",
            "佐賀県|33.249|130.300", "長崎県|32.745|129.874", "熊本県|32.790|130.742", "大分県|33.238|131.613",
            "宮崎県|31.911|131.424", "鹿児島県|31.560|130.558", "沖縄県|26.212|127.681",

            // 東京23区
            "東京都千代田区|35.694|139.754", "東京都中央区|35.671|139.772", "東京都港区|35.658|139.752",
            "東京都新宿区|35.694|139.703", "東京都文京区|35.708|139.752", "東京都台東区|35.713|139.780",
            "東京都墨田区|35.711|139.801", "東京都江東区|35.673|139.817", "東京都品川区|35.609|139.730",
            "東京都目黒区|35.641|139.698", "東京都大田区|35.561|139.716", "東京都世田谷区|35.646|139.653",
            "東京都渋谷区|35.664|139.698", "東京都中野区|35.707|139.664", "東京都杉並区|35.700|139.636",
            "東京都豊島区|35.726|139.717", "東京都北区|35.753|139.734", "東京都荒川区|35.736|139.783",
            "東京都板橋区|35.751|139.709", "東京都練馬区|35.735|139.652", "東京都足立区|35.775|139.804",
            "東京都葛飾区|35.743|139.847", "東京都江戸川区|35.707|139.868",

            // 東京都下（多摩地区）
            "東京都八王子市|35.666|139.316", "東京都立川市|35.714|139.407", "東京都武蔵野市|35.718|139.566",
            "東京都三鷹市|35.683|139.560", "東京都府中市|35.669|139.478", "東京都調布市|35.651|139.541",
            "東京都町田市|35.546|139.439", "東京都小平市|35.728|139.477", "東京都日野市|35.671|139.395",
            "東京都西東京市|35.725|139.538", "東京都多摩市|35.637|139.446", "東京都国分寺市|35.710|139.462",

            // 神奈川
            "神奈川県横浜市西区|35.459|139.620", "神奈川県横浜市中区|35.445|139.641", "神奈川県横浜市港北区|35.519|139.632",
            "神奈川県横浜市鶴見区|35.507|139.678", "神奈川県横浜市神奈川区|35.477|139.629", "神奈川県横浜市南区|35.432|139.610",
            "神奈川県横浜市戸塚区|35.398|139.534", "神奈川県横浜市青葉区|35.554|139.537", "神奈川県横浜市都筑区|35.545|139.573",
            "神奈川県横浜市|35.448|139.643",
            "神奈川県川崎市川崎区|35.531|139.703", "神奈川県川崎市中原区|35.576|139.658", "神奈川県川崎市高津区|35.599|139.611",
            "神奈川県川崎市宮前区|35.582|139.583", "神奈川県川崎市多摩区|35.620|139.556", "神奈川県川崎市|35.531|139.703",
            "神奈川県相模原市|35.571|139.373", "神奈川県藤沢市|35.339|139.491", "神奈川県横須賀市|35.281|139.672",
            "神奈川県平塚市|35.335|139.349", "神奈川県厚木市|35.443|139.362", "神奈川県小田原市|35.264|139.152",
            "神奈川県鎌倉市|35.319|139.550", "神奈川県茅ヶ崎市|35.333|139.404",

            // 埼玉・千葉
            "埼玉県さいたま市大宮区|35.906|139.624", "埼玉県さいたま市浦和区|35.859|139.657",
            "埼玉県さいたま市中央区|35.878|139.630", "埼玉県さいたま市|35.861|139.646",
            "埼玉県川口市|35.808|139.724", "埼玉県川越市|35.925|139.485", "埼玉県所沢市|35.799|139.469",
            "埼玉県越谷市|35.891|139.791", "埼玉県草加市|35.825|139.806", "埼玉県春日部市|35.975|139.752",
            "千葉県千葉市中央区|35.607|140.123", "千葉県千葉市美浜区|35.646|140.052", "千葉県千葉市|35.607|140.123",
            "千葉県船橋市|35.694|139.983", "千葉県市川市|35.722|139.931", "千葉県松戸市|35.788|139.903",
            "千葉県柏市|35.868|139.976", "千葉県浦安市|35.653|139.902", "千葉県成田市|35.777|140.318",

            // 大阪・京都・兵庫
            "大阪府大阪市北区|34.706|135.498", "大阪府大阪市中央区|34.680|135.507", "大阪府大阪市西区|34.679|135.492",
            "大阪府大阪市浪速区|34.663|135.499", "大阪府大阪市天王寺区|34.658|135.519", "大阪府大阪市淀川区|34.727|135.489",
            "大阪府大阪市阿倍野区|34.639|135.513", "大阪府大阪市|34.694|135.502",
            "大阪府堺市|34.573|135.483", "大阪府東大阪市|34.680|135.601", "大阪府豊中市|34.782|135.470",
            "大阪府吹田市|34.759|135.516", "大阪府枚方市|34.814|135.650", "大阪府高槻市|34.846|135.617",
            "京都府京都市中京区|35.011|135.760", "京都府京都市下京区|34.990|135.758", "京都府京都市左京区|35.045|135.784",
            "京都府京都市|35.011|135.768",
            "兵庫県神戸市中央区|34.691|135.196", "兵庫県神戸市|34.690|135.196", "兵庫県姫路市|34.815|134.686",
            "兵庫県西宮市|34.737|135.342", "兵庫県尼崎市|34.733|135.406", "兵庫県芦屋市|34.728|135.303",

            // 中部・その他の政令市と主要都市
            "愛知県名古屋市中区|35.167|136.907", "愛知県名古屋市中村区|35.172|136.882",
            "愛知県名古屋市東区|35.183|136.923", "愛知県名古屋市|35.181|136.907",
            "愛知県豊田市|35.083|137.156", "愛知県岡崎市|34.954|137.174", "愛知県豊橋市|34.769|137.391",
            "静岡県静岡市|34.976|138.383", "静岡県浜松市|34.711|137.726", "静岡県沼津市|35.096|138.863",
            "新潟県新潟市|37.916|139.036", "長野県長野市|36.649|138.194", "長野県松本市|36.238|137.972",
            "石川県金沢市|36.578|136.648", "富山県富山市|36.696|137.214", "福井県福井市|36.064|136.220",
            "岐阜県岐阜市|35.409|136.761", "三重県四日市市|34.965|136.624", "山梨県甲府市|35.664|138.568",

            // 北海道・東北・中国・四国・九州の主要都市
            "北海道札幌市中央区|43.056|141.341", "北海道札幌市北区|43.090|141.341", "北海道札幌市|43.062|141.354",
            "北海道旭川市|43.771|142.365", "北海道函館市|41.769|140.729",
            "宮城県仙台市青葉区|38.269|140.869", "宮城県仙台市|38.268|140.872", "福島県郡山市|37.400|140.360",
            "福島県いわき市|37.051|140.888", "岩手県盛岡市|39.702|141.155", "青森県青森市|40.822|140.747",
            "広島県広島市中区|34.391|132.459", "広島県広島市|34.385|132.455", "広島県福山市|34.486|133.362",
            "岡山県岡山市|34.665|133.919", "山口県下関市|33.958|130.941", "島根県松江市|35.468|133.049",
            "香川県高松市|34.343|134.047", "愛媛県松山市|33.839|132.766", "徳島県徳島市|34.070|134.555",
            "高知県高知市|33.559|133.531",
            "福岡県福岡市博多区|33.590|130.420", "福岡県福岡市中央区|33.589|130.396", "福岡県福岡市|33.590|130.402",
            "福岡県北九州市|33.884|130.876", "福岡県久留米市|33.319|130.508",
            "熊本県熊本市|32.803|130.708", "鹿児島県鹿児島市|31.596|130.557", "大分県大分市|33.238|131.613",
            "長崎県長崎市|32.750|129.878", "宮崎県宮崎市|31.911|131.424", "沖縄県那覇市|26.213|127.679",
        };

        static List<Entry> entries;

        public static List<Entry> Entries
        {
            get
            {
                if (entries == null)
                {
                    entries = new List<Entry>();
                    foreach (var row in Rows)
                    {
                        var p = row.Split('|');
                        entries.Add(new Entry(p[0],
                            double.Parse(p[1], CultureInfo.InvariantCulture),
                            double.Parse(p[2], CultureInfo.InvariantCulture)));
                    }
                }
                return entries;
            }
        }

        public static int Count { get { return Entries.Count; } }
    }
}
