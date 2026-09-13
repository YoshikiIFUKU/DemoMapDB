using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace StoreMapDemo
{
    /// <summary>
    /// 店舗一覧の CSV / タブ区切りテキストの読み書き。
    /// 列は「店舗名・住所（名前は設定で変えられる）＋項目の表示名＋緯度・経度」。
    /// 取り込みでは、見出しに表示名・変数名・よくある別名のどれを書いてもかまわない。
    /// </summary>
    public static class StoreCsv
    {
        public const string ColLat = "緯度", ColLng = "経度";

        /// <summary>自由項目の表示名には使えない名前（緯度・経度と、店舗名・住所に設定中の名前）</summary>
        public static string[] ReservedLabels(StoreData data)
        {
            return new[] { ColLat, ColLng, data.NameLabel, data.AddressLabel, data.NameApi, data.AddressApi };
        }

        // 緯度・経度の見出しのゆらぎを吸収する（店舗名・住所は設定された表示名・変数名で照合する）
        static readonly Dictionary<string, string> Aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "lat", ColLat }, { "latitude", ColLat }, { "緯度(lat)", ColLat },
            { "lng", ColLng }, { "lon", ColLng }, { "longitude", ColLng }, { "経度(lng)", ColLng },
        };

        /// <summary>書き出す列の見出し</summary>
        public static List<string> Columns(StoreData data)
        {
            var cols = new List<string> { data.NameLabel, data.AddressLabel };
            cols.AddRange(data.SortedFields.Select(f => f.Label));
            cols.Add(ColLat);
            cols.Add(ColLng);
            return cols;
        }

        public static string Export(StoreData data, IEnumerable<Store> stores)
        {
            var fields = data.SortedFields;
            var sb = new StringBuilder();
            sb.AppendLine(TextUtil.CsvLine(Columns(data)));
            foreach (var s in stores)
            {
                var cells = new List<string> { s.Name, s.Address };
                cells.AddRange(fields.Select(f => s.Get(f.ApiName)));
                cells.Add(s.HasLocation ? s.Lat.ToString("0.#####", CultureInfo.InvariantCulture) : "");
                cells.Add(s.HasLocation ? s.Lng.ToString("0.#####", CultureInfo.InvariantCulture) : "");
                sb.AppendLine(TextUtil.CsvLine(cells));
            }
            return sb.ToString();
        }

        /// <summary>
        /// 取り込む。1行目は見出し。キー項目（既定は店舗コード）が一致する店舗があれば更新、無ければ追加。
        /// 見出しにない項目は変更しない。戻り値は、取り込めなかった行などの説明。
        /// </summary>
        public static List<string> Import(StoreData data, string text, out int added, out int updated, out int skipped)
        {
            added = updated = skipped = 0;
            var errors = new List<string>();
            var lines = (text ?? "").Replace("\r\n", "\n").Split('\n').Where(l => l.Trim().Length > 0).ToList();
            if (lines.Count == 0) { errors.Add("中身がありません"); return errors; }

            char sep = lines[0].Contains("\t") ? '\t' : ',';
            var head = TextUtil.SplitLine(lines[0], sep).Select(h => Canonical(data, h)).ToList();
            if (!head.Contains(data.NameLabel))
            {
                errors.Add("1行目に見出しが必要です（少なくとも「" + data.NameLabel + "」の列）");
                return errors;
            }
            foreach (var h in head.Where(h => h.Length > 0 && !IsKnown(data, h)).Distinct())
                errors.Add("知らない列は読み飛ばします: " + h);

            var key = data.KeyField;
            for (int i = 1; i < lines.Count; i++)
            {
                var cells = TextUtil.SplitLine(lines[i], sep);
                Func<string, string> get = name =>
                {
                    int idx = head.IndexOf(name);
                    return idx >= 0 && idx < cells.Count ? cells[idx].Trim() : "";
                };
                Func<string, bool> has = name => head.IndexOf(name) >= 0;

                string storeName = get(data.NameLabel);
                if (storeName.Length == 0) { skipped++; continue; }

                Store s = null;
                if (key != null && has(key.Label)) s = data.ByKey(get(key.Label));
                bool isNew = s == null;
                if (isNew) s = new Store();

                s.Name = storeName;
                if (has(data.AddressLabel)) s.Address = get(data.AddressLabel);
                foreach (var f in data.Fields)
                    if (has(f.Label)) s.Set(f.ApiName, FieldTypes.Normalize(f.Type, get(f.Label)));

                if (has(ColLat) && has(ColLng))
                {
                    string rawLat = get(ColLat), rawLng = get(ColLng);
                    double lat, lng;
                    if (rawLat.Length > 0 || rawLng.Length > 0)
                    {
                        if (!double.TryParse(rawLat, NumberStyles.Float, CultureInfo.InvariantCulture, out lat) ||
                            !double.TryParse(rawLng, NumberStyles.Float, CultureInfo.InvariantCulture, out lng) ||
                            lat < -90 || lat > 90 || lng < -180 || lng > 180)
                            errors.Add((i + 1) + "行目: 緯度経度を読めませんでした（" + storeName + "）");
                        else { s.Lat = lat; s.Lng = lng; }
                    }
                }

                s.UpdatedAt = DateTime.Now;
                if (isNew) { data.Add(s); added++; }
                else updated++;
            }
            return errors;
        }

        /// <summary>見出しを、固定項目名かフィールドの表示名にそろえる</summary>
        static string Canonical(StoreData data, string header)
        {
            string h = (header ?? "").Trim().Trim('"');
            if (h.Length == 0) return "";
            if (data.IsNameName(h)) return data.NameLabel;
            if (data.IsAddressName(h)) return data.AddressLabel;
            string mapped;
            if (Aliases.TryGetValue(h, out mapped)) return mapped;
            if (h == ColLat || h == ColLng) return h;
            var f = data.Field(h);          // 表示名・変数名のどちらでも引く
            return f != null ? f.Label : h;
        }

        static bool IsKnown(StoreData data, string canonical)
        {
            return canonical == data.NameLabel || canonical == data.AddressLabel ||
                   canonical == ColLat || canonical == ColLng || data.FieldByLabel(canonical) != null;
        }

    }
}
