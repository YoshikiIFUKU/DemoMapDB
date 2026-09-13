using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace StoreMapDemo
{
    /// <summary>
    /// コマンドライン（引数あり起動）。
    /// ・検索（--near）: 住所を緯度経度に変換し、近い店舗を N 件、CSV / JSON / テキストで標準出力する
    /// ・登録（--add）・一覧（--list）・取り込み（--import）・書き出し（--export）・項目一覧（--fields）
    /// ・検索すると demomapdb_query.xml を書き、開いている画面の地図へ約1.2秒で反映される
    /// </summary>
    public static class Cli
    {
        public const int ExitFound = 0, ExitNoStore = 1, ExitNoAddress = 2, ExitError = 9;

        /// <summary>
        /// 「該当店舗なし」「住所を特定できない」は通常の運用でも起こるため、既定では終了コード0で返す
        /// （連携側でプロセスの異常終了と扱われないようにするため）。--strict を付けたときだけ 1 / 2 を返す。
        /// 呼び出しの誤りや例外（9）は常にそのまま返す。
        /// </summary>
        static int Soft(Options o, int code) { return o.Strict ? code : ExitFound; }
        public const string DefaultEnvPrefix = "AMIVOICE_ST_";
        public const string NotFoundMessage = "該当する店舗がありません";

        /// <summary>住所から場所を特定できない（住所が空のときも含む）ときに、標準出力へ出す文言</summary>
        public const string NoAddressMessage = "住所から場所を特定できませんでした";

        class CliError : Exception { public CliError(string m) : base(m) { } }

        class Options
        {
            public string Mode = "near";
            public string Address = "";
            public SearchOptions Search = new SearchOptions();
            public string Format = "csv";     // csv / json / text
            public bool Full;
            public bool Quiet;
            public bool NoGui;
            public bool Offline;
            public bool OnlineForced;
            public Encoding Enc;
            public string File = "";
            public string StoreName = "", StoreAddress = "";
            public double Lat, Lng;
            public bool HasLatLng;
            public readonly List<KeyValuePair<string, string>> Sets = new List<KeyValuePair<string, string>>();
            public readonly List<KeyValuePair<string, string>> Wheres = new List<KeyValuePair<string, string>>();
            public bool NoGeocode;
            public bool Strict;
            public string DataPath;
            public string EnvPrefix;
        }

        public static int Run(string[] args)
        {
            var sw = Stopwatch.StartNew();
            string detail = "";
            int code;
            try
            {
                code = RunCore(args, out detail);
            }
            catch (CliError ex)
            {
                WriteErr(ex.Message + Environment.NewLine, null);
                detail = ex.Message;
                code = ExitError;
            }
            catch (Exception ex)
            {
                WriteErr("エラー: " + ex.Message + Environment.NewLine, null);
                detail = ex.Message;
                code = ExitError;
            }
            RunLog.Write(args, code, sw.ElapsedMilliseconds, detail);
            return code;
        }

        static int RunCore(string[] args, out string detail)
        {
            detail = "";
            var o = Parse(args);
            if (o.Mode == "help") { Write(HelpText(), o.Enc); return ExitFound; }

            string path = o.DataPath ?? StoreStore.ResolveDefaultPath();
            var store = new StoreStore(path);
            bool online = !o.Offline && (o.OnlineForced || UseOnlineByDefault(path));

            // 環境変数からの取り込みは、フィールド定義を読んだあとに行う
            if (o.EnvPrefix != null) ApplyEnv(o, store.Data, o.EnvPrefix);
            ResolveWheres(o, store.Data);

            switch (o.Mode)
            {
                case "sample": return RunSample(store, o, out detail);
                case "fields": return RunFields(store, o, out detail);
                case "list": return RunList(store, o, out detail);
                case "add": return RunAdd(store, o, online, out detail);
                case "import": return RunImport(store, o, online, out detail);
                case "export": return RunExport(store, o, out detail);
                case "geocode": return RunGeocode(store, o, online, out detail);
                case "fill": return RunFill(store, o, online, out detail);
                default: return RunNear(store, o, online, out detail);
            }
        }

        static bool UseOnlineByDefault(string dataPath)
        {
            Settings.Load(dataPath);
            return Settings.GetBool("online_geocoding", true);
        }

        // ------------------------------------------------------------------ 引数の解釈

        static Options Parse(string[] args)
        {
            var o = new Options();
            bool sawMode = false, sawAddress = false;

            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                Func<string, string> next = name =>
                {
                    if (i + 1 >= args.Length) throw new CliError(name + " の値がありません");
                    return args[++i];
                };

                switch (a)
                {
                    case "-h": case "--help": case "/?": o.Mode = "help"; sawMode = true; break;

                    case "-a": case "--near": case "--address": o.Address = next(a); sawAddress = true; break;
                    case "--latlon":
                        {
                            o.Address = next(a);
                            sawAddress = true;
                            GeoPoint p0;
                            if (o.Address.Trim().Length > 0 && !Geocoder.TryParseLatLng(o.Address, out p0))
                                throw new CliError("--latlon は 緯度,経度 の形で指定してください（例: 35.65858,139.74543）: " + o.Address);
                            break;
                        }
                    case "-n": case "--count": o.Search.Count = ParseInt(next(a), "件数"); break;
                    case "-r": case "--radius": o.Search.RadiusKm = ParseDouble(next(a), "半径"); break;
                    case "-k": case "--keyword": o.Search.Keyword = next(a); break;
                    case "-w": case "--where": o.Wheres.Add(Pair(next(a), "--where")); break;
                    case "--open-now": o.Search.OpenAt = DateTime.Now; break;
                    case "--open-at": o.Search.OpenAt = ParseTime(next(a)); break;

                    case "--json": o.Format = "json"; break;
                    case "--text": o.Format = "text"; break;
                    case "--csv": o.Format = "csv"; break;
                    case "--full": o.Full = true; break;
                    case "-q": case "--quiet": o.Quiet = true; break;
                    case "--no-gui": o.NoGui = true; break;
                    case "--offline": o.Offline = true; break;
                    case "--online": o.OnlineForced = true; break;
                    case "--no-geocode": o.NoGeocode = true; break;
                    case "--strict": o.Strict = true; break;
                    case "--encoding": o.Enc = ParseEncoding(next(a)); break;
                    case "--data": o.DataPath = next(a); break;
                    case "--timeout": Geocoder.TimeoutMs = ParseInt(next(a), "タイムアウト"); break;

                    case "--list": o.Mode = "list"; sawMode = true; break;
                    case "--fields": o.Mode = "fields"; sawMode = true; break;
                    case "--sample": o.Mode = "sample"; sawMode = true; break;
                    case "--geocode": o.Mode = "geocode"; sawMode = true; o.Address = next(a); break;
                    case "--fill": o.Mode = "fill"; sawMode = true; break;
                    case "--import": o.Mode = "import"; sawMode = true; o.File = next(a); break;
                    case "--export": o.Mode = "export"; sawMode = true; o.File = next(a); break;

                    case "--add": o.Mode = "add"; sawMode = true; break;
                    case "--name": o.StoreName = next(a); break;
                    case "--store-address": o.StoreAddress = next(a); break;
                    case "--set": o.Sets.Add(Pair(next(a), "--set")); break;
                    case "--store-latlon":
                        {
                            GeoPoint p;
                            if (!Geocoder.TryParseLatLng(next(a), out p)) throw new CliError("緯度経度の形が正しくありません（例: 35.65858,139.74543）");
                            o.Lat = p.Lat; o.Lng = p.Lng; o.HasLatLng = true;
                            break;
                        }

                    case "--env-prefix":
                        o.EnvPrefix = (i + 1 < args.Length && !args[i + 1].StartsWith("-")) ? args[++i] : DefaultEnvPrefix;
                        break;

                    default:
                        if (a.StartsWith("-")) throw new CliError("不明なオプション: " + a);
                        if (a.Contains("=")) { o.Sets.Add(Pair(a, "値")); break; }   // 変数名=値 の短い形
                        if (o.Mode == "add" && o.StoreName.Length == 0) o.StoreName = a;
                        else if (o.Address.Length == 0) { o.Address = a; sawAddress = true; }
                        else throw new CliError("引数が多すぎます: " + a);
                        break;
                }
            }

            if (!sawMode && !sawAddress && o.EnvPrefix == null) o.Mode = "help";
            return o;
        }

        static KeyValuePair<string, string> Pair(string text, string what)
        {
            int eq = (text ?? "").IndexOf('=');
            if (eq <= 0) throw new CliError(what + " は 変数名=値 の形で指定してください: " + text);
            return new KeyValuePair<string, string>(text.Substring(0, eq).Trim(), text.Substring(eq + 1));
        }

        /// <summary>--where / -c で渡された名前を、フィールドの変数名に直す</summary>
        static void ResolveWheres(Options o, StoreData data)
        {
            foreach (var w in o.Wheres)
            {
                var f = data.Field(w.Key);
                if (f == null)
                {
                    WriteErr("知らない項目なので無視します: " + w.Key + Environment.NewLine, o.Enc);
                    continue;
                }
                o.Search.FieldFilters[f.ApiName] = w.Value;
            }
        }

        /// <summary>音声認識システムが環境変数で値を渡す場合（AMIVOICE_ST_ADDRESS、AMIVOICE_ST_CATEGORY など）</summary>
        static void ApplyEnv(Options o, StoreData data, string prefix)
        {
            string v;
            if (Env(prefix + "ADDRESS", out v)) o.Address = v;
            if (Env(prefix + "COUNT", out v)) o.Search.Count = ParseInt(v, "件数");
            if (Env(prefix + "RADIUS", out v)) o.Search.RadiusKm = ParseDouble(v, "半径");
            if (Env(prefix + "KEYWORD", out v)) o.Search.Keyword = v;
            if (Env(prefix + "NAME", out v)) o.StoreName = v;

            // 定義されている項目は、接頭辞＋変数名（大文字）の環境変数から取り込む
            foreach (var f in data.Fields)
            {
                if (!Env(prefix + f.ApiName.ToUpperInvariant(), out v)) continue;
                if (o.Mode == "add") o.Sets.Add(new KeyValuePair<string, string>(f.ApiName, v));
                else o.Search.FieldFilters[f.ApiName] = v;
            }
        }

        static bool Env(string name, out string value)
        {
            value = Environment.GetEnvironmentVariable(name);
            return !string.IsNullOrEmpty(value);
        }

        static int ParseInt(string s, string what)
        {
            int n;
            if (!int.TryParse(s.Trim(), out n) || n <= 0) throw new CliError(what + "は1以上の数で指定してください: " + s);
            return n;
        }

        /// <summary>「18:30」または「2026/09/13 18:30」。時刻だけなら今日の日付で扱う。</summary>
        static DateTime ParseTime(string text)
        {
            string t = (text ?? "").Trim();
            DateTime d;
            if (DateTime.TryParse(t, new CultureInfo("ja-JP"), DateTimeStyles.AllowWhiteSpaces, out d)) return d;
            int colon = t.IndexOf(':');
            int h, m = 0;
            string hh = colon < 0 ? t : t.Substring(0, colon);
            if (int.TryParse(hh, out h) && h >= 0 && h < 24 &&
                (colon < 0 || int.TryParse(t.Substring(colon + 1), out m)) && m >= 0 && m < 60)
                return DateTime.Today.AddHours(h).AddMinutes(m);
            throw new CliError("時刻は 18:30 のような形で指定してください: " + text);
        }

        static double ParseDouble(string s, string what)
        {
            double d;
            if (!double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out d) || d < 0)
                throw new CliError(what + "は0以上の数で指定してください: " + s);
            return d;
        }

        static Encoding ParseEncoding(string name)
        {
            switch (name.Trim().ToLowerInvariant())
            {
                case "utf8": return new UTF8Encoding(false);
                case "utf8bom": return new UTF8Encoding(true);
                case "sjis": case "shiftjis": case "cp932": case "932": return Encoding.GetEncoding(932);
                default: throw new CliError("不明な文字コード: " + name + "（utf8 / utf8bom / sjis）");
            }
        }

        // ------------------------------------------------------------------ 各モード

        static int RunNear(StoreStore store, Options o, bool online, out string detail)
        {
            if (o.Address.Trim().Length == 0)
            {
                detail = "住所が空";
                WriteNoAddress(store.Data, o);
                return Soft(o, ExitNoAddress);
            }

            var geo = Geocoder.Resolve(o.Address, store.Data, online);
            if (!geo.Ok)
            {
                detail = "住所を特定できない: " + o.Address;
                WriteNoAddress(store.Data, o);
                return Soft(o, ExitNoAddress);
            }

            var list = Finder.Nearest(store.Data, geo.Point, o.Search);
            detail = geo.Source + " " + geo.Point + " → " + list.Count + "件";

            if (!o.NoGui) LastQuery.From(geo, o.Search).Save(store.FilePath);

            if (list.Count == 0)
            {
                WriteErr(NotFoundMessage + Environment.NewLine, o.Enc);
                WriteEmptyResult(store.Data, o, geo, "no_store");
                return Soft(o, ExitNoStore);
            }

            switch (o.Format)
            {
                case "json": Write(Finder.ToJson(store.Data, geo, list, "ok", o.Full) + Environment.NewLine, o.Enc); break;
                case "text": Write(Finder.ToText(store.Data, list, o.Full), o.Enc); break;
                default: Write(Finder.ToCsv(store.Data, list, o.Full), o.Enc); break;
            }
            if (!o.Quiet && o.Format != "json")
                WriteErr("検索地点: " + (geo.Matched.Length > 0 ? geo.Matched : geo.Query) +
                         "（" + geo.SourceLabel + " " + geo.Point + "）" + Environment.NewLine, o.Enc);
            return ExitFound;
        }

        /// <summary>
        /// 結果が0件のときの標準出力。CSVは見出しだけ、JSONは stores が空の JSON を出す
        /// （終了コードを0で返すため、呼び出し側が中身で判別できるようにする）。テキストは何も出さない。
        /// </summary>
        /// <summary>
        /// 住所から場所を特定できなかったとき（住所が空のときも含む）。連携側が正常時の出力として受け取れるよう、
        /// 標準出力に「住所から場所を特定できませんでした」の1行だけを出す（見出し行は出さない。標準エラー出力にも出さない）。
        /// JSON のときは形を崩さないよう、status と message に入れて返す。
        /// </summary>
        static void WriteNoAddress(StoreData data, Options o)
        {
            if (o.Format == "json")
            {
                var geo = new GeocodeResult { Query = o.Address };
                Write(Finder.ToJson(data, geo, new List<Nearby>(), "no_address", o.Full, NoAddressMessage) + Environment.NewLine, o.Enc);
                return;
            }
            Write(NoAddressMessage + Environment.NewLine, o.Enc);
        }

        static void WriteEmptyResult(StoreData data, Options o, GeocodeResult geo, string status)
        {
            if (geo == null) geo = new GeocodeResult { Query = o.Address };
            var empty = new List<Nearby>();
            switch (o.Format)
            {
                case "json":
                    Write(Finder.ToJson(data, geo, empty, status, o.Full) + Environment.NewLine, o.Enc);
                    break;
                case "text":
                    break;
                default:
                    Write(Finder.ToCsv(data, empty, o.Full), o.Enc);
                    break;
            }
        }

        static int RunGeocode(StoreStore store, Options o, bool online, out string detail)
        {
            var geo = Geocoder.Resolve(o.Address, store.Data, online);
            detail = geo.Source;
            if (!geo.Ok)
            {
                WriteErr("住所から場所を特定できませんでした: " + o.Address + Environment.NewLine, o.Enc);
                return Soft(o, ExitNoAddress);
            }
            Write(TextUtil.CsvLine("入力", "一致した住所", "取得元", "緯度", "経度") + Environment.NewLine +
                  TextUtil.CsvLine(geo.Query, geo.Matched, geo.SourceLabel,
                      geo.Point.Lat.ToString("0.#####", CultureInfo.InvariantCulture),
                      geo.Point.Lng.ToString("0.#####", CultureInfo.InvariantCulture)) + Environment.NewLine, o.Enc);
            return ExitFound;
        }

        /// <summary>定義されている項目の一覧（変数名の確認用）</summary>
        static int RunFields(StoreStore store, Options o, out string detail)
        {
            var data = store.Data;
            var sb = new StringBuilder();
            sb.AppendLine(TextUtil.CsvLine("表示名", "変数名", "型", "一覧・標準出力", "キー項目", "必須", "選択肢"));
            sb.AppendLine(TextUtil.CsvLine(data.NameLabel, data.NameApi, "テキスト（固定）", data.OutName ? "○" : "", "", "○", ""));
            sb.AppendLine(TextUtil.CsvLine(data.AddressLabel, data.AddressApi, "テキスト（固定）", data.OutAddress ? "○" : "", "", "", ""));
            foreach (var f in data.SortedFields)
                sb.AppendLine(TextUtil.CsvLine(f.Label, f.ApiName, FieldTypes.Label(f.Type),
                    f.InList ? "○" : "", f.IsKey ? "○" : "", f.Required ? "○" : "",
                    string.Join(" / ", f.OptionList)));
            foreach (var r in StoreData.ResultItems)
                sb.AppendLine(TextUtil.CsvLine(r.Label, r.JsonKey, "検索結果（固定）", data.GetOutput(r.Key) ? "○" : "", "", "", ""));
            Write(sb.ToString(), o.Enc);
            detail = data.Fields.Count + "項目";
            return ExitFound;
        }

        static int RunList(StoreStore store, Options o, out string detail)
        {
            var data = store.Data;
            string kw = TextUtil.Norm(o.Search.Keyword);
            var list = data.Stores
                .Where(s => kw.Length == 0 || TextUtil.Norm(data.SearchText(s)).Contains(kw))
                .Where(s => o.Search.FieldFilters.All(kv => string.IsNullOrEmpty(kv.Value) ||
                            TextUtil.Norm(s.Get(kv.Key)).Contains(TextUtil.Norm(kv.Value))))
                .ToList();
            detail = list.Count + "件";
            Write(StoreCsv.Export(data, list), o.Enc);
            return list.Count > 0 ? ExitFound : Soft(o, ExitNoStore);
        }

        static int RunAdd(StoreStore store, Options o, bool online, out string detail)
        {
            var data = store.Data;

            // --set 店舗名=… / --set 住所=… でも受け取れるようにする（設定された変数名・表示名のどちらでも可）
            foreach (var kv in o.Sets)
            {
                if (data.IsNameName(kv.Key) && o.StoreName.Trim().Length == 0) o.StoreName = kv.Value;
                else if (data.IsAddressName(kv.Key) && o.StoreAddress.Trim().Length == 0) o.StoreAddress = kv.Value;
            }
            if (o.StoreName.Trim().Length == 0)
                throw new CliError(data.NameLabel + "を指定してください（--name \"○○店\" または --set " +
                                   data.NameApi + "=○○店）");

            // キー項目の値が既にある店舗なら、それを更新する（通話中に複数回送っても1件にまとまる）
            var key = data.KeyField;
            Store s = null;
            if (key != null)
            {
                var kv = o.Sets.FirstOrDefault(x => data.Field(x.Key) != null && data.Field(x.Key).ApiName == key.ApiName);
                if (kv.Key != null) s = data.ByKey(kv.Value);
            }
            bool isNew = s == null;
            if (isNew) s = new Store();

            s.Name = o.StoreName.Trim();
            if (o.StoreAddress.Trim().Length > 0) s.Address = o.StoreAddress.Trim();
            if (o.HasLatLng) { s.Lat = o.Lat; s.Lng = o.Lng; }

            foreach (var kv in o.Sets)
            {
                if (data.IsNameName(kv.Key)) { s.Name = kv.Value.Trim(); continue; }
                if (data.IsAddressName(kv.Key)) { s.Address = kv.Value.Trim(); continue; }
                var f = data.Field(kv.Key);
                if (f == null)
                {
                    WriteErr("定義されていない項目なので無視します: " + kv.Key +
                             "（--fields で確認できます）" + Environment.NewLine, o.Enc);
                    continue;
                }
                s.Set(f.ApiName, FieldTypes.Normalize(f.Type, kv.Value));
            }

            if (!s.HasLocation && !o.NoGeocode && s.Address.Trim().Length > 0)
            {
                var geo = Geocoder.Resolve(s.Address, data, online);
                if (geo.Ok) { s.Lat = geo.Point.Lat; s.Lng = geo.Point.Lng; }
            }

            s.UpdatedAt = DateTime.Now;
            if (isNew) data.Add(s);
            store.Save();

            detail = (isNew ? "登録 " : "更新 ") + s.Name;
            if (!o.Quiet)
                Write((isNew ? "登録しました" : "更新しました") + "（店舗ID:" + s.Id + " " + s.Name +
                      (s.HasLocation ? " " + new GeoPoint(s.Lat, s.Lng) : " 緯度経度は未設定") + "）" + Environment.NewLine, o.Enc);
            return ExitFound;
        }

        /// <summary>緯度経度が空の店舗を、住所から埋める。設定できた件数を返す。</summary>
        public static int FillMissingLocations(StoreData data, bool online)
        {
            int done = 0;
            foreach (var s in data.Stores.Where(x => !x.HasLocation && x.Address.Trim().Length > 0).ToList())
            {
                var geo = Geocoder.Resolve(s.Address, data, online);
                if (!geo.Ok) continue;
                s.Lat = geo.Point.Lat; s.Lng = geo.Point.Lng; s.UpdatedAt = DateTime.Now;
                done++;
            }
            return done;
        }

        static int RunFill(StoreStore store, Options o, bool online, out string detail)
        {
            int before = store.Data.Stores.Count(x => !x.HasLocation && x.Address.Trim().Length > 0);
            int done = FillMissingLocations(store.Data, online);
            if (done > 0) store.Save();
            detail = done + "件設定 / " + (before - done) + "件失敗";
            if (!o.Quiet) Write(detail + Environment.NewLine, o.Enc);
            return done == 0 && before > 0 ? Soft(o, ExitNoAddress) : ExitFound;
        }

        static int RunImport(StoreStore store, Options o, bool online, out string detail)
        {
            if (!File.Exists(o.File)) throw new CliError("ファイルがありません: " + o.File);
            int added, updated, skipped;
            var errors = StoreCsv.Import(store.Data, File.ReadAllText(o.File, DetectEncoding(o.File)),
                                         out added, out updated, out skipped);
            int filled = o.NoGeocode ? 0 : FillMissingLocations(store.Data, online);
            store.Save();
            detail = "追加" + added + " 更新" + updated + " 読み飛ばし" + skipped +
                     (filled > 0 ? " 緯度経度を取得" + filled : "");
            if (!o.Quiet) Write(detail + Environment.NewLine, o.Enc);
            foreach (var e in errors.Take(20)) WriteErr(e + Environment.NewLine, o.Enc);
            return ExitFound;
        }

        static int RunExport(StoreStore store, Options o, out string detail)
        {
            var enc = o.Enc ?? new UTF8Encoding(true);
            File.WriteAllText(o.File, StoreCsv.Export(store.Data, store.Data.Stores), enc);
            detail = store.Data.Stores.Count + "件 → " + o.File;
            if (!o.Quiet) Write(detail + Environment.NewLine, null);
            return ExitFound;
        }

        static int RunSample(StoreStore store, Options o, out string detail)
        {
            int before = store.Data.Stores.Count;
            store.Data.Seed();
            store.Save();
            detail = (store.Data.Stores.Count - before) + "件追加";
            if (!o.Quiet) Write("サンプル店舗を " + detail + "しました" + Environment.NewLine, o.Enc);
            return ExitFound;
        }

        public static Encoding DetectEncoding(string path)
        {
            try
            {
                var head = new byte[3];
                using (var fs = File.OpenRead(path)) fs.Read(head, 0, 3);
                if (head[0] == 0xEF && head[1] == 0xBB && head[2] == 0xBF) return new UTF8Encoding(true);
                // BOM が無い場合、UTF-8 として読めなければ Shift_JIS とみなす
                var bytes = File.ReadAllBytes(path);
                var strict = Encoding.GetEncoding("utf-8", EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
                try { strict.GetString(bytes); return new UTF8Encoding(false); }
                catch { return Encoding.GetEncoding(932); }
            }
            catch { return new UTF8Encoding(false); }
        }

        // ------------------------------------------------------------------ 出力

        /// <summary>標準出力へ書く。リダイレクト・パイプ時はバイト列で直接書き（既定UTF-8）。</summary>
        static void Write(string text, Encoding enc)
        {
            if (enc == null && !Console.IsOutputRedirected)
            {
                Encoding old = null;
                try { old = Console.OutputEncoding; Console.OutputEncoding = new UTF8Encoding(false); }
                catch { old = null; }
                try { Console.Out.Write(text); Console.Out.Flush(); }
                finally { if (old != null) try { Console.OutputEncoding = old; } catch { } }
                return;
            }
            if (enc == null) enc = new UTF8Encoding(false);
            var s = Console.OpenStandardOutput();
            byte[] pre = enc.GetPreamble();
            if (pre.Length > 0) s.Write(pre, 0, pre.Length);
            byte[] b = enc.GetBytes(text);
            s.Write(b, 0, b.Length);
            s.Flush();
        }

        static void WriteErr(string text, Encoding enc)
        {
            if (enc == null && !Console.IsErrorRedirected)
            {
                Encoding old = null;
                try { old = Console.OutputEncoding; Console.OutputEncoding = new UTF8Encoding(false); }
                catch { old = null; }
                try { Console.Error.Write(text); Console.Error.Flush(); }
                finally { if (old != null) try { Console.OutputEncoding = old; } catch { } }
                return;
            }
            if (enc == null) enc = new UTF8Encoding(false);
            var s = Console.OpenStandardError();
            byte[] b = enc.GetBytes(text);
            s.Write(b, 0, b.Length);
            s.Flush();
        }

        public static string HelpText()
        {
            return string.Join(Environment.NewLine, new[]
            {
                "DemoMapDB - 住所から近い店舗を探すデモ",
                "",
                "  DemoMapDB.exe                         画面を開く",
                "  DemoMapDB.exe --near \"<住所>\" -n 3      近い店舗を3件、CSVで出力",
                "",
                "検索（--near / -a）",
                "  -n, --count <件数>        表示する件数（既定 3）",
                "  -r, --radius <km>         この距離までにしぼる（既定 上限なし）",
                "  -w, --where <変数名>=<値>  項目でしぼる（部分一致。複数指定可。変数名は --fields で確認）",
                "  -k, --keyword <語>        店舗名・住所・自由項目の部分一致でしぼる",
                "      --open-now            いま営業時間内の店舗だけにしぼる",
                "      --open-at <時刻>      その時刻に営業している店舗だけにしぼる（例: 18:30）",
                "                            ※ 営業時間の項目（型が「営業時間」）を見ます。空・読めない値は除きません",
                "      --json / --text       出力の形を変える（既定は CSV）",
                "      --full                ［項目の設定］の選択にかかわらず、全項目と距離km・緯度経度も出す",
                "      --latlon \"35.65,139.74\"  住所の代わりに緯度経度を直接渡す",
                "      --offline / --online  住所の変換でオンライン検索を使わない／必ず使う",
                "      --timeout <ミリ秒>    オンライン検索の待ち時間（既定 4000）",
                "      --no-gui              開いている画面へ反映しない",
                "  -q, --quiet               「検索地点: …」の補足行を出さない（エラーは常に出ます）",
                "      --strict              該当店舗なし・住所を特定できないときの終了コードを 1 / 2 にする",
                "                            （既定は 0。CSVは見出しだけ、JSONは stores が空の形で返します）",
                "      --encoding <文字コード>  utf8 / utf8bom / sjis",
                "",
                "店舗の管理（項目は画面の［項目の設定］F4 で自由に増やせます）",
                "  --fields                          定義されている項目を一覧（変数名の確認用）",
                "  --list [-k <語>] [-w 変数名=値]    登録店舗をCSVで出力",
                "  --add --name \"○○店\" --store-address \"東京都港区...\" --set code=T001 --set phonenumber=03-0000-0000",
                "        [--store-latlon \"35.6,139.7\"] [--no-geocode]",
                "        ※ --set は 変数名=値。--set を省いて code=T001 と書いてもかまいません",
                "        ※ キー項目（既定は店舗コード）が同じ店舗があれば、その店舗を更新します",
                "  --fill                            緯度経度が空の店舗を住所から埋める",
                "  --import <CSVファイル> / --export <CSVファイル>",
                "        ※ 取り込んだあと、緯度経度が空の店舗は住所から補います（--no-geocode で抑止）",
                "  --geocode \"<住所>\"                 住所の変換結果だけを確認する",
                "  --sample                          デモ用のサンプル店舗を投入",
                "",
                "環境変数から受け取る",
                "  --env-prefix [接頭辞]   既定 " + DefaultEnvPrefix + "",
                "                          ADDRESS / COUNT / RADIUS / KEYWORD / NAME と、各項目の変数名（大文字）",
                "                          例: " + DefaultEnvPrefix + "ADDRESS、" + DefaultEnvPrefix + "CATEGORY",
                "  DEMOMAPDB_DATA           データファイルの場所（--data でも指定可）",
                "  DEMOMAPDB_LOG            実行ログの場所（off で無効）",
                "",
                "終了コード: 0 成功（該当店舗なし・住所を特定できない場合も既定では 0）/ 9 呼び出しの誤り・例外",
                "            --strict を付けると 1 該当店舗なし / 2 住所を特定できない を返します",
                "",
            });
        }
    }

    /// <summary>コマンドライン実行のログ（1実行1行、タブ区切り、UTF-8）</summary>
    public static class RunLog
    {
        public const string FileName = "demomapdb_log.txt";
        const long MaxBytes = 5 * 1024 * 1024;
        const string Header = "日時\t終了コード\t処理時間\t結果\t引数";

        public static void Write(string[] args, int code, long ms, string detail)
        {
            try
            {
                string path = ResolvePath();
                if (path == null) return;
                bool header = !File.Exists(path);
                if (File.Exists(path) && new FileInfo(path).Length > MaxBytes)
                {
                    File.Copy(path, path + ".1", true);
                    File.Delete(path);
                    header = true;
                }
                var line = new StringBuilder();
                if (header) line.AppendLine(Header);
                line.Append(DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss")).Append('\t')
                    .Append(code).Append('\t').Append(ms).Append("ms").Append('\t')
                    .Append(Clean(detail)).Append('\t')
                    .Append(Clean(string.Join(" ", args.Select(a => a.Contains(" ") ? "\"" + a + "\"" : a).ToArray())));
                File.AppendAllText(path, line.ToString() + Environment.NewLine, new UTF8Encoding(false));
            }
            catch { }
        }

        static string Clean(string s) { return (s ?? "").Replace('\t', ' ').Replace("\r", "").Replace("\n", " "); }

        /// <summary>保存先: 環境変数 DEMOMAPDB_LOG → データファイルと同じフォルダ。off なら記録しない。</summary>
        static string ResolvePath()
        {
            try
            {
                string env = Environment.GetEnvironmentVariable("DEMOMAPDB_LOG");
                if (!string.IsNullOrEmpty(env))
                {
                    string v = env.Trim().ToLowerInvariant();
                    if (v == "off" || v == "none" || v == "0" || v == "false") return null;
                    return Path.GetFullPath(env);
                }
                return Path.Combine(Path.GetDirectoryName(StoreStore.ResolveDefaultPath()), FileName);
            }
            catch { return null; }
        }
    }
}
