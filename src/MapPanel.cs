using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace StoreMapDemo
{
    /// <summary>
    /// 簡易地図。地図画像は使わず、緯度経度をそのまま平面に置いて描く（メルカトルではなく、
    /// 表示中心の緯度で経度方向を縮める正距円筒図法）。数十kmの範囲なら距離の見た目はほぼ正しい。
    /// </summary>
    public class MapPanel : Panel
    {
        const double KmPerDegLat = 111.32;

        GeoPoint center = new GeoPoint(35.681, 139.767);
        double pxPerKm = 40;                 // 表示倍率
        GeoPoint origin;                     // 検索地点（未設定なら描かない）
        string originLabel = "";
        List<Nearby> results = new List<Nearby>();
        List<Store> allStores = new List<Store>();

        readonly List<RectangleF> placedLabels = new List<RectangleF>();
        Store hover;
        Point mouse = new Point(-100, -100);
        bool dragging;
        Point dragFrom;
        GeoPoint dragCenter;

        public event EventHandler<StoreEventArgs> StoreClicked;

        /// <summary>画面のDPIに合わせた倍率（96dpi を 1 とする）。ピンや文字の余白の計算に使う。</summary>
        float S { get { return DeviceDpi / 96f; } }

        int Px(double px) { return (int)Math.Round(px * S); }

        public MapPanel()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            BackColor = Color.White;
            Cursor = Cursors.Hand;
        }

        public GeoPoint Origin { get { return origin; } }
        public List<Nearby> Results { get { return results; } }

        StoreData data;

        public void SetStores(StoreData source)
        {
            data = source;
            allStores = source.Stores.Where(s => s.HasLocation).ToList();
            Invalidate();
        }

        public void SetResult(GeoPoint originPoint, string label, List<Nearby> list)
        {
            origin = originPoint;
            originLabel = label ?? "";
            results = list ?? new List<Nearby>();
            FitToResult();
        }

        public void ClearResult()
        {
            origin = new GeoPoint();
            originLabel = "";
            results = new List<Nearby>();
            Invalidate();
        }

        // ---- 表示範囲

        /// <summary>検索地点と結果の全部が入るように寄せる</summary>
        public void FitToResult()
        {
            var pts = results.Select(n => new GeoPoint(n.Store.Lat, n.Store.Lng)).ToList();
            if (!origin.IsEmpty) pts.Add(origin);
            if (pts.Count == 0) { FitToAll(); return; }
            FitTo(pts, 1.35);
        }

        /// <summary>登録されている全店舗が入るように寄せる</summary>
        public void FitToAll()
        {
            var pts = allStores.Select(s => new GeoPoint(s.Lat, s.Lng)).ToList();
            if (!origin.IsEmpty) pts.Add(origin);
            if (pts.Count == 0)
            {
                center = new GeoPoint(35.681, 139.767);
                pxPerKm = 40;
                Invalidate();
                return;
            }
            FitTo(pts, 1.25);
        }

        List<GeoPoint> fitPts;     // 直近に「収めた」点。大きさが変わったら同じ点で合わせ直す
        double fitMargin;
        bool userMoved;            // ホイールやドラッグで動かされたら、合わせ直さない

        void FitTo(List<GeoPoint> pts, double margin)
        {
            fitPts = pts;
            fitMargin = margin;
            userMoved = false;
            // まだ大きさが決まっていない（画面表示前）ときは、大きさが決まってから合わせる
            if (ClientSize.Width < 80 || ClientSize.Height < 80) return;

            double minLat = pts.Min(p => p.Lat), maxLat = pts.Max(p => p.Lat);
            double minLng = pts.Min(p => p.Lng), maxLng = pts.Max(p => p.Lng);
            center = new GeoPoint((minLat + maxLat) / 2, (minLng + maxLng) / 2);

            double h = Math.Max(1, ClientSize.Height - 40), w = Math.Max(1, ClientSize.Width - 40);
            double kmLat = Math.Max(0.4, (maxLat - minLat) * KmPerDegLat * margin);
            double kmLng = Math.Max(0.4, (maxLng - minLng) * KmPerDegLng(center.Lat) * margin);
            pxPerKm = Math.Min(h / kmLat, w / kmLng);
            pxPerKm = Math.Max(0.02, Math.Min(3000, pxPerKm));
            Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (fitPts != null && !userMoved && ClientSize.Width >= 80 && ClientSize.Height >= 80)
                FitTo(fitPts, fitMargin);
        }

        static double KmPerDegLng(double lat) { return KmPerDegLat * Math.Cos(lat * Math.PI / 180); }

        PointF ToScreen(GeoPoint p)
        {
            double dxKm = (p.Lng - center.Lng) * KmPerDegLng(center.Lat);
            double dyKm = (p.Lat - center.Lat) * KmPerDegLat;
            return new PointF(
                (float)(ClientSize.Width / 2.0 + dxKm * pxPerKm),
                (float)(ClientSize.Height / 2.0 - dyKm * pxPerKm));
        }

        GeoPoint ToGeo(Point pt)
        {
            double dxKm = (pt.X - ClientSize.Width / 2.0) / pxPerKm;
            double dyKm = (ClientSize.Height / 2.0 - pt.Y) / pxPerKm;
            return new GeoPoint(center.Lat + dyKm / KmPerDegLat, center.Lng + dxKm / KmPerDegLng(center.Lat));
        }

        // ---- 操作（ホイールで拡大縮小、ドラッグで移動、ダブルクリックで全体表示）

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            GeoPoint under = ToGeo(e.Location);
            double f = e.Delta > 0 ? 1.25 : 1 / 1.25;
            double next = Math.Max(0.02, Math.Min(3000, pxPerKm * f));
            if (Math.Abs(next - pxPerKm) < 1e-9) return;
            pxPerKm = next;
            userMoved = true;
            // カーソルの下の地点が動かないように中心をずらす
            GeoPoint after = ToGeo(e.Location);
            center = new GeoPoint(center.Lat + (under.Lat - after.Lat), center.Lng + (under.Lng - after.Lng));
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            if (e.Button == MouseButtons.Left)
            {
                var s = HitTest(e.Location);
                if (s != null && StoreClicked != null) StoreClicked(this, new StoreEventArgs(s));
                dragging = true;
                dragFrom = e.Location;
                dragCenter = center;
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            mouse = e.Location;
            if (dragging)
            {
                userMoved = true;
                double dxKm = (e.X - dragFrom.X) / pxPerKm, dyKm = (e.Y - dragFrom.Y) / pxPerKm;
                center = new GeoPoint(dragCenter.Lat + dyKm / KmPerDegLat,
                                      dragCenter.Lng - dxKm / KmPerDegLng(dragCenter.Lat));
                Invalidate();
            }
            else
            {
                var s = HitTest(e.Location);
                if (!ReferenceEquals(s, hover)) { hover = s; Invalidate(); }
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            dragging = false;
            base.OnMouseUp(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            mouse = new Point(-100, -100);
            if (hover != null) { hover = null; Invalidate(); }
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            FitToAll();
            base.OnMouseDoubleClick(e);
        }

        Store HitTest(Point pt)
        {
            // 結果のピンを優先し、近いものから当てる
            foreach (var n in results)
            {
                var p = ToScreen(new GeoPoint(n.Store.Lat, n.Store.Lng));
                if (Math.Abs(p.X - pt.X) <= Px(11) && pt.Y <= p.Y + Px(2) && pt.Y >= p.Y - Px(26)) return n.Store;
            }
            foreach (var s in allStores)
            {
                var p = ToScreen(new GeoPoint(s.Lat, s.Lng));
                if (Math.Abs(p.X - pt.X) <= Px(6) && Math.Abs(p.Y - pt.Y) <= Px(6)) return s;
            }
            return null;
        }

        // ---- 描画

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            Color back = Theme.IsDark ? Color.FromArgb(28, 32, 38) : Color.FromArgb(243, 246, 240);
            using (var b = new SolidBrush(back)) g.FillRectangle(b, ClientRectangle);

            placedLabels.Clear();
            DrawGrid(g);
            DrawRings(g);
            DrawOtherStores(g);
            DrawResultPins(g);
            DrawOrigin(g);
            DrawScaleBar(g);
            DrawLegend(g);
            DrawTooltip(g);

            using (var pen = new Pen(Theme.Border)) g.DrawRectangle(pen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
        }

        /// <summary>緯度経度のグリッド。表示範囲に応じて刻みを変える。</summary>
        void DrawGrid(Graphics g)
        {
            double spanLng = ClientSize.Width / pxPerKm / KmPerDegLng(center.Lat);
            double step = NiceStep(spanLng / 6);

            Color line = Theme.IsDark ? Color.FromArgb(44, 50, 58) : Color.FromArgb(223, 229, 218);
            Color text = Theme.IsDark ? Color.FromArgb(110, 118, 128) : Color.FromArgb(150, 158, 145);
            using (var pen = new Pen(line))
            using (var br = new SolidBrush(text))
            using (var f = new Font("Yu Gothic UI", 7.5f))
            {
                double left = ToGeo(new Point(0, 0)).Lng, right = ToGeo(new Point(ClientSize.Width, 0)).Lng;
                for (double x = Math.Floor(left / step) * step; x <= right; x += step)
                {
                    float sx = ToScreen(new GeoPoint(center.Lat, x)).X;
                    g.DrawLine(pen, sx, 0, sx, ClientSize.Height);
                    g.DrawString("東経" + x.ToString("0.###"), f, br, sx + 2, ClientSize.Height - Px(15));
                }
                double top = ToGeo(new Point(0, 0)).Lat, bottom = ToGeo(new Point(0, ClientSize.Height)).Lat;
                for (double y = Math.Floor(bottom / step) * step; y <= top; y += step)
                {
                    float sy = ToScreen(new GeoPoint(y, center.Lng)).Y;
                    g.DrawLine(pen, 0, sy, ClientSize.Width, sy);
                    g.DrawString("北緯" + y.ToString("0.###"), f, br, 3, sy - Px(14));
                }
            }
        }

        static double NiceStep(double raw)
        {
            if (raw <= 0) return 0.01;
            double mag = Math.Pow(10, Math.Floor(Math.Log10(raw)));
            double n = raw / mag;
            double s = n <= 1 ? 1 : n <= 2 ? 2 : n <= 5 ? 5 : 10;
            return s * mag;
        }

        /// <summary>検索地点からの距離の目安（同心円）</summary>
        void DrawRings(Graphics g)
        {
            if (origin.IsEmpty) return;
            var c = ToScreen(origin);
            double maxKm = results.Count > 0 ? results.Max(n => n.DistanceKm) : 5;
            double ring = NiceStep(Math.Max(0.2, maxKm) / 2);

            Color col = Theme.IsDark ? Color.FromArgb(70, 90, 110) : Color.FromArgb(198, 212, 226);
            using (var pen = new Pen(col) { DashStyle = DashStyle.Dash })
            using (var br = new SolidBrush(col))
            using (var f = new Font("Yu Gothic UI", 7.5f))
            {
                for (int i = 1; i <= 4; i++)
                {
                    float r = (float)(ring * i * pxPerKm);
                    if (r < Px(12) || r > Math.Max(ClientSize.Width, ClientSize.Height) * 1.5) continue;
                    g.DrawEllipse(pen, c.X - r, c.Y - r, r * 2, r * 2);
                    g.DrawString(GeoMath.FormatKm(ring * i), f, br, c.X + Px(3), c.Y - r - Px(14));
                }
            }
        }

        /// <summary>検索結果に入らなかった店舗（小さい点）</summary>
        void DrawOtherStores(Graphics g)
        {
            var shown = new HashSet<int>(results.Select(n => n.Store.Id));
            Color dot = Theme.IsDark ? Color.FromArgb(120, 128, 138) : Color.FromArgb(150, 158, 168);
            using (var br = new SolidBrush(dot))
            using (var pen = new Pen(Theme.IsDark ? Color.FromArgb(20, 24, 28) : Color.White, 1.5f))
            using (var f = new Font("Yu Gothic UI", 7.5f))
            using (var tb = new SolidBrush(Theme.SubText))
            {
                foreach (var s in allStores)
                {
                    if (shown.Contains(s.Id)) continue;
                    var p = ToScreen(new GeoPoint(s.Lat, s.Lng));
                    if (p.X < -30 || p.Y < -30 || p.X > ClientSize.Width + 30 || p.Y > ClientSize.Height + 30) continue;
                    float r = 3.5f * S;
                    g.FillEllipse(br, p.X - r, p.Y - r, r * 2, r * 2);
                    g.DrawEllipse(pen, p.X - r, p.Y - r, r * 2, r * 2);
                    if (pxPerKm > 12) g.DrawString(s.Name, f, tb, p.X + Px(6), p.Y - Px(7));
                }
            }
        }

        /// <summary>近い順に番号を振ったピン</summary>
        void DrawResultPins(Graphics g)
        {
            using (var f = new Font("Yu Gothic UI", 8f, FontStyle.Bold))
            using (var nameFont = new Font("Yu Gothic UI", 8.5f))
            {
                foreach (var n in results.OrderByDescending(x => x.Rank))
                {
                    var p = ToScreen(new GeoPoint(n.Store.Lat, n.Store.Lng));
                    Color fill = n.Rank == 1 ? Color.FromArgb(0, 120, 212) : Theme.IsDark ? Color.FromArgb(60, 110, 170) : Color.FromArgb(84, 148, 205);
                    DrawPin(g, p, fill, n.Rank.ToString(), f);

                    string label = n.Store.Name + "  " + n.DistanceText;
                    var size = g.MeasureString(label, nameFont);
                    // 右端にはみ出すときはピンの左側に出す
                    float pad = Px(8), off = Px(12);
                    float lx = p.X + off + size.Width + pad > ClientSize.Width - Px(4)
                        ? p.X - off - size.Width - pad : p.X + off;
                    var rect = PlaceLabel(new RectangleF(lx, p.Y - Px(34), size.Width + pad, size.Height + 2));
                    using (var bg = new SolidBrush(Color.FromArgb(Theme.IsDark ? 200 : 225, Theme.IsDark ? Color.FromArgb(20, 24, 30) : Color.White)))
                    using (var pen = new Pen(Theme.Border))
                    using (var tb = new SolidBrush(Theme.Fore))
                    {
                        g.FillRectangle(bg, rect);
                        g.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
                        g.DrawString(label, nameFont, tb, rect.X + Px(4), rect.Y + 1);
                    }
                }
            }
        }

        /// <summary>すでに置いた吹き出しと重ならない位置へずらす</summary>
        RectangleF PlaceLabel(RectangleF rect)
        {
            for (int i = 0; i < 6; i++)
            {
                if (!placedLabels.Any(r => r.IntersectsWith(rect))) break;
                rect.Y += rect.Height + 3;
            }
            placedLabels.Add(rect);
            return rect;
        }

        void DrawPin(Graphics g, PointF p, Color fill, string text, Font font)
        {
            float s = S;
            var path = new GraphicsPath();
            path.AddArc(p.X - 9 * s, p.Y - 26 * s, 18 * s, 18 * s, 150, 240);   // 頭の丸
            path.AddLine(p.X + 6.5f * s, p.Y - 10.5f * s, p.X, p.Y);            // 先端
            path.AddLine(p.X, p.Y, p.X - 6.5f * s, p.Y - 10.5f * s);
            path.CloseFigure();
            using (var br = new SolidBrush(fill))
            using (var pen = new Pen(Color.FromArgb(60, 0, 0, 0)))
            {
                g.FillPath(br, path);
                g.DrawPath(pen, path);
            }
            using (var tb = new SolidBrush(Color.White))
            {
                var sz = g.MeasureString(text, font);
                g.DrawString(text, font, tb, p.X - sz.Width / 2, p.Y - 24 * s);
            }
            path.Dispose();
        }

        /// <summary>検索地点（赤い十字）</summary>
        void DrawOrigin(Graphics g)
        {
            if (origin.IsEmpty) return;
            var p = ToScreen(origin);
            Color red = Color.FromArgb(214, 58, 48);
            float s = S;
            using (var pen = new Pen(red, 2f * s))
            using (var br = new SolidBrush(Color.FromArgb(70, red)))
            {
                g.FillEllipse(br, p.X - 11 * s, p.Y - 11 * s, 22 * s, 22 * s);
                g.DrawEllipse(pen, p.X - 7 * s, p.Y - 7 * s, 14 * s, 14 * s);
                g.DrawLine(pen, p.X - 14 * s, p.Y, p.X - 9 * s, p.Y);
                g.DrawLine(pen, p.X + 9 * s, p.Y, p.X + 14 * s, p.Y);
                g.DrawLine(pen, p.X, p.Y - 14 * s, p.X, p.Y - 9 * s);
                g.DrawLine(pen, p.X, p.Y + 9 * s, p.X, p.Y + 14 * s);
            }
            if (originLabel.Length > 0)
            {
                using (var f = new Font("Yu Gothic UI", 8.5f, FontStyle.Bold))
                using (var tb = new SolidBrush(red))
                using (var bg = new SolidBrush(Color.FromArgb(Theme.IsDark ? 200 : 225, Theme.IsDark ? Color.FromArgb(20, 24, 30) : Color.White)))
                {
                    string label = "検索地点  " + originLabel;
                    var size = g.MeasureString(label, f);
                    float ox = p.X + Px(14) + size.Width + Px(8) > ClientSize.Width - Px(4)
                        ? p.X - Px(14) - size.Width - Px(8) : p.X + Px(14);
                    var rect = PlaceLabel(new RectangleF(ox, p.Y + Px(6), size.Width + Px(8), size.Height + 2));
                    g.FillRectangle(bg, rect);
                    g.DrawString(label, f, tb, rect.X + Px(4), rect.Y + 1);
                }
            }
        }

        /// <summary>縮尺バーと方位</summary>
        void DrawScaleBar(Graphics g)
        {
            double targetPx = Math.Min(160 * S, ClientSize.Width * 0.3);
            double km = NiceStep(targetPx / pxPerKm);
            float px = (float)(km * pxPerKm);
            float x = ClientSize.Width - px - Px(18), y = ClientSize.Height - Px(26);

            using (var pen = new Pen(Theme.Fore, 1.6f * S))
            using (var f = new Font("Yu Gothic UI", 8f))
            using (var br = new SolidBrush(Theme.Fore))
            {
                g.DrawLine(pen, x, y, x + px, y);
                g.DrawLine(pen, x, y - Px(4), x, y + Px(4));
                g.DrawLine(pen, x + px, y - Px(4), x + px, y + Px(4));
                string t = GeoMath.FormatKm(km);
                var ts = g.MeasureString(t, f);
                g.DrawString(t, f, br, x + px / 2 - ts.Width / 2, y - ts.Height - Px(2));

                // 方位（上が北）
                float nx = Px(20), ny = Px(22);
                g.DrawLine(pen, nx, ny + Px(12), nx, ny - Px(10));
                g.DrawLine(pen, nx, ny - Px(10), nx - Px(4), ny - Px(4));
                g.DrawLine(pen, nx, ny - Px(10), nx + Px(4), ny - Px(4));
                g.DrawString("N", f, br, nx - Px(5), ny + Px(12));
            }
        }

        void DrawLegend(Graphics g)
        {
            if (results.Count == 0 && origin.IsEmpty) return;
            using (var f = new Font("Yu Gothic UI", 8f))
            using (var br = new SolidBrush(Theme.SubText))
            {
                g.DrawString("ホイールで拡大縮小 / ドラッグで移動 / ダブルクリックで全体表示", f, br, Px(40), Px(6));
            }
        }

        void DrawTooltip(Graphics g)
        {
            if (hover == null) return;
            var lines = new List<string> { hover.Name };
            if (hover.Address.Length > 0) lines.Add(hover.Address);
            if (data != null)
                foreach (var f in data.ListFields)
                {
                    string v = hover.Get(f.ApiName);
                    if (v.Length > 0) lines.Add(f.Label + ": " + v);
                }
            if (!origin.IsEmpty)
            {
                double d = GeoMath.DistanceKm(origin, new GeoPoint(hover.Lat, hover.Lng));
                lines.Add("検索地点から " + GeoMath.FormatKm(d) + " " + GeoMath.Direction(origin, new GeoPoint(hover.Lat, hover.Lng)));
            }

            using (var f = new Font("Yu Gothic UI", 8.5f))
            {
                float lineH = g.MeasureString("あ", f).Height + Px(2);
                float w = lines.Max(l => g.MeasureString(l, f).Width) + Px(14);
                float h = lines.Count * lineH + Px(10);
                float x = Math.Min(mouse.X + Px(16), ClientSize.Width - w - Px(4));
                float y = Math.Min(mouse.Y + Px(16), ClientSize.Height - h - Px(4));
                using (var bg = new SolidBrush(Theme.IsDark ? Color.FromArgb(245, 34, 38, 44) : Color.FromArgb(248, 255, 255, 255)))
                using (var pen = new Pen(Theme.Border))
                using (var tb = new SolidBrush(Theme.Fore))
                using (var sb = new SolidBrush(Theme.SubText))
                {
                    g.FillRectangle(bg, x, y, w, h);
                    g.DrawRectangle(pen, x, y, w, h);
                    for (int i = 0; i < lines.Count; i++)
                        g.DrawString(lines[i], f, i == 0 ? tb : sb, x + Px(7), y + Px(5) + i * lineH);
                }
            }
        }
    }

    public class StoreEventArgs : EventArgs
    {
        public Store Store { get; private set; }
        public StoreEventArgs(Store s) { Store = s; }
    }
}
