// BuNgaim TV — الأفلام والمسلسلات (Xtream: get_vod_streams / get_series / get_series_info)
// نفس mpv ونفس مستويات التحسين والفلاتر والتخفيف التلقائي مثل البث المباشر؛ الجديد هنا القوائم، صفحة التفاصيل (بوستر + قصة)،
// حلقات المسلسل بالمواسم، وإكمال المشاهدة من حيث وقفت.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace NextBoost
{
    // اتصال بمهلة وبضغط gzip (قوائم الأفلام تكون عشرات الميغابايت)
    class Http : WebClient
    {
        public int Ms = 30000;
        protected override WebRequest GetWebRequest(Uri a)
        {
            WebRequest r = base.GetWebRequest(a); r.Timeout = Ms;
            HttpWebRequest h = r as HttpWebRequest; if (h != null) h.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            return r;
        }
    }

    // قراءة JSON بدون ما تنكسر لو الحقل ناقص أو شكله تغيّر (بعض السيرفرات ترجع [] مكان {})
    static class Jx
    {
        public static object Parse(string t) { return new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 200 }.DeserializeObject(t); }
        public static string Str(object o) { return o == null ? "" : Convert.ToString(o, CultureInfo.InvariantCulture).Trim().Replace("\\'", "'").Replace("\\\"", "\""); }   // بعض السيرفرات ترسل \' بالأسماء
        public static object Get(object o, string k) { var d = o as Dictionary<string, object>; object v; return d != null && d.TryGetValue(k, out v) ? v : null; }
        public static string S(object o, string k) { object v = Get(o, k); return v is string || v == null || v is ValueType ? Str(v) : ""; }
        public static double Num(string s) { double d; return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out d) ? d : 0; }
        public static long Long(string s) { long l; if (long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out l)) return l; return (long)Num(s); }
        public static IEnumerable<object> Arr(object o)
        {
            var e = o as System.Collections.IEnumerable;
            if (e == null || o is string || o is Dictionary<string, object>) return new object[0];
            return e.Cast<object>();
        }
    }

    class Cred { public string Host, User, Pass; }

    // حالة كل قسم (بث / أفلام / مسلسلات): قوائمه وتصنيفه المختار وبحثه، عشان التنقل بين الأقسام ما يضيّع مكانك
    class Sect
    {
        public List<Channel> All = new List<Channel>(); public List<string> Cats = new List<string>(), Order = new List<string>();
        public bool Loaded, FavOn; public int CatIdx, Chip; public string Search = "";
    }

    // مكان توقف كل فيلم وحلقة (مفتاح|الثانية|المدة)، يتحفظ كل ٢٠ ثانية وعند الإغلاق
    class WatchStore
    {
        readonly Dictionary<string, double[]> d = new Dictionary<string, double[]>(); readonly object gate = new object();
        string path; DateTime lastSave = DateTime.MinValue; bool dirty;
        static string Key(Channel c) { return (c.Kind == "ep" ? "e" : "m") + c.Id; }
        public void Open(string p)
        {
            lock (gate)
            {
                Save(); path = p; d.Clear(); dirty = false;
                try { if (File.Exists(p)) foreach (string l in File.ReadAllLines(p, Encoding.UTF8)) { string[] f = l.Split('|'); if (f.Length >= 3) d[f[0]] = new[] { Jx.Num(f[1]), Jx.Num(f[2]) }; } } catch { }
            }
        }
        public bool Get(Channel c, out double pos, out double dur)
        {
            pos = 0; dur = 0; double[] v;
            lock (gate) { if (!d.TryGetValue(Key(c), out v)) return false; pos = v[0]; dur = v[1]; }
            return true;
        }
        public void Set(Channel c, double pos, double dur)
        {
            lock (gate) { d[Key(c)] = new[] { pos, dur }; dirty = true; if ((DateTime.Now - lastSave).TotalSeconds > 20) Save(); }
        }
        public void Done(Channel c)
        {
            lock (gate) { double[] v; d.TryGetValue(Key(c), out v); double dur = v != null && v[1] > 0 ? v[1] : 1; d[Key(c)] = new[] { dur, dur }; dirty = true; }
        }
        public void Flush() { lock (gate) Save(); }
        void Save()   // يُستدعى داخل القفل
        {
            if (!dirty || path == null) return; lastSave = DateTime.Now; dirty = false;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllLines(path, d.Select(kv => kv.Key + "|" + kv.Value[0].ToString("0", CultureInfo.InvariantCulture) + "|" + kv.Value[1].ToString("0", CultureInfo.InvariantCulture)).ToArray(), new UTF8Encoding(false));
            }
            catch { }
        }
    }

    // البوسترات: تنزيل بالخلفية مرة وحدة، كاش على القرص (مجلد الحساب) وكاش صغير بالذاكرة
    static class Posters
    {
        static readonly Dictionary<string, Bitmap> mem = new Dictionary<string, Bitmap>(); static readonly List<string> order = new List<string>();
        static readonly HashSet<string> busy = new HashSet<string>(), failed = new HashSet<string>();
        public static string Dir;
        static string Key(string url) { byte[] h = System.Security.Cryptography.SHA1.Create().ComputeHash(Encoding.UTF8.GetBytes(url)); return BitConverter.ToString(h, 0, 8).Replace("-", "").ToLowerInvariant(); }

        // من خيط الواجهة فقط: يرجّع الصورة لو جاهزة، وإلا يبدأ تنزيلها ويستدعي done لما تجهز
        public static Bitmap Get(string url, System.ComponentModel.ISynchronizeInvoke ui, Action done)
        {
            if (string.IsNullOrEmpty(url) || !url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return null;
            Bitmap b; if (mem.TryGetValue(url, out b)) return b;
            if (busy.Contains(url) || failed.Contains(url)) return null;
            busy.Add(url); string dir = Dir;
            ThreadPool.QueueUserWorkItem(delegate
            {
                Bitmap bm = null;
                try
                {
                    string f = dir != null ? Path.Combine(dir, Key(url) + ".img") : null; byte[] bytes;
                    if (f != null && File.Exists(f)) bytes = File.ReadAllBytes(f);
                    else
                    {
                        ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;
                        using (var wc = new Http { Ms = 15000 }) { wc.Headers["User-Agent"] = "VLC/3.0.20"; bytes = wc.DownloadData(url); }
                        if (bytes.Length > 8 * 1024 * 1024) throw new Exception("poster too big");
                        if (f != null) { Directory.CreateDirectory(dir); File.WriteAllBytes(f, bytes); }
                    }
                    using (var ms = new MemoryStream(bytes)) using (Image img = Image.FromStream(ms)) bm = Fit(img, 520);
                }
                catch (Exception ex) { Log.Write("poster: " + ex.Message); }
                try
                {
                    ui.BeginInvoke(new Action(() =>
                    {
                        busy.Remove(url);
                        if (bm != null)
                        {
                            mem[url] = bm; order.Add(url);
                            while (order.Count > 40) { Bitmap old; if (mem.TryGetValue(order[0], out old)) { mem.Remove(order[0]); old.Dispose(); } order.RemoveAt(0); }
                        }
                        else failed.Add(url);
                        done();
                    }), null);
                }
                catch { if (bm != null) bm.Dispose(); }
            });
            return null;
        }
        // كاش القرص ما يكبر للأبد: نخلي آخر 800 صورة
        public static void Prune(string dir, int keep)
        {
            try
            {
                if (!Directory.Exists(dir)) return;
                foreach (FileInfo f in new DirectoryInfo(dir).GetFiles("*.img").OrderByDescending(x => x.LastWriteTime).Skip(keep).ToList()) { try { f.Delete(); } catch { } }
            }
            catch { }
        }
        static Bitmap Fit(Image img, int maxH)
        {
            float k = img.Height > maxH ? (float)maxH / img.Height : 1f; int w = Math.Max(1, (int)(img.Width * k)), h = Math.Max(1, (int)(img.Height * k));
            Bitmap b = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using (Graphics g = Graphics.FromImage(b)) { g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.DrawImage(img, 0, 0, w, h); }
            return b;
        }
    }

    // زر الرجوع من الحلقات لقائمة المسلسلات
    class BackBtn : Cmd
    {
        public BackBtn(Action act) : base("Back", act, false) { }
        protected override void OnPaint(PaintEventArgs e) { base.OnPaint(e); T.Icon(e.Graphics, "back", 17, Height / 2f, 9f, T.Text); }
    }

    // شريط التحكم فوق الفيديو بوضع الشاشة الكاملة: نافذة مستقلة (نافذة mpv تغطي أي عنصر داخل نافذة البرنامج)، يطلع لما تحرك الماوس ويختفي بعد 3 ثواني
    class FsBar : Form, IBackHost
    {
        public const int W = 640, HL = 66, HV = 108; Bitmap bg; public Bitmap Bg { get { return bg; } }
        public VolSlide Vol = new VolSlide(); public Cmd Mute, Pb; public SeekBar Seek = new SeekBar(); readonly List<Control> row = new List<Control>(); public bool Vod; Action liveAct, pauseAct;
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.ExStyle |= 0x08000000 | 0x80; return cp; } }   // NOACTIVATE | TOOLWINDOW
        public FsBar(Action<int> seek, Action toggleMute, Action exit, Action pause)
        {
            pauseAct = pause; liveAct = delegate { seek(99999); };
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true; StartPosition = FormStartPosition.Manual; BackColor = T.WindowBack;
            int x = 14, h = 40;
            string[] lab = { "-10s", "Live", "+10s" }; int[] sec = { -10, 99999, 10 };   // -10 | تشغيل/إيقاف (أو Live بالبث) | +10
            for (int i = 0; i < 3; i++) { int s = sec[i]; var c = new Cmd(lab[i], delegate { seek(s); }, false) { Bounds = new Rectangle(x, 0, 66, h) }; row.Add(c); if (i == 1) Pb = c; x += 72; }
            Mute = new Cmd("", toggleMute, false) { Glyph = "vol", Bounds = new Rectangle(x + 4, 0, 44, h) }; row.Add(Mute); x += 56;
            Vol.Bounds = new Rectangle(x, 0, W - 14 - 56 - 12 - x, h); row.Add(Vol);
            row.Add(new Cmd("", exit, true) { Glyph = "full", Bounds = new Rectangle(W - 14 - 56, 0, 56, h) });   // زر الخروج أيقونة فقط
            foreach (Control c in row) Controls.Add(c);
            Seek.Bounds = new Rectangle(14, 10, W - 28, 36); Controls.Add(Seek);
            SetVod(false);
        }
        // أفلام ومسلسلات: صف إضافي فيه شريط سحب للتقديم لأي مكان
        public void SetVod(bool v)
        {
            Vod = v; int H = v ? HV : HL; ClientSize = new Size(W, H); Region = new Region(T.RR(new RectangleF(0, 0, W, H), 20));
            bg = new Bitmap(W, H);
            using (Graphics g = Graphics.FromImage(bg))
            {
                T.Setup(g);
                using (LinearGradientBrush b = new LinearGradientBrush(new Rectangle(0, 0, W, H), Color.FromArgb(52, 82, 64), Color.FromArgb(15, 33, 22), 62f)) g.FillRectangle(b, 0, 0, W, H);
                T.Card(g, new RectangleF(1, 1, W - 3, H - 3), 20);
            }
            foreach (Control c in row) c.Top = H - 13 - 40;
            Seek.Visible = v; Pb.Text2 = v ? "" : "Live"; Pb.Glyph = v ? "pause" : null; Pb.Act = v ? pauseAct : liveAct;   // أفلام ومسلسلات: زر تشغيل/إيقاف مكان Live
            Pb.Invalidate(); Invalidate();
        }
        protected override void OnPaint(PaintEventArgs e) { e.Graphics.DrawImageUnscaled(bg, 0, 0); }
    }

    // شريط سحب للتقديم والتأخير (للأفلام والمسلسلات): تسحب الدائرة وتفلت فيقفز mpv للمكان
    class SeekBar : VC
    {
        public double Frac; public string Label = ""; public bool Dragging; public event Action<double> Commit;
        const int LabelW = 150;
        float L { get { return 8; } } float Rr { get { return Width - LabelW - 6; } }
        void SetFrom(int mx) { Frac = Math.Max(0, Math.Min(1, (mx - L) / (Rr - L))); Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; T.Setup(g); float cy = Height / 2f, x = L + (float)Frac * (Rr - L);
            using (Pen p = new Pen(Color.FromArgb(60, 255, 255, 255), 6) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawLine(p, L, cy, Rr, cy);
            using (Pen p = new Pen(T.Green, 6) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawLine(p, L, cy, Math.Max(L + 0.1f, x), cy);
            using (Brush b = new SolidBrush(Color.FromArgb(60, 0, 0, 0))) g.FillEllipse(b, x - 9, cy - 8, 20, 20);
            using (Brush b = new SolidBrush(Color.White)) g.FillEllipse(b, x - 10, cy - 10, 20, 20);
            T.Txt(g, Label, T.F(9.5f, FontStyle.Bold), T.Text, new RectangleF(Width - LabelW, 0, LabelW, Height), StringAlignment.Center);
        }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { Dragging = true; Capture = true; SetFrom(e.X); } }
        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (Dragging) SetFrom(e.X); }
        protected override void OnMouseUp(MouseEventArgs e) { if (!Dragging) return; Dragging = false; Capture = false; SetFrom(e.X); if (Commit != null) Commit(Frac); }
    }

    class CatItem { public string Text; public bool Sep, Checked; public Action Act; }

    // قائمة التصنيفات: نافذة منبثقة بشريط تمرير وعجلة ماوس، ترسم الصفوف الظاهرة بس (سريعة حتى مع مئات التصنيفات)
    class CatPopup : Form, IBackHost
    {
        public const int RowH = 30, Pad = 8;
        readonly List<CatItem> items; int top, hover = -1; readonly VScroll sb = new VScroll(); readonly Bitmap bg; public Bitmap Bg { get { return bg; } }
        int Vis { get { return Math.Max(1, (Height - 2 * Pad) / RowH); } }
        public CatPopup(List<CatItem> it, int w, int h)
        {
            items = it; FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual; ClientSize = new Size(w, h); BackColor = T.WindowBack; DoubleBuffered = true;
            Region = new Region(T.RR(new RectangleF(0, 0, w, h), 14));
            bg = new Bitmap(w, h);
            using (Graphics g = Graphics.FromImage(bg))
            {
                T.Setup(g);
                using (LinearGradientBrush b = new LinearGradientBrush(new Rectangle(0, 0, w, h + 1), Color.FromArgb(34, 68, 51), Color.FromArgb(20, 44, 33), 90f)) g.FillRectangle(b, 0, 0, w, h);
                using (GraphicsPath p = T.RR(new RectangleF(0.5f, 0.5f, w - 1.5f, h - 1.5f), 14)) using (Pen pen = new Pen(Color.FromArgb(70, 255, 255, 255), 1.2f)) g.DrawPath(pen, p);
            }
            sb.Bounds = new Rectangle(w - 18, Pad + 2, 12, h - 2 * Pad - 4); sb.Total = items.Count; sb.VisibleCount = Vis; sb.Scrolled += i => SetTop(i);
            sb.Visible = items.Count > Vis; Controls.Add(sb);
            int c = items.FindIndex(x => x.Checked); if (c > 3) SetTop(c - 3);
        }
        void SetTop(int t)
        {
            top = Math.Max(0, Math.Min(Math.Max(0, items.Count - Vis), t)); sb.TopIndex = top; sb.Invalidate(); Invalidate();
        }
        int IndexAt(int y) { int r = (y - Pad) / RowH; int i = top + r; return y >= Pad && r < Vis && i < items.Count ? i : -1; }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; T.Setup(g); g.DrawImageUnscaled(bg, 0, 0); int rw = Width - 2 * Pad - (sb.Visible ? 14 : 0);
            for (int r = 0; r < Vis && top + r < items.Count; r++)
            {
                CatItem it = items[top + r]; Rectangle row = new Rectangle(Pad, Pad + r * RowH, rw, RowH);
                if (it.Sep) { using (Pen p = new Pen(Color.FromArgb(50, 255, 255, 255))) g.DrawLine(p, row.X + 8, row.Y + RowH / 2, row.Right - 8, row.Y + RowH / 2); continue; }
                bool hot = top + r == hover;
                if (hot)
                    using (GraphicsPath p = T.RR(new RectangleF(row.X, row.Y + 1, row.Width, row.Height - 2), 9))
                    {
                        using (LinearGradientBrush b = new LinearGradientBrush(new Rectangle(row.X, row.Y, row.Width, row.Height + 1), Color.FromArgb(150, 52, 199, 89), Color.FromArgb(150, 22, 120, 46), 90f)) g.FillPath(b, p);
                        using (Pen pen = new Pen(Color.FromArgb(90, 255, 255, 255))) g.DrawPath(pen, p);
                    }
                if (it.Checked) using (Brush b = new SolidBrush(hot ? Color.White : T.Green)) g.FillEllipse(b, row.X + 10, row.Y + RowH / 2 - 4, 8, 8);
                T.Txt(g, it.Text, T.F(9.5f), hot ? Color.White : T.Text, new RectangleF(row.X + 28, row.Y, row.Width - 36, row.Height), StringAlignment.Near);
            }
        }
        protected override void OnMouseMove(MouseEventArgs e) { int i = IndexAt(e.Y); if (i != hover) { hover = i; Invalidate(); } base.OnMouseMove(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseWheel(MouseEventArgs e) { SetTop(top + (e.Delta > 0 ? -3 : 3)); }
        protected override void OnMouseClick(MouseEventArgs e)
        {
            int i = IndexAt(e.Y); if (e.Button != MouseButtons.Left || i < 0 || items[i].Sep) return;
            Action act = items[i].Act; Close(); if (act != null) act();
        }
        protected override void OnDeactivate(EventArgs e) { base.OnDeactivate(e); Close(); }
        protected override bool ProcessCmdKey(ref Message m, Keys k) { if (k == Keys.Escape) { Close(); return true; } return base.ProcessCmdKey(ref m, k); }
    }

    partial class MainForm
    {
        FsBar fsBar; volatile bool fsVod, fsLive; double curPos, curDur, curCache; System.Windows.Forms.Timer fsTimer; Point fsLast; DateTime fsMoved;

        // ---------- شريط التشغيل تحت الفيديو (أفلام ومسلسلات): إيقاف/تشغيل وسحب للتقديم بدون ملء الشاشة ----------
        Cmd btnPause; SeekBar seekMain; bool paused; System.Windows.Forms.Timer trTimer;
        bool TransportOn { get { return playing && fsVod; } }
        void TogglePause() { if (curIpc != null) curIpc.Send("{\"command\":[\"cycle\",\"pause\"]}"); }
        const double LiveWindow = 120;   // البث المباشر: الشريط يغطي آخر دقيقتين (يمين = على الهواء)
        void SeekPct(double f)
        {
            if (curIpc == null) return;
            if (fsLive) { double t = f >= 0.98 ? curCache : Math.Max(0, curCache - (1 - f) * LiveWindow); curIpc.Send("{\"command\":[\"seek\"," + t.ToString("0.##", Inv) + ",\"absolute\"]}"); return; }
            curIpc.Send("{\"command\":[\"seek\"," + (f * 100).ToString("0.###", Inv) + ",\"absolute-percent\"]}");
        }
        // موضع الشريط والنص: فيلم = الوقت/المدة؛ بث = كم متأخر عن الهواء
        void SeekModel(out double frac, out string label)
        {
            if (!fsLive) { double d = curDur, ps = curPos; frac = d > 0 ? Math.Min(1, ps / d) : 0; label = Clock(ps) + " / " + Clock(d); return; }
            double lag = curCache > 0 ? Math.Max(0, curCache - curPos) : 0; frac = 1 - Math.Min(lag, LiveWindow) / LiveWindow; label = lag < 4 ? "LIVE" : "-" + Clock(lag);
        }
        void SyncPause()
        {
            btnPause.Glyph = paused ? "play" : "pause"; btnPause.Invalidate();
            if (fsBar != null && fsBar.Vod) { fsBar.Pb.Glyph = paused ? "play" : "pause"; fsBar.Pb.Invalidate(); }
        }
        void BuildTransport()
        {
            btnPause = new Cmd("", TogglePause, false) { Glyph = "pause" }; seekMain = new SeekBar(); seekMain.Commit += SeekPct;
            foreach (Control c in new Control[] { btnPause, seekMain }) { Controls.Add(c); uiControls.Add(c); }
            trTimer = new System.Windows.Forms.Timer { Interval = 250 };
            trTimer.Tick += (s, e) =>
            {
                if (!seekMain.Visible || seekMain.Dragging) return;
                double fr; string lb; SeekModel(out fr, out lb); seekMain.Frac = fr; seekMain.Label = lb; seekMain.Invalidate();
            };
            trTimer.Start();
        }
        // صف التشغيل: [إيقاف] [شريط التقديم ....] [كتم] [صوت] [ملء الشاشة]؛ لو النافذة ضيقة يرجعون للشريط السفلي
        void PlaceTransport(int x, int y, int w)
        {
            btnPause.Bounds = R(x, y, 46, 38); int right = x + w;
            if (w >= 760)   // الكتم والصوت والملء الشاشة دايماً في هذا الصف (حتى قبل التشغيل)
            {
                btnFull.Bounds = R(right - 130, y + 3, 130, 32); vol.Bounds = R(btnFull.Left - 8 - 220, y + 3, 220, 32); btnMute.Bounds = R(vol.Left - 4 - 40, y + 3, 40, 32);
                right = btnMute.Left - 10; status.Width = Math.Max(100, ClientSize.Width - 2 * status.Left);
            }
            seekMain.Bounds = R(x + 54, y, Math.Max(60, right - (x + 54)), 38);
        }

        void FsSeek(int sec) { if (curIpc != null) curIpc.Send("{\"command\":[\"seek\"," + sec + ",\"relative\"]}"); }

        // يُستدعى عند الدخول للشاشة الكاملة والخروج منها
        void FsState(bool on)
        {
            if (on)
            {
                if (fsBar == null)
                {
                    fsBar = new FsBar(FsSeek, delegate { if (curIpc != null) curIpc.Send("{\"command\":[\"cycle\",\"mute\"]}"); }, delegate { if (videoFull) ToggleVideoFull(); }, TogglePause);
                    fsBar.Vol.Changed += v => vol.Nudge(v);
                    fsBar.Seek.Commit += SeekPct;
                    fsTimer = new System.Windows.Forms.Timer { Interval = 120 }; fsTimer.Tick += FsTick;
                }
                fsLast = Cursor.Position; fsMoved = DateTime.MinValue; fsTimer.Start();
            }
            else { if (fsTimer != null) fsTimer.Stop(); if (fsBar != null) fsBar.Hide(); }
        }

        void FsTick(object s, EventArgs e)
        {
            if (!videoFull || fsBar == null) return;
            // يطلع بس لما تنزّل الماوس لأسفل حافة الشاشة (أو وهو فوق الشريط نفسه)، ويختفي بعد لحظة من ابتعادك
            Point p = Cursor.Position; Rectangle b = Bounds;
            bool overBar = fsBar.Visible && (fsBar.Bounds.Contains(p) || fsBar.Seek.Dragging);
            bool atEdge = p.Y >= b.Bottom - 170 && p.X >= b.Left && p.X < b.Right;
            if (atEdge || overBar) fsMoved = DateTime.Now;
            bool show = (DateTime.Now - fsMoved).TotalSeconds < 1.2;
            if (show && !fsBar.Visible)
            {
                if (fsBar.Vod != fsVod) fsBar.SetVod(fsVod);
                fsBar.Location = new Point(b.Left + (b.Width - FsBar.W) / 2, b.Bottom - fsBar.Height - 16);
                fsBar.Vol.SetSilent(vol.Value); fsBar.Mute.Glyph = muted ? "mute" : "vol"; fsBar.Show(this);
            }
            else if (!show && fsBar.Visible) fsBar.Hide();
            if (fsBar.Visible)
            {
                fsBar.Vol.SetSilent(vol.Value); string gl = muted ? "mute" : "vol"; if (fsBar.Mute.Glyph != gl) { fsBar.Mute.Glyph = gl; fsBar.Mute.Invalidate(); }
                if (fsBar.Vod) { string pg = paused ? "play" : "pause"; if (fsBar.Pb.Glyph != pg) { fsBar.Pb.Glyph = pg; fsBar.Pb.Invalidate(); } }
                if (fsBar.Vod && !fsBar.Seek.Dragging) { double fr; string lb; SeekModel(out fr, out lb); fsBar.Seek.Frac = fr; fsBar.Seek.Label = lb; fsBar.Seek.Invalidate(); }
            }
        }

        const int Live = 0, Movies = 1, Series = 2;
        static readonly string[] LiveChips = { "All", "NFL", "BEIN", "BEIN 4K", "BEIN FHD", "Alwan", "Premier League", "Recent" };
        static readonly string[] VodChips = { "All", "Top rated", "A-Z", "Recent", "Arabic", "Turkish", "Asian", "American" };
        // تجميع التصنيفات حسب النوع (مطابقة على اسم تصنيف السيرفر)
        static readonly Regex[] GroupRx = {
            new Regex(@"خليج|مصر|شام|عرب|سعود|كويت|ام[ا]?رات|إمارات|لبنان|سور|عراق|رمضان|arab|gulf|egypt", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new Regex(@"ترك|turk", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new Regex(@"[اأإآ]?سيو|هند|كور|ياباني|صين|[اأإآ]نمي|asia|korea|india|anime|japan|chin", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new Regex(@"[اأإآ]جنب|[اأإآ]مريك|نتفليكس|netflix|hollywood|english|american|(^|\s)[اأإآ]بل(\s|$)|apple", RegexOptions.IgnoreCase | RegexOptions.Compiled) };
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        int mode, accountGen, startMode; bool suppress, seriesBusy, vodAutoDone;
        Sect[] sects = new Sect[3];
        Tog tgLive, tgMovies, tgSeries; BackBtn btnBack; System.Windows.Forms.Timer detailTimer;
        Channel openSeries; SortedDictionary<int, List<Channel>> epSeasons; int curSeason;
        readonly WatchStore store = new WatchStore();
        List<Channel> playQueue; Channel playSeries; volatile bool eofFlag; long restartTicks; bool autoNext = true;
        int argPick = -1, argEp = -1; bool argEpisodes, argVplay;

        void ParseVodArgs(string[] args)
        {
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--mode" && i + 1 < args.Length) int.TryParse(args[++i], out startMode);
                else if (args[i] == "--pick" && i + 1 < args.Length) int.TryParse(args[++i], out argPick);
                else if (args[i] == "--epick" && i + 1 < args.Length) int.TryParse(args[++i], out argEp);
                else if (args[i] == "--episodes") argEpisodes = true;
                else if (args[i] == "--vplay") argVplay = true;
            }
            startMode = Math.Max(0, Math.Min(2, startMode));
        }

        void BuildVodUi()
        {
            autoNext = cfg.Int("AutoNext", 1) == 1; BuildTransport();
            tgLive = new Tog("Live TV", true, null); tgMovies = new Tog("Movies", false, null); tgSeries = new Tog("Series", false, null);
            tgLive.Changed += () => SetMode(Live); tgMovies.Changed += () => SetMode(Movies); tgSeries.Changed += () => SetMode(Series);
            btnBack = new BackBtn(delegate { LeaveEpisodes(true); }) { Visible = false };
            foreach (Control c in new Control[] { tgLive, tgMovies, tgSeries, btnBack }) { Controls.Add(c); uiControls.Add(c); }
            // بعد ما تتوقف على عنصر بنص ثانية نجيب تفاصيله (قصة وممثلين) بدل ما نزعج السيرفر مع كل حركة بالأسهم
            detailTimer = new System.Windows.Forms.Timer { Interval = 280 };
            detailTimer.Tick += (s, e) => { detailTimer.Stop(); if (playing || mode == Live) return; Channel c = Selected(); if (c != null && c.Kind == "movie") FetchVodInfo(c); };
        }

        // ---------- الأقسام ----------
        Sect GetSect(int m) { return sects[m] ?? (sects[m] = new Sect()); }

        string SecFile(int m, string what)
        {
            string[] names = what == "cache" ? new[] { "channels-cache.json", "movies-cache.json", "series-cache.json" }
                : what == "fav" ? new[] { "favorites.txt", "movies-favorites.txt", "series-favorites.txt" }
                : new[] { "recent.txt", "movies-recent.txt", "series-recent.txt" };
            return Path.Combine(AccountDir(), names[m]);
        }
        void SetSectPaths() { cachePath = SecFile(mode, "cache"); favPath = SecFile(mode, "fav"); recentPath = SecFile(mode, "recent"); }

        // يُستدعى كل ما تغيّر الحساب: قوائم الأقسام كلها تتصفّر ويرجع كل شي نظيف
        void ResetSections()
        {
            accountGen++; sects = new Sect[3]; openSeries = null; epSeasons = null; seriesBusy = false; playQueue = null;
            catIdx = 0; chip = 0; all = new List<Channel>(); cats = new List<string>(); catOrder = new List<string>();
            string dir = AccountDir(); store.Open(Path.Combine(dir, "progress.txt")); Posters.Dir = Path.Combine(dir, "posters");
            string pd = Posters.Dir; ThreadPool.QueueUserWorkItem(delegate { Posters.Prune(pd, 800); });
            if (IsHandleCreated) { suppress = true; tbSearch.Text = ""; suppress = false; tgFav.On = false; tgFav.Invalidate(); UpdateModeLabels(); ApplyLayout(); }
        }

        void SaveSect()
        {
            if (openSeries != null) return; Sect s = GetSect(mode);
            s.All = all; s.Cats = cats; s.Order = catOrder; s.CatIdx = catIdx; s.Chip = chip; s.Search = tbSearch.Text; s.FavOn = tgFav.On;
        }

        void LoadSect()
        {
            Sect s = GetSect(mode);
            all = s.All; cats = s.Cats; catOrder = s.Order; catIdx = s.CatIdx; chip = s.Chip;
            SetSectPaths(); fav.Clear(); recent.Clear(); LoadFavorites(); LoadRecent();
            suppress = true; tbSearch.Text = s.Search ?? ""; suppress = false;
            tgFav.On = s.FavOn; tgFav.Invalidate(); SyncFilterUi();
        }

        void SetMode(int m)
        {
            if (openSeries != null) LeaveEpisodes(false);
            if (m != mode)
            {
                SaveSect(); mode = m; LoadSect(); lv.SelectedIndices.Clear(); Log.Write("section: " + (m == Live ? "live" : m == Movies ? "movies" : "series"));
                if (GetSect(mode).Loaded) Refilter();
                else { view = new List<Channel>(); lv.VirtualListSize = 0; lv.Invalidate(); count.Value = "0"; LoadChannelsAsync(false); }
            }
            UpdateModeLabels(); ApplyLayout();
            if (GetSect(mode).Loaded) SetStatus(CountStatus());
            videoPanel.Invalidate();
        }

        string CountStatus()
        {
            return mode == Movies ? string.Format("{0} movies — pick one and double-click to play", all.Count)
                : mode == Series ? string.Format("{0} series — double-click one to see its episodes", all.Count)
                : string.Format("{0} channels — pick one and double-click to play", all.Count);
        }

        string[] ChipLabels()
        {
            if (openSeries != null) return epSeasons.Keys.Take(chips.Count).Select(k => SeasonLabel(k)).ToArray();
            return mode == Live ? LiveChips : VodChips;
        }
        string CatLabel()
        {
            if (openSeries != null) return SeasonLabel(curSeason);
            if (catIdx > 0 && catIdx - 1 < catOrder.Count) { string n = EnName(catOrder[catIdx - 1]); return n.Length > 22 ? n.Substring(0, 22) + "…" : n; }
            return "All categories";
        }
        void SyncFilterUi()
        {
            string[] lab = ChipLabels();
            for (int i = 0; i < chips.Count; i++)
            {
                chips[i].Label = i < lab.Length ? lab[i] : "";
                chips[i].On = openSeries != null ? (i < lab.Length && SeasonKeyAt(i) == curSeason) : i == chip;
                chips[i].Invalidate();
            }
            cmdCat.Text2 = CatLabel(); cmdCat.Invalidate();
        }
        void UpdateModeLabels()
        {
            string cue = openSeries != null ? "Search episodes…" : mode == Live ? "Search channels (e.g. NFL or BEIN)…" : mode == Movies ? "Search movies…" : "Search series…";
            if (tbSearch.IsHandleCreated) Native.SendMessage(tbSearch.Handle, 0x1501, (IntPtr)1, cue);
            btnRefresh.Text2 = openSeries != null ? "Refresh episodes" : mode == Live ? "Refresh channels" : mode == Movies ? "Refresh movies" : "Refresh series";
            bool eps = mode == Series && openSeries == null; btnPlay.Text2 = eps ? "Episodes" : "Play"; btnPlay.Glyph = eps ? null : "play";
            tgLive.On = mode == Live; tgMovies.On = mode == Movies; tgSeries.On = mode == Series;
            SyncFilterUi();
            foreach (Control c in new Control[] { btnRefresh, btnPlay, tgLive, tgMovies, tgSeries }) c.Invalidate();
        }

        // ---------- التخطيط: أزرار الأقسام جنب الاسم، والشرائح حسب القسم ----------
        void ApplyModeVisibility()
        {
            bool ep = openSeries != null; int n = ep ? Math.Min(chips.Count, epSeasons.Count) : ChipLabels().Length;
            for (int i = 0; i < chips.Count; i++) chips[i].Visible = i < n;
            tgFav.Visible = !ep; btnBack.Visible = ep; btnPause.Visible = seekMain.Visible = TransportOn;
        }

        void PlaceModeToggles()
        {
            int rightEdge = ClientSize.Width - 16, right = (Updater.Available ? rightEdge - 150 - 8 - 150 : rightEdge - 150) - 14;
            int x = Math.Max(nameEndX + 26, 160), w = Math.Max(78, Math.Min(124, (right - x - 16) / 3)), y = 58, h = 34;
            tgLive.Bounds = R(x, y, w, h); tgMovies.Bounds = R(x + w + 8, y, w, h); tgSeries.Bounds = R(x + 2 * (w + 8), y, w, h);
            if (openSeries != null) cmdCat.Bounds = new Rectangle(cmdCat.Left, cmdCat.Top, tgFav.Right - cmdCat.Left, cmdCat.Height);   // قائمة المواسم تاخذ مكان زر المفضلة
        }

        void PlaceEpisodeHeader(int x0, int listTop, int lw)
        {
            btnBack.Bounds = R(x0 + 14, listTop + 3, 84, 24);
            if (openSeries != null) { hint.Bounds = R(x0 + 14 + 84 + 8, listTop + 4, Math.Max(40, lw - 14 - 84 - 8 - 70), 22); hint.Col = T.Text; hint.Value = openSeries.Name; }
            else { hint.Col = T.Dim; hint.Value = ""; }
        }

        // ---------- التحميل ----------
        static string Api(Cred cr, string action, string extra)
        {
            ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;
            string url = cr.Host + "/player_api.php?username=" + Uri.EscapeDataString(cr.User) + "&password=" + Uri.EscapeDataString(cr.Pass) + "&action=" + action + extra;
            using (var wc = new Http { Ms = 45000 }) { wc.Encoding = Encoding.UTF8; wc.Headers["User-Agent"] = "VLC/3.0.20"; return wc.DownloadString(url); }
        }
        Cred Creds() { return new Cred { Host = acct.Get("host", ""), User = acct.Get("username", ""), Pass = acct.Get("password", "") }; }

        List<string> BuildOrder(List<Channel> list, List<string> cs, bool sportsFirst)
        {
            var have = new HashSet<string>(list.Select(x => x.Cat ?? ""));
            var r = cs.Where(have.Contains);
            return sportsFirst ? r.OrderBy(c => SportsRx.IsMatch(c) ? 0 : 1).ThenBy(c => c, Comparer<string>.Create(Natural)).ToList() : r.ToList();   // الأفلام: نفس ترتيب السيرفر (السنة ثم النوع)
        }

        void FinishLive(int gen, List<Channel> list, List<string> cs, bool fresh)
        {
            if (gen != accountGen) return;
            Sect s = GetSect(Live); if (!fresh && s.Loaded) return;
            s.All = list; s.Cats = cs; s.Order = BuildOrder(list, cs, true); s.Loaded = true;
            if (mode != Live) return;
            all = list; cats = cs; catOrder = s.Order; Refilter();
            if (fresh) { SetStatus(CountStatus()); AutoStart(); }
        }

        void LoadVodAsync(int m, bool force)
        {
            int gen = accountGen; Cred cr = Creds(); string cp = SecFile(m, "cache");
            SetStatus(m == Movies ? "Loading movies…" : "Loading series…");
            ThreadPool.QueueUserWorkItem(delegate
            {
                List<Channel> list = null; List<string> cs = null; string err = null;
                try
                {
                    if (!force && File.Exists(cp) && (DateTime.Now - File.GetLastWriteTime(cp)).TotalHours < 12 && ReadVodCache(cp, m, out list, out cs)) { }
                    else { FetchVod(cr, m, cp, out list, out cs); Log.Write("vod " + m + " fetched from server"); }
                    Log.Write("vod " + m + " ready: " + list.Count);
                }
                catch (Exception ex)
                {
                    err = ex.Message; Log.Write("vod " + m + ": " + ex.Message);
                    if (!ReadVodCache(cp, m, out list, out cs)) { list = null; cs = null; }
                }
                try { BeginInvoke(new Action(() => FinishVod(gen, m, list, cs, err))); } catch { }
            });
        }

        void FinishVod(int gen, int m, List<Channel> list, List<string> cs, string err)
        {
            if (gen != accountGen) return;
            if (list == null) { if (mode == m) SetStatus("Could not load " + (m == Movies ? "movies" : "series") + ": " + err); return; }
            Sect s = GetSect(m); s.All = list; s.Cats = cs; s.Order = BuildOrder(list, cs, false); s.Loaded = true;
            if (mode != m) return;
            all = list; cats = cs; catOrder = s.Order;
            if (openSeries == null) Refilter();
            SetStatus(CountStatus() + (err != null ? "   (offline copy)" : ""));
            AutoStartVod();
        }

        static void FetchVod(Cred cr, int m, string cp, out List<Channel> list, out List<string> cs)
        {
            bool movies = m == Movies; var map = new Dictionary<string, string>(); cs = new List<string>(); list = null;
            foreach (object c in Jx.Arr(Jx.Parse(Api(cr, movies ? "get_vod_categories" : "get_series_categories", ""))))
            {
                string nm = Jx.S(c, "category_name").Trim('-', '|', ' ');
                map[Jx.S(c, "category_id")] = nm; if (nm.Length > 0 && !cs.Contains(nm)) cs.Add(nm);
            }
            var l = new List<Channel>();
            foreach (object o in Jx.Arr(Jx.Parse(Api(cr, movies ? "get_vod_streams" : "get_series", ""))))
            {
                long id = Jx.Long(Jx.S(o, movies ? "stream_id" : "series_id")); if (id <= 0) continue;
                string cn; map.TryGetValue(Jx.S(o, "category_id"), out cn);
                var c = new Channel { Id = id, Kind = movies ? "movie" : "series", Name = Jx.S(o, "name"), Cat = cn ?? "", Rating = Jx.Num(Jx.S(o, "rating")) };
                if (movies) { c.Icon = Jx.S(o, "stream_icon"); c.Added = Jx.Long(Jx.S(o, "added")); c.Ext = Jx.S(o, "container_extension"); }
                else
                {
                    c.Icon = Jx.S(o, "cover"); c.Added = Jx.Long(Jx.S(o, "last_modified")); c.Plot = Jx.S(o, "plot"); c.Cast = Jx.S(o, "cast"); c.Director = Jx.S(o, "director"); c.Genre = Jx.S(o, "genre");
                    string rd = Jx.S(o, "releaseDate"); if (rd.Length < 4) rd = Jx.S(o, "release_date"); c.Year = rd.Length >= 4 ? rd.Substring(0, 4) : "";
                    c.Duration = FmtDur(Jx.S(o, "episode_run_time"));
                }
                l.Add(c);
            }
            list = l; WriteVodCache(cp, l, cs);
        }

        static void WriteVodCache(string cp, List<Channel> list, List<string> cs)
        {
            var items = new List<Dictionary<string, object>>(list.Count);
            foreach (Channel c in list)
            {
                var d = new Dictionary<string, object> { { "i", c.Id }, { "n", c.Name }, { "c", c.Cat } };
                if (!string.IsNullOrEmpty(c.Icon)) d["p"] = c.Icon; if (c.Rating > 0) d["r"] = c.Rating; if (c.Added > 0) d["a"] = c.Added; if (!string.IsNullOrEmpty(c.Ext)) d["e"] = c.Ext;
                if (!string.IsNullOrEmpty(c.Plot)) d["pl"] = c.Plot; if (!string.IsNullOrEmpty(c.Cast)) d["ca"] = c.Cast; if (!string.IsNullOrEmpty(c.Director)) d["d"] = c.Director;
                if (!string.IsNullOrEmpty(c.Genre)) d["g"] = c.Genre; if (!string.IsNullOrEmpty(c.Year)) d["y"] = c.Year; if (!string.IsNullOrEmpty(c.Duration)) d["du"] = c.Duration;
                items.Add(d);
            }
            var js = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            File.WriteAllText(cp, js.Serialize(new Dictionary<string, object> { { "v", 1 }, { "items", items }, { "categories", cs } }), new UTF8Encoding(false));
        }

        static bool ReadVodCache(string cp, int m, out List<Channel> list, out List<string> cs)
        {
            list = null; cs = null;
            try
            {
                object root = Jx.Parse(File.ReadAllText(cp, Encoding.UTF8)); var l = new List<Channel>();
                foreach (object o in Jx.Arr(Jx.Get(root, "items")))
                    l.Add(new Channel
                    {
                        Id = Jx.Long(Jx.S(o, "i")), Kind = m == Movies ? "movie" : "series", Name = Jx.S(o, "n"), Cat = Jx.S(o, "c"), Icon = Jx.S(o, "p"), Rating = Jx.Num(Jx.S(o, "r")), Added = Jx.Long(Jx.S(o, "a")), Ext = Jx.S(o, "e"),
                        Plot = Jx.S(o, "pl"), Cast = Jx.S(o, "ca"), Director = Jx.S(o, "d"), Genre = Jx.S(o, "g"), Year = Jx.S(o, "y"), Duration = Jx.S(o, "du")
                    });
                var c2 = Jx.Arr(Jx.Get(root, "categories")).Select(x => Jx.Str(x)).ToList();
                if (l.Count == 0) return false; list = l; cs = c2; return true;
            }
            catch { return false; }
        }

        // ---------- التصفية والترتيب ----------
        Channel Selected() { if (lv.SelectedIndices.Count == 0) return null; int i = lv.SelectedIndices[0]; return i >= 0 && i < view.Count ? view[i] : null; }
        void SelectFirst() { if (view.Count > 0) { try { lv.SelectedIndices.Clear(); lv.SelectedIndices.Add(0); } catch { } } }
        void FinishView(bool selectFirst)
        {
            lv.VirtualListSize = view.Count; lv.Invalidate();
            if (view.Count > 0) { try { lv.EnsureVisible(0); } catch { } }
            count.Value = view.Count.ToString();
            if (selectFirst) SelectFirst(); else if (!playing) videoPanel.Invalidate();
        }
        static string[] Words(string q) { return q.Length == 0 ? new string[0] : q.Split(new[] { ' ', '-', '_', '|', ':' }, StringSplitOptions.RemoveEmptyEntries); }

        void RefilterVod()
        {
            string q = tbSearch.Text.Trim(); string c = catIdx > 0 && catIdx - 1 < catOrder.Count ? catOrder[catIdx - 1] : null; string[] words = Words(q);
            IEnumerable<Channel> r = all;
            if (tgFav.On) r = r.Where(x => fav.Contains(x.Id));
            if (c != null) r = r.Where(x => x.Cat == c);
            if (chip >= 4 && chip <= 7) { Regex gr = GroupRx[chip - 4]; r = r.Where(x => gr.IsMatch(x.Cat ?? "")); }
            if (chip == 3)
            {
                var map = new Dictionary<long, Channel>(); foreach (Channel x in all) map[x.Id] = x;
                view = recent.Where(map.ContainsKey).Select(i => map[i]).Where(x => words.All(w => x.Name.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0)).ToList();
                FinishView(true); return;
            }
            if (words.Length > 0)
            {
                Func<Channel, int> score = x =>
                {
                    int s = 0; string n = x.Name;
                    foreach (string w in words)
                    {
                        int idx = n.IndexOf(w, StringComparison.OrdinalIgnoreCase);
                        if (idx < 0) { if ((x.Cat ?? "").IndexOf(w, StringComparison.OrdinalIgnoreCase) < 0) return -1; s += 6; continue; }
                        s += idx == 0 ? 0 : (!char.IsLetterOrDigit(n[idx - 1]) ? 1 : 3);
                    }
                    return s;
                };
                view = r.Select(x => new { x, s = score(x) }).Where(z => z.s >= 0).OrderBy(z => z.s).ThenByDescending(z => z.x.Rating).ThenBy(z => z.x.Name, Comparer<string>.Create(Natural)).Select(z => z.x).ToList();
            }
            else if (chip == 1) view = r.OrderByDescending(x => x.Rating).ThenByDescending(x => x.Added).ToList();
            else if (chip == 2) view = r.OrderBy(x => x.Name, Comparer<string>.Create(Natural)).ToList();
            else view = r.OrderByDescending(x => fav.Contains(x.Id)).ThenByDescending(x => x.Added).ThenBy(x => x.Name, Comparer<string>.Create(Natural)).ToList();   // الأحدث أول
            FinishView(true);
        }

        // ---------- عرض الصفوف ----------
        static string Clock(double sec)
        {
            int t = (int)sec, h = t / 3600, m = t / 60 % 60, s = t % 60;
            return h > 0 ? string.Format("{0}:{1:00}:{2:00}", h, m, s) : string.Format("{0}:{1:00}", m, s);
        }
        static string FmtDur(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            string[] f = s.Split(':'); int h, m;
            if (f.Length >= 2 && int.TryParse(f[0], out h) && int.TryParse(f[1], out m)) { int t = h * 60 + m; return t <= 0 ? "" : t >= 60 ? (t / 60) + "h " + (t % 60) + "m" : t + "m"; }
            int mm; return int.TryParse(s, out mm) && mm > 0 ? mm + "m" : "";
        }
        string RowSub(Channel c)
        {
            if (c.Kind == null) return EnName(c.Cat);
            double p = 0, d = 0;
            if (c.Kind != "series" && store.Get(c, out p, out d)) { if (d > 0 && p / d >= 0.92) return "Watched"; if (p >= 30) return "Resume " + Clock(p); }
            if (c.Kind == "ep") return c.Duration ?? "";
            return c.Rating > 0 ? c.Rating.ToString("0.0", Inv) : "";   // التصنيف يطلع في صفحة التفاصيل (العمود ضيق ومعظمه عربي)
        }
        Color RowSubColor(Channel c) { string s = RowSub(c); return s == "Watched" || s.StartsWith("Resume") ? T.Green : T.Dim; }

        static string EpTitle(string title, string series, int season, int num)
        {
            string t = title ?? ""; if (series.Length > 0 && t.StartsWith(series, StringComparison.OrdinalIgnoreCase)) t = t.Substring(series.Length);
            t = Regex.Replace(t, @"(?i)\bS\d+\s*[._ -]*E\d+\b|\b\d+x\d+\b|\bE(?:p(?:isode)?)?\s*\d+\b", " ");
            t = Regex.Replace(t, @"[\s._\-|:]+", " ").Trim();
            if (t.Length == 0 || Regex.IsMatch(t, @"^[\d\s]+$")) return "Episode " + num;
            return "E" + num.ToString("00") + "   " + t;
        }

        // ---------- المسلسلات: الحلقات بالمواسم ----------
        static string SeasonLabel(int k) { return k == 0 ? "Specials" : "Season " + k; }
        int SeasonKeyAt(int idx) { int i = 0; foreach (int k in epSeasons.Keys) if (i++ == idx) return k; return int.MinValue; }

        void OpenSeries(Channel s)
        {
            if (seriesBusy) return; seriesBusy = true; SetStatus("Loading episodes…");
            Cred cr = Creds(); int gen = accountGen;
            ThreadPool.QueueUserWorkItem(delegate
            {
                SortedDictionary<int, List<Channel>> seasons = null; object info = null; string err = null;
                try
                {
                    object root = Jx.Parse(Api(cr, "get_series_info", "&series_id=" + s.Id)); info = Jx.Get(root, "info");
                    seasons = ParseEpisodes(Jx.Get(root, "episodes"), s);
                }
                catch (Exception ex) { err = ex.Message; Log.Write("series info: " + ex.Message); }
                try
                {
                    BeginInvoke(new Action(() =>
                    {
                        seriesBusy = false; if (gen != accountGen) return;
                        if (seasons == null || seasons.Count == 0) { SetStatus(err != null ? "Could not load episodes: " + err : "No episodes found for this series"); return; }
                        if (info != null)
                        {
                            if (string.IsNullOrEmpty(s.Plot)) s.Plot = Jx.S(info, "plot"); if (string.IsNullOrEmpty(s.Cast)) s.Cast = Jx.S(info, "cast");
                            if (string.IsNullOrEmpty(s.Genre)) s.Genre = Jx.S(info, "genre"); if (string.IsNullOrEmpty(s.Icon)) s.Icon = Jx.S(info, "cover");
                        }
                        EnterEpisodes(s, seasons);
                    }));
                }
                catch { }
            });
        }

        // الحلقات تجي إما {"1":[...],"2":[...]} أو [[...],[...]] حسب السيرفر
        static SortedDictionary<int, List<Channel>> ParseEpisodes(object eps, Channel series)
        {
            var seasons = new SortedDictionary<int, List<Channel>>(Comparer<int>.Create((a, b) => (a == 0 ? int.MaxValue : a).CompareTo(b == 0 ? int.MaxValue : b)));   // الخاصة (0) آخر شي
            Action<int, object> add = (sn, lst) =>
            {
                foreach (object e in Jx.Arr(lst))
                {
                    long id = Jx.Long(Jx.S(e, "id")); if (id <= 0) continue;
                    int season = sn > 0 ? sn : (int)Jx.Long(Jx.S(e, "season")), num = (int)Jx.Long(Jx.S(e, "episode_num")); object inf = Jx.Get(e, "info");
                    var c = new Channel
                    {
                        Id = id, Kind = "ep", Ext = Jx.S(e, "container_extension"), Season = season, Ep = num, Cat = SeasonLabel(season), Added = Jx.Long(Jx.S(e, "added")),
                        Name = EpTitle(Jx.S(e, "title"), series.Name, season, num), Icon = Jx.S(inf, "movie_image"), Plot = Jx.S(inf, "plot"), Rating = Jx.Num(Jx.S(inf, "rating")),
                        Duration = FmtDur(Jx.S(inf, "duration"))
                    };
                    List<Channel> l; if (!seasons.TryGetValue(season, out l)) seasons[season] = l = new List<Channel>(); l.Add(c);
                }
            };
            var dict = eps as Dictionary<string, object>;
            if (dict != null) foreach (var kv in dict) add((int)Jx.Long(kv.Key), kv.Value);
            else { int i = 0; foreach (object lst in Jx.Arr(eps)) add(++i, lst); }
            foreach (var l in seasons.Values) l.Sort((a, b) => a.Ep != b.Ep ? a.Ep.CompareTo(b.Ep) : a.Id.CompareTo(b.Id));
            return seasons;
        }

        void EnterEpisodes(Channel s, SortedDictionary<int, List<Channel>> seasons)
        {
            if (openSeries == null) SaveSect();
            openSeries = s; epSeasons = seasons;
            // نفتح على الحلقة اللي توقفت عندها: آخر حلقة فيها تقدّم، ولو خلصتها فاللي بعدها
            var flat = seasons.Values.SelectMany(x => x).ToList(); int last = -1;
            for (int i = 0; i < flat.Count; i++) { double p, d; if (store.Get(flat[i], out p, out d)) last = i; }
            int target = 0; if (last >= 0) { double p, d; store.Get(flat[last], out p, out d); target = d > 0 && p / d >= 0.92 ? Math.Min(flat.Count - 1, last + 1) : last; }
            curSeason = flat[target].Season;
            suppress = true; tbSearch.Text = ""; suppress = false;
            UpdateModeLabels(); ApplyLayout(); RefilterEpisodes();
            int vi = view.IndexOf(flat[target]); if (vi < 0) vi = 0;
            if (view.Count > 0) { try { lv.SelectedIndices.Clear(); lv.SelectedIndices.Add(vi); lv.EnsureVisible(vi); } catch { } }
            SetStatus(string.Format("Episodes: {0}   Seasons: {1}   —   {2}", flat.Count, seasons.Count, s.Name));   // الاسم آخر شي عشان الاسم العربي ما يخرّب ترتيب الأرقام
            if (!vodAutoDone && argEpisodes) AfterVodReady();
        }

        void LeaveEpisodes(bool refilter)
        {
            if (openSeries == null) return;
            Channel back = openSeries; openSeries = null; epSeasons = null; LoadSect(); UpdateModeLabels();
            if (!refilter) return;
            ApplyLayout(); Refilter(); SetStatus(CountStatus());
            int vi = view.IndexOf(back);   // نرجع على المسلسل اللي كنت فاتحه
            if (vi > 0) { try { lv.SelectedIndices.Clear(); lv.SelectedIndices.Add(vi); lv.EnsureVisible(vi); } catch { } }
        }

        void RefilterEpisodes()
        {
            string[] words = Words(tbSearch.Text.Trim()); List<Channel> eps;
            if (words.Length > 0) view = epSeasons.Values.SelectMany(x => x).Where(x => words.All(w => x.Name.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0)).ToList();   // البحث يشمل كل المواسم
            else { if (!epSeasons.TryGetValue(curSeason, out eps)) eps = new List<Channel>(); view = eps.ToList(); }
            FinishView(false);
        }

        void SetSeasonKey(int k) { curSeason = k; SyncFilterUi(); RefilterEpisodes(); SelectFirst(); }
        void SetSeason(int idx) { int k = SeasonKeyAt(idx); if (k != int.MinValue) SetSeasonKey(k); }
        void ShowSeasonMenu()
        {
            var m = T.NewMenu();
            foreach (var kv in epSeasons)
            {
                int k = kv.Key; var it = new ToolStripMenuItem(SeasonLabel(k) + "   (" + kv.Value.Count + ")") { Checked = k == curSeason };
                it.Click += (s, e) => SetSeasonKey(k); m.Items.Add(it);
            }
            m.Show(cmdCat, new Point(0, cmdCat.Height + 2));
        }

        // ---------- التفاصيل (تظهر مكان "NO SIGNAL" لما ما فيه تشغيل) ----------
        void OnSelChanged() { if (mode == Live) return; detailTimer.Stop(); detailTimer.Start(); if (!playing) videoPanel.Invalidate(); }

        void FetchVodInfo(Channel c)
        {
            if (c.Info != 0) return; c.Info = 1; Cred cr = Creds(); int gen = accountGen;
            ThreadPool.QueueUserWorkItem(delegate
            {
                object info = null; bool ok = false;
                try { info = Jx.Get(Jx.Parse(Api(cr, "get_vod_info", "&vod_id=" + c.Id)), "info"); ok = true; } catch (Exception ex) { Log.Write("vod info: " + ex.Message); }
                try
                {
                    BeginInvoke(new Action(() =>
                    {
                        if (gen != accountGen) return; c.Info = (byte)(ok ? 2 : 0);
                        if (info != null)
                        {
                            c.Plot = Jx.S(info, "plot"); c.Genre = Jx.S(info, "genre"); c.Cast = Jx.S(info, "cast"); c.Director = Jx.S(info, "director"); c.Duration = FmtDur(Jx.S(info, "duration"));
                            string rd = Jx.S(info, "releasedate"); c.Year = rd.Length >= 4 ? rd.Substring(0, 4) : Jx.S(info, "year");
                            string img = Jx.S(info, "movie_image"); if (!string.IsNullOrEmpty(img) && string.IsNullOrEmpty(c.Icon)) c.Icon = img;
                            double rt = Jx.Num(Jx.S(info, "rating")); if (rt > 0 && c.Rating <= 0) c.Rating = rt;
                        }
                        if (string.IsNullOrEmpty(c.Year)) c.Year = YearFrom(c.Name);
                        if (Selected() == c && !playing) videoPanel.Invalidate();
                    }));
                }
                catch { }
            });
        }

        static string YearFrom(string name)
        {
            Match m = Regex.Match(name ?? "", @"[\(\[]((?:19|20)\d{2})[\)\]]"); if (m.Success) return m.Groups[1].Value;
            string y = ""; foreach (Match x in Regex.Matches(name ?? "", @"(?<!\d)((?:19|20)\d{2})(?!\d)")) { int n = int.Parse(x.Groups[1].Value); if (n <= DateTime.Now.Year + 1) y = x.Groups[1].Value; }
            return y;
        }

        static bool Ar(string s) { if (s != null) foreach (char ch in s) if (ch >= '؀' && ch <= 'ۿ') return true; return false; }
        static float Para(Graphics g, string s, Font f, Color col, float x, float y, float w, float maxH, bool ltr = false)
        {
            if (string.IsNullOrEmpty(s) || w < 30 || maxH < f.Height) return 0;
            using (StringFormat sf = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Near, Trimming = StringTrimming.EllipsisWord, FormatFlags = StringFormatFlags.LineLimit | (!ltr && Ar(s) ? StringFormatFlags.DirectionRightToLeft : 0) })
            {
                SizeF z = g.MeasureString(s, f, new SizeF(w, maxH), sf); float h = Math.Min(maxH, (float)Math.Ceiling(z.Height));
                using (Brush b = new SolidBrush(col)) g.DrawString(s, f, b, new RectangleF(x, y, w, h), sf);
                return h;
            }
        }

        static void DrawPoster(Graphics g, RectangleF pr, Bitmap pb)
        {
            using (GraphicsPath p = T.RR(pr, 12))
            {
                using (Brush b = new SolidBrush(Color.FromArgb(34, 255, 255, 255))) g.FillPath(b, p);
                if (pb != null)
                {
                    GraphicsState st = g.Save(); g.SetClip(p, CombineMode.Intersect); g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    float ia = (float)pb.Width / pb.Height, ra = pr.Width / pr.Height;
                    RectangleF src = ia > ra ? new RectangleF((pb.Width - pb.Height * ra) / 2f, 0, pb.Height * ra, pb.Height) : new RectangleF(0, (pb.Height - pb.Width / ra) / 2f, pb.Width, pb.Width / ra);
                    g.DrawImage(pb, pr, src, GraphicsUnit.Pixel); g.Restore(st);
                }
                else T.Icon(g, "play", pr.X + pr.Width / 2f, pr.Y + pr.Height / 2f, 26f, Color.FromArgb(70, 255, 255, 255));
                using (Pen pen = new Pen(Color.FromArgb(90, 52, 199, 89), 1.5f)) g.DrawPath(pen, p);
            }
        }

        void DrawDetails(Graphics g, RectangleF r)
        {
            using (LinearGradientBrush bg = new LinearGradientBrush(new RectangleF(r.X, r.Y, r.Width, r.Height + 1), Color.FromArgb(14, 34, 24), Color.FromArgb(3, 10, 7), 90f)) g.FillRectangle(bg, r);
            Channel c = Selected();
            if (c == null)
            {
                string big = openSeries != null ? "EPISODES" : mode == Movies ? "MOVIES" : "SERIES";
                string msg = !GetSect(mode).Loaded ? "Loading…" : view.Count == 0 ? "Nothing matches your search" : "Pick one and press Play";
                T.Txt(g, big, T.F(30f, FontStyle.Bold), Color.FromArgb(150, 255, 255, 255), new RectangleF(r.X, r.Y + r.Height / 2f - 40, r.Width, 44), StringAlignment.Center);
                T.Txt(g, msg, T.F(11f), Color.FromArgb(100, 255, 255, 255), new RectangleF(r.X, r.Y + r.Height / 2f + 8, r.Width, 26), StringAlignment.Center);
                return;
            }
            Channel sr = c.Kind == "ep" ? openSeries : null, meta = sr ?? c;   // الحلقة تورث تفاصيل المسلسل (بوستر، سنة، تقييم، ممثلين)
            string icon = !string.IsNullOrEmpty(c.Icon) ? c.Icon : (sr != null ? sr.Icon : "");
            Bitmap pb = Posters.Get(icon, this, () => { if (!playing) videoPanel.Invalidate(); });
            float pad = 24, x0 = r.X + pad, y0 = r.Y + pad, by = r.Bottom - pad, avail = r.Width - 2 * pad, pw = 0;
            bool side = r.Width >= 540;
            if (side)
            {
                float ph = Math.Min(r.Height - 2 * pad, 380f); pw = ph * 2f / 3f; if (pw > avail * 0.4f) { pw = avail * 0.4f; ph = pw * 1.5f; }
                DrawPoster(g, new RectangleF(x0, y0, pw, ph), pb);
            }
            float tx = side ? x0 + pw + 22 : x0, tw = r.Right - pad - tx, y = y0 + 2;
            y += Para(g, sr != null ? sr.Name : c.Name, T.F(21f, FontStyle.Bold), Color.White, tx, y, tw, 84) + 8;
            if (sr != null) y += Para(g, c.Name, T.F(13f, FontStyle.Bold), T.Green, tx, y, tw, 30) + 8;
            // السنة والتقييم والنوع كل واحد داخل إطار زجاجي؛ التقييم أصفر (لون زر Y في إكسبوكس 360)
            var chipsL = new List<KeyValuePair<string, bool>>();
            if (!string.IsNullOrEmpty(meta.Year)) chipsL.Add(new KeyValuePair<string, bool>(meta.Year, false));
            if (meta.Rating > 0) chipsL.Add(new KeyValuePair<string, bool>(meta.Rating.ToString("0.0", Inv) + " / 10", true));
            if (!string.IsNullOrEmpty(c.Duration)) chipsL.Add(new KeyValuePair<string, bool>(c.Duration, false));
            if (!string.IsNullOrEmpty(meta.Genre)) foreach (string gn in meta.Genre.Split(new[] { ',', '/' }, StringSplitOptions.RemoveEmptyEntries).Take(3)) chipsL.Add(new KeyValuePair<string, bool>(gn.Trim(), false));
            float cx = tx, cy = y; Font cf = T.F(11f, FontStyle.Bold);
            foreach (var ch in chipsL)
            {
                float cw = g.MeasureString(ch.Key, cf).Width + 26; if (cx > tx && cx + cw > tx + tw) { cx = tx; cy += 36; }
                RectangleF cr = new RectangleF(cx, cy, cw, 30);
                T.Pill(g, cr, false, null);   // إطار شفاف؛ التقييم بس رقمه أصفر
                T.Txt(g, ch.Key, cf, ch.Value ? T.Yellow : T.Text, cr, StringAlignment.Center);
                cx += cw + 8;
            }
            if (chipsL.Count > 0) y = cy + 30 + 14;
            string plot = !string.IsNullOrEmpty(c.Plot) ? c.Plot : (sr != null ? sr.Plot : "");
            if (plot.Length == 0 && c.Info == 1) plot = "Loading details…";
            y += Para(g, plot, T.F(13f), Color.FromArgb(225, 226, 240, 230), tx, y, tw, Math.Max(0, by - y - 130)) + 12;
            Font fs = T.F(11.5f); Color fc = Color.FromArgb(185, 214, 236, 222);
            if (!string.IsNullOrEmpty(meta.Cast)) y += Para(g, "Cast:  " + meta.Cast, fs, fc, tx, y, tw, 60, true) + 6;
            if (!string.IsNullOrEmpty(meta.Director)) y += Para(g, "Director:  " + meta.Director, fs, fc, tx, y, tw, 28, true) + 6;
            if (!string.IsNullOrEmpty(meta.Cat)) y += Para(g, "Category:  " + EnName(meta.Cat), fs, fc, tx, y, tw, 28, true);
            double p, d; string hintTxt = c.Kind == "series" ? "Double-click or press Episodes to open it" : "Double-click or press Play";
            if (c.Kind != "series" && store.Get(c, out p, out d)) { if (d > 0 && p / d >= 0.92) hintTxt = "Watched  —  double-click to watch again"; else if (p >= 30) hintTxt = "Resume from " + Clock(p) + "  —  hold Shift to start over"; }
            T.Txt(g, hintTxt, T.F(9.5f, FontStyle.Bold), Color.FromArgb(150, 52, 199, 89), new RectangleF(tx, by - 24, tw, 24), StringAlignment.Near);
        }

        // ---------- التشغيل ----------
        string UrlFor(Channel c)
        {
            string h = acct.Get("host", ""), u = acct.Get("username", ""), p = acct.Get("password", "");
            if (c.Kind == "movie" || c.Kind == "ep") return h + (c.Kind == "movie" ? "/movie/" : "/series/") + u + "/" + p + "/" + c.Id + "." + (string.IsNullOrEmpty(c.Ext) ? "mkv" : c.Ext);
            return h + "/live/" + u + "/" + p + "/" + c.Id + ".m3u8";
        }
        string TitleFor(Channel c) { return c.Kind == "ep" && playSeries != null ? playSeries.Name + " — " + c.Name : c.Name; }

        // قبل التشغيل: نجهّز قائمة الحلقات عشان التشغيل التلقائي للحلقة اللي بعدها يشتغل حتى لو طلعت من صفحة المسلسل
        void PrepareVodPlay(Channel c, bool auto)
        {
            bool wasVod = fsVod; eofFlag = false; fsVod = true; fsLive = c.Kind == null; curPos = 0; curDur = 0; curCache = 0; paused = false; if (btnPause != null) SyncPause();
            if (wasVod != fsVod && IsHandleCreated) ApplyLayout();   // يظهر/يختفي شريط التشغيل تحت الفيديو
            if (c.Kind != "ep") { playQueue = null; playSeries = null; return; }
            if (!auto && epSeasons != null) { playQueue = epSeasons.Values.SelectMany(x => x).ToList(); playSeries = openSeries; }
        }

        // يكمّل من حيث وقفت (إلا لو ضغطت Shift أو كنت خلصته تقريباً)
        string StartArg(Channel c)
        {
            if (c.Kind == null || (Control.ModifierKeys & Keys.Shift) != 0) return "";
            double p, d; if (!store.Get(c, out p, out d) || p < 30 || (d > 0 && (p > d - 60 || p / d > 0.92))) return "";
            return " --start=" + Math.Max(0, p - 4).ToString("0", Inv);
        }

        void SaveProgress(MpvIpc ipc, Channel c)
        {
            double pos = Jx.Num(ipc.Get("time-pos") ?? ""), dur = Jx.Num(ipc.Get("duration") ?? "");
            if (pos > 1) store.Set(c, pos, dur);
        }
        void VodEnded(Channel c) { if (eofFlag) store.Done(c); store.Flush(); }

        Channel NextAfter(Channel c)
        {
            if (!eofFlag || !autoNext || c == null || c.Kind != "ep" || playQueue == null) return null;
            int i = playQueue.IndexOf(c); return i >= 0 && i + 1 < playQueue.Count ? playQueue[i + 1] : null;
        }

        // ---------- أوامر الاختبار (--mode 1 --search ... --pick 0 --shot ملف.png) ----------
        void AutoStartVod()
        {
            if (vodAutoDone || (startMode == 0)) return;
            if (argSearch != null) tbSearch.Text = argSearch;
            if (argChip >= 0) SetChip(argChip);
            if (argPick >= 0 && argPick < view.Count) { lv.SelectedIndices.Clear(); lv.SelectedIndices.Add(argPick); }
            if (argEpisodes && mode == Series && openSeries == null && Selected() != null) { OpenSeries(Selected()); return; }
            AfterVodReady();
        }

        void AfterVodReady()
        {
            if (vodAutoDone) return; vodAutoDone = true;
            if (argEp >= 0 && openSeries != null && argEp < view.Count) { lv.SelectedIndices.Clear(); lv.SelectedIndices.Add(argEp); lv.EnsureVisible(argEp); }
            if (argVplay && Selected() != null && Selected().Kind != "series")
            {
                StartMpv(Selected(), argPreset >= 0 ? argPreset : preset);
                if (argSeconds > 0)
                {
                    var t = new System.Windows.Forms.Timer { Interval = argSeconds * 1000 };
                    t.Tick += (s, e) => { t.Stop(); Log.Write("test-end: " + lastReport + "  video=" + videoPanel.Width + "x" + videoPanel.Height); StopMpv(false); Close(); };
                    t.Start();
                }
                return;
            }
            if (argShot != null)
            {
                var st = new System.Windows.Forms.Timer { Interval = 5000 };
                st.Tick += (s, e) => { st.Stop(); using (var bm = new Bitmap(ClientSize.Width, ClientSize.Height)) { DrawToBitmap(bm, new Rectangle(0, 0, ClientSize.Width, ClientSize.Height)); bm.Save(argShot); } Close(); };
                st.Start();
            }
        }
    }
}
