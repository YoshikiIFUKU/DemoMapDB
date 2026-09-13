using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Serialization;

namespace StoreMapDemo
{
    public static class FieldTypes
    {
        public const string Text = "text", TextArea = "textarea", Url = "url", Number = "number",
                            Date = "date", Select = "select", Checkbox = "checkbox", Phone = "phone",
                            Hours = "hours";

        public static readonly string[] All = { Text, TextArea, Phone, Hours, Url, Number, Date, Select, Checkbox };

        public static string Label(string type)
        {
            switch (type)
            {
                case TextArea: return "長文テキスト";
                case Phone: return "電話番号";
                case Hours: return "営業時間";
                case Url: return "URL（リンク）";
                case Number: return "数値";
                case Date: return "日付";
                case Select: return "選択リスト";
                case Checkbox: return "チェックボックス";
                default: return "テキスト";
            }
        }

        /// <summary>保存する形にそろえる（日付は yyyy/MM/dd、チェックは 1 か空）</summary>
        public static string Normalize(string type, string value)
        {
            string v = (value ?? "").Replace("\r\n", "\n").Trim();
            if (v.Length == 0) return "";
            switch (type)
            {
                case Date:
                    DateTime d;
                    return DateTime.TryParse(v, new CultureInfo("ja-JP"), DateTimeStyles.AllowWhiteSpaces, out d)
                        ? d.ToString("yyyy/MM/dd") : v;
                case Checkbox:
                    string n = TextUtil.Norm(v);
                    return (n == "1" || n == "true" || n == "yes" || n == "はい" || n == "○" ||
                            n == "on" || n == "有" || n == "済") ? "1" : "";
                default:
                    return v;
            }
        }
    }

    /// <summary>
    /// 店舗の入力項目の定義。ここで増やした項目が、画面の入力欄・一覧の列・コマンドの変数名に
    /// そのまま増える（店舗名・住所・緯度経度・休止中は地図に必要なので固定）。
    /// </summary>
    public class FieldDef
    {
        public int Id;
        public string Label = "";      // 表示名（例: 電話番号）
        public string ApiName = "";    // 変数名（例: phone）
        public string Type = FieldTypes.Text;
        public string Options = "";    // 選択リストの候補（改行区切り）
        public bool Required;
        public bool InList;            // 一覧と標準出力に含める
        public bool IsKey;             // キー項目（同じ値の店舗があれば更新。1つだけ指定できる）
        public int Sort;

        public string[] OptionList
        {
            get
            {
                return (Options ?? "").Replace("\r\n", "\n").Split('\n')
                       .Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();
            }
        }

        public FieldDef Clone() { return (FieldDef)MemberwiseClone(); }
    }

    /// <summary>自由項目の値（変数名と値の組）</summary>
    public class FieldValue
    {
        [XmlAttribute("name")]
        public string Name = "";
        [XmlText]
        public string Value = "";

        public FieldValue() { }
        public FieldValue(string name, string value) { Name = name; Value = value ?? ""; }
    }

    /// <summary>店舗1件。固定項目のほかは、フィールド定義で決まる自由項目を持つ。</summary>
    public class Store
    {
        public int Id;
        public string Name = "";       // 店舗名（固定）
        public string Address = "";    // 住所（固定。ここから緯度経度を求める）
        public double Lat;             // 0 は未設定
        public double Lng;
        public DateTime CreatedAt = DateTime.Now;
        public DateTime UpdatedAt = DateTime.Now;
        public List<FieldValue> Values = new List<FieldValue>();

        [XmlIgnore]
        public bool HasLocation { get { return Math.Abs(Lat) > 0.0001 || Math.Abs(Lng) > 0.0001; } }

        public string Get(string apiName)
        {
            var v = Values.FirstOrDefault(x => x.Name == apiName);
            return v == null ? "" : (v.Value ?? "");
        }

        public void Set(string apiName, string value)
        {
            if (value == null) value = "";
            var v = Values.FirstOrDefault(x => x.Name == apiName);
            if (v == null) Values.Add(new FieldValue(apiName, value));
            else v.Value = value;
        }

        public Store Clone()
        {
            var s = (Store)MemberwiseClone();
            s.Values = Values.Select(v => new FieldValue(v.Name, v.Value)).ToList();
            return s;
        }
    }

    /// <summary>保存ファイル（demomapdb_data.xml）の中身</summary>
    [XmlRoot("DemoMapDbData")]
    public class StoreData
    {
        public int NextStoreId = 1;
        public int NextFieldId = 1;

        // 店舗名・住所は地図に必要なので項目としては固定だが、表示名と変数名は変えられる
        public string NameLabel = "店舗名";
        public string NameApi = "name";
        public string AddressLabel = "住所";
        public string AddressApi = "address";

        // 検索結果の標準出力（CSV / JSON / テキスト）に含めるか。画面の一覧には常に表示する
        public bool OutRank = true, OutName = true, OutDistance = true, OutDirection = true, OutAddress = true;
        public bool OutDistanceKm, OutLat, OutLng;

        /// <summary>
        /// データの版。1: 「一覧・標準出力」が1つも指定されていないとき先頭3項目を出す決まりをやめた版。
        /// 0（古いデータ）を読んだときは、それまでと同じ出力になるよう先頭3項目に印を付ける。
        /// </summary>
        public int OutputVersion;

        public List<FieldDef> Fields = new List<FieldDef>();
        public List<Store> Stores = new List<Store>();

        /// <summary>店舗名・住所を、項目と同じように扱うための見せかけの定義（一覧や編集画面で使う）</summary>
        [XmlIgnore]
        public FieldDef NameFieldDef
        {
            get { return new FieldDef { Label = NameLabel, ApiName = NameApi, Type = FieldTypes.Text, Required = true }; }
        }

        [XmlIgnore]
        public FieldDef AddressFieldDef
        {
            get { return new FieldDef { Label = AddressLabel, ApiName = AddressApi, Type = FieldTypes.Text }; }
        }

        public bool IsNameName(string nameOrLabel)
        {
            string n = TextUtil.Norm(nameOrLabel);
            return n == TextUtil.Norm(NameLabel) || n == TextUtil.Norm(NameApi);
        }

        public bool IsAddressName(string nameOrLabel)
        {
            string n = TextUtil.Norm(nameOrLabel);
            return n == TextUtil.Norm(AddressLabel) || n == TextUtil.Norm(AddressApi);
        }

        /// <summary>営業時間の型が付いている項目（複数あれば、どれかが営業中なら営業中とみなす）</summary>
        [XmlIgnore]
        public List<FieldDef> HoursFields
        {
            get { return SortedFields.Where(f => f.Type == FieldTypes.Hours).ToList(); }
        }

        /// <summary>その時刻に営業しているか。営業時間の項目が無い・空なら Unknown。</summary>
        public OpenState OpenStateOf(Store s, DateTime at)
        {
            var fields = HoursFields;
            if (fields.Count == 0) return OpenState.Unknown;
            var result = OpenState.Unknown;
            foreach (var f in fields)
            {
                var one = OpeningHours.Check(s.Get(f.ApiName), at);
                if (one == OpenState.Open) return OpenState.Open;
                if (one == OpenState.Closed) result = OpenState.Closed;
            }
            return result;
        }

        // ---- フィールド定義

        [XmlIgnore]
        public List<FieldDef> SortedFields { get { return Fields.OrderBy(f => f.Sort).ThenBy(f => f.Id).ToList(); } }

        /// <summary>一覧と標準出力に出す項目（「一覧・標準出力」に印を付けたもの）</summary>
        [XmlIgnore]
        public List<FieldDef> ListFields
        {
            get { return SortedFields.Where(f => f.InList).ToList(); }
        }

        /// <summary>検索結果だけにある項目（店舗には保存しない。地図の検索で計算する）</summary>
        public class ResultItem
        {
            public string Key, Label, JsonKey;
            public ResultItem(string key, string label, string jsonKey) { Key = key; Label = label; JsonKey = jsonKey; }
        }

        public static readonly ResultItem[] ResultItems =
        {
            new ResultItem("rank", "順位", "rank"),
            new ResultItem("distance", "距離", "distance_text"),
            new ResultItem("direction", "方角", "direction"),
            new ResultItem("distance_km", "距離km", "distance_km"),
            new ResultItem("lat", "緯度", "lat"),
            new ResultItem("lng", "経度", "lng"),
        };

        /// <summary>検索結果の項目の表示名・変数名と重なるか（自由項目の名前には使えない）</summary>
        public static bool IsResultName(string nameOrLabel)
        {
            string n = TextUtil.Norm(nameOrLabel);
            return ResultItems.Any(r => TextUtil.Norm(r.Label) == n || TextUtil.Norm(r.JsonKey) == n || TextUtil.Norm(r.Key) == n);
        }

        /// <summary>固定の項目（name / address / rank / distance …）を標準出力に含めるか</summary>
        public bool GetOutput(string key)
        {
            switch (key)
            {
                case "name": return OutName;
                case "address": return OutAddress;
                case "rank": return OutRank;
                case "distance": return OutDistance;
                case "direction": return OutDirection;
                case "distance_km": return OutDistanceKm;
                case "lat": return OutLat;
                case "lng": return OutLng;
                default: return false;
            }
        }

        public void SetOutput(string key, bool on)
        {
            switch (key)
            {
                case "name": OutName = on; break;
                case "address": OutAddress = on; break;
                case "rank": OutRank = on; break;
                case "distance": OutDistance = on; break;
                case "direction": OutDirection = on; break;
                case "distance_km": OutDistanceKm = on; break;
                case "lat": OutLat = on; break;
                case "lng": OutLng = on; break;
            }
        }

        [XmlIgnore]
        public FieldDef KeyField { get { return SortedFields.FirstOrDefault(f => f.IsKey); } }

        public FieldDef FieldByApi(string apiName)
        {
            string n = (apiName ?? "").Trim();
            return Fields.FirstOrDefault(f => string.Equals(f.ApiName, n, StringComparison.OrdinalIgnoreCase));
        }

        public FieldDef FieldByLabel(string label)
        {
            string n = TextUtil.Norm(label);
            return Fields.FirstOrDefault(f => TextUtil.Norm(f.Label) == n);
        }

        /// <summary>表示名・変数名のどちらでも引ける</summary>
        public FieldDef Field(string nameOrLabel)
        {
            return FieldByApi(nameOrLabel) ?? FieldByLabel(nameOrLabel);
        }

        public FieldDef AddField(FieldDef f)
        {
            f.Id = NextFieldId++;
            if (f.Sort == 0) f.Sort = Fields.Count == 0 ? 1 : Fields.Max(x => x.Sort) + 1;
            if (f.IsKey) foreach (var o in Fields) o.IsKey = false;
            Fields.Add(f);
            return f;
        }

        public void RemoveField(FieldDef f)
        {
            Fields.Remove(f);
            foreach (var s in Stores) s.Values.RemoveAll(v => v.Name == f.ApiName);
        }

        // 初期の項目の変数名（サンプル投入でも使う）
        public const string CodeApi = "code", PhoneApi = "phonenumber", HoursApi = "businesshours", MemoApi = "memo";

        /// <summary>初期の項目（店舗コード・電話番号・営業時間・備考）</summary>
        public void SeedFields()
        {
            if (Fields.Count > 0) return;
            OutputVersion = 1;
            AddField(new FieldDef { Label = "店舗コード", ApiName = CodeApi, Type = FieldTypes.Text, IsKey = true, InList = true, Sort = 1 });
            AddField(new FieldDef { Label = "電話番号", ApiName = PhoneApi, Type = FieldTypes.Phone, InList = true, Sort = 2 });
            AddField(new FieldDef { Label = "営業時間", ApiName = HoursApi, Type = FieldTypes.Hours, InList = true, Sort = 3 });
            AddField(new FieldDef { Label = "備考", ApiName = MemoApi, Type = FieldTypes.TextArea, Sort = 4 });
        }

        // ---- 店舗

        public Store ById(int id) { return Stores.FirstOrDefault(s => s.Id == id); }

        /// <summary>キー項目（既定は店舗コード）の値で引く</summary>
        public Store ByKey(string value)
        {
            var key = KeyField;
            string n = TextUtil.Norm(value);
            if (key == null || n.Length == 0) return null;
            return Stores.FirstOrDefault(s => TextUtil.Norm(s.Get(key.ApiName)) == n);
        }

        public Store Add(Store s)
        {
            s.Id = NextStoreId++;
            s.CreatedAt = s.UpdatedAt = DateTime.Now;
            Stores.Add(s);
            return s;
        }

        public void Remove(Store s) { Stores.Remove(s); }

        /// <summary>自由項目の値を、ひとまとめの文字列にする（絞り込み用）</summary>
        public string SearchText(Store s)
        {
            return s.Name + " " + s.Address + " " + string.Join(" ", s.Values.Select(v => v.Value).ToArray());
        }

        // デモ用のサンプル店舗。店舗コード|店舗名|住所|電話番号|営業時間|緯度|経度
        static readonly string[] SampleRows =
        {
            "T001|東京タワー前店|東京都港区芝公園4-2-8|03-3433-5111|9:00-21:00|35.65858|139.74543",
            "T002|新橋駅前店|東京都港区新橋2-16-1|03-3571-0101|10:00-20:00|35.66604|139.75833",
            "T003|品川港南店|東京都港区港南2-16-3|03-5479-1000|9:30-19:30|35.62834|139.74048",
            "T004|六本木ヒルズ店|東京都港区六本木6-10-1|03-6406-6000|11:00-21:00|35.66028|139.72944",
            "T005|東京駅八重洲店|東京都中央区八重洲2-1-1|03-3201-1111|8:00-22:00|35.68050|139.76960",
            "T006|銀座四丁目店|東京都中央区銀座4-5-11|03-3562-1111|11:00-20:00|35.67150|139.76500",
            "T007|渋谷センター街店|東京都渋谷区宇田川町25-4|03-3464-1111|10:00-22:00|35.66100|139.69880",
            "T008|新宿西口店|東京都新宿区西新宿1-1-3|03-3342-1111|10:00-21:00|35.68930|139.69940",
            "T009|池袋東口店|東京都豊島区南池袋1-28-1|03-3981-1111|10:00-21:00|35.72890|139.71460",
            "T010|上野広小路店|東京都台東区上野4-8-1|03-3831-1111|10:00-20:00|35.70670|139.77410",
            "T011|お台場サービスセンター|東京都江東区青海1-1-10|03-3599-1111|9:00-18:00|35.61960|139.77800",
            "T012|川崎駅前店|神奈川県川崎市川崎区駅前本町26-1|044-200-1111|10:00-20:00|35.53080|139.69690",
            "T013|吉祥寺サンロード店|東京都武蔵野市吉祥寺本町1-15-1|0422-21-1111|10:00-20:00|35.70360|139.57970",
            "T014|町田駅前店|東京都町田市原町田6-12-20|042-722-1111|10:00-20:00|35.54180|139.44680",
            "Y001|横浜みなとみらい店|神奈川県横浜市西区みなとみらい2-3-5|045-222-1111|10:00-21:00|35.45740|139.63170",
            "Y002|横浜駅西口店|神奈川県横浜市西区南幸1-1-1|045-311-1111|10:00-21:00|35.46580|139.62060",
            "O001|大阪梅田店|大阪府大阪市北区梅田3-1-3|06-6341-1111|10:00-21:00|34.70250|135.49510",
            "O002|なんば千日前店|大阪府大阪市中央区難波3-8-9|06-6211-1111|10:00-22:00|34.66560|135.50170",
            "N001|名古屋栄店|愛知県名古屋市中区栄3-5-12|052-251-1111|10:00-20:00|35.16790|136.90790",
            "F001|博多駅前店|福岡県福岡市博多区博多駅中央街1-1|092-431-1111|9:00-21:00|33.58990|130.42030",
            "S001|札幌大通店|北海道札幌市中央区大通西3-6|011-231-1111|10:00-20:00|43.06080|141.35450",
        };

        public void Seed()
        {
            SeedFields();
            foreach (var row in SampleRows)
            {
                var p = row.Split('|');
                var s = new Store
                {
                    Name = p[1],
                    Address = p[2],
                    Lat = double.Parse(p[5], CultureInfo.InvariantCulture),
                    Lng = double.Parse(p[6], CultureInfo.InvariantCulture),
                };
                SetIfDefined(s, CodeApi, p[0]);
                SetIfDefined(s, PhoneApi, p[3]);
                SetIfDefined(s, HoursApi, p[4]);
                Add(s);
            }
        }

        void SetIfDefined(Store s, string apiName, string value)
        {
            if (FieldByApi(apiName) != null) s.Set(apiName, value);
        }
    }

    /// <summary>データファイルの読み書き。一時ファイルに書いてから置き換えるので、書き込み途中で壊れない。</summary>
    public class StoreStore
    {
        public const string FileName = "demomapdb_data.xml";
        public string FilePath { get; private set; }   // 保存できないと %LOCALAPPDATA% に切り替わる
        public StoreData Data { get; private set; }

        DateTime stamp;
        DateTime Stamp()
        {
            try { return File.Exists(FilePath) ? File.GetLastWriteTimeUtc(FilePath) : DateTime.MinValue; }
            catch { return DateTime.MinValue; }
        }

        /// <summary>別のプロセス（コマンドラインでの登録など）がデータファイルを更新したか</summary>
        public bool ChangedOnDisk { get { return Stamp() != stamp; } }

        public StoreStore(string path)
        {
            FilePath = Path.GetFullPath(path);
            Load();
        }

        /// <summary>
        /// データファイルの場所: 環境変数 DEMOMAPDB_DATA → exeと同じフォルダ →
        /// (exeフォルダに書き込めない場合) %LOCALAPPDATA%\DemoMapDB
        /// </summary>
        public static string ResolveDefaultPath()
        {
            string env = Environment.GetEnvironmentVariable("DEMOMAPDB_DATA");
            if (!string.IsNullOrEmpty(env)) return Path.GetFullPath(env);

            string local = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, FileName);
            if (File.Exists(local)) return local;
            if (File.Exists(AppDataPath)) return AppDataPath;
            return local;
        }

        /// <summary>exe と同じフォルダに書けないときの保存先</summary>
        public static string AppDataPath
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DemoMapDB", FileName);
            }
        }

        public void Load()
        {
            stamp = Stamp();
            if (!File.Exists(FilePath))
            {
                Data = new StoreData();
                Data.SeedFields();
                return;
            }
            var ser = new XmlSerializer(typeof(StoreData));
            using (var fs = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                Data = (StoreData)ser.Deserialize(fs);
            }
            if (Data.Fields.Count == 0) Data.SeedFields();
            if (Data.OutputVersion < 1)
            {
                // 以前は「一覧・標準出力」が未指定なら先頭3項目を出していた。出力が変わらないように印を付けておく
                if (!Data.Fields.Any(f => f.InList))
                    foreach (var f in Data.SortedFields.Take(3)) f.InList = true;
                Data.OutputVersion = 1;
                try { Save(); } catch { }
            }
            if (Data.Stores.Count > 0 && Data.NextStoreId <= Data.Stores.Max(s => s.Id))
                Data.NextStoreId = Data.Stores.Max(s => s.Id) + 1;
            if (Data.Fields.Count > 0 && Data.NextFieldId <= Data.Fields.Max(f => f.Id))
                Data.NextFieldId = Data.Fields.Max(f => f.Id) + 1;
        }

        /// <summary>
        /// 保存する。直前の版は .bak に残す。
        /// exe と同じフォルダに書けなかったときは %LOCALAPPDATA% に切り替えて保存し直す。
        /// </summary>
        public void Save()
        {
            try { SaveTo(FilePath); }
            catch (Exception ex)
            {
                if (!(ex is UnauthorizedAccessException || ex is IOException) ||
                    string.Equals(FilePath, AppDataPath, StringComparison.OrdinalIgnoreCase)) throw;
                FilePath = AppDataPath;
                SaveTo(FilePath);
            }
            stamp = Stamp();
        }

        void SaveTo(string target)
        {
            string dir = Path.GetDirectoryName(target);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            string tmp = target + ".tmp";
            var ser = new XmlSerializer(typeof(StoreData));
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write))
            using (var w = new StreamWriter(fs, new UTF8Encoding(false)))
            {
                ser.Serialize(w, Data);
            }
            if (File.Exists(target))
            {
                File.Copy(target, target + ".bak", true);
                File.Copy(tmp, target, true);
                File.Delete(tmp);
            }
            else File.Move(tmp, target);
        }

        public string CreateBackup()
        {
            string dir = Path.Combine(Path.GetDirectoryName(FilePath), "backup");
            Directory.CreateDirectory(dir);
            string dest = Path.Combine(dir, "demomapdb_data_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".xml");
            Save();
            File.Copy(FilePath, dest, true);
            return dest;
        }
    }

    /// <summary>文字列ユーティリティ</summary>
    public static class TextUtil
    {
        /// <summary>比較用にそろえる（全角→半角、大文字小文字、空白とハイフンの違いを無視）</summary>
        public static string Norm(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length);
            foreach (char ch in s.Normalize(NormalizationForm.FormKC))
            {
                if (char.IsWhiteSpace(ch)) continue;
                if (ch == '-' || ch == '‐' || ch == '−' || ch == 'ー' || ch == '–' || ch == '―')
                {
                    sb.Append('-');
                    continue;
                }
                sb.Append(char.ToLowerInvariant(ch));
            }
            return sb.ToString();
        }

        /// <summary>CSVの1項目を RFC 4180 で囲む</summary>
        public static string Csv(string s)
        {
            s = (s ?? "").Replace("\r\n", "\n");
            if (s.IndexOfAny(new[] { ',', '"', '\n' }) < 0) return s;
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }

        public static string CsvLine(params string[] cells)
        {
            return string.Join(",", cells.Select(Csv).ToArray());
        }

        public static string CsvLine(IEnumerable<string> cells)
        {
            return string.Join(",", cells.Select(Csv).ToArray());
        }

        /// <summary>CSV/TSV の1行を項目に分ける</summary>
        public static List<string> SplitLine(string line, char sep)
        {
            var cells = new List<string>();
            var sb = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (quoted)
                {
                    if (c == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                        else quoted = false;
                    }
                    else sb.Append(c);
                }
                else if (c == '"') quoted = true;
                else if (c == sep) { cells.Add(sb.ToString()); sb.Length = 0; }
                else sb.Append(c);
            }
            cells.Add(sb.ToString());
            return cells;
        }

        public static string JsonEscape(string s)
        {
            var sb = new StringBuilder();
            foreach (char c in s ?? "")
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }

        /// <summary>表示名から変数名の候補を作る（電話番号 → field6 のように、英字が無ければ連番）</summary>
        public static string ToApiName(string label, int seq)
        {
            var sb = new StringBuilder();
            foreach (char c in (label ?? "").Trim())
            {
                if (c >= 'a' && c <= 'z') sb.Append(c);
                else if (c >= 'A' && c <= 'Z') sb.Append(char.ToLowerInvariant(c));
                else if (c >= '0' && c <= '9' && sb.Length > 0) sb.Append(c);
                else if (c == '_' && sb.Length > 0) sb.Append(c);
            }
            return sb.Length > 0 ? sb.ToString() : "field" + seq;
        }
    }
}
