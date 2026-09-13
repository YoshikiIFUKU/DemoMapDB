using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace StoreMapDemo
{
    /// <summary>
    /// 項目（カラム）の設定。ここで増やした項目が、店舗の入力欄・一覧の列・CSVの列・
    /// コマンドラインの変数名（--set 変数名=値）に同時に増える。
    /// </summary>
    public class FieldSettingsForm : Form
    {
        readonly StoreData data;
        DataGridView grid;
        bool addingNew;

        public bool Changed { get; private set; }

        public FieldSettingsForm(StoreData data)
        {
            this.data = data;
            Text = "項目の設定";
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(760, 480);
            MinimumSize = new Size(620, 400);
            Font = new Font("Yu Gothic UI", 9f);
            Theme.Attach(this);

            var head = new Panel { Dock = DockStyle.Top, Height = 54, Tag = "panel" };
            head.Controls.Add(new Label
            {
                Left = 12, Top = 8, Width = 740, Height = 40, Tag = "sub",
                Text = "「一覧・標準出力」の欄をクリックすると、標準出力（CSV / JSON / テキスト）に含めるかを切り替えられます。\r\n" +
                       "店舗名・住所は名前だけ変えられます。順位・距離などの検索結果の項目と店舗名・住所は、画面の一覧には常に表示します。",
            });

            grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                BorderStyle = BorderStyle.FixedSingle,
                EnableHeadersVisualStyles = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,   // 画面の幅いっぱいに列を広げる
                ColumnHeadersHeight = LogicalToDeviceUnits(30),
                RowTemplate = { Height = LogicalToDeviceUnits(26) },
            };
            grid.Columns.Add(C("label", "表示名", 160));
            grid.Columns.Add(C("api", "変数名", 120));
            grid.Columns.Add(C("type", "型", 110));
            grid.Columns.Add(C("list", "一覧・標準出力", 100));
            grid.Columns.Add(C("key", "キー項目", 80));
            grid.Columns.Add(C("req", "必須", 60));
            grid.Columns.Add(C("options", "選択肢", 200));
            grid.CellDoubleClick += (s, e) =>
            {
                if (e.RowIndex >= 0 && grid.Columns[e.ColumnIndex].Name != "list") Edit(Selected());
            };
            grid.CellClick += (s, e) =>
            {
                if (e.RowIndex >= 0 && grid.Columns[e.ColumnIndex].Name == "list") ToggleOutput(e.RowIndex);
            };

            var foot = new Panel { Dock = DockStyle.Bottom, Height = 46, Width = ClientSize.Width };
            var add = new Button { Text = "追加", Left = 12, Top = 9, Width = 76, Height = 28, Tag = "primary" };
            var edit = new Button { Text = "編集", Left = 94, Top = 9, Width = 76, Height = 28 };
            var del = new Button { Text = "削除", Left = 176, Top = 9, Width = 76, Height = 28 };
            var up = new Button { Text = "↑", Left = 262, Top = 9, Width = 40, Height = 28 };
            var down = new Button { Text = "↓", Left = 306, Top = 9, Width = 40, Height = 28 };
            var close = new Button { Text = "閉じる", Left = 640, Top = 9, Width = 100, Height = 28, DialogResult = DialogResult.OK, Anchor = AnchorStyles.Right | AnchorStyles.Top };
            add.Click += (s, e) => { addingNew = true; Edit(null); addingNew = false; };
            edit.Click += (s, e) => Edit(Selected());
            del.Click += (s, e) => Delete();
            up.Click += (s, e) => MoveField(-1);
            down.Click += (s, e) => MoveField(1);
            foot.Controls.AddRange(new Control[] { add, edit, del, up, down, close });

            Controls.Add(grid);
            Controls.Add(foot);
            Controls.Add(head);
            AcceptButton = close;
            CancelButton = close;
            Shown += (s, e) => { Theme.StyleGrid(grid); Reload(); };
            Reload();
        }

        DataGridViewTextBoxColumn C(string name, string header, int width)
        {
            return new DataGridViewTextBoxColumn
            {
                Name = name, HeaderText = header, Width = LogicalToDeviceUnits(width),
                FillWeight = width, MinimumWidth = LogicalToDeviceUnits(Math.Max(40, width * 2 / 3)),
                SortMode = DataGridViewColumnSortMode.NotSortable,
            };
        }

        void Reload()
        {
            int keep = grid.CurrentRow != null ? grid.CurrentRow.Index : -1;
            grid.Rows.Clear();

            // 店舗名・住所は固定の項目。表示名と変数名だけ変えられる
            AddBuiltinRow("name", data.NameLabel, data.NameApi);
            AddBuiltinRow("address", data.AddressLabel, data.AddressApi);

            foreach (var f in data.SortedFields)
            {
                int i = grid.Rows.Add(f.Label, f.ApiName, FieldTypes.Label(f.Type),
                    f.InList ? "○" : "", f.IsKey ? "○" : "", f.Required ? "○" : "",
                    string.Join(" / ", f.OptionList));
                grid.Rows[i].Tag = f;
            }

            // 検索結果だけにある項目（順位・距離など）。標準出力に含めるかだけを切り替えられる
            foreach (var r in StoreData.ResultItems)
            {
                int i = grid.Rows.Add(r.Label, r.JsonKey, "検索結果（固定）", data.GetOutput(r.Key) ? "○" : "", "", "", "");
                grid.Rows[i].Tag = "result:" + r.Key;
                grid.Rows[i].DefaultCellStyle.ForeColor = Theme.SubText;
            }
            if (keep >= 0 && keep < grid.Rows.Count) grid.CurrentCell = grid.Rows[keep].Cells[0];
            Theme.StyleGrid(grid);
        }

        void AddBuiltinRow(string kind, string label, string api)
        {
            int i = grid.Rows.Add(label, api, "テキスト（固定）", data.GetOutput(kind) ? "○" : "", "", kind == "name" ? "○" : "", "");
            grid.Rows[i].Tag = kind;
            grid.Rows[i].DefaultCellStyle.ForeColor = Theme.SubText;
        }

        FieldDef Selected() { return grid.CurrentRow == null ? null : grid.CurrentRow.Tag as FieldDef; }

        /// <summary>選ばれている行が固定項目なら "name" / "address" / "result:rank" など、そうでなければ null</summary>
        string SelectedBuiltin() { return grid.CurrentRow == null ? null : grid.CurrentRow.Tag as string; }

        /// <summary>「一覧・標準出力」の印を付け外しする</summary>
        void ToggleOutput(int rowIndex)
        {
            var tag = grid.Rows[rowIndex].Tag;
            var f = tag as FieldDef;
            string key = tag as string;
            if (f != null) f.InList = !f.InList;
            else if (key != null)
            {
                if (key.StartsWith("result:")) key = key.Substring(7);
                data.SetOutput(key, !data.GetOutput(key));
            }
            else return;
            Changed = true;
            Reload();
            grid.CurrentCell = grid.Rows[rowIndex].Cells[0];
        }

        void Edit(FieldDef target)
        {
            string builtin = SelectedBuiltin();
            if (target == null && builtin != null && !addingNew)
            {
                // 検索結果の項目は名前を変えられないので、標準出力の印だけ切り替える
                if (builtin.StartsWith("result:")) ToggleOutput(grid.CurrentRow.Index);
                else EditBuiltin(builtin);
                return;
            }
            using (var dlg = new FieldEditForm(data, target))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                var r = dlg.Result;
                if (target == null) data.AddField(r);
                else
                {
                    string oldApi = target.ApiName;
                    target.Label = r.Label;
                    target.ApiName = r.ApiName;
                    target.Type = r.Type;
                    target.Options = r.Options;
                    target.Required = r.Required;
                    target.InList = r.InList;
                    target.IsKey = r.IsKey;
                    if (oldApi != r.ApiName)     // 変数名を変えたら、入っている値も付け替える
                        foreach (var s in data.Stores)
                            foreach (var v in s.Values.Where(v => v.Name == oldApi))
                                v.Name = r.ApiName;
                }
                if (r.IsKey)
                    foreach (var f in data.Fields)
                        if (!ReferenceEquals(f, target) && f.ApiName != r.ApiName) f.IsKey = false;
                Changed = true;
                Reload();
            }
        }

        /// <summary>店舗名・住所の表示名と変数名を変える</summary>
        void EditBuiltin(string kind)
        {
            bool isName = kind == "name";
            using (var dlg = new BuiltinFieldEditForm(data, isName))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                if (isName) { data.NameLabel = dlg.NewLabel; data.NameApi = dlg.NewApi; }
                else { data.AddressLabel = dlg.NewLabel; data.AddressApi = dlg.NewApi; }
                Changed = true;
                Reload();
            }
        }

        void Delete()
        {
            if (SelectedBuiltin() != null)
            {
                MessageBox.Show("固定の項目は削除できません。標準出力に出したくない場合は「一覧・標準出力」の印を外してください。",
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var f = Selected();
            if (f == null) return;
            int used = data.Stores.Count(s => s.Get(f.ApiName).Length > 0);
            string msg = "項目「" + f.Label + "」を削除します。よろしいですか？";
            if (used > 0) msg += "\r\n" + used + "件の店舗に入っている値も消えます。";
            if (MessageBox.Show(msg, Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
            data.RemoveField(f);
            Changed = true;
            Reload();
        }

        void MoveField(int delta)
        {
            var f = Selected();
            if (f == null) return;   // 固定項目（店舗名・住所）は先頭のまま動かさない
            var sorted = data.SortedFields;
            int i = sorted.IndexOf(f);
            int j = i + delta;
            if (i < 0 || j < 0 || j >= sorted.Count) return;
            sorted.RemoveAt(i);
            sorted.Insert(j, f);
            for (int k = 0; k < sorted.Count; k++) sorted[k].Sort = k + 1;
            Changed = true;
            Reload();
            grid.CurrentCell = grid.Rows[j + 2].Cells[0];   // 先頭2行は固定項目
        }
    }

    /// <summary>項目1つの定義を編集する</summary>
    public class FieldEditForm : Form
    {
        readonly StoreData data;
        readonly FieldDef original;
        readonly FieldDef work;

        TextBox tbLabel, tbApi, tbOptions;
        ComboBox cbType;
        CheckBox ckList, ckKey, ckRequired;
        Label lbOptions;
        bool apiEdited;

        public FieldDef Result { get { return work; } }

        public FieldEditForm(StoreData data, FieldDef target)
        {
            this.data = data;
            original = target;
            work = target == null ? new FieldDef() : target.Clone();
            apiEdited = target != null;

            Text = target == null ? "項目の追加" : "項目の編集";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ClientSize = new Size(460, 400);
            Font = new Font("Yu Gothic UI", 9f);
            Theme.Attach(this);

            int y = 16;
            Controls.Add(new Label { Text = "表示名", Left = 16, Top = y + 4, Width = 110 });
            tbLabel = new TextBox { Left = 130, Top = y, Width = 300, Text = work.Label };
            tbLabel.TextChanged += (s, e) =>
            {
                if (!apiEdited) tbApi.Text = TextUtil.ToApiName(tbLabel.Text, data.Fields.Count + 1);
            };
            Controls.Add(tbLabel);
            y += 34;

            Controls.Add(new Label { Text = "変数名", Left = 16, Top = y + 4, Width = 110 });
            tbApi = new TextBox { Left = 130, Top = y, Width = 200, Text = work.ApiName };
            tbApi.TextChanged += (s, e) => { if (tbApi.Focused) apiEdited = true; };
            Controls.Add(tbApi);
            Controls.Add(new Label { Left = 336, Top = y + 4, Width = 110, Tag = "sub", Text = "--set で使う名前" });
            y += 34;

            Controls.Add(new Label { Text = "型", Left = 16, Top = y + 4, Width = 110 });
            cbType = new ComboBox { Left = 130, Top = y, Width = 200, DropDownStyle = ComboBoxStyle.DropDownList };
            foreach (var t in FieldTypes.All) cbType.Items.Add(FieldTypes.Label(t));
            cbType.SelectedIndex = Math.Max(0, Array.IndexOf(FieldTypes.All, work.Type));
            cbType.SelectedIndexChanged += (s, e) => UpdateOptionsVisibility();
            Controls.Add(cbType);
            y += 34;

            lbOptions = new Label { Text = "選択肢（1行に1つ）", Left = 16, Top = y + 4, Width = 110 };
            Controls.Add(lbOptions);
            tbOptions = new TextBox
            {
                Left = 130, Top = y, Width = 300, Height = 110, Multiline = true,
                ScrollBars = ScrollBars.Vertical, Text = work.Options.Replace("\n", "\r\n"),
            };
            Controls.Add(tbOptions);
            y += 122;

            ckList = new CheckBox { Text = "一覧と標準出力に含める", Left = 130, Top = y, Width = 300, Checked = work.InList };
            Controls.Add(ckList);
            y += 28;
            ckKey = new CheckBox { Text = "キー項目にする（同じ値の店舗があれば更新）", Left = 130, Top = y, Width = 300, Checked = work.IsKey };
            Controls.Add(ckKey);
            y += 28;
            ckRequired = new CheckBox { Text = "必須にする", Left = 130, Top = y, Width = 300, Checked = work.Required };
            Controls.Add(ckRequired);
            y += 38;

            var ok = new Button { Text = "保存", Left = 260, Top = y, Width = 84, Height = 30, Tag = "primary", DialogResult = DialogResult.OK };
            var cancel = new Button { Text = "キャンセル", Left = 350, Top = y, Width = 84, Height = 30, DialogResult = DialogResult.Cancel };
            ok.Click += (s, e) => { if (!Commit()) DialogResult = DialogResult.None; };
            Controls.Add(ok);
            Controls.Add(cancel);
            AcceptButton = ok;
            CancelButton = cancel;
            ClientSize = new Size(460, y + 46);
            UpdateOptionsVisibility();
        }

        void UpdateOptionsVisibility()
        {
            bool isSelect = FieldTypes.All[cbType.SelectedIndex] == FieldTypes.Select;
            lbOptions.Visible = tbOptions.Visible = isSelect;
        }

        bool Commit()
        {
            string label = tbLabel.Text.Trim();
            string api = tbApi.Text.Trim().ToLowerInvariant();
            if (label.Length == 0) { Warn("表示名を入れてください。", tbLabel); return false; }
            if (api.Length == 0) { Warn("変数名を入れてください。", tbApi); return false; }
            if (!Regex.IsMatch(api, "^[a-z][a-z0-9_]*$"))
            {
                Warn("変数名は英小文字で始まり、英小文字・数字・_ だけが使えます（例: phone、open_hours）。", tbApi);
                return false;
            }
            if (StoreCsv.ReservedLabels(data).Any(r => TextUtil.Norm(r) == TextUtil.Norm(label)))
            {
                Warn("「" + label + "」は固定項目（店舗名・住所・緯度・経度）の名前なので使えません。", tbLabel);
                return false;
            }
            if (data.IsNameName(api) || data.IsAddressName(api) || StoreData.IsResultName(api))
            {
                Warn("「" + api + "」は固定項目の変数名なので使えません。", tbApi);
                return false;
            }
            if (StoreData.IsResultName(label))
            {
                Warn("「" + label + "」は検索結果の項目（順位・距離・方角・距離km・緯度・経度）の名前なので使えません。", tbLabel);
                return false;
            }
            foreach (var f in data.Fields)
            {
                if (ReferenceEquals(f, original)) continue;
                if (f.ApiName == api) { Warn("同じ変数名の項目があります: " + f.Label, tbApi); return false; }
                if (TextUtil.Norm(f.Label) == TextUtil.Norm(label)) { Warn("同じ表示名の項目があります。", tbLabel); return false; }
            }

            work.Label = label;
            work.ApiName = api;
            work.Type = FieldTypes.All[cbType.SelectedIndex];
            work.Options = work.Type == FieldTypes.Select ? tbOptions.Text.Replace("\r\n", "\n").Trim() : "";
            work.InList = ckList.Checked;
            work.IsKey = ckKey.Checked;
            work.Required = ckRequired.Checked;
            return true;
        }

        void Warn(string message, Control focus)
        {
            MessageBox.Show(message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            focus.Focus();
        }
    }

    /// <summary>店舗名・住所の表示名と変数名だけを変える画面</summary>
    public class BuiltinFieldEditForm : Form
    {
        readonly StoreData data;
        readonly bool isName;
        TextBox tbLabel, tbApi;

        public string NewLabel { get; private set; }
        public string NewApi { get; private set; }

        public BuiltinFieldEditForm(StoreData data, bool isName)
        {
            this.data = data;
            this.isName = isName;
            NewLabel = isName ? data.NameLabel : data.AddressLabel;
            NewApi = isName ? data.NameApi : data.AddressApi;

            Text = (isName ? "店舗名" : "住所") + "の名前を変える";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ClientSize = new Size(460, 190);
            Font = new Font("Yu Gothic UI", 9f);
            Theme.Attach(this);

            Controls.Add(new Label
            {
                Left = 16, Top = 14, Width = 424, Height = 34, Tag = "sub",
                Text = (isName ? "店舗名" : "住所") + "は地図に必要なため項目としては固定ですが、" +
                       "画面・CSV・コマンドで使う名前は変えられます。",
            });

            Controls.Add(new Label { Text = "表示名", Left = 16, Top = 62, Width = 110 });
            tbLabel = new TextBox { Left = 130, Top = 58, Width = 300, Text = NewLabel };
            Controls.Add(tbLabel);

            Controls.Add(new Label { Text = "変数名", Left = 16, Top = 96, Width = 110 });
            tbApi = new TextBox { Left = 130, Top = 92, Width = 200, Text = NewApi };
            Controls.Add(tbApi);
            Controls.Add(new Label { Left = 336, Top = 96, Width = 110, Tag = "sub", Text = "--set で使う名前" });

            var ok = new Button { Text = "保存", Left = 260, Top = 138, Width = 84, Height = 30, Tag = "primary" };
            var cancel = new Button { Text = "キャンセル", Left = 350, Top = 138, Width = 84, Height = 30, DialogResult = DialogResult.Cancel };
            ok.Click += (s, e) => { if (Commit()) DialogResult = DialogResult.OK; };
            Controls.Add(ok);
            Controls.Add(cancel);
            AcceptButton = ok;
            CancelButton = cancel;
        }

        bool Commit()
        {
            string label = tbLabel.Text.Trim();
            string api = tbApi.Text.Trim().ToLowerInvariant();
            if (label.Length == 0) return Warn("表示名を入れてください。", tbLabel);
            if (!Regex.IsMatch(api, "^[a-z][a-z0-9_]*$"))
                return Warn("変数名は英小文字で始まり、英小文字・数字・_ だけが使えます（例: name、shop_name）。", tbApi);

            string otherLabel = isName ? data.AddressLabel : data.NameLabel;
            string otherApi = isName ? data.AddressApi : data.NameApi;
            if (TextUtil.Norm(label) == TextUtil.Norm(otherLabel)) return Warn("もう一方の固定項目と同じ表示名は使えません。", tbLabel);
            if (api == otherApi) return Warn("もう一方の固定項目と同じ変数名は使えません。", tbApi);
            if (StoreData.IsResultName(label)) return Warn("検索結果の項目（順位・距離・方角・距離km・緯度・経度）と同じ表示名は使えません。", tbLabel);
            if (StoreData.IsResultName(api)) return Warn("検索結果の項目と同じ変数名は使えません。", tbApi);
            foreach (var f in data.Fields)
            {
                if (TextUtil.Norm(f.Label) == TextUtil.Norm(label)) return Warn("同じ表示名の項目があります: " + f.Label, tbLabel);
                if (f.ApiName == api) return Warn("同じ変数名の項目があります: " + f.Label, tbApi);
            }

            NewLabel = label;
            NewApi = api;
            return true;
        }

        bool Warn(string message, Control focus)
        {
            MessageBox.Show(message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            focus.Focus();
            return false;
        }
    }
}
