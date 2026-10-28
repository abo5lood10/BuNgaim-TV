// هوية DsMaN "Default" (أخضر داكن زجاجي) — الأرقام منسوخة حرفياً من theme\default theme\reference\DsMaN-theme-code.cs.txt
// (T، GlassRenderer، NameArt، WinBtn، Cmd) وتمّ تكييفها لـ BuNgaim TV.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace NextBoost
{
    static class T
    {
        public static int W = 940, H = 700;
        public static Color Text = Color.FromArgb(226, 240, 230), Dim = Color.FromArgb(126, 160, 142), Green = Color.FromArgb(52, 199, 89),
            GreenDark = Color.FromArgb(22, 120, 46), Red = Color.FromArgb(224, 72, 72), Yellow = Color.FromArgb(240, 196, 70),
            WindowBack = Color.FromArgb(20, 44, 33), Field = Color.FromArgb(22, 48, 36);
        public const string Face = "Bahnschrift";   // احتياط فقط
        static Dictionary<int, Font> fontCache = new Dictionary<int, Font>();
        public static Font F(float s, FontStyle st)
        {
            int key = (int)(s * 10) * 8 + (int)st; Font f;
            if (fontCache.TryGetValue(key, out f)) return f;
            FontFamily ff = Fonts.Family;
            f = ff != null && ff.IsStyleAvailable(st) ? new Font(ff, s, st) : new Font(Face, s, st);
            fontCache[key] = f; return f;
        }
        public static Font F(float s) { return F(s, FontStyle.Regular); }

        public static GraphicsPath RR(RectangleF r, float rad)
        {
            GraphicsPath p = new GraphicsPath(); float d = Math.Min(rad * 2, Math.Min(r.Width, r.Height));
            p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure(); return p;
        }

        // خلفية النافذة كلها (تدرّج واحد)، والأزرار ترسم الجزء اللي تحتها منها عشان ما يبان قص
        public static Bitmap StaticBg;
        static Bitmap grad;
        public static void ResetBg() { grad = null; }
        public static Bitmap Gradient()
        {
            if (grad != null && grad.Width == W && grad.Height == H) return grad;
            grad = new Bitmap(W, H, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using (Graphics g = Graphics.FromImage(grad))
            using (LinearGradientBrush b = new LinearGradientBrush(new Rectangle(0, 0, W, H), Color.FromArgb(52, 82, 64), Color.FromArgb(15, 33, 22), 62f))
                g.FillRectangle(b, 0, 0, W, H);
            return grad;
        }
        public static void Back(Graphics g, Control c)
        {
            Bitmap b = StaticBg ?? Gradient();
            g.DrawImageUnscaled(b, -c.Left, -c.Top);
        }

        // زر زجاجي: مربع مدوّر (نصف قطر 14)، لمعة خفيفة فوق، الـ hover يفتّح التعبئة
        public static void Glass1(Graphics g, RectangleF r, bool on, Color? tint, bool hover)
        {
            float rad = Math.Min(14f, r.Height / 2f);
            RectangleF f = new RectangleF(r.X, r.Y, r.Width, r.Height + 1);
            using (GraphicsPath p = RR(r, rad))
            {
                Color top, bot, e1, e2;
                if (on)
                {
                    top = tint ?? (hover ? Color.FromArgb(54, 212, 90) : Color.FromArgb(42, 172, 71));
                    bot = tint.HasValue ? Color.FromArgb(tint.Value.R * 3 / 5, tint.Value.G * 3 / 5, tint.Value.B * 3 / 5) : (hover ? Color.FromArgb(19, 143, 19) : Color.FromArgb(14, 112, 14));
                    if (tint.HasValue && hover) { top = Color.FromArgb(Math.Min(255, top.R + 28), Math.Min(255, top.G + 28), Math.Min(255, top.B + 28)); }
                    e1 = e2 = hover ? Color.FromArgb(224, 255, 255, 255) : Color.FromArgb(128, 168, 240, 184);
                }
                else
                {
                    top = Color.FromArgb(hover ? 72 : 48, 255, 255, 255); bot = Color.FromArgb(hover ? 28 : 15, 255, 255, 255);
                    e1 = Color.FromArgb(112, 255, 255, 255); e2 = Color.FromArgb(24, 255, 255, 255);
                }
                using (LinearGradientBrush b = new LinearGradientBrush(f, top, bot, 90f)) g.FillPath(b, p);
                RectangleF sh = new RectangleF(r.X + 1, r.Y + 1, r.Width - 2, Math.Min(20f, r.Height * 0.45f));
                if (sh.Height > 2)
                    using (GraphicsPath sp = RR(sh, Math.Min(rad - 1, sh.Height / 2f)))
                    using (LinearGradientBrush sb = new LinearGradientBrush(new RectangleF(sh.X, sh.Y, sh.Width, sh.Height + 1), Color.FromArgb(56, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90f)) g.FillPath(sb, sp);
                using (LinearGradientBrush eb = new LinearGradientBrush(f, e1, e2, 90f))
                using (Pen pen = new Pen(eb, 1f)) g.DrawPath(pen, p);
            }
        }

        // بطاقة زجاجية: تعبئة شفافة، حافة بيضاء خفيفة
        public static void Card(Graphics g, RectangleF r, float rad)
        {
            using (GraphicsPath p = RR(r, rad))
            {
                RectangleF f = new RectangleF(r.X, r.Y, r.Width, r.Height + 1);
                using (LinearGradientBrush b = new LinearGradientBrush(f, Color.FromArgb(48, 255, 255, 255), Color.FromArgb(15, 255, 255, 255), 90f)) g.FillPath(b, p);
                using (LinearGradientBrush eb = new LinearGradientBrush(f, Color.FromArgb(112, 255, 255, 255), Color.FromArgb(24, 255, 255, 255), 90f))
                using (Pen pen = new Pen(eb, 1f)) g.DrawPath(pen, p);
            }
        }

        public static void FieldBox(Graphics g, RectangleF r)
        {
            using (GraphicsPath p = RR(r, 12))
            {
                using (Brush b = new SolidBrush(Field)) g.FillPath(b, p);
                using (Pen pen = new Pen(Color.FromArgb(90, 52, 199, 89), 1.5f)) g.DrawPath(pen, p);
            }
        }

        public static void Pill(Graphics g, RectangleF r, bool on, Color? tint) { Glass1(g, r, on, tint, false); }
        public static void PillH(Graphics g, RectangleF r, bool on, Color? tint, bool hover) { Glass1(g, r, on, tint, hover); }

        static bool HasArabic(string s) { foreach (char ch in s) if (ch >= '\u0600' && ch <= '\u06FF') return true; return false; }
        public static void Txt(Graphics g, string s, Font f, Color c, RectangleF r, StringAlignment h)
        {
            bool rtl = HasArabic(s);
            // النص العربي يُرسم من اليمين لليسار عشان يطلع الترتيب صح مع الكلمات الإنجليزية؛ والمحاذاة تبقى بالاتجاه الفيزيائي المطلوب
            StringAlignment al = rtl ? (h == StringAlignment.Near ? StringAlignment.Far : (h == StringAlignment.Far ? StringAlignment.Near : h)) : h;
            using (StringFormat sf = new StringFormat { Alignment = al, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap | (rtl ? StringFormatFlags.DirectionRightToLeft : 0) })
            using (Brush b = new SolidBrush(c)) g.DrawString(s, f, b, r, sf);
        }
        // رموز مرسومة (الخط ما فيه ★ ▶ ■ ▾، فنرسمها أشكال عشان كل النص يبقى بخط الهوية)
        public static void Icon(Graphics g, string kind, float cx, float cy, float s, Color c)
        {
            using (Brush b = new SolidBrush(c))
            using (Pen p = new Pen(c, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            {
                switch (kind)
                {
                    case "play": g.FillPolygon(b, new PointF[] { new PointF(cx - s * 0.45f, cy - s * 0.55f), new PointF(cx - s * 0.45f, cy + s * 0.55f), new PointF(cx + s * 0.6f, cy) }); break;
                    case "stop": using (GraphicsPath r = RR(new RectangleF(cx - s * 0.5f, cy - s * 0.5f, s, s), 2.5f)) g.FillPath(b, r); break;
                    case "starO":
                        {
                            var sp = new PointF[10];
                            for (int i = 0; i < 10; i++) { float rad = i % 2 == 0 ? s * 0.62f : s * 0.27f; double a = Math.PI * i / 5 - Math.PI / 2; sp[i] = new PointF(cx + (float)(rad * Math.Cos(a)), cy + (float)(rad * Math.Sin(a))); }
                            using (Pen po = new Pen(c, 1.6f) { LineJoin = LineJoin.Round }) g.DrawPolygon(po, sp); break;
                        }
                    case "full":
                        { float q = s * 0.55f, k = s * 0.28f;
                          g.DrawLines(p, new PointF[] { new PointF(cx - q, cy - q + k), new PointF(cx - q, cy - q), new PointF(cx - q + k, cy - q) });
                          g.DrawLines(p, new PointF[] { new PointF(cx + q - k, cy - q), new PointF(cx + q, cy - q), new PointF(cx + q, cy - q + k) });
                          g.DrawLines(p, new PointF[] { new PointF(cx - q, cy + q - k), new PointF(cx - q, cy + q), new PointF(cx - q + k, cy + q) });
                          g.DrawLines(p, new PointF[] { new PointF(cx + q - k, cy + q), new PointF(cx + q, cy + q), new PointF(cx + q, cy + q - k) }); break; }
                    case "vol": case "mute":
                        { g.FillPolygon(b, new PointF[] { new PointF(cx - s * 0.62f, cy - s * 0.22f), new PointF(cx - s * 0.30f, cy - s * 0.22f), new PointF(cx + s * 0.05f, cy - s * 0.55f), new PointF(cx + s * 0.05f, cy + s * 0.55f), new PointF(cx - s * 0.30f, cy + s * 0.22f), new PointF(cx - s * 0.62f, cy + s * 0.22f) });
                          if (kind == "vol") { g.DrawArc(p, cx - s * 0.15f, cy - s * 0.30f, s * 0.55f, s * 0.60f, -50, 100); g.DrawArc(p, cx - s * 0.15f, cy - s * 0.52f, s * 0.90f, s * 1.04f, -50, 100); }
                          else { g.DrawLine(p, cx + s * 0.2f, cy - s * 0.3f, cx + s * 0.75f, cy + s * 0.3f); g.DrawLine(p, cx + s * 0.2f, cy + s * 0.3f, cx + s * 0.75f, cy - s * 0.3f); } break; }
                    case "chev": g.DrawLines(p, new PointF[] { new PointF(cx - s * 0.5f, cy - s * 0.25f), new PointF(cx, cy + s * 0.3f), new PointF(cx + s * 0.5f, cy - s * 0.25f) }); break;
                    case "star":
                        var pts = new PointF[10];
                        for (int i = 0; i < 10; i++) { float rad = i % 2 == 0 ? s * 0.62f : s * 0.26f; double a = Math.PI * i / 5 - Math.PI / 2; pts[i] = new PointF(cx + (float)(rad * Math.Cos(a)), cy + (float)(rad * Math.Sin(a))); }
                        g.FillPolygon(b, pts); break;
                }
            }
        }
        public static void Setup(Graphics g) { g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit; }

        // قائمة منبثقة بنمط الزجاج
        public static ContextMenuStrip NewMenu()
        {
            ContextMenuStrip m = new ContextMenuStrip();
            m.Renderer = new GlassRenderer(); m.Font = F(9.5f); m.ForeColor = Text; m.BackColor = Color.FromArgb(24, 52, 39);
            m.ShowImageMargin = false; m.ShowCheckMargin = true; m.DropShadowEnabled = false;
            m.Opened += delegate { m.Region = new Region(RR(new RectangleF(0, 0, m.Width, m.Height), 14)); };
            return m;
        }
    }

    class GlassRenderer : ToolStripRenderer
    {
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using (LinearGradientBrush b = new LinearGradientBrush(new Rectangle(0, 0, e.ToolStrip.Width, e.ToolStrip.Height + 1), Color.FromArgb(34, 68, 51), Color.FromArgb(20, 44, 33), 90f))
                e.Graphics.FillRectangle(b, 0, 0, e.ToolStrip.Width, e.ToolStrip.Height);
        }
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath p = T.RR(new RectangleF(0.5f, 0.5f, e.ToolStrip.Width - 1.5f, e.ToolStrip.Height - 1.5f), 14))
            using (Pen pen = new Pen(Color.FromArgb(70, 255, 255, 255), 1.2f)) e.Graphics.DrawPath(pen, p);
        }
        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }
        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e) { }
        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            Graphics g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle r = new Rectangle(4, 1, e.Item.Width - 8, e.Item.Height - 2);
            if (e.Item.Selected && e.Item.Enabled)
                using (GraphicsPath p = T.RR(r, 9))
                {
                    using (LinearGradientBrush b = new LinearGradientBrush(new Rectangle(r.X, r.Y, r.Width, r.Height + 1), Color.FromArgb(150, 52, 199, 89), Color.FromArgb(150, 22, 120, 46), 90f)) g.FillPath(b, p);
                    using (Pen pen = new Pen(Color.FromArgb(90, 255, 255, 255))) g.DrawPath(pen, p);
                }
            ToolStripMenuItem mi = e.Item as ToolStripMenuItem;
            if (mi != null && mi.Checked)
                using (Brush b = new SolidBrush(e.Item.Selected ? Color.White : T.Green)) g.FillEllipse(b, 12, e.Item.Height / 2 - 4, 8, 8);
        }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Selected ? Color.White : T.Text;
            base.OnRenderItemText(e);
        }
    }

    // خط الواجهة (thmanyah sans) مدمج داخل الـ exe كمصدر (الترخيص يسمح بالتضمين داخل المنتج فقط)، وإلا Bahnschrift
    static class Fonts
    {
        static PrivateFontCollection pfc; static FontFamily fam; static List<IntPtr> pins = new List<IntPtr>();
        public static FontFamily Family { get { Init(); return fam; } }
        static void Init()
        {
            if (pfc != null) return;
            pfc = new PrivateFontCollection();
            try
            {
                foreach (string r in new string[] { "f1.bin", "f2.bin" })
                {
                    byte[] b = Read(r); if (b == null) continue;
                    IntPtr p = Marshal.AllocHGlobal(b.Length); Marshal.Copy(b, 0, p, b.Length); pins.Add(p); pfc.AddMemoryFont(p, b.Length);
                }
                foreach (FontFamily f in pfc.Families) if (f.Name == "thmanyah sans") fam = f;
            }
            catch { fam = null; }
        }
        static byte[] Read(string res)
        {
            byte[] key = { 0x5A, 0xC3, 0x27, 0x91 };
            using (System.IO.Stream s = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream(res))
            {
                if (s == null) return null;
                using (System.IO.MemoryStream ms = new System.IO.MemoryStream())
                {
                    s.CopyTo(ms); byte[] b = ms.ToArray();
                    for (int i = 0; i < b.Length; i++) b[i] ^= key[i & 3];
                    return b;
                }
            }
        }
    }

    // اسم البرنامج بخط الكرتون: مخطط مرسوم (مصدر name.bin) بأربع طبقات: ظل، حدّ غامق، تعبئة متدرجة، لمعة
    static class NameArt
    {
        static GraphicsPath cartoon;
        static GraphicsPath Cartoon
        {
            get
            {
                if (cartoon != null) return cartoon;
                using (System.IO.Stream s = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("name.bin"))
                using (System.IO.BinaryReader r = new System.IO.BinaryReader(s))
                {
                    int n = r.ReadInt32(); PointF[] pts = new PointF[n];
                    for (int i = 0; i < n; i++) pts[i] = new PointF(r.ReadSingle(), r.ReadSingle());
                    cartoon = new GraphicsPath(pts, r.ReadBytes(n), FillMode.Winding);
                }
                return cartoon;
            }
        }
        public static float DrawText(Graphics g, float x, float y, float height)
        {
            RectangleF b0 = Cartoon.GetBounds(); float k = height / b0.Height;
            using (GraphicsPath p = (GraphicsPath)Cartoon.Clone())
            {
                using (Matrix m = new Matrix(k, 0, 0, k, x + 4 - b0.X * k, y + 4 - b0.Y * k)) p.Transform(m);
                RectangleF b = p.GetBounds();
                using (Pen shadow = new Pen(Color.FromArgb(120, 0, 25, 8), height * 0.2f) { LineJoin = LineJoin.Round })
                using (Pen outline = new Pen(Color.FromArgb(8, 46, 20), height * 0.15f) { LineJoin = LineJoin.Round })
                using (Pen shine = new Pen(Color.FromArgb(150, 255, 255, 255), height * 0.06f) { LineJoin = LineJoin.Round })
                using (LinearGradientBrush br = new LinearGradientBrush(new RectangleF(b.X, b.Y, b.Width, b.Height), Color.FromArgb(255, 252, 150), Color.FromArgb(52, 199, 89), 90f))
                {
                    GraphicsState st = g.Save(); g.TranslateTransform(2, 4); g.DrawPath(shadow, p); g.Restore(st);
                    g.DrawPath(outline, p); g.FillPath(br, p);
                    using (GraphicsPath hl = (GraphicsPath)p.Clone()) using (Matrix mm = new Matrix(1, 0, 0, 1, -1.5f, -1.5f)) { hl.Transform(mm); g.DrawPath(shine, hl); }
                }
                return b.Width + 8;
            }
        }
    }

    interface IBackHost { Bitmap Bg { get; } }

    // أساس عناصر الواجهة المرسومة: خلفيتها هي نفس جزء الخلفية العام تحتها
    class VC : Control
    {
        public bool Hover;
        public VC()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            SetStyle(ControlStyles.Selectable, false); Cursor = Cursors.Hand;
        }
        protected override void OnPaintBackground(PaintEventArgs e) { T.Setup(e.Graphics); IBackHost h = FindForm() as IBackHost; Bitmap b = h != null ? h.Bg : (T.StaticBg ?? T.Gradient()); e.Graphics.DrawImageUnscaled(b, -Left, -Top); }
        protected override void OnMouseEnter(EventArgs e) { Hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { Hover = false; Invalidate(); base.OnMouseLeave(e); }
    }

    class Cmd : VC
    {
        public string Text2; public Action Act; public bool Primary; public Color? Tint; public Color? LabelColor; public string Glyph; public bool Chev;
        public Cmd(string text, Action act, bool primary) { Text2 = text; Act = act; Primary = primary; }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; T.Setup(g);
            T.PillH(g, new RectangleF(1, 1, Width - 3, Height - 3), Primary || Tint.HasValue, Tint, Hover);
            Color col = LabelColor ?? (Primary ? Color.White : T.Text);
            float pad = Chev ? 30 : 8;
            T.Txt(g, Text2, T.F(9.5f, FontStyle.Bold), col, new RectangleF(pad, 0, Width - pad - 8, Height), StringAlignment.Center);
            if (Chev) T.Icon(g, "chev", 18, Height / 2f, 9f, T.Dim);
            if (Glyph != null)
            {
                SizeF m = g.MeasureString(Text2, T.F(9.5f, FontStyle.Bold));
                T.Icon(g, Glyph, Text2.Length == 0 ? Width / 2f : Width / 2f + m.Width / 2f + 14, Height / 2f, 11f, col);
            }
        }
        protected override void OnMouseClick(MouseEventArgs e) { if (Act != null) Act(); }
    }
    // مفتاح تشغيل/إيقاف بنمط الزجاج
    class Tog : VC
    {
        public string Label; public bool On; public Color? Tint; public string Glyph; public event Action Changed;
        public Tog(string label, bool on, Color? tint) { Label = label; On = on; Tint = tint; }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; T.Setup(g);
            T.PillH(g, new RectangleF(1, 1, Width - 3, Height - 3), On, On ? Tint : null, Hover);
            Color col = On ? (Tint.HasValue && Tint.Value.Equals(T.Yellow) ? Color.FromArgb(40, 34, 10) : Color.White) : (Tint.HasValue && Tint.Value.Equals(T.Yellow) ? T.Yellow : T.Dim);
            T.Txt(g, Label, T.F(9f, FontStyle.Bold), col, new RectangleF(6, 0, Width - 12, Height), StringAlignment.Center);
            if (Glyph != null) { SizeF m = g.MeasureString(Label, T.F(9f, FontStyle.Bold)); T.Icon(g, Glyph, Width / 2f + m.Width / 2f + 14, Height / 2f, 10f, col); }
        }
        protected override void OnMouseClick(MouseEventArgs e) { On = !On; Invalidate(); if (Changed != null) Changed(); }
    }

    class WinBtn : VC
    {
        int kind; Action act;   // 0 تصغير، 1 تكبير/استعادة، 2 إغلاق
        public WinBtn(int k, Action a) { kind = k; act = a; }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; T.Setup(g); bool close = kind == 2;
            T.Pill(g, new RectangleF(1, 1, Width - 3, Height - 3), Hover && close, Hover && close ? (Color?)T.Red : null);
            if (Hover && !close)
                using (GraphicsPath hp = T.RR(new RectangleF(1, 1, Width - 3, Height - 3), 12))
                {
                    using (Brush hb = new SolidBrush(Color.FromArgb(70, 255, 255, 255))) g.FillPath(hb, hp);
                    using (Pen hpn = new Pen(Color.FromArgb(150, 255, 255, 255), 1.5f)) g.DrawPath(hpn, hp);
                }
            using (Pen p = new Pen(Color.White, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            {
                float cx = Width / 2f, cy = Height / 2f;
                if (kind == 2) { g.DrawLine(p, cx - 5, cy - 5, cx + 5, cy + 5); g.DrawLine(p, cx - 5, cy + 5, cx + 5, cy - 5); }
                else if (kind == 1) { using (GraphicsPath r = T.RR(new RectangleF(cx - 5.5f, cy - 5f, 11, 10), 2.5f)) g.DrawPath(p, r); }
                else g.DrawLine(p, cx - 6, cy + 3, cx + 6, cy + 3);
            }
        }
        protected override void OnMouseClick(MouseEventArgs e) { if (act != null) act(); }
    }

    // شريط صوت 0-200% بنمط الهوية: خط 100% معلّم، والرقم على اليمين
    class VolSlide : VC
    {
        public float Value = 100, Max = 200; public event Action<float> Changed; bool drag;
        const int LabelW = 58;
        float RailL { get { return 12; } } float RailR { get { return Width - LabelW - 6; } }
        float X(float v) { return RailL + v / Max * (RailR - RailL); }
        void SetFrom(int mx) { float v = (mx - RailL) / (RailR - RailL) * Max; v = Math.Max(0f, Math.Min(Max, (float)Math.Round(v / 1f))); if (Math.Abs(v - 100f) < 3f) v = 100f; Value = v; Invalidate(); if (Changed != null) Changed(Value); }
        public void Nudge(float v) { Value = Math.Max(0f, Math.Min(Max, v)); Invalidate(); if (Changed != null) Changed(Value); }
        public void SetSilent(float v) { if (drag) return; Value = Math.Max(0f, Math.Min(Max, v)); Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; T.Setup(g); float cy = Height / 2f, x = X(Value);
            using (Pen p = new Pen(Color.FromArgb(60, 255, 255, 255), 5) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawLine(p, RailL, cy, RailR, cy);
            Color fill = Value > 150 ? T.Yellow : T.Green;
            using (Pen p = new Pen(fill, 5) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawLine(p, RailL, cy, Math.Max(RailL + 0.1f, x), cy);
            using (Pen p = new Pen(Color.FromArgb(150, 255, 255, 255), 1.5f)) g.DrawLine(p, X(100), cy - 9, X(100), cy + 9);
            using (Brush b = new SolidBrush(Color.FromArgb(60, 0, 0, 0))) g.FillEllipse(b, x - 8, cy - 7, 18, 18);
            using (Brush b = new SolidBrush(Color.White)) g.FillEllipse(b, x - 9, cy - 9, 18, 18);
            T.Txt(g, ((int)Value) + "%", T.F(9.5f, FontStyle.Bold), Value > 150 ? T.Yellow : T.Text, new RectangleF(Width - LabelW, 0, LabelW, Height), StringAlignment.Center);
        }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { drag = true; Capture = true; SetFrom(e.X); } }
        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (drag) SetFrom(e.X); }
        protected override void OnMouseUp(MouseEventArgs e) { drag = false; Capture = false; }
        protected override void OnMouseWheel(MouseEventArgs e) { Value = Math.Max(0f, Math.Min(Max, Value + (e.Delta > 0 ? 5 : -5))); Invalidate(); if (Changed != null) Changed(Value); }
        protected override void OnMouseDoubleClick(MouseEventArgs e) { Value = 100; Invalidate(); if (Changed != null) Changed(Value); }
    }

    // شريط تمرير عمودي بنمط الهوية: مجرى أبيض شفاف، ومقبض أخضر متدرّج بحواف مدوّرة
    class VScroll : VC
    {
        public int Total, VisibleCount, TopIndex; public event Action<int> Scrolled; bool drag; float grab;
        public VScroll() { Cursor = Cursors.Default; }
        RectangleF Thumb()
        {
            float h = Height; float th = Math.Max(40f, h * VisibleCount / (float)Math.Max(VisibleCount, Total)); float range = Math.Max(1f, h - th);
            float y = Total <= VisibleCount ? 0 : range * TopIndex / (float)(Total - VisibleCount);
            return new RectangleF(0, y, Width, th);
        }
        int IndexFor(float thumbTop)
        {
            float h = Height; float th = Thumb().Height; float range = Math.Max(1f, h - th);
            return (int)Math.Round(Math.Max(0f, Math.Min(range, thumbTop)) / range * Math.Max(0, Total - VisibleCount));
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; T.Setup(g);
            float cx = Width / 2f;
            using (Pen p = new Pen(Color.FromArgb(46, 255, 255, 255), 5) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawLine(p, cx, 3, cx, Height - 3);
            if (Total <= VisibleCount) return;
            RectangleF t = Thumb(); RectangleF tr = new RectangleF(cx - 4, t.Y, 8, t.Height);
            using (GraphicsPath gp = T.RR(tr, 4))
            {
                using (LinearGradientBrush b = new LinearGradientBrush(new RectangleF(tr.X, tr.Y, tr.Width, tr.Height + 1), Hover || drag ? Color.FromArgb(54, 212, 90) : T.Green, T.GreenDark, 90f)) g.FillPath(b, gp);
                using (Pen pen = new Pen(Color.FromArgb(Hover || drag ? 200 : 110, 255, 255, 255), 1f)) g.DrawPath(pen, gp);
            }
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || Total <= VisibleCount) return;
            RectangleF t = Thumb();
            if (t.Contains(e.X, e.Y)) { drag = true; grab = e.Y - t.Y; Capture = true; }
            else if (Scrolled != null) Scrolled(TopIndex + (e.Y < t.Y ? -VisibleCount : VisibleCount));
        }
        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (drag && Scrolled != null) Scrolled(IndexFor(e.Y - grab)); }
        protected override void OnMouseUp(MouseEventArgs e) { drag = false; Capture = false; Invalidate(); }
        protected override void OnMouseWheel(MouseEventArgs e) { if (Scrolled != null) Scrolled(TopIndex + (e.Delta > 0 ? -3 : 3)); }
    }
    // سطر الحالة (نص يتغير)
    class StatusLine : VC
    {
        string txt = ""; public Color Col = Color.FromArgb(255, 214, 54); public StringAlignment Al = StringAlignment.Near;
        public StatusLine() { Cursor = Cursors.Default; }
        public string Value { get { return txt; } set { if (txt != value) { txt = value; Invalidate(); } } }
        protected override void OnPaint(PaintEventArgs e) { T.Setup(e.Graphics); T.Txt(e.Graphics, txt, T.F(9f), Col, new RectangleF(2, 0, Width - 4, Height), Al); }
    }

    // نافذة حوار بنمط البطاقة (بدل MessageBox)
    class ThemedBox : Form, IBackHost
    {
        [DllImport("user32")] static extern bool ReleaseCapture();
        [DllImport("user32")] static extern IntPtr SendMessage(IntPtr h, int msg, int w, int l);
        readonly string title, message; readonly bool askYesNo; Bitmap bgBmp; public Bitmap Bg { get { return bgBmp; } }
        public ThemedBox(string title, string message, bool yesNo)
        {
            this.title = title; this.message = message; askYesNo = yesNo;
            FormBorderStyle = FormBorderStyle.None; StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false;
            ClientSize = new Size(440, 210); BackColor = T.WindowBack; DoubleBuffered = true;
            Region = new Region(T.RR(new RectangleF(0, 0, Width, Height), 20));
            Cmd ok = new Cmd(yesNo ? "Yes" : "OK", delegate { DialogResult = DialogResult.OK; }, true) { Bounds = new Rectangle(yesNo ? 336 : 336, 154, 84, 34) };
            Controls.Add(ok);
            if (yesNo) Controls.Add(new Cmd("No", delegate { DialogResult = DialogResult.Cancel; }, false) { Bounds = new Rectangle(244, 154, 84, 34) });
            MouseDown += delegate(object o, MouseEventArgs e) { if (e.Button == MouseButtons.Left) { ReleaseCapture(); SendMessage(Handle, 0xA1, 2, 0); } };
            Bitmap bg = bgBmp = new Bitmap(440, 210);
            using (Graphics g = Graphics.FromImage(bg)) { T.Setup(g); using (LinearGradientBrush b = new LinearGradientBrush(new Rectangle(0, 0, 440, 210), Color.FromArgb(52, 82, 64), Color.FromArgb(15, 33, 22), 62f)) g.FillRectangle(b, 0, 0, 440, 210); T.Card(g, new RectangleF(1, 1, 437, 207), 20); }
            Paint += delegate(object o, PaintEventArgs e) { e.Graphics.DrawImageUnscaled(bg, 0, 0); T.Setup(e.Graphics); T.Txt(e.Graphics, title, T.F(14, FontStyle.Bold), T.Text, new RectangleF(26, 14, 388, 30), StringAlignment.Near); using (StringFormat sf = new StringFormat()) using (Brush br = new SolidBrush(T.Text)) e.Graphics.DrawString(message, T.F(10.5f), br, new RectangleF(26, 54, 388, 90), sf); };
            foreach (Control c in Controls) { Control cc = c; cc.Tag = bg; }
        }
        protected override bool ProcessCmdKey(ref Message m, Keys k)
        {
            if (k == Keys.Enter) { DialogResult = DialogResult.OK; return true; }
            if (k == Keys.Escape) { DialogResult = askYesNo ? DialogResult.Cancel : DialogResult.OK; return true; }
            return base.ProcessCmdKey(ref m, k);
        }
        public static bool Ask(IWin32Window owner, string title, string msg) { using (ThemedBox b = new ThemedBox(title, msg, true)) return b.ShowDialog(owner) == DialogResult.OK; }
        public static void Info(IWin32Window owner, string title, string msg) { using (ThemedBox b = new ThemedBox(title, msg, false)) b.ShowDialog(owner); }
    }

    // نافذة تسجيل الدخول: رابط السيرفر + اسم المستخدم + كلمة السر، وتتحقق من الحساب قبل ما تقبله
    class LoginForm : Form, IBackHost
    {
        [DllImport("user32")] static extern bool ReleaseCapture();
        [DllImport("user32")] static extern IntPtr SendMessage(IntPtr h, int msg, int w, int l);
        public string Host, User, Pass; public bool SignedOut;
        readonly TextBox tbHost = new TextBox(), tbUser = new TextBox(), tbPass = new TextBox();
        readonly StatusLine msg = new StatusLine(); Bitmap bgBmp; public Bitmap Bg { get { return bgBmp; } }
        bool busy; const int W = 470, H = 420;

        public LoginForm(string host, string user, string pass, bool canSignOut)
        {
            FormBorderStyle = FormBorderStyle.None; StartPosition = FormStartPosition.CenterScreen; ShowInTaskbar = true; Text = "BuNgaim TV";
            ClientSize = new Size(W, H); BackColor = T.WindowBack; DoubleBuffered = true;
            Region = new Region(T.RR(new RectangleF(0, 0, Width, Height), 20));
            int[] ys = { 92, 168, 244 }; TextBox[] tbs = { tbHost, tbUser, tbPass }; string[] vals = { host, user, pass };
            Bitmap bg = bgBmp = new Bitmap(W, H);
            using (Graphics g = Graphics.FromImage(bg))
            {
                T.Setup(g);
                using (LinearGradientBrush b = new LinearGradientBrush(new Rectangle(0, 0, W, H), Color.FromArgb(52, 82, 64), Color.FromArgb(15, 33, 22), 62f)) g.FillRectangle(b, 0, 0, W, H);
                T.Card(g, new RectangleF(1, 1, W - 3, H - 3), 20);
                foreach (int y in ys) T.FieldBox(g, new RectangleF(28, y, W - 56, 40));
            }
            for (int i = 0; i < 3; i++)
            {
                TextBox tb = tbs[i]; tb.BorderStyle = BorderStyle.None; tb.BackColor = T.Field; tb.ForeColor = T.Text; tb.Font = T.F(11f);
                tb.Bounds = new Rectangle(44, ys[i] + 10, W - 88, 24); tb.Text = vals[i] ?? ""; Controls.Add(tb);
            }
            tbPass.UseSystemPasswordChar = true;
            tbHost.HandleCreated += (s, e) => Native.SendMessage(tbHost.Handle, 0x1501, (IntPtr)1, "http://example.com:8080");
            msg.Bounds = new Rectangle(28, 296, W - 56, 24); msg.Value = ""; msg.Tag = bg; Controls.Add(msg);
            Cmd ok = new Cmd("Sign in", TrySignIn, true) { Bounds = new Rectangle(W - 28 - 130, 352, 130, 40), Tag = bg };
            Cmd cancel = new Cmd("Cancel", delegate { DialogResult = DialogResult.Cancel; }, false) { Bounds = new Rectangle(W - 28 - 130 - 8 - 100, 352, 100, 40), Tag = bg };
            Controls.Add(ok); Controls.Add(cancel);
            if (canSignOut) Controls.Add(new Cmd("Sign out", delegate { SignedOut = true; DialogResult = DialogResult.OK; }, false) { Bounds = new Rectangle(28, 352, 100, 40), LabelColor = Color.FromArgb(255, 150, 150), Tag = bg });
            MouseDown += delegate(object o, MouseEventArgs e) { if (e.Button == MouseButtons.Left) { ReleaseCapture(); SendMessage(Handle, 0xA1, 2, 0); } };
            Paint += delegate(object o, PaintEventArgs e)
            {
                e.Graphics.DrawImageUnscaled(bg, 0, 0); T.Setup(e.Graphics);
                T.Txt(e.Graphics, "Sign in", T.F(16, FontStyle.Bold), T.Text, new RectangleF(28, 16, W - 56, 32), StringAlignment.Near);
                T.Txt(e.Graphics, "Enter your own IPTV subscription details", T.F(9.5f), T.Dim, new RectangleF(28, 50, W - 56, 22), StringAlignment.Near);
                string[] lb = { "Server URL", "Username", "Password" };
                for (int i = 0; i < 3; i++) T.Txt(e.Graphics, lb[i], T.F(9f, FontStyle.Bold), T.Dim, new RectangleF(30, ys[i] - 20, 200, 18), StringAlignment.Near);
            };
        }

        public void CenterOn(Rectangle wa) { StartPosition = FormStartPosition.Manual; Location = new Point(wa.X + (wa.Width - W) / 2, wa.Y + (wa.Height - H) / 2); }

        protected override bool ProcessCmdKey(ref Message m, Keys k)
        {
            if (k == Keys.Enter) { TrySignIn(); return true; }
            if (k == Keys.Escape) { DialogResult = DialogResult.Cancel; return true; }
            return base.ProcessCmdKey(ref m, k);
        }

        void Say(string s, bool bad) { msg.Col = bad ? Color.FromArgb(255, 150, 150) : Color.FromArgb(255, 214, 54); msg.Value = s; }

        static string NormalizeHost(string h)
        {
            h = (h ?? "").Trim(); if (h.Length == 0) return "";
            if (!h.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !h.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) h = "http://" + h;
            Uri u; if (!Uri.TryCreate(h, UriKind.Absolute, out u)) return "";
            return u.GetLeftPart(UriPartial.Authority);
        }

        void TrySignIn()
        {
            if (busy) return;
            string host = NormalizeHost(tbHost.Text), user = tbUser.Text.Trim(), pass = tbPass.Text;
            // لو لصق رابط M3U كامل (get.php?username=..&password=..) نطلّع منه الحساب تلقائياً
            var mu = System.Text.RegularExpressions.Regex.Match(tbHost.Text, @"[?&]username=([^&\s]+).*?[?&]password=([^&\s]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (mu.Success) { user = Uri.UnescapeDataString(mu.Groups[1].Value); pass = Uri.UnescapeDataString(mu.Groups[2].Value); tbUser.Text = user; tbPass.Text = pass; tbHost.Text = host; }
            if (host == "" || user == "" || pass == "") { Say("Fill in all three fields (server URL, username, password).", true); return; }
            busy = true; Say("Checking your account…", false);
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                string err = null; string used = host;
                // نجرّب الرابط كما كتبه، ولو فشل الاتصال نجرّب http/https الثاني
                string[] tries = host.StartsWith("https://") ? new[] { host, "http://" + host.Substring(8) } : new[] { host, "https://" + host.Substring(7) };
                foreach (string h in tries)
                {
                    err = null;
                    try
                    {
                        System.Net.ServicePointManager.SecurityProtocol = (System.Net.SecurityProtocolType)3072;
                        var wc = new System.Net.WebClient { Encoding = System.Text.Encoding.UTF8 }; wc.Headers["User-Agent"] = "VLC/3.0.20";
                        string json = wc.DownloadString(h + "/player_api.php?username=" + Uri.EscapeDataString(user) + "&password=" + Uri.EscapeDataString(pass));
                        string shown = json.Length > 90 ? json.Substring(0, 90) : json; shown = shown.Replace("\r", " ").Replace("\n", " ");
                        System.Collections.Generic.Dictionary<string, object> root = null;
                        try { root = new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<System.Collections.Generic.Dictionary<string, object>>(json); } catch { }
                        var ui = root != null && root.ContainsKey("user_info") ? root["user_info"] as System.Collections.Generic.Dictionary<string, object> : null;
                        if (ui == null) err = "This address is not an IPTV server (or it does not accept this login). Server said: " + shown;
                        else
                        {
                            string auth = ui.ContainsKey("auth") ? Convert.ToString(ui["auth"]).Trim().ToLower() : "";
                            string status = ui.ContainsKey("status") ? Convert.ToString(ui["status"]).Trim() : "";
                            if (auth == "0" || auth == "false" || (auth == "" && status.Length == 0)) err = "Wrong username or password.";
                            else if (status.Length > 0 && !status.Equals("Active", StringComparison.OrdinalIgnoreCase)) err = "This subscription is " + status + ".";
                        }
                        used = h; if (err == null || !err.StartsWith("Could not reach")) break;
                    }
                    catch (Exception ex)
                    {
                        var wex = ex as System.Net.WebException;
                        if (wex != null && wex.Response != null)
                        {
                            int code = (int)((System.Net.HttpWebResponse)wex.Response).StatusCode;
                            err = (code == 401 || code == 403 || code == 404 || code >= 500 ? "The server refused this login (HTTP " + code + "). Usually a wrong username/password, an expired account, or the wrong server address." : "The server answered with an error (HTTP " + code + ").");
                            used = h; break;
                        }
                        err = "Could not reach the server (" + h + "): " + ex.Message;
                    }
                }
                host = used; Log.Write("sign-in " + (err == null ? "ok" : "failed: " + err) + "  host=" + used);
                try
                {
                    BeginInvoke(new Action(() =>
                    {
                        busy = false;
                        if (err != null) { Say(err, true); return; }
                        Host = host; User = user; Pass = pass; DialogResult = DialogResult.OK;
                    }));
                }
                catch { }
            });
        }
    }
    // فحص التحديث (نفس فكرة DsMaN): ملف نصي صغير فيه version=... و url=... على رابط أو مسار. التحديث ما يتم إلا لما المستخدم يضغط الزر.
    static class Updater
    {
        public const string Version = "1.6";
        public const string DefaultSource = "https://raw.githubusercontent.com/abo5lood10/BuNgaim-TV/main/update.txt";
        public static string Source = "";   // يتبدّل من settings.ini (UpdateUrl): رابط أو مسار ملف
        public static volatile bool Available, Busy; public static string NewVersion = "", DownloadUrl = "", Message = "";

        public static void Check(Action done)
        {
            if (Busy) return; Busy = true;
            new System.Threading.Thread(delegate()
            {
                try
                {
                    string src = (Source ?? "").Trim(); if (src.Length == 0) src = DefaultSource;
                    string txt;
                    if (src.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    {
                        System.Net.ServicePointManager.SecurityProtocol = (System.Net.SecurityProtocolType)3072;
                        using (System.Net.WebClient wc = new System.Net.WebClient()) { wc.Headers["User-Agent"] = "BuNgaimTV"; txt = wc.DownloadString(src); }
                    }
                    else txt = System.IO.File.ReadAllText(src);
                    string v = "", url = "";
                    foreach (string line in txt.Split('\n'))
                    {
                        int i = line.IndexOf('='); if (i < 0) continue;
                        string k = line.Substring(0, i).Trim().ToLower(), val = line.Substring(i + 1).Trim();
                        if (k == "version") v = val; else if (k == "url") url = val;
                    }
                    Version nv, cur = new Version(Version);
                    Log.Write("update check: server says " + v + ", running " + Version);
                    if (v.Length > 0 && url.Length > 0 && System.Version.TryParse(v, out nv) && nv > cur) { NewVersion = v; DownloadUrl = url; Available = true; Message = "Update v" + v + " available"; }
                    else { Available = false; Message = "You are up to date (v" + Version + ")"; }
                }
                catch (Exception ex) { Available = false; Message = "Update check failed: " + ex.Message; }
                finally { Busy = false; if (done != null) done(); }
            }) { IsBackground = true }.Start();
        }

        // ينزّل الـ exe الجديد جنب الحالي، ويتأكد إنه برنامج ويندوز بحجم معقول، وبعد ما نغلق يبدّله ويشغّله من جديد
        public static void Apply()
        {
            string exe = Application.ExecutablePath, tmp = exe + ".new";
            if (DownloadUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                System.Net.ServicePointManager.SecurityProtocol = (System.Net.SecurityProtocolType)3072;
                using (System.Net.WebClient wc = new System.Net.WebClient()) { wc.Headers["User-Agent"] = "BuNgaimTV"; wc.DownloadFile(DownloadUrl, tmp); }
            }
            else System.IO.File.Copy(DownloadUrl, tmp, true);
            using (System.IO.FileStream fs = System.IO.File.OpenRead(tmp))
                if (fs.Length < 200000 || fs.ReadByte() != 'M' || fs.ReadByte() != 'Z') { fs.Close(); System.IO.File.Delete(tmp); throw new Exception("the downloaded file is not a valid program"); }
            Log.Write("update: downloaded v" + NewVersion + " (" + new System.IO.FileInfo(tmp).Length + " bytes), swapping and restarting");
            string bak = exe + ".bak";
            string bat = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bungaim_update.bat");
            System.IO.File.WriteAllText(bat, "@echo off\r\nset n=0\r\n:r\r\nping 127.0.0.1 -n 3 >nul\r\ncopy /y \"" + exe + "\" \"" + bak + "\" >nul 2>nul\r\nmove /y \"" + tmp + "\" \"" + exe + "\" >nul 2>nul\r\nif errorlevel 1 (set /a n+=1 & if %n% lss 10 goto r)\r\nstart \"\" \"" + exe + "\"\r\ndel \"%~f0\"\r\n");
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", "/c \"" + bat + "\"") { CreateNoWindow = true, UseShellExecute = false });
        }
    }

    // زر النسخة: زجاجي عادي، ولما يتوفر تحديث يتحول للأصفر (لون زر Y في يد Xbox 360 مثل DsMaN) والضغط عليه يحدّث
    class VerBtn : VC
    {
        public Action Act; bool hot;
        public VerBtn() { Cursor = Cursors.Hand; }
        protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { hot = false; Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; T.Setup(g);
            if (!Updater.Available)
            {
                T.PillH(g, new RectangleF(1, 1, Width - 3, Height - 3), false, null, hot);
                T.Txt(g, "v" + Updater.Version, T.F(9.5f, FontStyle.Bold), T.Text, new RectangleF(0, 0, Width, Height), StringAlignment.Center);
                return;
            }
            RectangleF r = hot ? new RectangleF(1, 0, Width - 3, Height - 4) : new RectangleF(1, 1, Width - 3, Height - 3);
            if (hot)
                for (int i = 3; i >= 0; i--)
                    using (GraphicsPath sp = T.RR(new RectangleF(r.X + 1 - i * 0.5f, r.Y + 3 - i * 0.2f, r.Width - 2 + i, r.Height + i * 0.8f), r.Height / 2.4f))
                    using (Brush sb = new SolidBrush(Color.FromArgb(34 + (3 - i) * 8, 0, 0, 0))) g.FillPath(sb, sp);
            using (GraphicsPath p = T.RR(r, r.Height / 2.4f))
            {
                using (LinearGradientBrush b = new LinearGradientBrush(new RectangleF(r.X, r.Y, r.Width, r.Height + 1), hot ? Color.FromArgb(255, 226, 96) : Color.FromArgb(255, 214, 54), hot ? Color.FromArgb(245, 180, 10) : Color.FromArgb(232, 164, 0), 90f)) g.FillPath(b, p);
                using (Pen pen = new Pen(Color.FromArgb(150, 255, 255, 255), 1f)) g.DrawPath(pen, p);
            }
            T.Txt(g, "Update  v" + Updater.NewVersion, T.F(9.5f, FontStyle.Bold), Color.FromArgb(52, 36, 0), new RectangleF(0, 0, Width, Height), StringAlignment.Center);
        }
        protected override void OnMouseClick(MouseEventArgs e) { if (Updater.Available && Act != null) Act(); }
    }

    // عدّاد الأيام: إطار معلومات (مو زر): كبسولة داكنة بحدّ أخضر خفيف، بدون لمعة ولا تفاعل
    class DaysBadge : VC
    {
        public string Text2; public Color? LabelColor;
        public DaysBadge() { Cursor = Cursors.Default; SetStyle(ControlStyles.Selectable, false); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; T.Setup(g);
            RectangleF r = new RectangleF(1, 3, Width - 3, Height - 7);
            using (GraphicsPath p = T.RR(r, r.Height / 2f))
            {
                using (Brush b = new SolidBrush(Color.FromArgb(70, 4, 18, 10))) g.FillPath(b, p);
                using (Pen pen = new Pen(Color.FromArgb(120, 52, 199, 89), 1.2f)) g.DrawPath(pen, p);
            }
            T.Txt(g, Text2 ?? "", T.F(9.5f, FontStyle.Bold), LabelColor ?? T.Dim, new RectangleF(0, 0, Width, Height), StringAlignment.Center);
        }
    }
    // نافذة إدخال نص بنمط البطاقة
    class InputBox : Form, IBackHost
    {
        [DllImport("user32")] static extern bool ReleaseCapture();
        [DllImport("user32")] static extern IntPtr SendMessage(IntPtr h, int msg, int w, int l);
        Bitmap bgBmp; public Bitmap Bg { get { return bgBmp; } }
        readonly TextBox tb = new TextBox(); const int W = 480, H = 240;
        InputBox(string title, string prompt, string value)
        {
            FormBorderStyle = FormBorderStyle.None; StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false;
            ClientSize = new Size(W, H); BackColor = T.WindowBack; DoubleBuffered = true;
            Region = new Region(T.RR(new RectangleF(0, 0, Width, Height), 20));
            Bitmap bg = bgBmp = new Bitmap(W, H);
            using (Graphics g = Graphics.FromImage(bg))
            {
                T.Setup(g);
                using (LinearGradientBrush b = new LinearGradientBrush(new Rectangle(0, 0, W, H), Color.FromArgb(52, 82, 64), Color.FromArgb(15, 33, 22), 62f)) g.FillRectangle(b, 0, 0, W, H);
                T.Card(g, new RectangleF(1, 1, W - 3, H - 3), 20);
                T.FieldBox(g, new RectangleF(28, 96, W - 56, 44));
            }
            tb.BorderStyle = BorderStyle.None; tb.BackColor = T.Field; tb.ForeColor = T.Text; tb.Font = T.F(11f); tb.Bounds = new Rectangle(44, 108, W - 88, 24); tb.Text = value ?? ""; Controls.Add(tb);
            Controls.Add(new Cmd("OK", delegate { DialogResult = DialogResult.OK; }, true) { Bounds = new Rectangle(W - 28 - 100, 178, 100, 40), Tag = bg });
            Controls.Add(new Cmd("Cancel", delegate { DialogResult = DialogResult.Cancel; }, false) { Bounds = new Rectangle(W - 28 - 100 - 8 - 100, 178, 100, 40), Tag = bg });
            MouseDown += delegate(object o, MouseEventArgs e) { if (e.Button == MouseButtons.Left) { ReleaseCapture(); SendMessage(Handle, 0xA1, 2, 0); } };
            Paint += delegate(object o, PaintEventArgs e)
            {
                e.Graphics.DrawImageUnscaled(bg, 0, 0); T.Setup(e.Graphics);
                T.Txt(e.Graphics, title, T.F(15, FontStyle.Bold), T.Text, new RectangleF(28, 16, W - 56, 30), StringAlignment.Near);
                T.Txt(e.Graphics, prompt, T.F(9.5f), T.Dim, new RectangleF(28, 52, W - 56, 36), StringAlignment.Near);
            };
        }
        protected override bool ProcessCmdKey(ref Message m, Keys k)
        {
            if (k == Keys.Enter) { DialogResult = DialogResult.OK; return true; }
            if (k == Keys.Escape) { DialogResult = DialogResult.Cancel; return true; }
            return base.ProcessCmdKey(ref m, k);
        }
        public static string Ask(IWin32Window owner, string title, string prompt, string value)
        {
            using (var f = new InputBox(title, prompt, value)) return f.ShowDialog(owner) == DialogResult.OK ? f.tb.Text : null;
        }
    }

    // ---------- الفلاتر المخصصة ----------
    class FilterDef
    {
        public string Name; public int[] V; public int[] Def; public bool Builtin;
        public FilterDef Clone() { return new FilterDef { Name = Name, V = (int[])V.Clone(), Def = Def == null ? null : (int[])Def.Clone(), Builtin = Builtin }; }
    }

    static class FilterStore
    {
        public static readonly string[] Props = { "brightness", "contrast", "gamma", "saturation", "deband-iterations", "deband-threshold", "deband-grain" };
        public static readonly string[] Labels = { "Brightness", "Contrast", "Gamma", "Saturation", "Banding fix passes", "Banding fix strength", "Grain" };
        public static readonly int[] Min = { -40, -40, -40, -40, 0, 5, 0 }, Max = { 40, 40, 40, 40, 6, 90, 40 };
        public static List<FilterDef> Items = new List<FilterDef>(); static string path;

        static FilterDef B(string n, params int[] v) { return new FilterDef { Name = n, V = (int[])v.Clone(), Def = (int[])v.Clone(), Builtin = true }; }
        public static void Load(string file)
        {
            path = file; Items = new List<FilterDef> { B("Off", 0, 0, 0, 0, 2, 35, 8), B("Night", 4, 8, 10, 6, 4, 48, 14), B("Day", -3, 10, -5, 10, 2, 38, 8) };
            try
            {
                if (!System.IO.File.Exists(file)) return;
                foreach (string line in System.IO.File.ReadAllLines(file, System.Text.Encoding.UTF8))
                {
                    string[] p = line.Split('|'); if (p.Length < 2) continue;
                    string[] vs = p[1].Split(','); if (vs.Length != Props.Length) continue;
                    int[] v = new int[vs.Length]; bool ok = true; for (int i = 0; i < v.Length; i++) if (!int.TryParse(vs[i], out v[i])) ok = false;
                    if (!ok) continue;
                    var ex = Items.Find(f => f.Name.Equals(p[0], StringComparison.OrdinalIgnoreCase));
                    if (ex != null) { if (ex.Name != "Off") ex.V = v; }
                    else Items.Add(new FilterDef { Name = p[0], V = v });
                }
            }
            catch { }
        }
        public static void Save()
        {
            try { System.IO.File.WriteAllLines(path, Items.FindAll(f => f.Name != "Off").ConvertAll(f => f.Name + "|" + string.Join(",", Array.ConvertAll(f.V, n => n.ToString()))).ToArray(), new System.Text.UTF8Encoding(false)); } catch { }
        }
        public static string UniqueName(string n, FilterDef self)
        {
            n = (n ?? "").Trim().Replace("|", " "); if (n.Length == 0) n = "My filter"; string b = n; int k = 2;
            while (Items.Exists(f => f != self && f.Name.Equals(n, StringComparison.OrdinalIgnoreCase))) n = b + " " + (k++);
            return n;
        }
    }

    // شريط قيمة بنمط الهوية (للمحرر)
    class ParamSlider : VC
    {
        public int Min, Max, Value; public string Label; public event Action Changed; bool drag;
        public ParamSlider(string label, int min, int max, int v) { Label = label; Min = min; Max = max; Value = v; }
        float X0 { get { return 8; } } float X1 { get { return Width - 8; } }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; T.Setup(g);
            T.Txt(g, Label, T.F(9.5f, FontStyle.Bold), T.Text, new RectangleF(2, 0, Width / 2f, 20), StringAlignment.Near);
            T.Txt(g, (Value > 0 && Min < 0 ? "+" : "") + Value, T.F(9.5f, FontStyle.Bold), Color.FromArgb(255, 214, 54), new RectangleF(Width / 2f, 0, Width / 2f - 2, 20), StringAlignment.Far);
            float cy = 31; float frac = (Value - Min) / (float)(Max - Min), px = X0 + (X1 - X0) * frac;
            using (Brush b = new SolidBrush(Color.FromArgb(70, 255, 255, 255))) g.FillRectangle(b, X0, cy - 2, X1 - X0, 4);
            if (Min < 0) { float zx = X0 + (X1 - X0) * (-Min / (float)(Max - Min)); using (Brush b = new SolidBrush(Color.FromArgb(120, 255, 255, 255))) g.FillRectangle(b, zx - 1, cy - 6, 2, 12); }
            using (Brush b = new SolidBrush(Color.FromArgb(52, 199, 89))) { float zx = Min < 0 ? X0 + (X1 - X0) * (-Min / (float)(Max - Min)) : X0; g.FillRectangle(b, Math.Min(zx, px), cy - 2, Math.Abs(px - zx), 4); }
            using (Brush b = new SolidBrush(Color.White)) g.FillEllipse(b, px - 7, cy - 7, 14, 14);
        }
        void SetFrom(int x) { float f = (x - X0) / (X1 - X0); f = Math.Max(0, Math.Min(1, f)); int v = (int)Math.Round(Min + f * (Max - Min)); if (v != Value) { Value = v; Invalidate(); if (Changed != null) Changed(); } }
        protected override void OnMouseDown(MouseEventArgs e) { drag = true; Capture = true; SetFrom(e.X); }
        protected override void OnMouseMove(MouseEventArgs e) { if (drag) SetFrom(e.X); base.OnMouseMove(e); }
        protected override void OnMouseUp(MouseEventArgs e) { drag = false; Capture = false; }
    }

    // محرر الفلتر: اسم + قيم، ويطبّق لحظياً على الفيديو أثناء السحب
    class FilterEditor : Form, IBackHost
    {
        [DllImport("user32")] static extern bool ReleaseCapture();
        [DllImport("user32")] static extern IntPtr SendMessage(IntPtr h, int msg, int w, int l);
        Bitmap bgBmp; public Bitmap Bg { get { return bgBmp; } }
        public bool Deleted; public FilterDef Work; readonly TextBox tb = new TextBox(); readonly List<ParamSlider> sl = new List<ParamSlider>();
        const int W = 520, H = 580;
        public FilterEditor(FilterDef work, bool isNew, Action<int[]> preview)
        {
            Work = work;
            FormBorderStyle = FormBorderStyle.None; StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false;
            ClientSize = new Size(W, H); BackColor = T.WindowBack; DoubleBuffered = true;
            Region = new Region(T.RR(new RectangleF(0, 0, Width, Height), 20));
            Bitmap bg = bgBmp = new Bitmap(W, H);
            using (Graphics g = Graphics.FromImage(bg))
            {
                T.Setup(g);
                using (LinearGradientBrush b = new LinearGradientBrush(new Rectangle(0, 0, W, H), Color.FromArgb(52, 82, 64), Color.FromArgb(15, 33, 22), 62f)) g.FillRectangle(b, 0, 0, W, H);
                T.Card(g, new RectangleF(1, 1, W - 3, H - 3), 20);
                T.FieldBox(g, new RectangleF(28, 84, W - 56, 44));
            }
            tb.BorderStyle = BorderStyle.None; tb.BackColor = T.Field; tb.ForeColor = T.Text; tb.Font = T.F(11f); tb.Bounds = new Rectangle(44, 96, W - 88, 24); tb.Text = work.Name; tb.MaxLength = 28;
            tb.ReadOnly = work.Name == "Night" || work.Name == "Day"; tb.GotFocus += (s, e) => tb.SelectAll(); Controls.Add(tb);
            for (int i = 0; i < FilterStore.Props.Length; i++)
            {
                int k = i; var s = new ParamSlider(FilterStore.Labels[i], FilterStore.Min[i], FilterStore.Max[i], work.V[i]) { Bounds = new Rectangle(28, 146 + i * 46, W - 56, 42), Tag = bg };
                s.Changed += () => { work.V[k] = s.Value; if (preview != null) preview(work.V); };
                sl.Add(s); Controls.Add(s);
            }
            int by = H - 62;
            Controls.Add(new Cmd("Save", delegate { Work.Name = work.Builtin ? work.Name : FilterStore.UniqueName(tb.Text, isNew ? null : work); DialogResult = DialogResult.OK; }, true) { Bounds = new Rectangle(W - 28 - 110, by, 110, 40), Tag = bg });
            Controls.Add(new Cmd("Cancel", delegate { DialogResult = DialogResult.Cancel; }, false) { Bounds = new Rectangle(W - 28 - 110 - 8 - 100, by, 100, 40), Tag = bg });
            if (work.Builtin && work.Name != "Off" && work.Def != null)
                Controls.Add(new Cmd("Reset", delegate { for (int i = 0; i < work.V.Length; i++) { work.V[i] = work.Def[i]; sl[i].Value = work.V[i]; sl[i].Invalidate(); } if (preview != null) preview(work.V); }, false) { Bounds = new Rectangle(28, by, 100, 40), Tag = bg });
            if (!work.Builtin && !isNew)
                Controls.Add(new Cmd("Delete", delegate { Deleted = true; DialogResult = DialogResult.OK; }, false) { Bounds = new Rectangle(28, by, 100, 40), LabelColor = Color.FromArgb(255, 150, 150), Tag = bg });
            MouseDown += delegate(object o, MouseEventArgs e) { if (e.Button == MouseButtons.Left && e.Y < 70) { ReleaseCapture(); SendMessage(Handle, 0xA1, 2, 0); } };
            Paint += delegate(object o, PaintEventArgs e)
            {
                e.Graphics.DrawImageUnscaled(bg, 0, 0); T.Setup(e.Graphics);
                T.Txt(e.Graphics, isNew ? "Create filter" : "Edit filter", T.F(16, FontStyle.Bold), T.Text, new RectangleF(28, 16, W - 56, 32), StringAlignment.Near);
                T.Txt(e.Graphics, "Changes show live on the video while you drag.", T.F(9.5f), T.Dim, new RectangleF(28, 48, W - 56, 22), StringAlignment.Near);
            };
        }
        protected override bool ProcessCmdKey(ref Message m, Keys k) { if (k == Keys.Escape) { DialogResult = DialogResult.Cancel; return true; } return base.ProcessCmdKey(ref m, k); }
    }

    // قائمة الفلاتر المنبثقة: صف لكل فلتر (اضغط للاختيار) وترس على اليمين للتعديل، وآخرها "Create filter"
    class FilterPopup : Form
    {
        readonly List<FilterDef> items; int cur, hover = -1; bool hoverGear; readonly Action<int> onPick, onEdit; readonly Action onCreate; readonly Control owner; const int RH = 38;
        public FilterPopup(Control owner, List<FilterDef> items, int cur, Action<int> onPick, Action<int> onEdit, Action onCreate)
        {
            this.owner = owner; this.items = items; this.cur = cur; this.onPick = onPick; this.onEdit = onEdit; this.onCreate = onCreate;
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual; TopMost = true; DoubleBuffered = true; BackColor = T.WindowBack;
            Deactivate += (s, e) => Close();
        }
        public void ShowAt(Point screen, int width)
        {
            ClientSize = new Size(Math.Max(width, 300), (items.Count + 1) * RH + 16); Location = screen;
            Region = new Region(T.RR(new RectangleF(0, 0, Width, Height), 14)); Show();
        }
        int RowAt(int y) { int r = (y - 8) / RH; return (y < 8 || r > items.Count) ? -1 : r; }
        protected override void OnMouseMove(MouseEventArgs e) { int r = RowAt(e.Y); bool gz = e.X > Width - 44 && r >= 0 && r < items.Count && items[r].Name != "Off"; if (r != hover || gz != hoverGear) { hover = r; hoverGear = gz; Invalidate(); } }
        protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            int r = RowAt(e.Y); if (r < 0) return;
            Action act;
            if (r == items.Count) act = onCreate;
            else if (e.X > Width - 44 && items[r].Name != "Off") act = () => onEdit(r);
            else act = () => onPick(r);
            owner.BeginInvoke(act); Close();
        }
        static void Gear(Graphics g, float cx, float cy, float r, Color c)
        {
            using (Pen p = new Pen(c, 1.8f))
            {
                g.DrawEllipse(p, cx - r * 0.5f, cy - r * 0.5f, r, r);
                for (int i = 0; i < 8; i++) { double a = Math.PI / 4 * i; g.DrawLine(p, (float)(cx + Math.Cos(a) * r * 0.62), (float)(cy + Math.Sin(a) * r * 0.62), (float)(cx + Math.Cos(a) * r * 0.95), (float)(cy + Math.Sin(a) * r * 0.95)); }
            }
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; T.Setup(g);
            using (LinearGradientBrush b = new LinearGradientBrush(new Rectangle(0, 0, Width, Height), Color.FromArgb(46, 74, 58), Color.FromArgb(14, 30, 20), 70f)) g.FillRectangle(b, 0, 0, Width, Height);
            using (Pen p = new Pen(Color.FromArgb(110, 255, 255, 255))) using (GraphicsPath gp = T.RR(new RectangleF(0.5f, 0.5f, Width - 2, Height - 2), 14)) g.DrawPath(p, gp);
            for (int i = 0; i <= items.Count; i++)
            {
                RectangleF row = new RectangleF(8, 8 + i * RH, Width - 16, RH - 2);
                if (i == hover) using (GraphicsPath gp = T.RR(row, 9)) using (Brush b = new SolidBrush(Color.FromArgb(i == items.Count ? 70 : 50, 52, 199, 89))) g.FillPath(b, gp);
                if (i == items.Count)
                {
                    T.Txt(g, "+  Create filter…", T.F(10f, FontStyle.Bold), Color.FromArgb(255, 214, 54), new RectangleF(row.X + 12, row.Y, row.Width - 20, row.Height), StringAlignment.Near);
                    continue;
                }
                if (i == cur) using (Brush b = new SolidBrush(Color.FromArgb(52, 199, 89))) g.FillEllipse(b, row.X + 10, row.Y + row.Height / 2f - 4, 8, 8);
                T.Txt(g, items[i].Name, T.F(10f, i == cur ? FontStyle.Bold : FontStyle.Regular), T.Text, new RectangleF(row.X + 28, row.Y, row.Width - 80, row.Height), StringAlignment.Near);
                if (items[i].Name != "Off") Gear(g, row.Right - 20, row.Y + row.Height / 2f, 8f, (i == hover && hoverGear) ? Color.FromArgb(255, 214, 54) : T.Dim);
            }
        }
    }}
