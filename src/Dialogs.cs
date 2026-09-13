using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace StoreMapDemo
{
    /// <summary>
    /// 店舗1件の登録・編集。固定項目（店舗名・住所・緯度経度）のあとに、
    /// 項目の設定で定義された項目の入力欄が型に応じて並ぶ。
    /// </summary>
    public class StoreEditForm : Form
    {
        readonly StoreData data;
        readonly Store store;
        readonly bool isNew;
        readonly Dictionary<string, Control> inputs = new Dictionary<string, Control>();

        TextBox tbName, tbAddress, tbLat, tbLng;
        Label lbGeo;
        Panel body;

        public Store Result { get { return store; } }

        public StoreEditForm(Store target, StoreData data)
        {
            this.data = data;
            isNew = target == null;
            store = isNew ? new Store() : target.Clone();

            Text = isNew ? "店舗の登録" : "店舗の編集";
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(560, 560);
            MinimumSize = new Size(500, 400);
            Font = new Font("Yu Gothic UI", 9f);
            Theme.Attach(this);

            body = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(0, 8, 0, 8) };
            int y = 12;

            tbName = Row(data.NameLabel, ref y, 320, store.Name);
            tbAddress = Row(data.AddressLabel, ref y, 380, store.Address);

            var btnGeo = new Button { Text = "住所から緯度経度を取得", Left = 140, Top = y, Width = 190, Height = 26 };
            btnGeo.Click += (s, e) => Geocode();
            body.Controls.Add(btnGeo);
            lbGeo = new Label { Left = 338, Top = y + 5, Width = 190, Tag = "sub", Text = "" };
            body.Controls.Add(lbGeo);
            y += 36;

            tbLat = Row("緯度", ref y, 140, store.HasLocation ? Num(store.Lat) : "");
            tbLng = Row("経度", ref y, 140, store.HasLocation ? Num(store.Lng) : "");

            var sep = new Label { Left = 16, Top = y, Width = 500, Height = 1, BorderStyle = BorderStyle.Fixed3D };
            body.Controls.Add(sep);
            y += 12;

            foreach (var f in data.SortedFields) AddFieldInput(f, ref y);

            var foot = new Panel { Dock = DockStyle.Bottom, Height = 48, Width = ClientSize.Width };
            var ok = new Button { Text = "保存", Left = 356, Top = 9, Width = 88, Height = 30, Tag = "primary", Anchor = AnchorStyles.Right | AnchorStyles.Top };
            var cancel = new Button { Text = "キャンセル", Left = 452, Top = 9, Width = 88, Height = 30, DialogResult = DialogResult.Cancel, Anchor = AnchorStyles.Right | AnchorStyles.Top };
            ok.Click += (s, e) => { if (Commit()) DialogResult = DialogResult.OK; };
            foot.Controls.Add(ok);
            foot.Controls.Add(cancel);

            Controls.Add(body);
            Controls.Add(foot);
            AcceptButton = ok;
            CancelButton = cancel;
            ClientSize = new Size(560, Math.Min(720, y + 70));
        }

        static string Num(double d) { return d.ToString("0.#####", CultureInfo.InvariantCulture); }

        TextBox Row(string label, ref int y, int width, string text)
        {
            body.Controls.Add(new Label { Text = label, Left = 16, Top = y + 4, Width = 118 });
            var tb = new TextBox { Left = 140, Top = y, Width = width, Text = text };
            body.Controls.Add(tb);
            y += 34;
            return tb;
        }

        /// <summary>項目の型に合わせた入力欄を置く</summary>
        void AddFieldInput(FieldDef f, ref int y)
        {
            string value = store.Get(f.ApiName);
            body.Controls.Add(new Label
            {
                Text = f.Label + (f.Required ? " *" : ""),
                Left = 16, Top = y + 4, Width = 118, AutoEllipsis = true,
                Tag = f.Required ? "accent" : null,
            });

            Control c;
            switch (f.Type)
            {
                case FieldTypes.TextArea:
                    c = new TextBox { Left = 140, Top = y, Width = 380, Height = 64, Multiline = true, ScrollBars = ScrollBars.Vertical, Text = value };
                    y += 74;
                    break;
                case FieldTypes.Checkbox:
                    c = new CheckBox { Left = 140, Top = y, Width = 300, Checked = value == "1", Text = "" };
                    y += 30;
                    break;
                case FieldTypes.Select:
                    {
                        var cb = new ComboBox { Left = 140, Top = y, Width = 220, DropDownStyle = ComboBoxStyle.DropDown };
                        cb.Items.Add("");
                        cb.Items.AddRange(f.OptionList);
                        cb.Text = value;
                        c = cb;
                        y += 34;
                        break;
                    }
                case FieldTypes.Number:
                    c = new TextBox { Left = 140, Top = y, Width = 140, Text = value };
                    y += 34;
                    break;
                case FieldTypes.Date:
                    c = new TextBox { Left = 140, Top = y, Width = 140, Text = value };
                    body.Controls.Add(new Label { Left = 288, Top = y + 4, Width = 220, Tag = "sub", Text = "例: 2026/04/01" });
                    y += 34;
                    break;
                case FieldTypes.Hours:
                    c = new TextBox { Left = 140, Top = y, Width = 200, Text = value };
                    body.Controls.Add(new Label
                    {
                        Left = 348, Top = y + 4, Width = 200, Tag = "sub",
                        Text = "例: 9:00-21:00 / 24時間",
                    });
                    y += 34;
                    break;
                default:
                    c = new TextBox { Left = 140, Top = y, Width = 300, Text = value };
                    y += 34;
                    break;
            }
            body.Controls.Add(c);
            inputs[f.ApiName] = c;
        }

        string InputValue(FieldDef f)
        {
            Control c;
            if (!inputs.TryGetValue(f.ApiName, out c)) return store.Get(f.ApiName);
            var ck = c as CheckBox;
            if (ck != null) return ck.Checked ? "1" : "";
            return c.Text.Trim();
        }

        void Geocode()
        {
            string address = tbAddress.Text.Trim();
            if (address.Length == 0) { lbGeo.Text = "住所を入れてください"; return; }
            lbGeo.Text = "取得中…";
            Application.DoEvents();
            var geo = Geocoder.Resolve(address, data, Settings.GetBool("online_geocoding", true));
            if (!geo.Ok) { lbGeo.Text = "見つかりませんでした"; return; }
            tbLat.Text = Num(geo.Point.Lat);
            tbLng.Text = Num(geo.Point.Lng);
            lbGeo.Text = geo.Source == "online" ? "オンラインで取得" : "内蔵の住所表から";
        }

        bool Commit()
        {
            if (tbName.Text.Trim().Length == 0) return Warn(data.NameLabel + "を入れてください。", tbName);

            double lat = 0, lng = 0;
            bool hasLat = tbLat.Text.Trim().Length > 0, hasLng = tbLng.Text.Trim().Length > 0;
            if (hasLat != hasLng) return Warn("緯度と経度は両方を入れてください。", tbLat);
            if (hasLat)
            {
                if (!double.TryParse(tbLat.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out lat) ||
                    !double.TryParse(tbLng.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out lng) ||
                    lat < -90 || lat > 90 || lng < -180 || lng > 180)
                    return Warn("緯度経度の形が正しくありません（例: 35.65858 / 139.74543）。", tbLat);
            }

            var key = data.KeyField;
            foreach (var f in data.SortedFields)
            {
                string v = InputValue(f);
                if (f.Required && v.Length == 0) return Warn("「" + f.Label + "」は必須です。", inputs[f.ApiName]);
                if (f.Type == FieldTypes.Number && v.Length > 0)
                {
                    double d;
                    if (!double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                        return Warn("「" + f.Label + "」は数値で入れてください。", inputs[f.ApiName]);
                }
                if (key != null && f.ApiName == key.ApiName && v.Length > 0)
                {
                    var dup = data.ByKey(v);
                    if (dup != null && dup.Id != store.Id)
                        return Warn("同じ「" + f.Label + "」の店舗があります: " + dup.Name, inputs[f.ApiName]);
                }
            }

            store.Name = tbName.Text.Trim();
            store.Address = tbAddress.Text.Trim();
            store.Lat = lat;
            store.Lng = lng;
            foreach (var f in data.SortedFields) store.Set(f.ApiName, FieldTypes.Normalize(f.Type, InputValue(f)));
            store.UpdatedAt = DateTime.Now;
            return true;
        }

        bool Warn(string message, Control focus)
        {
            MessageBox.Show(message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            if (focus != null) focus.Focus();
            return false;
        }
    }

    /// <summary>CSV / タブ区切りのテキストを貼り付けて取り込む</summary>
    public class CsvPasteForm : Form
    {
        readonly StoreData data;
        TextBox tbInput;
        Label lbResult;
        DataGridView preview;
        CheckBox ckNoHeader;
        Button btnImport;

        public bool Imported { get; private set; }

        public CsvPasteForm(StoreData data)
        {
            this.data = data;
            Text = "CSVを貼り付けて取り込む";
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(900, 640);
            MinimumSize = new Size(700, 520);
            Font = new Font("Yu Gothic UI", 9f);
            Theme.Attach(this);

            var cols = StoreCsv.Columns(data);
            var head = new Panel { Dock = DockStyle.Top, Height = 70, Tag = "panel" };
            head.Controls.Add(new Label
            {
                Left = 12, Top = 8, Width = 860, Height = 18, Tag = "sub", AutoEllipsis = true,
                Text = "1行目は見出し。列は " + string.Join(" / ", cols) + "（並び順は自由、要らない列は省けます）",
            });
            var btnHeader = new Button { Text = "見出しの行を入れる", Left = 12, Top = 32, Width = 140, Height = 27 };
            var btnSample = new Button { Text = "記入例を入れる", Left = 160, Top = 32, Width = 120, Height = 27 };
            var btnPaste = new Button { Text = "クリップボードから貼り付け", Left = 288, Top = 32, Width = 180, Height = 27 };
            ckNoHeader = new CheckBox { Left = 480, Top = 36, Width = 400, Text = "1行目から店舗データ（上の並びとして読む）" };
            btnHeader.Click += (s, e) => InsertTop(TextUtil.CsvLine(cols));
            btnSample.Click += (s, e) => InsertTop(TextUtil.CsvLine(cols) + Environment.NewLine + SampleRows());
            btnPaste.Click += (s, e) =>
            {
                try { if (Clipboard.ContainsText()) tbInput.Text = Clipboard.GetText(); } catch { }
                Check();
            };
            ckNoHeader.CheckedChanged += (s, e) => Check();
            head.Controls.AddRange(new Control[] { btnHeader, btnSample, btnPaste, ckNoHeader });

            tbInput = new TextBox
            {
                Dock = DockStyle.Top, Height = 210, Multiline = true, AcceptsTab = true,
                ScrollBars = ScrollBars.Both, WordWrap = false, Font = new Font("Consolas", 9.5f),
            };
            tbInput.TextChanged += (s, e) => lbResult.Text = "［内容を確認］を押すと、取り込まれる内容を表示します";

            var mid = new Panel { Dock = DockStyle.Top, Height = 38 };
            var btnCheck = new Button { Text = "内容を確認", Left = 0, Top = 5, Width = 110, Height = 27 };
            btnCheck.Click += (s, e) => Check();
            lbResult = new Label { Left = 120, Top = 10, Width = 760, Tag = "sub", AutoEllipsis = true };
            mid.Controls.Add(btnCheck);
            mid.Controls.Add(lbResult);

            preview = new DataGridView
            {
                Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false,
                BorderStyle = BorderStyle.FixedSingle, EnableHeadersVisualStyles = false,
                ColumnHeadersHeight = LogicalToDeviceUnits(28), RowTemplate = { Height = LogicalToDeviceUnits(24) },
            };
            preview.Columns.Add("state", "取り込み");
            preview.Columns.Add("name", "店舗名");
            preview.Columns.Add("address", "住所");
            preview.Columns.Add("latlng", "緯度・経度");
            preview.Columns.Add("fields", "その他の項目");
            preview.Columns[1].HeaderText = data.NameLabel;
            preview.Columns[2].HeaderText = data.AddressLabel;
            preview.Columns[0].Width = LogicalToDeviceUnits(70);
            preview.Columns[1].Width = LogicalToDeviceUnits(160);
            preview.Columns[2].Width = LogicalToDeviceUnits(250);
            preview.Columns[3].Width = LogicalToDeviceUnits(140);
            preview.Columns[4].Width = LogicalToDeviceUnits(250);

            var foot = new Panel { Dock = DockStyle.Bottom, Height = 46, Width = ClientSize.Width };
            btnImport = new Button { Text = "取り込む", Left = 668, Top = 8, Width = 104, Height = 30, Tag = "primary", Enabled = false, Anchor = AnchorStyles.Right | AnchorStyles.Top };
            var cancel = new Button { Text = "閉じる", Left = 780, Top = 8, Width = 104, Height = 30, DialogResult = DialogResult.Cancel, Anchor = AnchorStyles.Right | AnchorStyles.Top };
            btnImport.Click += (s, e) => DoImport();
            foot.Controls.Add(btnImport);
            foot.Controls.Add(cancel);

            Controls.Add(preview);
            Controls.Add(foot);
            Controls.Add(mid);
            Controls.Add(tbInput);
            Controls.Add(head);
            CancelButton = cancel;
            Shown += (s, e) => { Theme.StyleGrid(preview); tbInput.Focus(); };
        }

        /// <summary>いまの項目定義に合わせた記入例を作る</summary>
        string SampleRows()
        {
            var examples = new Dictionary<string, string[]>
            {
                { StoreData.CodeApi, new[] { "T101", "T102" } },
                { StoreData.PhoneApi, new[] { "03-0000-0000", "03-0000-0001" } },
                { StoreData.HoursApi, new[] { "10:00-21:00", "11:00-20:00" } },
            };
            var sb = new StringBuilder();
            string[] names = { "新宿三丁目店", "秋葉原店" };
            string[] addrs = { "東京都新宿区新宿3-1-1", "東京都千代田区外神田1-1-1" };
            for (int r = 0; r < 2; r++)
            {
                var cells = new List<string> { names[r], addrs[r] };
                foreach (var f in data.SortedFields)
                {
                    string[] ex;
                    if (examples.TryGetValue(f.ApiName, out ex)) cells.Add(ex[r]);
                    else if (f.Type == FieldTypes.Select) cells.Add(f.OptionList.FirstOrDefault() ?? "");
                    else cells.Add("");
                }
                cells.Add("");   // 緯度（空なら住所から取得）
                cells.Add("");   // 経度
                sb.AppendLine(TextUtil.CsvLine(cells));
            }
            return sb.ToString().TrimEnd();
        }

        void InsertTop(string text)
        {
            tbInput.Text = tbInput.Text.Trim().Length == 0 ? text : text + Environment.NewLine + tbInput.Text;
            Check();
        }

        string InputText()
        {
            string text = tbInput.Text;
            if (!ckNoHeader.Checked) return text;
            char sep = text.Contains("\t") ? '\t' : ',';
            return string.Join(sep.ToString(), StoreCsv.Columns(data)) + Environment.NewLine + text;  // 見出しを補う
        }

        /// <summary>取り込む前に、写しに対して同じ処理をして結果を見せる</summary>
        void Check()
        {
            preview.Rows.Clear();
            btnImport.Enabled = false;
            if (tbInput.Text.Trim().Length == 0) { lbResult.Text = ""; return; }

            var copy = new StoreData { NextStoreId = data.NextStoreId, NextFieldId = data.NextFieldId };
            copy.Fields = data.Fields.Select(f => f.Clone()).ToList();
            var before = new Dictionary<int, Store>();
            foreach (var s in data.Stores) { var c = s.Clone(); copy.Stores.Add(c); before[c.Id] = s; }

            int added, updated, skipped;
            var errors = StoreCsv.Import(copy, InputText(), out added, out updated, out skipped);

            foreach (var s in copy.Stores)
            {
                Store old;
                bool isNew = !before.TryGetValue(s.Id, out old);
                if (!isNew && !Differs(old, s, copy)) continue;
                int i = preview.Rows.Add(isNew ? "新規" : "更新", s.Name, s.Address,
                    s.HasLocation ? s.Lat.ToString("0.#####", CultureInfo.InvariantCulture) + ", " +
                                    s.Lng.ToString("0.#####", CultureInfo.InvariantCulture) : "（住所から取得）",
                    string.Join(" / ", copy.SortedFields.Select(f => s.Get(f.ApiName))
                                          .Where(v => v.Length > 0).ToArray()));
                preview.Rows[i].Cells[0].Style.ForeColor = isNew ? Theme.Success : Theme.Accent;
            }

            lbResult.Text = "新規 " + added + "件 / 更新 " + updated + "件 / 読み飛ばし " + skipped + "件" +
                            (errors.Count > 0 ? "　※ " + errors[0] : "");
            lbResult.ForeColor = errors.Count > 0 ? Theme.Warn : Theme.SubText;
            btnImport.Enabled = added + updated > 0;
            Theme.StyleGrid(preview);
        }

        static bool Differs(Store a, Store b, StoreData data)
        {
            if (a.Name != b.Name || a.Address != b.Address || a.Lat != b.Lat || a.Lng != b.Lng) return true;
            return data.Fields.Any(f => a.Get(f.ApiName) != b.Get(f.ApiName));
        }

        void DoImport()
        {
            int added, updated, skipped;
            var errors = StoreCsv.Import(data, InputText(), out added, out updated, out skipped);
            Imported = added + updated > 0;
            var msg = new StringBuilder("追加 " + added + "件 / 更新 " + updated + "件 / 読み飛ばし " + skipped + "件");
            foreach (var e in errors.Take(10)) msg.AppendLine().Append(e);
            MessageBox.Show(msg.ToString(), Text, MessageBoxButtons.OK,
                errors.Count > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
            DialogResult = DialogResult.OK;
        }
    }

    /// <summary>コマンドラインの使い方（F1）</summary>
    public class HelpForm : Form
    {
        public HelpForm(string dataPath, StoreData data)
        {
            Text = "コマンドラインの使い方";
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(780, 580);
            Font = new Font("Yu Gothic UI", 9f);
            Theme.Attach(this);

            var sb = new StringBuilder(Cli.HelpText().Replace("\n", "\r\n"));
            sb.AppendLine("いま定義されている項目（表示名 / 変数名 / 型）");
            foreach (var f in data.SortedFields)
                sb.AppendLine("  " + f.Label + " / " + f.ApiName + " / " + FieldTypes.Label(f.Type) +
                              (f.IsKey ? "（キー項目）" : "") + (f.InList ? "（一覧・標準出力）" : ""));
            sb.AppendLine();
            sb.AppendLine("データファイル: " + dataPath);
            sb.AppendLine("実行ログ: " + System.IO.Path.Combine(System.IO.Path.GetDirectoryName(dataPath), RunLog.FileName));

            Controls.Add(new TextBox
            {
                Dock = DockStyle.Fill, Multiline = true, ReadOnly = true,
                ScrollBars = ScrollBars.Both, WordWrap = false,
                Font = new Font("Consolas", 9.5f), Text = sb.ToString(),
            });
            var close = new Button { Text = "閉じる", Dock = DockStyle.Bottom, Height = 34, DialogResult = DialogResult.OK };
            Controls.Add(close);
            CancelButton = close;
        }
    }

    /// <summary>音声認識システムからの呼び出しを画面上で試す（F2）</summary>
    public class CommandTestForm : Form
    {
        readonly MainForm owner;
        readonly StoreData data;
        TextBox tbAddress, tbOut;
        NumericUpDown numCount;

        public CommandTestForm(MainForm owner, StoreData data, string sampleAddress)
        {
            this.owner = owner;
            this.data = data;
            Text = "連携コマンドのテスト";
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(720, 440);
            Font = new Font("Yu Gothic UI", 9f);
            Theme.Attach(this);

            Controls.Add(new Label { Text = "音声認識で取れた住所", Left = 14, Top = 16, Width = 150 });
            tbAddress = new TextBox { Left = 168, Top = 12, Width = 380, Text = sampleAddress };
            Controls.Add(tbAddress);

            Controls.Add(new Label { Text = "件数", Left = 566, Top = 16, Width = 36 });
            numCount = new NumericUpDown { Left = 606, Top = 12, Width = 60, Minimum = 1, Maximum = 50, Value = 3 };
            Controls.Add(numCount);

            var run = new Button { Text = "実行して画面に反映", Left = 168, Top = 46, Width = 180, Height = 28, Tag = "primary" };
            run.Click += (s, e) => RunIt();
            Controls.Add(run);

            var copy = new Button { Text = "コマンドをコピー", Left = 356, Top = 46, Width = 150, Height = 28 };
            copy.Click += (s, e) => { try { Clipboard.SetText(CommandLine()); } catch { } };
            Controls.Add(copy);

            tbOut = new TextBox
            {
                Left = 14, Top = 86, Width = 690, Height = 330,
                Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false,
                Font = new Font("Consolas", 9.5f),
                Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right | AnchorStyles.Bottom,
            };
            Controls.Add(tbOut);
            AcceptButton = run;
        }

        string CommandLine()
        {
            return "DemoMapDB.exe --near \"" + tbAddress.Text.Trim() + "\" -n " + (int)numCount.Value;
        }

        void RunIt()
        {
            string address = tbAddress.Text.Trim();
            if (address.Length == 0) return;
            var geo = owner.SearchByAddress(address, (int)numCount.Value);
            var sb = new StringBuilder();
            sb.AppendLine("> " + CommandLine()).AppendLine();
            if (!geo.Ok)
            {
                sb.AppendLine("住所から場所を特定できませんでした（終了コード 2）");
            }
            else
            {
                sb.AppendLine(Finder.ToCsv(data, owner.CurrentResults, false));
                sb.AppendLine("（標準エラー出力）検索地点: " +
                    (geo.Matched.Length > 0 ? geo.Matched : geo.Query) + "（" + geo.SourceLabel + " " + geo.Point + "）");
            }
            tbOut.Text = sb.ToString().Replace("\n", "\r\n");
        }
    }
}
