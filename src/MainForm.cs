using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace StoreMapDemo
{
    /// <summary>
    /// 画面は2つ。「店舗管理」で登録内容を手入れし、「地図」で検索地点と近い店舗を見る。
    /// 一覧の列と入力欄は、項目の設定（F4）で定義された項目から作る。
    /// コマンドラインで検索されると、約1.2秒で地図へ反映される。
    /// </summary>
    public class MainForm : Form
    {
        readonly StoreStore store;
        StoreData Data { get { return store.Data; } }

        Button tabStores, tabMap;
        Panel pageStores, pageMap, tabBar, searchBar;
        MenuStrip menu;
        SplitContainer mapSplit;
        DataGridView gridStores, gridResult;
        MapPanel map;
        TextBox tbAddress, tbFilter;
        NumericUpDown numCount;
        ComboBox cbFilterField, cbFilterValue;
        CheckBox ckOpenNow;
        Label lbGeoInfo;
        StatusStrip status;
        ToolStripStatusLabel stCount, stQuery, stPath;
        ToolStripMenuItem miOnline, miLight, miDark, miSystem;
        Timer watch;

        GeocodeResult lastGeo = new GeocodeResult();
        List<Nearby> results = new List<Nearby>();
        DateTime queryStamp = DateTime.MinValue;
        bool splitReady;
        Control searchLabel;
        Control[] searchOptions, searchButtons;
        bool layingOut;

        public List<Nearby> CurrentResults { get { return results; } }

        public MainForm(StoreStore store)
        {
            this.store = store;

            Text = "DemoMapDB - 近隣店舗の検索";
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Yu Gothic UI", 9f);
            KeyPreview = true;
            Theme.Attach(this);   // Load で DPI に合わせて拡大する。大きさと位置はそのあと決める

            BuildMenu();
            BuildSearchBar();
            BuildTabs();
            BuildStoresPage();
            BuildMapPage();
            BuildStatus();

            // Dock は後から足したものほど外側になるので、中身 → 端の順に足す
            Controls.Add(pageMap);
            Controls.Add(pageStores);
            Controls.Add(status);
            Controls.Add(tabBar);
            Controls.Add(searchBar);
            Controls.Add(menu);
            MainMenuStrip = menu;

            Theme.Changed += (s, e) => { Theme.Apply(this); RestyleGrids(); map.Invalidate(); };
            KeyDown += OnKey;

            watch = new Timer { Interval = 1200 };
            watch.Tick += (s, e) => CheckExternalChanges();
            watch.Start();

            Load += delegate { ApplyWindowSize(); LayoutSearchBar(); };
            ResizeEnd += delegate { SaveWindowBounds(); };

            RebuildColumns();
            ReloadStores();
            ShowPage(false);
            LoadLastQuery(true);
            if (results.Count == 0) map.FitToAll();
        }

        // ------------------------------------------------------------------ 画面の組み立て

        void BuildMenu()
        {
            var ms = new MenuStrip();

            var file = new ToolStripMenuItem("ファイル(&F)");
            file.DropDownItems.Add(Item("CSVファイルを取り込む…", Keys.Control | Keys.I, (s, e) => ImportCsvFile()));
            file.DropDownItems.Add(Item("CSVを貼り付けて取り込む…", Keys.Control | Keys.Shift | Keys.I, (s, e) => ImportCsvPaste()));
            file.DropDownItems.Add(Item("CSVに書き出す…", Keys.Control | Keys.E, (s, e) => ExportCsv()));
            file.DropDownItems.Add(new ToolStripSeparator());
            file.DropDownItems.Add(Item("バックアップを作る", Keys.None, (s, e) => Backup()));
            file.DropDownItems.Add(Item("データフォルダを開く", Keys.None, (s, e) => OpenDataFolder()));
            file.DropDownItems.Add(new ToolStripSeparator());
            file.DropDownItems.Add(Item("終了", Keys.None, (s, e) => Close()));
            ms.Items.Add(file);

            var data = new ToolStripMenuItem("データ(&D)");
            data.DropDownItems.Add(Item("項目の設定…", Keys.F4, (s, e) => EditFields()));
            data.DropDownItems.Add(new ToolStripSeparator());
            data.DropDownItems.Add(Item("サンプル店舗を投入", Keys.None, (s, e) => Seed()));
            data.DropDownItems.Add(Item("緯度経度が空の店舗を住所から埋める", Keys.None, (s, e) => FillMissing(false)));
            data.DropDownItems.Add(new ToolStripSeparator());
            data.DropDownItems.Add(Item("店舗をすべて削除", Keys.None, (s, e) => DeleteAll()));
            ms.Items.Add(data);

            var view = new ToolStripMenuItem("表示(&V)");
            view.DropDownItems.Add(Item("店舗管理", Keys.F7, (s, e) => ShowPage(false)));
            view.DropDownItems.Add(Item("地図", Keys.F8, (s, e) => ShowPage(true)));
            view.DropDownItems.Add(new ToolStripSeparator());
            miLight = Item("ライト", Keys.None, (s, e) => Theme.Set(ThemeMode.Light));
            miDark = Item("ダーク", Keys.None, (s, e) => Theme.Set(ThemeMode.Dark));
            miSystem = Item("Windows の設定に合わせる", Keys.None, (s, e) => Theme.Set(ThemeMode.System));
            view.DropDownItems.Add(miLight);
            view.DropDownItems.Add(miDark);
            view.DropDownItems.Add(miSystem);
            view.DropDownOpening += (s, e) =>
            {
                miLight.Checked = Theme.Mode == ThemeMode.Light;
                miDark.Checked = Theme.Mode == ThemeMode.Dark;
                miSystem.Checked = Theme.Mode == ThemeMode.System;
            };
            ms.Items.Add(view);

            var setting = new ToolStripMenuItem("設定(&S)");
            miOnline = Item("住所の変換にオンライン検索を使う", Keys.None, (s, e) =>
            {
                bool now = !Settings.GetBool("online_geocoding", true);
                Settings.Set("online_geocoding", now ? "1" : "0");
                miOnline.Checked = now;
                SetGeoInfo(now
                    ? "オンライン検索を使います（国土地理院の住所検索）"
                    : "内蔵の住所表だけで変換します（市区町村の代表点）");
            });
            miOnline.Checked = Settings.GetBool("online_geocoding", true);
            setting.DropDownItems.Add(miOnline);
            ms.Items.Add(setting);

            var help = new ToolStripMenuItem("ヘルプ(&H)");
            help.DropDownItems.Add(Item("コマンドラインの使い方", Keys.F1, (s, e) => new HelpForm(store.FilePath, Data).ShowDialog(this)));
            help.DropDownItems.Add(Item("連携コマンドのテスト", Keys.F2, (s, e) =>
                new CommandTestForm(this, Data, tbAddress.Text.Trim()).ShowDialog(this)));
            ms.Items.Add(help);

            menu = ms;
        }

        /// <summary>
        /// 大きさと位置を決める（DPI に合わせた拡大のあとに呼ぶ）。
        /// 前回の位置が今の画面に収まればそれを使い、無ければ作業領域（タスクバーを除く）の 92% までで中央に置く。
        /// </summary>
        void ApplyWindowSize()
        {
            var work = Screen.FromPoint(Cursor.Position).WorkingArea;
            MinimumSize = new Size(Math.Min(LogicalToDeviceUnits(820), work.Width),
                                   Math.Min(LogicalToDeviceUnits(560), work.Height));
            if (RestoreWindowBounds()) return;

            var size = new Size(Math.Min(LogicalToDeviceUnits(1260), (int)(work.Width * 0.92)),
                                Math.Min(LogicalToDeviceUnits(800), (int)(work.Height * 0.92)));
            Bounds = new Rectangle(work.Left + (work.Width - size.Width) / 2,
                                   work.Top + (work.Height - size.Height) / 2, size.Width, size.Height);
        }

        /// <summary>前回の大きさ・位置を復元する（いまつながっている画面に収まるときだけ）</summary>
        bool RestoreWindowBounds()
        {
            try
            {
                var saved = Settings.GetRect("window");
                if (saved.Width <= 0 || saved.Height <= 0) return false;
                foreach (Screen sc in Screen.AllScreens)
                {
                    if (!sc.WorkingArea.IntersectsWith(saved)) continue;
                    var work = sc.WorkingArea;
                    var size = new Size(Math.Max(MinimumSize.Width, Math.Min(saved.Width, work.Width)),
                                        Math.Max(MinimumSize.Height, Math.Min(saved.Height, work.Height)));
                    var at = new Point(Math.Max(work.Left, Math.Min(saved.X, work.Right - size.Width)),
                                       Math.Max(work.Top, Math.Min(saved.Y, work.Bottom - size.Height)));
                    Bounds = new Rectangle(at, size);
                    if (Settings.GetBool("window_max", false)) WindowState = FormWindowState.Maximized;
                    return true;
                }
            }
            catch { }
            return false;
        }

        void SaveWindowBounds()
        {
            try
            {
                Settings.Set("window_max", WindowState == FormWindowState.Maximized ? "1" : "0");
                if (WindowState == FormWindowState.Normal) Settings.SetRect("window", Bounds);
            }
            catch { }
        }

        static ToolStripMenuItem Item(string text, Keys keys, EventHandler onClick)
        {
            var mi = new ToolStripMenuItem(text, null, onClick);
            if (keys != Keys.None) mi.ShortcutKeys = keys;
            return mi;
        }

        void BuildSearchBar()
        {
            var bar = new Panel { Dock = DockStyle.Top, Height = 74, Tag = "panel", Padding = new Padding(12, 8, 12, 8) };

            var lb = new Label { Text = "住所", Left = 12, Top = 12, AutoSize = true, Tag = "accent", Font = new Font("Yu Gothic UI", 9f, FontStyle.Bold) };
            tbAddress = new TextBox { Left = 48, Top = 8, Width = 400, Font = new Font("Yu Gothic UI", 10.5f) };
            tbAddress.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; DoSearch(); } };

            var lbN = new Label { Text = "件数", Left = 460, Top = 12, AutoSize = true };
            numCount = new NumericUpDown { Left = 496, Top = 8, Width = 56, Minimum = 1, Maximum = 50, Value = Settings.GetInt("count", 3) };

            var lbC = new Label { Text = "しぼり込み", Left = 566, Top = 12, AutoSize = true };
            cbFilterField = new ComboBox { Left = 634, Top = 8, Width = 110, DropDownStyle = ComboBoxStyle.DropDownList };
            cbFilterValue = new ComboBox { Left = 748, Top = 8, Width = 130, DropDownStyle = ComboBoxStyle.DropDown };
            cbFilterField.SelectedIndexChanged += (s, e) => ReloadFilterValues();

            ckOpenNow = new CheckBox
            {
                Text = "営業時間内のみ", Left = 886, Top = 11, AutoSize = true,
                Checked = Settings.GetBool("open_now", false),
            };
            ckOpenNow.CheckedChanged += (s, e) => Settings.Set("open_now", ckOpenNow.Checked ? "1" : "0");

            var btn = new Button { Text = "近い店舗を探す", Left = 1012, Top = 6, Width = 140, Height = 28, Tag = "primary" };
            btn.Click += (s, e) => DoSearch();

            var btnClear = new Button { Text = "クリア", Left = 1160, Top = 6, Width = 70, Height = 28 };
            btnClear.Click += (s, e) =>
            {
                tbAddress.Text = "";
                cbFilterValue.Text = "";
                results = new List<Nearby>();
                lastGeo = new GeocodeResult();
                map.ClearResult();
                map.FitToAll();
                FillResultGrid();
                SetGeoInfo("");
                stQuery.Text = "";
            };

            lbGeoInfo = new Label { Left = 48, Top = 44, Width = 1080, Tag = "sub", AutoEllipsis = true };

            bar.Controls.AddRange(new Control[]
                { lb, tbAddress, lbN, numCount, lbC, cbFilterField, cbFilterValue, ckOpenNow, btn, btnClear, lbGeoInfo });
            bar.Resize += delegate { LayoutSearchBar(); };
            searchBar = bar;
            searchLabel = lb;
            searchOptions = new Control[] { lbN, numCount, lbC, cbFilterField, cbFilterValue, ckOpenNow };
            searchButtons = new Control[] { btn, btnClear };
        }

        /// <summary>
        /// 検索バーは幅に合わせて並べ直す。
        /// 広いときは1段（住所・件数・しぼり込み・ボタン）、狭いときは2段（1段目: 住所とボタン / 2段目: 件数・しぼり込み）。
        /// 住所欄は残りの幅を埋める。部品の大きさは実際の値（DPIで拡大済み）から計算する。
        /// </summary>
        void LayoutSearchBar()
        {
            if (searchBar == null || searchOptions == null || layingOut) return;
            layingOut = true;
            try
            {
                var pad = searchBar.Padding;
                int gap = Math.Max(4, pad.Left / 2);
                int width = searchBar.ClientSize.Width;
                int rowH = Math.Max(tbAddress.Height, searchButtons.Max(c => c.Height));
                int minAddress = LogicalToDeviceUnits(260);

                searchLabel.Left = pad.Left;
                int addressLeft = searchLabel.Right + gap;

                int optionsW = WidthOf(searchOptions, gap);
                int buttonsW = WidthOf(searchButtons, gap);
                bool oneRow = width - pad.Right - addressLeft - (optionsW + gap * 2 + buttonsW) - gap >= minAddress;

                int row1 = pad.Top;
                // ボタンは1段目の右端
                int right = PlaceRightToLeft(searchButtons, width - pad.Right, row1, rowH, gap);
                int nextTop;
                if (oneRow)
                {
                    right = PlaceRightToLeft(searchOptions, right - gap * 2, row1, rowH, gap);
                    nextTop = row1 + rowH;
                }
                else
                {
                    // 2段目に件数・しぼり込みを左から並べる
                    int row2 = row1 + rowH + gap;
                    int x = addressLeft;
                    foreach (var c in searchOptions)
                    {
                        c.Left = x;
                        c.Top = row2 + (rowH - c.Height) / 2;
                        x = c.Right + (c is Label ? gap / 2 : gap + gap / 2);
                    }
                    nextTop = row2 + rowH;
                }

                CenterInRow(searchLabel, row1, rowH);
                tbAddress.Left = addressLeft;
                tbAddress.Top = row1 + (rowH - tbAddress.Height) / 2;
                tbAddress.Width = Math.Max(LogicalToDeviceUnits(120), right - gap - addressLeft);

                lbGeoInfo.Left = addressLeft;
                lbGeoInfo.Top = nextTop + gap;
                lbGeoInfo.Width = Math.Max(LogicalToDeviceUnits(120), width - pad.Right - addressLeft);

                int height = lbGeoInfo.Bottom + pad.Bottom;
                if (searchBar.Height != height) searchBar.Height = height;
            }
            finally { layingOut = false; }
        }

        static int WidthOf(Control[] controls, int gap)
        {
            int w = 0;
            foreach (var c in controls) w += c.Width + (c is Label ? gap / 2 : gap + gap / 2);
            return w;
        }

        /// <summary>右端 right から左へ詰めて並べ、いちばん左の部品の左端を返す</summary>
        static int PlaceRightToLeft(Control[] controls, int right, int rowTop, int rowH, int gap)
        {
            int left = right;
            for (int i = controls.Length - 1; i >= 0; i--)
            {
                var c = controls[i];
                c.Left = right - c.Width;
                c.Top = rowTop + (rowH - c.Height) / 2;
                left = c.Left;
                // 左隣がラベルなら、この部品の見出しなので詰めて置く
                right = c.Left - (i > 0 && controls[i - 1] is Label ? gap / 2 : gap + gap / 2);
            }
            return left;
        }

        static void CenterInRow(Control c, int rowTop, int rowH)
        {
            c.Top = rowTop + (rowH - c.Height) / 2;
        }

        void BuildTabs()
        {
            var tabs = new Panel { Dock = DockStyle.Top, Height = 34, Tag = "tabbar" };
            tabStores = new Button { Text = "  店舗管理  ", Left = 12, Top = 3, Width = 120, Height = 28, Tag = "tab-active" };
            tabMap = new Button { Text = "  地図  ", Left = 136, Top = 3, Width = 120, Height = 28, Tag = "tab" };
            tabStores.Click += (s, e) => ShowPage(false);
            tabMap.Click += (s, e) => ShowPage(true);
            tabs.Controls.Add(tabStores);
            tabs.Controls.Add(tabMap);
            tabBar = tabs;
        }

        void BuildStoresPage()
        {
            pageStores = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 10, 12, 10) };

            // 幅が足りないときは折り返す（狭い画面・高DPIでもボタンが切れない）
            var top = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = true,
                Padding = new Padding(0, 0, 0, 6),
            };
            var lb = new Label { Text = "絞り込み", AutoSize = true, Margin = new Padding(0, 9, 4, 3) };
            tbFilter = new TextBox { Width = 220, Margin = new Padding(0, 5, 16, 3) };
            tbFilter.TextChanged += (s, e) => ReloadStores();

            var add = new Button { Text = "追加", Width = 76, Height = 27, Tag = "primary", Margin = new Padding(0, 4, 6, 3) };
            var edit = new Button { Text = "編集", Width = 76, Height = 27, Margin = new Padding(0, 4, 6, 3) };
            var del = new Button { Text = "削除", Width = 76, Height = 27, Margin = new Padding(0, 4, 6, 3) };
            var onMap = new Button { Text = "地図で見る", Width = 100, Height = 27, Margin = new Padding(0, 4, 10, 3) };
            var paste = new Button { Text = "CSVを貼り付けて取り込む", Width = 180, Height = 27, Margin = new Padding(0, 4, 10, 3) };
            var fields = new Button { Text = "項目の設定", Width = 100, Height = 27, Margin = new Padding(0, 4, 0, 3) };
            add.Click += (s, e) => EditStore(null);
            edit.Click += (s, e) => EditStore(SelectedStore());
            del.Click += (s, e) => DeleteStore();
            onMap.Click += (s, e) => ShowOnMap(SelectedStore());
            paste.Click += (s, e) => ImportCsvPaste();
            fields.Click += (s, e) => EditFields();
            top.Controls.AddRange(new Control[] { lb, tbFilter, add, edit, del, onMap, paste, fields });

            gridStores = NewGrid();
            gridStores.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0) EditStore(SelectedStore()); };

            pageStores.Controls.Add(gridStores);
            pageStores.Controls.Add(top);
        }

        void BuildMapPage()
        {
            pageMap = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 10, 12, 10), Visible = false };

            var split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterWidth = 6,
                FixedPanel = FixedPanel.Panel1,   // 幅を変えても一覧の幅は保つ
            };
            mapSplit = split;

            gridResult = NewGrid();
            gridResult.SelectionChanged += (s, e) => map.Invalidate();
            gridResult.CellDoubleClick += (s, e) =>
            {
                var st = SelectedResultStore();
                if (st != null) EditStore(st);
            };

            map = new MapPanel { Dock = DockStyle.Fill };
            map.StoreClicked += (s, e) => SelectResultRow(e.Store);

            var mapTools = new Panel { Dock = DockStyle.Bottom, Height = 36 };
            var fitResult = new Button { Text = "検索結果に合わせる", Left = 0, Top = 4, Width = 150, Height = 27 };
            var fitAll = new Button { Text = "全店舗を表示", Left = 158, Top = 4, Width = 120, Height = 27 };
            var copy = new Button { Text = "結果をコピー", Left = 286, Top = 4, Width = 110, Height = 27 };
            fitResult.Click += (s, e) => map.FitToResult();
            fitAll.Click += (s, e) => map.FitToAll();
            copy.Click += (s, e) =>
            {
                if (results.Count == 0) return;
                try { Clipboard.SetText(Finder.ToCsv(Data, results, false)); } catch { }
            };
            mapTools.Controls.AddRange(new Control[] { fitResult, fitAll, copy });

            split.Panel1.Controls.Add(gridResult);
            split.Panel2.Controls.Add(map);
            split.Panel2.Controls.Add(mapTools);
            pageMap.Controls.Add(split);
        }

        void BuildStatus()
        {
            status = new StatusStrip();
            stCount = new ToolStripStatusLabel("");
            stQuery = new ToolStripStatusLabel("") { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
            stPath = new ToolStripStatusLabel(store.FilePath);
            status.Items.Add(stCount);
            status.Items.Add(stQuery);
            status.Items.Add(stPath);
        }

        DataGridView NewGrid()
        {
            return new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                ReadOnly = true,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                BorderStyle = BorderStyle.FixedSingle,
                EnableHeadersVisualStyles = false,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                // 表の列幅・行の高さは画面の自動拡大の対象外なので、DPI の倍率を自分で掛ける
                ColumnHeadersHeight = LogicalToDeviceUnits(30),
                RowTemplate = { Height = LogicalToDeviceUnits(26) },
            };
        }

        DataGridViewTextBoxColumn Col(string name, string header, int width)
        {
            return new DataGridViewTextBoxColumn
            {
                Name = name, HeaderText = header, Width = LogicalToDeviceUnits(width),
                SortMode = DataGridViewColumnSortMode.NotSortable,
            };
        }

        /// <summary>項目の設定が変わったら、一覧の列を作り直す</summary>
        void RebuildColumns()
        {
            gridStores.Rows.Clear();
            gridStores.Columns.Clear();
            gridStores.Columns.Add(Col("name", Data.NameLabel, 180));
            gridStores.Columns.Add(Col("address", Data.AddressLabel, 260));
            foreach (var f in Data.ListFields) gridStores.Columns.Add(Col("f_" + f.ApiName, f.Label, 110));
            gridStores.Columns.Add(Col("latlng", "緯度・経度", 150));

            gridResult.Rows.Clear();
            gridResult.Columns.Clear();
            gridResult.Columns.Add(Col("rank", "順位", 46));
            gridResult.Columns.Add(Col("name", Data.NameLabel, 150));
            gridResult.Columns.Add(Col("dist", "距離", 70));
            gridResult.Columns.Add(Col("dir", "方角", 46));
            gridResult.Columns.Add(Col("address", Data.AddressLabel, 210));
            foreach (var f in Data.ListFields) gridResult.Columns.Add(Col("f_" + f.ApiName, f.Label, 100));
            if (Data.HoursFields.Count > 0) gridResult.Columns.Add(Col("open", "営業", 60));

            // しぼり込みに使える項目
            string keep = cbFilterField.SelectedItem as string;
            cbFilterField.Items.Clear();
            cbFilterField.Items.Add("（なし）");
            foreach (var f in Data.SortedFields) cbFilterField.Items.Add(f.Label);
            int idx = keep == null ? 0 : Math.Max(0, cbFilterField.Items.IndexOf(keep));
            cbFilterField.SelectedIndex = idx;
            RestyleGrids();
        }

        void ReloadFilterValues()
        {
            var f = FilterField();
            string keep = cbFilterValue.Text;
            cbFilterValue.Items.Clear();
            cbFilterValue.Items.Add("");
            if (f != null)
            {
                var values = f.Type == FieldTypes.Select && f.OptionList.Length > 0
                    ? f.OptionList
                    : Data.Stores.Select(s => s.Get(f.ApiName)).Where(v => v.Length > 0).Distinct().OrderBy(v => v).ToArray();
                cbFilterValue.Items.AddRange(values);
            }
            cbFilterValue.Enabled = f != null;
            cbFilterValue.Text = f == null ? "" : keep;
        }

        FieldDef FilterField()
        {
            if (cbFilterField.SelectedIndex <= 0) return null;
            return Data.FieldByLabel(cbFilterField.SelectedItem as string);
        }

        void RestyleGrids()
        {
            Theme.StyleGrid(gridStores);
            Theme.StyleGrid(gridResult);
        }

        void ShowPage(bool mapPage)
        {
            pageStores.Visible = !mapPage;
            pageMap.Visible = mapPage;
            tabStores.Tag = mapPage ? "tab" : "tab-active";
            tabMap.Tag = mapPage ? "tab-active" : "tab";
            Theme.StyleButton(tabStores);
            Theme.StyleButton(tabMap);
            if (mapPage)
            {
                // 一覧の幅は、地図の画面を初めて開いたとき（大きさが決まったあと）に決める
                if (!splitReady && mapSplit.Width > 200)
                {
                    splitReady = true;
                    try
                    {
                        mapSplit.SplitterDistance = Math.Max(LogicalToDeviceUnits(320),
                            Math.Min(LogicalToDeviceUnits(620), mapSplit.Width / 2 - LogicalToDeviceUnits(40)));
                    }
                    catch { }
                }
                map.Focus();
            }
        }

        // ------------------------------------------------------------------ 一覧

        void ReloadStores()
        {
            string kw = TextUtil.Norm(tbFilter == null ? "" : tbFilter.Text);
            var fields = Data.ListFields;
            var list = Data.Stores
                .Where(s => kw.Length == 0 || TextUtil.Norm(Data.SearchText(s)).Contains(kw))
                .OrderBy(s => s.Name)
                .ToList();

            int keep = gridStores.CurrentRow != null ? gridStores.CurrentRow.Index : -1;
            gridStores.Rows.Clear();
            foreach (var s in list)
            {
                var cells = new List<object> { s.Name, s.Address };
                cells.AddRange(fields.Select(f => (object)s.Get(f.ApiName)));
                cells.Add(s.HasLocation
                    ? s.Lat.ToString("0.#####", CultureInfo.InvariantCulture) + ", " +
                      s.Lng.ToString("0.#####", CultureInfo.InvariantCulture)
                    : "（未設定）");
                int i = gridStores.Rows.Add(cells.ToArray());
                gridStores.Rows[i].Tag = s;
                if (!s.HasLocation) gridStores.Rows[i].Cells["latlng"].Style.ForeColor = Theme.Warn;
            }
            if (keep >= 0 && keep < gridStores.Rows.Count) gridStores.CurrentCell = gridStores.Rows[keep].Cells[0];

            ReloadFilterValues();
            map.SetStores(Data);
            int missing = Data.Stores.Count(s => !s.HasLocation);
            stPath.Text = store.FilePath;
            stCount.Text = "店舗 " + Data.Stores.Count + "件" + (missing > 0 ? "（緯度経度なし " + missing + "件）" : "");
            RestyleGrids();
        }

        Store SelectedStore()
        {
            return gridStores.CurrentRow == null ? null : gridStores.CurrentRow.Tag as Store;
        }

        Store SelectedResultStore()
        {
            return gridResult.CurrentRow == null ? null : gridResult.CurrentRow.Tag as Store;
        }

        void SelectResultRow(Store s)
        {
            foreach (DataGridViewRow row in gridResult.Rows)
                if (ReferenceEquals(row.Tag, s)) { gridResult.CurrentCell = row.Cells[0]; return; }
        }

        void FillResultGrid()
        {
            var fields = Data.ListFields;
            bool hasHours = Data.HoursFields.Count > 0;
            var at = DateTime.Now;
            gridResult.Rows.Clear();
            foreach (var n in results)
            {
                var cells = new List<object> { n.Rank, n.Store.Name, n.DistanceText, n.Direction, n.Store.Address };
                cells.AddRange(fields.Select(f => (object)n.Store.Get(f.ApiName)));
                var state = hasHours ? Data.OpenStateOf(n.Store, at) : OpenState.Unknown;
                if (hasHours) cells.Add(OpeningHours.Label(state));
                int i = gridResult.Rows.Add(cells.ToArray());
                gridResult.Rows[i].Tag = n.Store;
                if (hasHours && state != OpenState.Unknown)
                    gridResult.Rows[i].Cells["open"].Style.ForeColor =
                        state == OpenState.Open ? Theme.Success : Theme.Muted;
            }
            RestyleGrids();
        }

        // ------------------------------------------------------------------ 検索

        SearchOptions CurrentOptions(int count)
        {
            var opt = new SearchOptions { Count = count };
            if (ckOpenNow.Checked) opt.OpenAt = DateTime.Now;
            var f = FilterField();
            if (f != null && cbFilterValue.Text.Trim().Length > 0)
                opt.FieldFilters[f.ApiName] = cbFilterValue.Text.Trim();
            return opt;
        }

        void DoSearch()
        {
            string address = tbAddress.Text.Trim();
            if (address.Length == 0) { tbAddress.Focus(); return; }
            Settings.Set("count", ((int)numCount.Value).ToString());
            SearchByAddress(address, (int)numCount.Value);
        }

        /// <summary>住所で検索して画面に反映する（連携テスト画面からも使う）</summary>
        public GeocodeResult SearchByAddress(string address, int count)
        {
            Cursor = Cursors.WaitCursor;
            SetGeoInfo("住所を調べています…");
            Application.DoEvents();
            try
            {
                var geo = Geocoder.Resolve(address, Data, Settings.GetBool("online_geocoding", true));
                lastGeo = geo;
                if (!geo.Ok)
                {
                    results = new List<Nearby>();
                    FillResultGrid();
                    map.ClearResult();
                    SetGeoInfo("住所から場所を特定できませんでした: " + address, true);
                    return geo;
                }

                results = Finder.Nearest(Data, geo.Point, CurrentOptions(count));
                tbAddress.Text = address;
                numCount.Value = Clamp(count);
                FillResultGrid();
                map.SetStores(Data);
                map.SetResult(geo.Point, geo.Matched.Length > 0 ? geo.Matched : geo.Query, results);
                ShowPage(true);
                if (gridResult.Rows.Count > 0) gridResult.CurrentCell = gridResult.Rows[0].Cells[0];

                SetGeoInfo("検索地点: " + (geo.Matched.Length > 0 ? geo.Matched : geo.Query) +
                           "　（" + geo.SourceLabel + " / " + geo.Point + "）　" +
                           (results.Count > 0 ? "近い順に " + results.Count + "件" : Cli.NotFoundMessage));
                stQuery.Text = DateTime.Now.ToString("HH:mm:ss") + "  " + address + " → " + results.Count + "件";
                return geo;
            }
            finally { Cursor = Cursors.Default; }
        }

        decimal Clamp(int count)
        {
            return Math.Max(numCount.Minimum, Math.Min(numCount.Maximum, count));
        }

        void SetGeoInfo(string text, bool warn = false)
        {
            lbGeoInfo.Text = text;
            lbGeoInfo.Tag = warn ? "danger" : "sub";
            lbGeoInfo.ForeColor = warn ? Theme.Danger : Theme.SubText;
        }

        void ShowOnMap(Store s)
        {
            if (s == null) return;
            if (!s.HasLocation)
            {
                MessageBox.Show("この店舗には緯度経度が設定されていません。\r\n編集画面の［住所から緯度経度を取得］で設定できます。",
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            tbAddress.Text = s.Address;
            SearchByAddress(s.Address.Length > 0 ? s.Address : new GeoPoint(s.Lat, s.Lng).ToString(), (int)numCount.Value);
        }

        // ------------------------------------------------------------------ コマンドラインからの反映

        void CheckExternalChanges()
        {
            try
            {
                if (store.ChangedOnDisk)
                {
                    store.Load();
                    RebuildColumns();
                    ReloadStores();
                }
                LoadLastQuery(false);
            }
            catch { }   // 他プロセスが書き込み中なら次回に拾う
        }

        /// <summary>コマンドラインの検索結果（demomapdb_query.xml）を読み、地図に反映する</summary>
        void LoadLastQuery(bool initial)
        {
            var q = LastQuery.Load(store.FilePath);
            if (q == null || q.At <= queryStamp) return;
            queryStamp = q.At;
            if (initial && (DateTime.Now - q.At).TotalHours > 12) return;   // 起動時は古すぎる結果を出さない

            var origin = new GeoPoint(q.Lat, q.Lng);
            if (origin.IsEmpty) return;

            results = Finder.Nearest(Data, origin, q.ToOptions());
            lastGeo = new GeocodeResult { Query = q.Query, Matched = q.Matched, Source = q.Source, Point = origin };

            tbAddress.Text = q.Query;
            numCount.Value = Clamp(q.Count);
            ckOpenNow.Checked = q.OpenAt != default(DateTime);
            ShowFilterFromQuery(q);
            FillResultGrid();
            map.SetStores(Data);
            map.SetResult(origin, q.Matched.Length > 0 ? q.Matched : q.Query, results);
            if (gridResult.Rows.Count > 0) gridResult.CurrentCell = gridResult.Rows[0].Cells[0];

            SetGeoInfo("検索地点: " + (q.Matched.Length > 0 ? q.Matched : q.Query) +
                       "　（" + lastGeo.SourceLabel + " / " + origin + "）　近い順に " + results.Count + "件");
            stQuery.Text = q.At.ToString("HH:mm:ss") + "  " + (initial ? "" : "コマンドラインから受信  ") +
                           q.Query + " → " + results.Count + "件";
            if (!initial)
            {
                ShowPage(true);
                Flash();
            }
        }

        /// <summary>コマンドラインで指定されたしぼり込みを、検索バーにも映す</summary>
        void ShowFilterFromQuery(LastQuery q)
        {
            var first = q.Filters.FirstOrDefault();
            if (first == null)
            {
                cbFilterField.SelectedIndex = 0;
                cbFilterValue.Text = "";
                return;
            }
            var f = Data.FieldByApi(first.Name);
            if (f == null) return;
            int idx = cbFilterField.Items.IndexOf(f.Label);
            if (idx >= 0) cbFilterField.SelectedIndex = idx;
            cbFilterValue.Text = first.Value;
        }

        /// <summary>通話中に呼ばれたとき、最小化されていても気づけるように前面へ出す</summary>
        void Flash()
        {
            try
            {
                if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
                Activate();
            }
            catch { }
        }

        // ------------------------------------------------------------------ 店舗・項目の編集

        void EditStore(Store target)
        {
            using (var dlg = new StoreEditForm(target, Data))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                if (target == null) Data.Add(dlg.Result);
                else
                {
                    var s = Data.ById(target.Id);
                    if (s == null) return;
                    var r = dlg.Result;
                    s.Name = r.Name; s.Address = r.Address;
                    s.Lat = r.Lat; s.Lng = r.Lng;
                    s.Values = r.Values;
                    s.UpdatedAt = r.UpdatedAt;
                }
                store.Save();
                ReloadStores();
                RefreshResults();
            }
        }

        void EditFields()
        {
            using (var dlg = new FieldSettingsForm(Data))
            {
                dlg.ShowDialog(this);
                if (!dlg.Changed) return;
                store.Save();
                RebuildColumns();
                ReloadStores();
                RefreshResults();
            }
        }

        void DeleteStore()
        {
            var s = SelectedStore();
            if (s == null) return;
            if (MessageBox.Show("「" + s.Name + "」を削除します。よろしいですか？", Text,
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
            Data.Remove(s);
            store.Save();
            ReloadStores();
            RefreshResults();
        }

        /// <summary>店舗を直したあと、表示中の検索結果を同じ条件で計算し直す</summary>
        void RefreshResults()
        {
            if (!lastGeo.Ok) { map.SetStores(Data); return; }
            results = Finder.Nearest(Data, lastGeo.Point, CurrentOptions((int)numCount.Value));
            FillResultGrid();
            map.SetStores(Data);
            map.SetResult(lastGeo.Point, lastGeo.Matched.Length > 0 ? lastGeo.Matched : lastGeo.Query, results);
        }

        // ------------------------------------------------------------------ データ管理

        void Seed()
        {
            if (MessageBox.Show("デモ用のサンプル店舗を追加します。よろしいですか？", Text,
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            Data.Seed();
            store.Save();
            Load += delegate { ApplyWindowSize(); LayoutSearchBar(); };
            ResizeEnd += delegate { SaveWindowBounds(); };

            RebuildColumns();
            ReloadStores();
            map.FitToAll();
        }

        void FillMissing(bool skipConfirm = false)
        {
            var targets = Data.Stores.Where(s => !s.HasLocation && s.Address.Trim().Length > 0).ToList();
            if (targets.Count == 0)
            {
                if (!skipConfirm)
                    MessageBox.Show("緯度経度が空の店舗はありません。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (!skipConfirm && MessageBox.Show(targets.Count + "件の店舗の緯度経度を、住所から取得します。よろしいですか？", Text,
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;

            Cursor = Cursors.WaitCursor;
            int done = 0;
            bool online = Settings.GetBool("online_geocoding", true);
            try
            {
                foreach (var s in targets)
                {
                    var geo = Geocoder.Resolve(s.Address, Data, online);
                    if (!geo.Ok) continue;
                    s.Lat = geo.Point.Lat; s.Lng = geo.Point.Lng; s.UpdatedAt = DateTime.Now;
                    done++;
                }
                if (done > 0) store.Save();
            }
            finally { Cursor = Cursors.Default; }

            ReloadStores();
            RefreshResults();
            MessageBox.Show(done + "件を設定しました。（残り " + (targets.Count - done) + "件）", Text,
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        void DeleteAll()
        {
            if (Data.Stores.Count == 0) return;
            if (MessageBox.Show("登録されている店舗をすべて削除します。よろしいですか？\r\n（直前の内容は " +
                    StoreStore.FileName + ".bak に残ります。項目の設定はそのままです）", Text,
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
            Data.Stores.Clear();
            store.Save();
            results = new List<Nearby>();
            lastGeo = new GeocodeResult();
            map.ClearResult();
            FillResultGrid();
            ReloadStores();
        }

        void ImportCsvFile()
        {
            using (var dlg = new OpenFileDialog { Filter = "CSV / テキスト (*.csv;*.txt)|*.csv;*.txt|すべてのファイル (*.*)|*.*" })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                int added, updated, skipped;
                var text = File.ReadAllText(dlg.FileName, Cli.DetectEncoding(dlg.FileName));
                var errors = StoreCsv.Import(Data, text, out added, out updated, out skipped);
                store.Save();
                ReloadStores();
                RefreshResults();
                var msg = new StringBuilder("追加 " + added + "件 / 更新 " + updated + "件 / 読み飛ばし " + skipped + "件");
                foreach (var e in errors.Take(10)) msg.AppendLine().Append(e);
                MessageBox.Show(msg.ToString(), Text, MessageBoxButtons.OK,
                    errors.Count > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
                OfferFillMissing();
            }
        }

        void ImportCsvPaste()
        {
            using (var dlg = new CsvPasteForm(Data))
            {
                dlg.ShowDialog(this);
                if (!dlg.Imported) return;
                store.Save();
                ReloadStores();
                RefreshResults();
                OfferFillMissing();
            }
        }

        /// <summary>取り込んだ店舗に緯度経度が無いと地図に出ないので、その場で取得をすすめる</summary>
        void OfferFillMissing()
        {
            int missing = Data.Stores.Count(s => !s.HasLocation && s.Address.Trim().Length > 0);
            if (missing == 0) return;
            if (MessageBox.Show(missing + "件の店舗に緯度経度がありません（地図に出ません）。" +
                    Environment.NewLine + "住所から取得しますか？",
                    Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            FillMissing(true);
        }

        void ExportCsv()
        {
            using (var dlg = new SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = "stores.csv" })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                File.WriteAllText(dlg.FileName, StoreCsv.Export(Data, Data.Stores), new UTF8Encoding(true));
                MessageBox.Show(Data.Stores.Count + "件を書き出しました。", Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        void Backup()
        {
            try
            {
                string dest = store.CreateBackup();
                MessageBox.Show("バックアップを作りました。\r\n" + dest, Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("バックアップに失敗しました。\r\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        void OpenDataFolder()
        {
            try { Process.Start("explorer.exe", "/select,\"" + store.FilePath + "\""); }
            catch { }
        }

        void OnKey(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.F5) { store.Load(); RebuildColumns(); ReloadStores(); RefreshResults(); }
            else if (e.Control && e.KeyCode == Keys.F) { tbAddress.Focus(); tbAddress.SelectAll(); }
            else if (e.Control && e.KeyCode == Keys.N) { ShowPage(false); EditStore(null); }
            else return;
            e.Handled = true;
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            SaveWindowBounds();
            watch.Stop();
            base.OnFormClosed(e);
        }
    }
}
