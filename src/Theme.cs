using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace StoreMapDemo
{
    public enum ThemeMode { System, Light, Dark }

    /// <summary>
    /// ライト / ダークの配色。画面部品の色は Tag の役割名で決める
    /// （"sub" 補足文字、"accent" 強調文字、"panel" 情報パネル、"primary" 主ボタン、"tab" / "tab-active" タブ）。
    /// </summary>
    public static class Theme
    {
        public static ThemeMode Mode { get; private set; }
        public static bool IsDark { get; private set; }
        public static event EventHandler Changed;

        public static Color Back, Surface, PanelBack, Input, Fore, SubText, Muted, Border,
            GridBack, GridAlt, GridHeader, GridLine, Selection, SelectionFore,
            Accent, AccentBack, Danger, Success, Warn, WarnBack, ButtonBack, ButtonHover, Disabled;

        static string settingsPath;

        static Theme() { Set(ThemeMode.System, false); }

        public static bool SystemIsDark()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object v = k == null ? null : k.GetValue("AppsUseLightTheme");
                    return v is int && (int)v == 0;
                }
            }
            catch { return false; }
        }

        public static void Set(ThemeMode mode, bool save = true)
        {
            Mode = mode;
            IsDark = mode == ThemeMode.Dark || (mode == ThemeMode.System && SystemIsDark());
            if (IsDark)
            {
                Back = C(32, 32, 35); Surface = C(40, 40, 44); PanelBack = C(45, 49, 56); Input = C(46, 46, 51);
                Fore = C(236, 236, 240); SubText = C(170, 175, 182); Muted = C(112, 116, 124); Border = C(68, 71, 78);
                GridBack = C(37, 37, 41); GridAlt = C(42, 43, 47); GridHeader = C(52, 56, 64); GridLine = C(56, 58, 64);
                Selection = C(36, 78, 122); SelectionFore = Color.White;
                Accent = C(110, 178, 255); AccentBack = C(0, 103, 192); Danger = C(255, 118, 106); Success = C(110, 205, 110);
                Warn = C(255, 188, 84); WarnBack = C(70, 60, 30); ButtonBack = C(52, 53, 58); ButtonHover = C(66, 68, 75);
                Disabled = C(66, 74, 88);
            }
            else
            {
                Back = C(247, 248, 250); Surface = Color.White; PanelBack = C(238, 243, 250); Input = Color.White;
                Fore = C(28, 28, 32); SubText = C(92, 96, 104); Muted = C(160, 164, 170); Border = C(212, 216, 223);
                GridBack = Color.White; GridAlt = C(249, 250, 252); GridHeader = C(232, 238, 247); GridLine = C(226, 229, 234);
                Selection = C(204, 228, 247); SelectionFore = Color.Black;
                Accent = C(0, 95, 184); AccentBack = C(0, 95, 184); Danger = C(196, 43, 28); Success = C(16, 124, 16);
                Warn = C(176, 98, 0); WarnBack = C(255, 244, 206); ButtonBack = Color.White; ButtonHover = C(229, 241, 251);
                Disabled = C(190, 200, 214);
            }
            if (save) SaveSetting();
            if (Changed != null) Changed(null, EventArgs.Empty);
        }

        static Color C(int r, int g, int b) { return Color.FromArgb(r, g, b); }

        static Color Mix(Color a, Color b, float t)
        {
            return Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }

        // ---- 設定の保存（データファイルと同じフォルダの demomapdb_settings.ini）

        public static void LoadSetting(string dataFilePath)
        {
            Settings.Load(dataFilePath);
            settingsPath = dataFilePath;
            ThemeMode m = ThemeMode.System;
            try { Enum.TryParse(Settings.Get("theme", "System"), true, out m); }
            catch { }
            Set(m, false);
            // 「Windows の設定に合わせる」のときは、Windows 側の切り替えに追従する
            SystemEvents.UserPreferenceChanged += (s, e) =>
            {
                if (Mode == ThemeMode.System && SystemIsDark() != IsDark) Set(ThemeMode.System, false);
            };
        }

        static void SaveSetting()
        {
            if (settingsPath == null) return;
            Settings.Set("theme", Mode.ToString());   // 列の設定と同じファイルに保存する
        }

        // ---- 画面への適用

        /// <summary>ダイアログ・メイン画面の表示時に配色を適用する</summary>
        public static void Attach(Form f)
        {
            f.HandleCreated += delegate { TitleBar(f); };
            f.Load += delegate { Apply(f); };
            f.Shown += delegate { Apply(f); };
        }

        public static void Apply(Control root)
        {
            ApplyOne(root);
            foreach (Control c in root.Controls) Apply(c);
        }

        static void ApplyOne(Control c)
        {
            string role = c.Tag as string;
            if (c is Form) { c.BackColor = Back; c.ForeColor = Fore; TitleBar((Form)c); }
            else if (c is DataGridView) StyleGrid((DataGridView)c);
            else if (c is Button) StyleButton((Button)c);
            else if (c is TextBox)
            {
                var t = (TextBox)c;
                t.BorderStyle = BorderStyle.FixedSingle;
                t.BackColor = Input; t.ForeColor = Fore;
                DarkScrollBars(t);
            }
            else if (c is ToolStrip) StyleStrip((ToolStrip)c);
            else if (c is SplitContainer) c.BackColor = Border;
            else if (c is SplitterPanel) c.BackColor = Back;
            else if (c is DateTimePicker || c is ScrollBar) { } // Windows 標準部品のため配色は変えない
            else if (c is Label || c is CheckBox)
            {
                c.BackColor = Color.Transparent;
                c.ForeColor = role == "sub" ? SubText : role == "accent" ? Accent : role == "danger" ? Danger : Fore;
            }
            else
            {
                c.BackColor = role == "panel" ? PanelBack : role == "tabbar" ? Surface
                    : (c.Parent != null && !(c.Parent is SplitContainer) ? c.Parent.BackColor : Back);
                c.ForeColor = Fore;
            }
        }

        public static void StyleGrid(DataGridView g)
        {
            g.BackgroundColor = GridBack;
            g.GridColor = GridLine;
            g.DefaultCellStyle.BackColor = GridBack;
            g.DefaultCellStyle.ForeColor = Fore;
            g.DefaultCellStyle.SelectionBackColor = Selection;
            g.DefaultCellStyle.SelectionForeColor = SelectionFore;
            g.AlternatingRowsDefaultCellStyle.BackColor = GridAlt;
            g.ColumnHeadersDefaultCellStyle.BackColor = GridHeader;
            g.ColumnHeadersDefaultCellStyle.ForeColor = Fore;
            g.ColumnHeadersDefaultCellStyle.SelectionBackColor = GridHeader;
            g.ColumnHeadersDefaultCellStyle.SelectionForeColor = Fore;
            DarkScrollBars(g);
            foreach (Control sb in g.Controls) DarkScrollBars(sb);
        }

        public static void StyleButton(Button b)
        {
            string role = b.Tag as string;
            bool strong = role == "primary" || role == "tab-active";
            b.FlatStyle = FlatStyle.Flat;
            b.UseVisualStyleBackColor = false;
            b.FlatAppearance.BorderSize = 1;
            if (strong)
            {
                b.BackColor = b.Enabled ? AccentBack : Disabled;
                b.ForeColor = Color.White;
                b.FlatAppearance.BorderColor = b.BackColor;
                b.FlatAppearance.MouseOverBackColor = Mix(AccentBack, Color.White, 0.12f);
                b.FlatAppearance.MouseDownBackColor = Mix(AccentBack, Color.Black, 0.15f);
            }
            else if (role == "tab")
            {
                b.BackColor = Surface; b.ForeColor = SubText;
                b.FlatAppearance.BorderColor = Surface;
                b.FlatAppearance.MouseOverBackColor = ButtonHover;
                b.FlatAppearance.MouseDownBackColor = Selection;
            }
            else
            {
                b.BackColor = ButtonBack; b.ForeColor = Fore;
                b.FlatAppearance.BorderColor = Border;
                b.FlatAppearance.MouseOverBackColor = ButtonHover;
                b.FlatAppearance.MouseDownBackColor = Selection;
            }
        }

        static readonly ToolStripRenderer renderer = new ThemeRenderer();

        static void StyleStrip(ToolStrip s)
        {
            s.Renderer = renderer;
            s.BackColor = s is StatusStrip ? Surface : Back;
            s.ForeColor = Fore;
            foreach (ToolStripItem it in s.Items) StyleItem(it);
        }

        static void StyleItem(ToolStripItem it)
        {
            if (!(it is ToolStripStatusLabel)) it.ForeColor = Fore; // ステータス欄の文字色は画面側で状態に応じて設定
            var mi = it as ToolStripMenuItem;
            if (mi == null) return;
            mi.DropDown.BackColor = Surface;
            foreach (ToolStripItem sub in mi.DropDownItems) StyleItem(sub);
        }

        // ---- タイトルバー・スクロールバー（Windows 10 1809 以降 / 11）

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        static extern int SetWindowTheme(IntPtr hwnd, string appName, string idList);
        [DllImport("user32.dll")]
        static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

        public static void TitleBar(Form f)
        {
            if (!f.IsHandleCreated) return;
            try
            {
                int v = IsDark ? 1 : 0;
                if (DwmSetWindowAttribute(f.Handle, 20, ref v, 4) != 0) DwmSetWindowAttribute(f.Handle, 19, ref v, 4);
                SetWindowPos(f.Handle, IntPtr.Zero, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0004 | 0x0020); // 枠を再描画
            }
            catch { }
        }

        static void DarkScrollBars(Control c)
        {
            if (!c.IsHandleCreated) return;
            try { SetWindowTheme(c.Handle, IsDark ? "DarkMode_Explorer" : "Explorer", null); } catch { }
        }
    }

    /// <summary>メニュー・ステータスバーの描画</summary>
    class ThemeRenderer : ToolStripProfessionalRenderer
    {
        public ThemeRenderer() : base(new ThemeColors()) { RoundedEdges = false; }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            if (e.Item is ToolStripMenuItem) e.TextColor = e.Item.Enabled ? Theme.Fore : Theme.Muted;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = Theme.Fore;
            base.OnRenderArrow(e);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            var r = e.ImageRectangle;
            using (var b = new SolidBrush(Theme.Selection)) e.Graphics.FillRectangle(b, r);
            TextRenderer.DrawText(e.Graphics, "✓", e.Item.Font, r, Theme.Fore,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    class ThemeColors : ProfessionalColorTable
    {
        public ThemeColors() { UseSystemColors = false; }
        public override Color MenuStripGradientBegin { get { return Theme.Back; } }
        public override Color MenuStripGradientEnd { get { return Theme.Back; } }
        public override Color ToolStripDropDownBackground { get { return Theme.Surface; } }
        public override Color ImageMarginGradientBegin { get { return Theme.Surface; } }
        public override Color ImageMarginGradientMiddle { get { return Theme.Surface; } }
        public override Color ImageMarginGradientEnd { get { return Theme.Surface; } }
        public override Color MenuBorder { get { return Theme.Border; } }
        public override Color MenuItemBorder { get { return Theme.Border; } }
        public override Color MenuItemSelected { get { return Theme.ButtonHover; } }
        public override Color MenuItemSelectedGradientBegin { get { return Theme.ButtonHover; } }
        public override Color MenuItemSelectedGradientEnd { get { return Theme.ButtonHover; } }
        public override Color MenuItemPressedGradientBegin { get { return Theme.Surface; } }
        public override Color MenuItemPressedGradientMiddle { get { return Theme.Surface; } }
        public override Color MenuItemPressedGradientEnd { get { return Theme.Surface; } }
        public override Color SeparatorDark { get { return Theme.Border; } }
        public override Color SeparatorLight { get { return Theme.Surface; } }
        public override Color StatusStripGradientBegin { get { return Theme.Surface; } }
        public override Color StatusStripGradientEnd { get { return Theme.Surface; } }
        public override Color CheckBackground { get { return Theme.Selection; } }
        public override Color CheckSelectedBackground { get { return Theme.Selection; } }
        public override Color CheckPressedBackground { get { return Theme.Selection; } }
        public override Color ToolStripBorder { get { return Theme.Border; } }
    }
}
