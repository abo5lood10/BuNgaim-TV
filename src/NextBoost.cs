// NEXT Boost — مشغّل قنوات IPTV محسّن (C#، يشتغل بدون تثبيت) بهوية DsMaN (أخضر داكن زجاجي)
// الفيديو (mpv) يشتغل داخل نافذة البرنامج نفسها: تكبّرها وتصغّرها وتنقلها والقائمة قدامك.
// يستخدم mpv + فلاتر الذكاء الاصطناعي (FSRCNNX / ArtCNN) من GitHub ويستغل كرت الشاشة، ويخفّف الفلتر تلقائياً لو حصل تقطيع.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

[assembly: AssemblyTitle("BuNgaim TV")]
[assembly: AssemblyVersion("1.2.0.0")]

namespace NextBoost
{
    class Channel { public long Id; public string Name; public string Cat; }

    static class Native
    {
        [StructLayout(LayoutKind.Sequential)] public struct PPTS { public uint Version; public uint ControlMask; public uint StateMask; }
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool SetProcessInformation(IntPtr h, int cls, ref PPTS s, int size);
        [DllImport("user32.dll")] public static extern bool ReleaseCapture();
        [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, int msg, int w, int l);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, string l);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] public static extern int SetWindowTheme(IntPtr h, string app, string idlist);
        public delegate bool EnumProc(IntPtr h, IntPtr l);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc f, IntPtr l);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern IntPtr SetWindowLongPtr(IntPtr h, int idx, IntPtr v);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr h, int idx);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
        public static IntPtr FindMpvWindow(int pid)
        {
            IntPtr found = IntPtr.Zero;
            EnumWindows((h, l) => { uint p; GetWindowThreadProcessId(h, out p); if (p == (uint)pid) { var sb = new StringBuilder(64); GetClassName(h, sb, 64); if (sb.ToString() == "mpv") { found = h; return false; } } return true; }, IntPtr.Zero);
            return found;
        }
        // نخلي نافذة mpv مملوكة للتطبيق (تبقى فوقه وتتصغّر معه) وما تطلع كزر مستقل في شريط المهام
        public static void OwnWindow(IntPtr mpvWnd, IntPtr owner)
        {
            SetWindowLongPtr(mpvWnd, -8, owner);
            long ex = GetWindowLongPtr(mpvWnd, -20).ToInt64(); ex = (ex | 0x80) & ~0x40000L; SetWindowLongPtr(mpvWnd, -20, (IntPtr)ex);
        }
        public static void NoThrottle(Process p)
        {
            try { var s = new PPTS { Version = 1, ControlMask = 0x1 | 0x4, StateMask = 0 }; SetProcessInformation(p.Handle, 4, ref s, Marshal.SizeOf(s)); }
            catch { }
        }
    }

    static class Log
    {
        static readonly object gate = new object();
        public static string Path;
        public static void Write(string m)
        {
            try { lock (gate) { var fi = new FileInfo(Path); if (fi.Exists && fi.Length > 200 * 1024) fi.Delete(); File.AppendAllText(Path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + m + Environment.NewLine, Encoding.UTF8); } }
            catch { }
        }
    }

    class Ini
    {
        readonly string path; readonly Dictionary<string, string> d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public Ini(string path)
        {
            this.path = path;
            if (!File.Exists(path)) return;
            foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
            {
                string t = line.Trim(); if (t.Length == 0 || t[0] == ';' || t[0] == '#') continue;
                int i = t.IndexOf('='); if (i <= 0) continue; d[t.Substring(0, i).Trim()] = t.Substring(i + 1).Trim();
            }
        }
        public string Get(string k, string def) { string v; return d.TryGetValue(k, out v) ? v : def; }
        public int Int(string k, int def) { int n; return int.TryParse(Get(k, ""), out n) ? n : def; }
        public void Set(string k, string v) { d[k] = v; }
        public void Save() { try { File.WriteAllLines(path, d.Select(kv => kv.Key + "=" + kv.Value).ToArray(), new UTF8Encoding(false)); } catch { } }
    }

    // مستويات التحسين، من الأقوى للأخف (التخفيض التلقائي يمشي بهذا الترتيب)
    static class Presets
    {
        public static readonly string[] Names = { "Ultra", "High", "Balance", "Original" };
        // Ultra يعالج 24 فريم بالثانية بس (أخف بكثير على الكرت)؛ الباقي بفريمات البث الأصلية
        public static readonly int[] Fps = { 24, 0, 0, 0 };
        public static readonly string[] Shaders = { "ArtCNN_C4F32.glsl", "FSRCNNX_x2_16-0-4-1.glsl", "FSRCNNX_x2_8-0-4-1.glsl", null };
    }

    // قناة التحكم مع mpv (named pipe): قارئ دائم للأحداث + طلبات get_property
    class MpvIpc
    {
        // أنبوب غير متزامن (Overlapped): القراءة المعلّقة ما توقف الكتابة (مع المتزامن كانت الواجهة تتجمد)
        NamedPipeClientStream pc; Thread th;
        readonly Dictionary<int, string> resp = new Dictionary<int, string>(); readonly object gate = new object(), wlock = new object();
        int rid = 100; public volatile bool Closed;
        public event Action<string> Line;
        public bool Connect(string pipe, int ms)
        {
            try { pc = new NamedPipeClientStream(".", pipe, PipeDirection.InOut, PipeOptions.Asynchronous); pc.Connect(ms); } catch { return false; }
            th = new Thread(Loop) { IsBackground = true }; th.Start(); return true;
        }
        void Loop()
        {
            try
            {
                var buf = new byte[8192]; var sb = new StringBuilder(); var dec = Encoding.UTF8.GetDecoder(); var chars = new char[8192];
                while (true)
                {
                    int n = pc.ReadAsync(buf, 0, buf.Length).GetAwaiter().GetResult();
                    if (n <= 0) break;
                    int cn = dec.GetChars(buf, 0, n, chars, 0); sb.Append(chars, 0, cn);
                    int nl;
                    while ((nl = IndexOfNl(sb)) >= 0)
                    {
                        string l = sb.ToString(0, nl).TrimEnd('\r'); sb.Remove(0, nl + 1);
                        if (l.Length == 0) continue;
                        var m = Regex.Match(l, "\"request_id\":(\\d+)");
                        if (m.Success && !l.Contains("\"event\"")) { lock (gate) { resp[int.Parse(m.Groups[1].Value)] = l; System.Threading.Monitor.PulseAll(gate); } }
                        else { var h = Line; if (h != null) h(l); }
                    }
                }
            }
            catch { }
            Closed = true; lock (gate) System.Threading.Monitor.PulseAll(gate);
        }
        static int IndexOfNl(StringBuilder sb) { for (int i = 0; i < sb.Length; i++) if (sb[i] == '\n') return i; return -1; }
        public void Send(string json)
        {
            try { byte[] b = Encoding.UTF8.GetBytes(json + "\n"); lock (wlock) { pc.Write(b, 0, b.Length); pc.Flush(); } }
            catch { Closed = true; }
        }
        public string Get(string prop)
        {
            int id = Interlocked.Increment(ref rid);
            Send("{\"command\":[\"get_property\",\"" + prop + "\"],\"request_id\":" + id + "}");
            lock (gate)
            {
                DateTime end = DateTime.Now.AddMilliseconds(2500);
                while (!resp.ContainsKey(id) && !Closed && DateTime.Now < end) System.Threading.Monitor.Wait(gate, 200);
                string l; if (!resp.TryGetValue(id, out l)) return null; resp.Remove(id);
                var m = Regex.Match(l, "\"data\":(\"[^\"]*\"|-?[\\d.]+|true|false)"); return m.Success ? m.Groups[1].Value.Trim('"') : null;
            }
        }
    }
    class MainForm : Form, IBackHost
    {
        readonly string appDir, mpvExe, shaderDir, cachePath, favPath, lastPath, recentPath;
        readonly Ini cfg; readonly Ini acct;
        List<Channel> all = new List<Channel>(); List<string> cats = new List<string>(); List<string> catOrder = new List<string>();
        List<Channel> view = new List<Channel>();
        readonly HashSet<long> fav = new HashSet<long>(); readonly List<long> recent = new List<long>();

        readonly TextBox tbSearch = new TextBox();
        readonly ListView lv = new ListView();
        readonly Panel videoPanel = new Panel { BackColor = Color.Black, Visible = true };
        WinBtn btnMin, btnMax, btnClose;
        Cmd cmdCat, cmdPreset, btnPlay, btnStop, btnRefresh, btnFull, btnMute; VolSlide vol; MpvIpc curIpc; bool muted;
        Tog tgFav, tgAuto; Cmd btnAccount, btnSettings; UpdateBtn btnUpdate; readonly List<Tog> chips = new List<Tog>();
        StatusLine status, count, hint; VScroll vscroll;
        readonly List<Control> uiControls = new List<Control>();
        int preset, catIdx, chip;
        Icon appIcon;

        // تخطيط
        readonly List<RectangleF> cards = new List<RectangleF>(), fields = new List<RectangleF>(); RectangleF videoFrame = RectangleF.Empty;
        IntPtr mpvWnd; Rectangle lastVideoRect; string lastVideoWnd; bool playing, videoFull, layouting; Size prevSize; Rectangle fullPrevBounds; FormWindowState fullPrevState;

        Process mpv; int token; string pipeName; string lastReport = "";
        readonly long argPlay; readonly int argScreen = -1, argPreset = -1, argSeconds, argChip = -1; readonly string argSearch, argShot; readonly bool argTestUi;

        public Bitmap Bg { get { return T.StaticBg; } }

        const int WS_MINIMIZEBOX = 0x20000;
        protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.Style |= WS_MINIMIZEBOX; return cp; } }

        public MainForm(string[] args)
        {
            appDir = Path.GetDirectoryName(Application.ExecutablePath);
            mpvExe = Path.Combine(appDir, @"mpv\mpv.exe");
            shaderDir = Path.Combine(appDir, @"mpv\portable_config\shaders");
            cachePath = Path.Combine(appDir, "channels-cache.json");
            favPath = Path.Combine(appDir, "favorites.txt");
            lastPath = Path.Combine(appDir, "last-channel.txt");
            recentPath = Path.Combine(appDir, "recent.txt");
            Log.Path = Path.Combine(appDir, "NextBoost.log");
            cfg = new Ini(Path.Combine(appDir, "settings.ini"));
            acct = new Ini(Path.Combine(appDir, "account.ini"));

            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--play" && i + 1 < args.Length) long.TryParse(args[++i], out argPlay);
                else if (args[i] == "--screen" && i + 1 < args.Length) int.TryParse(args[++i], out argScreen);
                else if (args[i] == "--preset" && i + 1 < args.Length) int.TryParse(args[++i], out argPreset);
                else if (args[i] == "--seconds" && i + 1 < args.Length) int.TryParse(args[++i], out argSeconds);
                else if (args[i] == "--search" && i + 1 < args.Length) argSearch = args[++i];
                else if (args[i] == "--chip" && i + 1 < args.Length) int.TryParse(args[++i], out argChip);
                else if (args[i] == "--shot" && i + 1 < args.Length) argShot = args[++i];
                else if (args[i] == "--testui") argTestUi = true;
            }

            Text = "BuNgaim TV";
            FormBorderStyle = FormBorderStyle.None; var wa0 = (argScreen >= 0 && argScreen < Screen.AllScreens.Length ? Screen.AllScreens[argScreen] : Screen.PrimaryScreen).WorkingArea;
            int iw0 = Math.Min(1500, wa0.Width - 60), ih0 = Math.Min(840, wa0.Height - 60);
            ClientSize = new Size(iw0, ih0); MinimumSize = new Size(1040, 620);
            T.W = iw0; T.H = ih0;
            if (argScreen >= 0 && argScreen < Screen.AllScreens.Length)
            { StartPosition = FormStartPosition.Manual; var wa = Screen.AllScreens[argScreen].WorkingArea; Location = new Point(wa.X + (wa.Width - Width) / 2, wa.Y + (wa.Height - Height) / 2); }
            else StartPosition = FormStartPosition.CenterScreen;
            BackColor = T.WindowBack; DoubleBuffered = true;
            try { appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); Icon = appIcon; } catch { }

            preset = Math.Max(0, Math.Min(Presets.Names.Length - 1, cfg.Int("Preset", 1)));
            BuildUi();
            LoadFavorites(); LoadRecent();
            Load += (s, e) => { ApplyLayout(); if (!EnsureAccount()) { BeginInvoke(new Action(Close)); return; } LoadChannelsAsync(false); StartUpdateChecks(); };
            SizeChanged += (s, e) => { ApplyLayout(); ShaderForVisibility(); }; LocationChanged += (s, e) => SyncVideoWindow();
            // mpv يعيد وضع نافذته لما يبدأ الفيديو، فنثبّتها بمكانها كل نص ثانية
            var syncTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            syncTimer.Tick += (s, e) =>
            {
                if (!playing || mpv == null) return;
                try
                {
                    if (mpv.HasExited) return;
                    if (mpvWnd == IntPtr.Zero || !Native.IsWindow(mpvWnd)) { var w = Native.FindMpvWindow(mpv.Id); if (w != IntPtr.Zero) { Native.OwnWindow(w, Handle); mpvWnd = w; lastVideoRect = Rectangle.Empty; } }
                    else if (WindowRectChanged()) lastVideoRect = Rectangle.Empty;   // mpv حرّك نافذته بنفسه: نرجّعها
                }
                catch { return; }
                SyncVideoWindow();
            };
            syncTimer.Start();
            FormClosing += (s, e) => { token++; SaveSettings(); try { if (mpv != null && !mpv.HasExited) mpv.Kill(); } catch { } };
        }

        // ---------- النافذة: سحب من شريط العنوان + تغيير الحجم من الحواف ----------
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left && e.Y < 44 && !videoFull) { Native.ReleaseCapture(); Native.SendMessage(Handle, 0xA1, 2, 0); } base.OnMouseDown(e); }
        protected override void OnMouseDoubleClick(MouseEventArgs e) { if (e.Y < 44 && !videoFull) ToggleMax(); base.OnMouseDoubleClick(e); }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x84 && WindowState == FormWindowState.Normal && !videoFull)   // WM_NCHITTEST
            {
                long lp = m.LParam.ToInt64(); Point p = PointToClient(new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF)));
                int e = 6, c = 16, w = ClientSize.Width, h = ClientSize.Height; int code = 0;
                if (p.X < c && p.Y < c) code = 13; else if (p.X >= w - c && p.Y < c) code = 14; else if (p.X < c && p.Y >= h - c) code = 16; else if (p.X >= w - c && p.Y >= h - c) code = 17;
                else if (p.X < e) code = 10; else if (p.X >= w - e) code = 11; else if (p.Y < e) code = 12; else if (p.Y >= h - e) code = 15;
                if (code != 0) { m.Result = (IntPtr)code; return; }
            }
            base.WndProc(ref m);
        }
        void ToggleMax()
        {
            if (WindowState == FormWindowState.Maximized) { WindowState = FormWindowState.Normal; return; }
            var scr = Screen.FromControl(this); var wa = scr.WorkingArea;
            MaximizedBounds = new Rectangle(wa.Left - scr.Bounds.Left, wa.Top - scr.Bounds.Top, wa.Width, wa.Height);
            WindowState = FormWindowState.Maximized;
        }

        // وضع الفيديو الكامل داخل التطبيق (يخفي كل شي ويغطي الشاشة)
        void ToggleVideoFull()
        {
            if (!playing) return;
            if (!videoFull)
            {
                fullPrevState = WindowState; fullPrevBounds = Bounds;
                videoFull = true; Region = null; WindowState = FormWindowState.Normal; Bounds = Screen.FromControl(this).Bounds; ApplyLayout();
            }
            else
            {
                videoFull = false; Bounds = fullPrevBounds; if (fullPrevState == FormWindowState.Maximized) ToggleMax(); ApplyLayout();
            }
        }

        // ---------- بناء العناصر ----------
        void BuildUi()
        {
            btnMin = new WinBtn(0, delegate { WindowState = FormWindowState.Minimized; });
            btnMax = new WinBtn(1, ToggleMax);
            btnClose = new WinBtn(2, delegate { Close(); });
            btnAccount = new Cmd("Account", ChangeAccount, false);
            btnSettings = new Cmd("Settings", delegate { using (var sf = new SettingsForm()) sf.ShowDialog(this); }, false);
            btnUpdate = new UpdateBtn { Act = DoUpdate }; Controls.Add(btnUpdate);
            Controls.Add(btnMin); Controls.Add(btnMax); Controls.Add(btnClose); Controls.Add(btnAccount); Controls.Add(btnSettings);
            uiControls.AddRange(new Control[] { btnMin, btnMax, btnClose, btnAccount, btnSettings });

            tbSearch.BorderStyle = BorderStyle.None; tbSearch.BackColor = T.Field; tbSearch.ForeColor = T.Text; tbSearch.Font = T.F(11f);
            tbSearch.HandleCreated += (s, e) => Native.SendMessage(tbSearch.Handle, 0x1501, (IntPtr)1, "Search channels (e.g. NFL or BEIN)…");
            cmdCat = new Cmd("All categories", ShowCatMenu, false) { Chev = true };
            tgFav = new Tog("Favorites", false, T.Yellow) { Glyph = "star" };
            string[] chipNames = { "All", "NFL", "BEIN", "BEIN 4K", "BEIN FHD", "Alwan", "Premier League", "Recent" };
            for (int i = 0; i < chipNames.Length; i++) { int k = i; var ch = new Tog(chipNames[i], i == 0, null); ch.Changed += () => SetChip(k); chips.Add(ch); }
            hint = new StatusLine { Col = T.Dim }; count = new StatusLine { Col = T.Dim, Al = StringAlignment.Far };

            lv.View = View.Details; lv.VirtualMode = true; lv.OwnerDraw = true; lv.FullRowSelect = true; lv.HideSelection = false; lv.MultiSelect = false;
            lv.BorderStyle = BorderStyle.None; lv.BackColor = T.Field; lv.ForeColor = T.Text; lv.HeaderStyle = ColumnHeaderStyle.None; lv.Font = T.F(10f);
            lv.Columns.Add("", 38); lv.Columns.Add("", 400); lv.Columns.Add("", 260);
            lv.SmallImageList = new ImageList { ImageSize = new Size(1, 32) };
            typeof(ListView).GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(lv, true, null);
            lv.HandleCreated += (s, e) => Native.SetWindowTheme(lv.Handle, "DarkMode_Explorer", null);
            lv.RetrieveVirtualItem += (s, e) => { var c = view[e.ItemIndex]; var it = new ListViewItem(""); it.SubItems.Add(c.Name); it.SubItems.Add(c.Cat ?? ""); e.Item = it; };
            lv.DrawColumnHeader += (s, e) => { };
            lv.DrawItem += (s, e) =>
            {
                Graphics g = e.Graphics; T.Setup(g);
                using (Brush b = new SolidBrush(T.Field)) g.FillRectangle(b, e.Bounds);
                if (e.Item.Selected)
                {
                    Rectangle r = new Rectangle(0, e.Bounds.Y, lv.ClientSize.Width + 40, e.Bounds.Height);
                    using (LinearGradientBrush b = new LinearGradientBrush(new Rectangle(r.X, r.Y, r.Width, r.Height + 1), Color.FromArgb(235, 40, 160, 70), Color.FromArgb(235, 22, 120, 46), 90f)) g.FillRectangle(b, r);
                }
            };
            lv.DrawSubItem += (s, e) =>
            {
                Graphics g = e.Graphics; T.Setup(g); var c = view[e.ItemIndex]; bool sel = e.Item.Selected;
                RectangleF r = new RectangleF(e.Bounds.X, e.Bounds.Y, e.Bounds.Width, e.Bounds.Height);
                if (e.ColumnIndex == 0) { if (fav.Contains(c.Id)) T.Icon(g, "star", r.X + 20, r.Y + r.Height / 2f, 11f, T.Yellow); else T.Icon(g, "starO", r.X + 20, r.Y + r.Height / 2f, 10f, sel ? Color.FromArgb(220, 255, 255, 255) : Color.FromArgb(95, 255, 255, 255)); }
                else if (e.ColumnIndex == 1) T.Txt(g, c.Name, T.F(10f, FontStyle.Bold), sel ? Color.White : T.Text, new RectangleF(r.X, r.Y, r.Width - 8, r.Height), StringAlignment.Near);
                else T.Txt(g, EnName(c.Cat), T.F(9f), sel ? Color.FromArgb(230, 255, 230) : T.Dim, new RectangleF(r.X, r.Y, r.Width - 12, r.Height), StringAlignment.Near);
            };
            lv.DoubleClick += (s, e) => { if (lv.PointToClient(Cursor.Position).X >= 44) PlaySelected(); };
            lv.MouseDown += (s, e) =>
            {
                if (e.Button != MouseButtons.Left || e.X >= 44) return;
                var hit = lv.HitTest(e.Location); if (hit.Item != null) ToggleFavAt(hit.Item.Index);
            };
            lv.SelectedIndexChanged += (s, e) =>
            {
                if (lv.SelectedIndices.Count == 0) return; int i = lv.SelectedIndices[0]; int top = lv.TopItem != null ? lv.TopItem.Index : 0, vis = Math.Max(1, lv.ClientSize.Height / 32);
                if (i < top) lv.EnsureVisible(i); else if (i >= top + vis) lv.EnsureVisible(i);
            };
            lv.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { PlaySelected(); e.SuppressKeyPress = true; } else if (e.KeyCode == Keys.F) ToggleFav(); };

            cmdPreset = new Cmd("", ShowPresetMenu, false) { Chev = true };
            tgAuto = new Tog("Auto step-down", cfg.Int("Auto", 1) == 1, null);
            btnPlay = new Cmd("Play", PlaySelected, true) { Glyph = "play" };
            btnStop = new Cmd("Stop", delegate { StopMpv(true); }, false) { Glyph = "stop", LabelColor = Color.FromArgb(255, 150, 150) };
            btnRefresh = new Cmd("Refresh channels", delegate { LoadChannelsAsync(true); }, false);
            status = new StatusLine { Value = "Ready" };
            vscroll = new VScroll(); vscroll.Scrolled += i => ScrollTo(i);
            var scrollSync = new System.Windows.Forms.Timer { Interval = 100 };
            scrollSync.Tick += (s, e) =>
            {
                if (!lv.Visible) return; int top = lv.TopItem != null ? lv.TopItem.Index : 0, vis = Math.Max(1, lv.ClientSize.Height / 32);
                if (vscroll.TopIndex != top || vscroll.VisibleCount != vis || vscroll.Total != view.Count) { vscroll.TopIndex = top; vscroll.VisibleCount = vis; vscroll.Total = view.Count; vscroll.Invalidate(); }
            };
            scrollSync.Start();
            btnFull = new Cmd("Fullscreen", ToggleVideoFull, false) { Glyph = "full" };
            btnMute = new Cmd("", delegate { if (curIpc != null) curIpc.Send("{\"command\":[\"cycle\",\"mute\"]}"); }, false) { Glyph = "vol" };
            vol = new VolSlide();
            vol.Changed += v => { if (curIpc != null) curIpc.Send("{\"command\":[\"set_property\",\"volume\"," + ((int)v) + "]}"); };

            foreach (Control c in new Control[] { tbSearch, cmdCat, tgFav, hint, count, lv, cmdPreset, tgAuto, btnPlay, btnStop, btnRefresh, status, btnFull, btnMute, vol, vscroll }) { Controls.Add(c); uiControls.Add(c); }
            foreach (var ch in chips) { Controls.Add(ch); uiControls.Add(ch); }
            videoPanel.Paint += (s, e) =>
            {
                if (playing) return;
                Graphics g = e.Graphics; T.Setup(g); RectangleF r = new RectangleF(0, 0, videoPanel.Width, videoPanel.Height);
                T.Txt(g, "NO SIGNAL", T.F(30f, FontStyle.Bold), Color.FromArgb(150, 255, 255, 255), new RectangleF(r.X, r.Y + r.Height / 2f - 40, r.Width, 44), StringAlignment.Center);
                T.Txt(g, "Pick a channel and press Play", T.F(11f), Color.FromArgb(100, 255, 255, 255), new RectangleF(r.X, r.Y + r.Height / 2f + 8, r.Width, 26), StringAlignment.Center);
            };
            Controls.Add(videoPanel);
            UpdateButtons();

            tbSearch.TextChanged += (s, e) => Refilter(); tgFav.Changed += () => { if (tgFav.On && (chip != 0 || catIdx != 0)) { SetChip(0); tgFav.On = true; tgFav.Invalidate(); } Refilter(); };
            tbSearch.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) { if (lv.SelectedIndices.Count == 0 && view.Count > 0) lv.SelectedIndices.Add(0); PlaySelected(); e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.Down && view.Count > 0) { lv.Focus(); if (lv.SelectedIndices.Count == 0) lv.SelectedIndices.Add(0); }
            };
        }

        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
        [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }
        bool WindowRectChanged()
        {
            RECT r; if (!GetWindowRect(mpvWnd, out r)) return false;
            return r.L != lastVideoRect.X || r.T != lastVideoRect.Y || (r.R - r.L) != lastVideoRect.Width || (r.B - r.T) != lastVideoRect.Height;
        }

        // نافذة mpv مستقلة (بدون إطار) مملوكة للتطبيق، نثبّتها فوق منطقة الفيديو بالضبط
        void SyncVideoWindow()
        {
            if (mpvWnd == IntPtr.Zero || !playing || WindowState == FormWindowState.Minimized) return;
            Rectangle r = videoFull ? Bounds : videoPanel.RectangleToScreen(videoPanel.ClientRectangle);
            if (r.Width < 50 || r.Height < 50) return;
            if (r == lastVideoRect && lastVideoWnd == mpvWnd.ToString()) return;       // ما تغيّر شي: لا نكلّم نافذة mpv
            lastVideoRect = r; lastVideoWnd = mpvWnd.ToString();
            Native.SetWindowPos(mpvWnd, IntPtr.Zero, r.X, r.Y, r.Width, r.Height, 0x4000 | 0x0010 | 0x0004 | 0x0040);
            RoundVideoWindow(mpvWnd, r.Width, r.Height, !videoFull);   // ASYNC (ما نوقف الواجهة) | NOACTIVATE | NOZORDER | SHOWWINDOW
        }

        // ---------- التحديث: يفحص عند التشغيل وكل ساعة، ويظهر زر أصفر؛ ما يتحدّث شي إلا لما تضغط ----------
        void StartUpdateChecks()
        {
            Updater.Source = cfg.Get("UpdateUrl", "");
            Action check = () => Updater.Check(() => { try { BeginInvoke(new Action(() => { btnUpdate.Visible = Updater.Available && !(videoFull && playing); btnUpdate.Invalidate(); })); } catch { } });
            check();
            var ut = new System.Windows.Forms.Timer { Interval = 3600000 }; ut.Tick += (s, e) => check(); ut.Start();
        }

        void DoUpdate()
        {
            if (!ThemedBox.Ask(this, "Update available", "Update to v" + Updater.NewVersion + " now?\n\nBuNgaim TV will close and open again by itself.")) return;
            StopMpv(false); SetStatus("Downloading update v" + Updater.NewVersion + "…"); btnUpdate.Enabled = false;
            new Thread(() =>
            {
                try { Updater.Apply(); BeginInvoke(new Action(Close)); }
                catch (Exception ex) { Log.Write("update: " + ex.Message); BeginInvoke(new Action(() => { btnUpdate.Enabled = true; SetStatus("Update failed: " + ex.Message); })); }
            }) { IsBackground = true }.Start();
        }

        // ---------- الحساب: كل شخص يدخل اشتراكه الخاص (ما فيه حساب داخل البرنامج) ----------
        bool AccountComplete() { return acct.Get("host", "") != "" && acct.Get("username", "") != "" && acct.Get("password", "") != ""; }

        bool EnsureAccount()
        {
            while (!AccountComplete())
            {
                using (var lf = new LoginForm(acct.Get("host", ""), acct.Get("username", ""), "", false))
                {
                    lf.CenterOn(Screen.FromControl(this).WorkingArea); if (lf.ShowDialog() != DialogResult.OK) return false;
                    SaveAccount(lf.Host, lf.User, lf.Pass);
                }
            }
            return true;
        }

        void SaveAccount(string host, string user, string pass)
        {
            acct.Set("host", host); acct.Set("username", user); acct.Set("password", pass); acct.Save();
            try { File.Delete(cachePath); } catch { }
        }

        void ChangeAccount()
        {
            using (var lf = new LoginForm(acct.Get("host", ""), acct.Get("username", ""), acct.Get("password", ""), true))
            {
                lf.CenterOn(Screen.FromControl(this).WorkingArea); if (lf.ShowDialog(this) != DialogResult.OK) return;
                StopMpv(false);
                if (lf.SignedOut)
                {
                    SaveAccount("", "", "");
                    foreach (string p in new[] { favPath, recentPath, lastPath }) { try { File.Delete(p); } catch { } }
                    fav.Clear(); recent.Clear(); all = new List<Channel>(); view = new List<Channel>(); lv.VirtualListSize = 0; lv.Invalidate(); count.Value = "0";
                    if (!EnsureAccount()) { Close(); return; }
                }
                else SaveAccount(lf.Host, lf.User, lf.Pass);
                LoadChannelsAsync(true);
            }
        }

        string activeShader; bool shaderOff;
        // أثناء التصغير ما أحد يشوف الفيديو: نوقف فلتر الذكاء الاصطناعي ونرجّعه أول ما تفتح النافذة (يوفّر الكرت بدون أي أثر على الجودة)
        void ShaderForVisibility()
        {
            var ipc = curIpc; if (ipc == null || !playing) return;
            bool min = WindowState == FormWindowState.Minimized;
            if (min && !shaderOff) { shaderOff = true; ipc.Send("{\"command\":[\"change-list\",\"glsl-shaders\",\"clr\",\"\"]}"); }
            else if (!min && shaderOff) { shaderOff = false; if (activeShader != null) ipc.Send("{\"command\":[\"change-list\",\"glsl-shaders\",\"set\"," + new JavaScriptSerializer().Serialize(activeShader) + "]}"); }
        }
        const int VideoRadius = 18;
        [DllImport("gdi32.dll")] static extern IntPtr CreateRoundRectRgn(int l, int t, int r, int b, int w, int h);
        [DllImport("user32.dll")] static extern int SetWindowRgn(IntPtr h, IntPtr rgn, bool redraw);
        // نافذة mpv مستطيلة، فنقصّها بمنطقة بزوايا مدوّرة (بخيط جانبي عشان ما نوقف الواجهة)
        void RoundVideoWindow(IntPtr w, int width, int height, bool round)
        {
            ThreadPool.QueueUserWorkItem(_ => { try { SetWindowRgn(w, round ? CreateRoundRectRgn(0, 0, width + 1, height + 1, VideoRadius * 2, VideoRadius * 2) : IntPtr.Zero, true); } catch { } });
        }

        // ---------- التخطيط (يتغير مع حجم النافذة ومع وضع التشغيل) ----------
        static Rectangle R(int x, int y, int w, int h) { return new Rectangle(x, y, Math.Max(1, w), Math.Max(1, h)); }

        void ApplyLayout()
        {
            if (layouting || WindowState == FormWindowState.Minimized || ClientSize.Width < 300 || ClientSize.Height < 300) return;
            layouting = true;
            try
            {
                int W = ClientSize.Width, H = ClientSize.Height; T.W = W; T.H = H; T.ResetBg();
                cards.Clear(); fields.Clear(); videoFrame = RectangleF.Empty;
                bool full = videoFull && playing;
                foreach (var c in uiControls) c.Visible = !full; btnUpdate.Visible = !full && Updater.Available;
                btnFull.Visible = btnMute.Visible = vol.Visible = !full;
                if (full)
                {
                    videoPanel.Region = null; videoPanel.SetBounds(0, 0, W, H); videoPanel.Visible = true; videoPanel.BringToFront(); Region = null;
                    T.StaticBg = BuildBackground(); Invalidate(); SyncVideoWindow(); return;
                }
                Region = WindowState == FormWindowState.Normal ? new Region(T.RR(new RectangleF(0, 0, W, H), 22)) : null;
                btnMin.Bounds = R(W - 10 - 38 * 3 - 12, 6, 38, 32); btnAccount.Bounds = R(btnMin.Left - 10 - 96, 6, 96, 32); btnSettings.Bounds = R(btnAccount.Left - 8 - 96, 6, 96, 32); btnUpdate.Bounds = R(btnSettings.Left - 8 - 150, 6, 150, 32); btnMax.Bounds = R(W - 10 - 38 * 2 - 6, 6, 38, 32); btnClose.Bounds = R(W - 10 - 38, 6, 38, 32);

                int x0 = 16, yTop = 62, barH = 38;
                int lw = Math.Max(390, Math.Min(480, W * 34 / 100));
                bool wide = lw >= 700; int y = yTop; int searchH;
                int cx = x0 + 14, iw = lw - 28;
                if (wide)
                {
                    searchH = 104; cards.Add(new RectangleF(x0, y, lw, searchH));
                    int fw = lw - 14 - 8 - 230 - 8 - 158 - 14;
                    fields.Add(new RectangleF(cx, y + 10, fw, 40)); tbSearch.Bounds = R(cx + 14, y + 19, fw - 28, 24);
                    cmdCat.Bounds = R(cx + fw + 8, y + 10, 230, 40); tgFav.Bounds = R(cx + fw + 8 + 230 + 8, y + 10, 158, 40);
                    int[] cw = { 76, 76, 84, 96, 92, 84, 150, 130 }; int px = cx;
                    for (int i = 0; i < chips.Count; i++) { chips[i].Bounds = R(px, y + 60, cw[i], 34); px += cw[i] + 8; }
                }
                else
                {
                    searchH = 214; cards.Add(new RectangleF(x0, y, lw, searchH));
                    fields.Add(new RectangleF(cx, y + 10, iw, 40)); tbSearch.Bounds = R(cx + 14, y + 19, iw - 28, 24);
                    int catW = iw * 60 / 100; cmdCat.Bounds = R(cx, y + 56, catW, 36); tgFav.Bounds = R(cx + catW + 8, y + 56, iw - catW - 8, 36);
                    int chw = (iw - 16) / 3;
                    for (int i = 0; i < chips.Count; i++) chips[i].Bounds = R(cx + (i % 3) * (chw + 8), y + 100 + (i / 3) * 36, chw, 30);
                }
                int ctlH = wide ? 124 : 146; int ctlTop = H - barH - ctlH - 6; int listTop = y + searchH + 8;
                cards.Add(new RectangleF(x0, listTop, lw, ctlTop - 8 - listTop));
                RectangleF lf = new RectangleF(x0 + 12, listTop + 28, lw - 24, ctlTop - 8 - listTop - 28 - 12); fields.Add(lf);
                hint.Bounds = R(x0 + 18, listTop + 4, lw / 2, 22); count.Bounds = R(x0 + lw - 18 - 160, listTop + 4, 160, 22);
                hint.Value = "";
                int sbw = SystemInformation.VerticalScrollBarWidth + 2, visW = (int)lf.Width - 8 - 18;
                lv.Bounds = R((int)lf.X + 4, (int)lf.Y + 4, visW + sbw, (int)lf.Height - 8);   // العرض الزايد يخفي شريط التمرير الأصلي (نقصّه بالـ Region)
                vscroll.Bounds = R((int)(lf.Right - 4 - 12), (int)lf.Y + 10, 12, (int)lf.Height - 20);
                int cw2 = Math.Min(280, visW * 36 / 100); lv.Columns[2].Width = cw2; lv.Columns[1].Width = Math.Max(120, visW - 38 - cw2);
                lv.Region = new Region(T.RR(new RectangleF(0, 0, visW, lv.Height), 10));

                cards.Add(new RectangleF(x0, ctlTop, lw, ctlH));
                if (wide)
                {
                    cmdPreset.Bounds = R(cx, ctlTop + 12, iw - 158, 40); tgAuto.Bounds = R(cx + iw - 150, ctlTop + 12, 150, 40);
                    int by = ctlTop + 62; int bx = cx;
                    btnPlay.Bounds = R(bx, by, 220, 40); bx += 230; btnStop.Bounds = R(bx, by, 120, 40); bx += 130;
                    btnRefresh.Bounds = R(bx, by, cx + iw - bx, 40);
                }
                else
                {
                    cmdPreset.Bounds = R(cx, ctlTop + 10, iw, 36);
                    int tw = (iw - 8) / 2;
                    tgAuto.Bounds = R(cx, ctlTop + 54, tw, 34); btnRefresh.Bounds = R(cx + tw + 8, ctlTop + 54, iw - tw - 8, 34);
                    int p3 = (iw - 8) * 58 / 100;
                    btnPlay.Bounds = R(cx, ctlTop + 96, p3, 38); btnStop.Bounds = R(cx + p3 + 8, ctlTop + 96, iw - p3 - 8, 38);
                }
                int sw = W - 40;
                {
                    int bw = 150, vw2 = 270, mw = 40, by2 = H - barH + 2, bh = 32;
                    btnFull.Bounds = R(W - 16 - bw, by2, bw, bh); vol.Bounds = R(btnFull.Left - 8 - vw2, by2, vw2, bh); btnMute.Bounds = R(vol.Left - 4 - mw, by2, mw, bh);
                    sw = btnMute.Left - 8 - (x0 + 4);
                }
                status.Bounds = R(x0 + 4, H - barH + 6, Math.Max(100, sw), 24);

                {
                    int vx = x0 + lw + 12, vy = yTop, vw = W - vx - 16, vh = H - barH - 6 - vy;
                    videoPanel.SetBounds(vx, vy, Math.Max(100, vw), Math.Max(100, vh)); videoPanel.Region = new Region(T.RR(new RectangleF(0, 0, videoPanel.Width, videoPanel.Height), VideoRadius));
                    videoPanel.Visible = true; videoPanel.BringToFront(); videoPanel.Invalidate();
                }

                T.StaticBg = BuildBackground();
                foreach (var c in uiControls) c.Invalidate(); Invalidate();
            }
            finally { layouting = false; SyncVideoWindow(); }
        }

        Bitmap BuildBackground()
        {
            Bitmap b = new Bitmap(Math.Max(1, T.W), Math.Max(1, T.H), System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using (Graphics g = Graphics.FromImage(b))
            {
                T.Setup(g); g.DrawImageUnscaled(T.Gradient(), 0, 0);
                if (videoFull && playing) return b;
                if (appIcon != null) { g.InterpolationMode = InterpolationMode.HighQualityBicubic; using (Bitmap ib = appIcon.ToBitmap()) g.DrawImage(ib, new Rectangle(14, 8, 24, 24)); }
                float w = NameArt.DrawText(g, 46, 1, 34);
                T.Txt(g, "v" + Updater.Version, T.F(9f, FontStyle.Bold), T.Dim, new RectangleF(46 + w + 6, 6, 60, 28), StringAlignment.Near);
                foreach (var c in cards) T.Card(g, c, 18);
                foreach (var f in fields) T.FieldBox(g, f);
                if (!videoFrame.IsEmpty)
                    using (GraphicsPath p = T.RR(videoFrame, 13))
                    {
                        using (Brush br = new SolidBrush(Color.FromArgb(6, 18, 12))) g.FillPath(br, p);
                        using (Pen pen = new Pen(Color.FromArgb(90, 52, 199, 89), 1.5f)) g.DrawPath(pen, p);
                    }
            }
            return b;
        }

        protected override void OnPaint(PaintEventArgs e) { if (T.StaticBg != null) e.Graphics.DrawImageUnscaled(T.StaticBg, 0, 0); }

        void EnterPlaying()
        {
            if (playing) return;
            playing = true; ApplyLayout(); videoPanel.Invalidate();
            var h = videoPanel.Handle;   // نتأكد إن النافذة الداخلية انخلقت قبل ما mpv يتربط فيها
        }

        void ExitPlaying()
        {
            if (!playing) return;
            if (videoFull) ToggleVideoFull();
            playing = false; ApplyLayout(); videoPanel.Invalidate();
        }

        void UpdateButtons()
        {
            cmdPreset.Text2 = "Quality:  " + Presets.Names[preset]; cmdPreset.Invalidate();
        }

        void ShowPresetMenu()
        {
            var m = T.NewMenu();
            for (int i = 0; i < Presets.Names.Length; i++) { int k = i; var it = new ToolStripMenuItem(Presets.Names[i]) { Checked = i == preset }; it.Click += (s, e) => { preset = k; UpdateButtons(); }; m.Items.Add(it); }
            m.Show(cmdPreset, new Point(0, cmdPreset.Height + 2));
        }

        void SaveSettings() { cfg.Set("Preset", preset.ToString()); cfg.Set("Auto", tgAuto.On ? "1" : "0"); cfg.Save(); }
        void SetStatus(string s) { if (IsHandleCreated && !IsDisposed) BeginInvoke(new Action(() => { status.Value = s; })); }

        // ---------- القنوات ----------
        void LoadFavorites() { try { if (File.Exists(favPath)) foreach (string l in File.ReadAllLines(favPath)) { long id; if (long.TryParse(l.Trim(), out id)) fav.Add(id); } } catch { } }
        void ScrollTo(int top)
        {
            int count = view.Count, vis = Math.Max(1, lv.ClientSize.Height / 32); if (count == 0) return;
            top = Math.Max(0, Math.Min(Math.Max(0, count - vis), top)); int cur = lv.TopItem != null ? lv.TopItem.Index : 0;
            try { if (top > cur) lv.EnsureVisible(Math.Min(count - 1, top + vis - 1)); else if (top < cur) lv.EnsureVisible(top); } catch { }
        }
        void ScrollList(int delta) { int cur = lv.TopItem != null ? lv.TopItem.Index : 0; ScrollTo(cur + delta); }
        void ToggleFavAt(int index)
        {
            if (index < 0 || index >= view.Count) return;
            long id = view[index].Id; if (!fav.Remove(id)) fav.Add(id);
            try { File.WriteAllLines(favPath, fav.Select(x => x.ToString()).ToArray()); } catch { }
            lv.Invalidate(); if (tgFav.On) Refilter();
        }
        void ToggleFav()
        {
            if (lv.SelectedIndices.Count == 0) return;
            long id = view[lv.SelectedIndices[0]].Id;
            if (!fav.Remove(id)) fav.Add(id);
            try { File.WriteAllLines(favPath, fav.Select(x => x.ToString()).ToArray()); } catch { }
            lv.Invalidate(); if (tgFav.On) Refilter();
        }
        void LoadRecent() { try { if (File.Exists(recentPath)) foreach (string l in File.ReadAllLines(recentPath)) { long id; if (long.TryParse(l.Trim(), out id) && !recent.Contains(id)) recent.Add(id); } } catch { } }
        void AddRecent(long id)
        {
            recent.Remove(id); recent.Insert(0, id); if (recent.Count > 30) recent.RemoveRange(30, recent.Count - 30);
            try { File.WriteAllLines(recentPath, recent.Select(x => x.ToString()).ToArray()); } catch { }
        }

        void LoadChannelsAsync(bool force)
        {
            SetStatus("Loading channels…");
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    if (!force && File.Exists(cachePath) && (DateTime.Now - File.GetLastWriteTime(cachePath)).TotalHours < 12 && ReadCache()) { }
                    else FetchFromApi();
                    BeginInvoke(new Action(() => { BuildCatOrder(); Refilter(); SetStatus(string.Format("{0} channels — pick one and double-click to play", all.Count)); AutoStart(); }));
                }
                catch (Exception ex)
                {
                    Log.Write("channels: " + ex.Message); SetStatus("Could not load channels: " + ex.Message);
                    if (all.Count == 0 && ReadCache()) BeginInvoke(new Action(() => { BuildCatOrder(); Refilter(); }));
                }
            });
        }

        bool ReadCache()
        {
            try
            {
                var js = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                var root = js.Deserialize<Dictionary<string, object>>(File.ReadAllText(cachePath, Encoding.UTF8));
                var list = new List<Channel>();
                foreach (Dictionary<string, object> o in (System.Collections.ArrayList)root["channels"])
                    list.Add(new Channel { Id = Convert.ToInt64(o["id"]), Name = Convert.ToString(o["name"]), Cat = Convert.ToString(o["cat"]) });
                all = list; cats = ((System.Collections.ArrayList)root["categories"]).Cast<object>().Select(Convert.ToString).ToList();
                return all.Count > 0;
            }
            catch { return false; }
        }

        void FetchFromApi()
        {
            string host = acct.Get("host", ""), u = acct.Get("username", ""), p = acct.Get("password", "");
            if (host == "" || u == "" || p == "") throw new Exception("account.ini is incomplete");
            ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;
            string api = host + "/player_api.php?username=" + Uri.EscapeDataString(u) + "&password=" + Uri.EscapeDataString(p);
            var js = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            Func<string, string> get = action => { var wc = new WebClient { Encoding = Encoding.UTF8 }; wc.Headers["User-Agent"] = "VLC/3.0.20"; return wc.DownloadString(api + "&action=" + action); };
            var catArr = js.Deserialize<System.Collections.ArrayList>(get("get_live_categories"));
            var map = new Dictionary<string, string>();
            foreach (Dictionary<string, object> c in catArr) map[Convert.ToString(c["category_id"])] = Convert.ToString(c["category_name"]).TrimStart('-', ' ');
            var st = js.Deserialize<System.Collections.ArrayList>(get("get_live_streams"));
            var list = new List<Channel>();
            foreach (Dictionary<string, object> s in st)
            {
                string cid = s.ContainsKey("category_id") ? Convert.ToString(s["category_id"]) : ""; string cn; map.TryGetValue(cid, out cn);
                list.Add(new Channel { Id = Convert.ToInt64(s["stream_id"]), Name = Convert.ToString(s["name"]), Cat = cn ?? "" });
            }
            all = list; cats = map.Values.Distinct().OrderBy(x => x).ToList();
            var obj = new Dictionary<string, object> { { "channels", list.Select(c => new Dictionary<string, object> { { "id", c.Id }, { "name", c.Name }, { "cat", c.Cat } }).ToList() }, { "categories", cats } };
            File.WriteAllText(cachePath, js.Serialize(obj), new UTF8Encoding(false));
        }

        // ---------- الترتيب والتصفية ----------
        static readonly Regex SportsRx = new Regex(@"SPORT|BEIN|NBA|NFL|MLB|NHL|ESPN|UFC|F1\b|DAZN|KASS|FOX\s?SPORT|TNT|رياضة|سبورت|الكأس|ملاعب", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        static readonly Regex NflRx = new Regex(@"\bNFL\b|RED\s?ZONE", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        static readonly Regex AlwanRx = new Regex(@"ALWAN|ألوان|الوان", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        static readonly Regex FhdRx = new Regex(@"\bFHD\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        static readonly Regex K4Rx = new Regex(@"4K|UHD", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        // قنوات أجنبية تعرض الدوري الإنجليزي (Sky / TNT / Premier Sports / ...): تم التأكد إنها تشتغل بفحص حي
        static readonly HashSet<long> PremierIds = new HashSet<long>(new long[] { 546036, 547098, 547097, 547096, 547094, 547093, 547092, 547091, 547090, 547077, 547049, 547048, 547046, 546735, 546725, 546722, 546718, 546715, 546712 });
        static readonly Regex Bein4kCat = new Regex(@"^BEIN (SPORT|ULTRA) 4K\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        static readonly Regex BeinFhdCat = new Regex(@"^BEIN SPORT (FHD|HD)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        static readonly Regex BeinRx = new Regex(@"BEIN|بي ?ان|بين ?سبورت", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static string EnName(string s) { s = s ?? ""; int i = s.LastIndexOf('|'); return i > 0 && System.Text.RegularExpressions.Regex.IsMatch(s.Substring(i + 1), @"[\u0600-\u06FF]") ? s.Substring(0, i).Trim() : s; }
        static int Natural(string a, string b)
        {
            int i = 0, j = 0;
            while (i < a.Length && j < b.Length)
            {
                if (char.IsDigit(a[i]) && char.IsDigit(b[j]))
                {
                    int si = i, sj = j; while (i < a.Length && char.IsDigit(a[i])) i++; while (j < b.Length && char.IsDigit(b[j])) j++;
                    long x, y; long.TryParse(a.Substring(si, Math.Min(i - si, 15)), out x); long.TryParse(b.Substring(sj, Math.Min(j - sj, 15)), out y);
                    if (x != y) return x < y ? -1 : 1;
                }
                else { int c = char.ToUpperInvariant(a[i]).CompareTo(char.ToUpperInvariant(b[j])); if (c != 0) return c; i++; j++; }
            }
            return (a.Length - i).CompareTo(b.Length - j);
        }

        // الزر السريع والتصنيف نطاقين لا يجتمعون: اختيار واحد منهم يلغي الثاني، والبحث يكون داخل النطاق المختار فقط
        void SetChip(int k) { tgFav.On = false; tgFav.Invalidate(); chip = k; catIdx = 0; cmdCat.Text2 = "All categories"; cmdCat.Invalidate(); for (int i = 0; i < chips.Count; i++) { chips[i].On = i == k; chips[i].Invalidate(); } Refilter(); }
        void ClearChips() { chip = -1; for (int i = 0; i < chips.Count; i++) { chips[i].On = false; chips[i].Invalidate(); } }

        void BuildCatOrder()
        {
            var counts = all.GroupBy(x => x.Cat ?? "").ToDictionary(g => g.Key, g => g.Count());
            catOrder = cats.Where(counts.ContainsKey).OrderBy(c => SportsRx.IsMatch(c) ? 0 : 1).ThenBy(c => c, Comparer<string>.Create(Natural)).ToList();
        }

        void ShowCatMenu()
        {
            var counts = all.GroupBy(x => x.Cat ?? "").ToDictionary(g => g.Key, g => g.Count());
            var m = T.NewMenu();
            var all0 = new ToolStripMenuItem("All categories  (" + all.Count + ")") { Checked = catIdx == 0 }; all0.Click += (s, e) => SetChip(0); m.Items.Add(all0);
            bool sportsDone = false, sep = false;
            for (int i = 0; i < catOrder.Count; i++)
            {
                int k = i + 1; string nm = catOrder[i]; bool sp = SportsRx.IsMatch(nm);
                if (!sep && i == 0 && sp) { m.Items.Add(new ToolStripSeparator()); sep = true; }
                if (!sp && !sportsDone) { m.Items.Add(new ToolStripSeparator()); sportsDone = true; }
                var it = new ToolStripMenuItem(EnName(nm) + "   (" + counts[nm] + ")") { Checked = catIdx == k };
                it.Click += (s, e) => { catIdx = k; tgFav.On = false; tgFav.Invalidate(); ClearChips(); cmdCat.Text2 = EnName(nm).Length > 22 ? EnName(nm).Substring(0, 22) + "…" : EnName(nm); cmdCat.Invalidate(); Refilter(); };
                m.Items.Add(it);
            }
            m.Show(cmdCat, new Point(0, cmdCat.Height + 2));
        }

        void Refilter()
        {
            string q = tbSearch.Text.Trim(); string c = catIdx > 0 && catIdx - 1 < catOrder.Count ? catOrder[catIdx - 1] : null;
            string[] words = q.Length == 0 ? new string[0] : q.Split(new[] { ' ', '-', '_', '|', ':' }, StringSplitOptions.RemoveEmptyEntries);
            IEnumerable<Channel> r = all;
            if (tgFav.On) r = r.Where(x => fav.Contains(x.Id));
            if (c != null) r = r.Where(x => x.Cat == c);
            switch (chip)
            {
                case 1: r = r.Where(x => NflRx.IsMatch(x.Name) || NflRx.IsMatch(x.Cat ?? "")); break;
                case 2: r = r.Where(x => BeinRx.IsMatch(x.Name) || BeinRx.IsMatch(x.Cat ?? "")); break;
                case 3: r = r.Where(x => Bein4kCat.IsMatch(x.Cat ?? "")); break;
                case 4: r = r.Where(x => BeinFhdCat.IsMatch(x.Cat ?? "")); break;
                case 5: r = r.Where(x => AlwanRx.IsMatch(x.Name) || AlwanRx.IsMatch(x.Cat ?? "")); break;
                case 6: r = r.Where(x => PremierIds.Contains(x.Id)); break;
                case 7:
                    { var map = all.ToDictionary(x => x.Id); view = recent.Where(map.ContainsKey).Select(i => map[i]).Where(x => words.All(w => x.Name.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0)).ToList(); lv.VirtualListSize = view.Count; lv.Invalidate(); count.Value = view.Count.ToString(); return; }
            }            if (words.Length > 0)
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
                view = r.Select(x => new { x, s = score(x) }).Where(z => z.s >= 0).OrderBy(z => z.s).ThenBy(z => z.x.Name, Comparer<string>.Create(Natural)).Select(z => z.x).ToList();
            }
            else view = r.OrderByDescending(x => fav.Contains(x.Id)).ThenBy(x => SportsRx.IsMatch(x.Cat ?? "") ? 0 : 1).ThenBy(x => x.Cat ?? "").ThenBy(x => x.Name, Comparer<string>.Create(Natural)).ToList();
            lv.VirtualListSize = view.Count; lv.Invalidate();
            if (lv.VirtualListSize > 0) { try { lv.EnsureVisible(0); } catch { } }
            count.Value = view.Count.ToString();
        }

        // ---------- التشغيل ----------
        void AutoStart()
        {
            if (argSearch != null) tbSearch.Text = argSearch;
            if (argChip >= 0) SetChip(argChip);
            if (argShot != null && argPlay <= 0)
            {
                var st = new System.Windows.Forms.Timer { Interval = 1500 };
                st.Tick += (s, e) => { st.Stop(); using (var bm = new Bitmap(ClientSize.Width, ClientSize.Height)) { DrawToBitmap(bm, new Rectangle(0, 0, ClientSize.Width, ClientSize.Height)); bm.Save(argShot); } Close(); };
                st.Start(); return;
            }
            if (argPlay <= 0) return;
            var c = all.FirstOrDefault(x => x.Id == argPlay) ?? new Channel { Id = argPlay, Name = "stream " + argPlay, Cat = "" };
            StartMpv(c, argPreset >= 0 ? argPreset : preset);
            if (argTestUi)
            {
                // اختبار توصيل أزرار الصوت: يحرّك الشريط ويضغط الكتم، ثم يقرأ قيم mpv الفعلية ويسجّلها
                var tu = new System.Windows.Forms.Timer { Interval = 11000 };
                tu.Tick += (s, e) =>
                {
                    tu.Stop(); vol.Nudge(60f); btnMute.Act();
                    ThreadPool.QueueUserWorkItem(_ =>
                    {
                        Thread.Sleep(1500); var ip = curIpc; if (ip == null) { Log.Write("testui: no ipc"); return; }
                        Log.Write("testui after Nudge(60)+mute click: mpv volume=" + ip.Get("volume") + " mute=" + ip.Get("mute"));
                        BeginInvoke(new Action(() => btnMute.Act()));
                        Thread.Sleep(1500); Log.Write("testui after second mute click: mute=" + ip.Get("mute") + " volume=" + ip.Get("volume"));
                        BeginInvoke(new Action(() => { vol.Nudge(200f); }));
                        Thread.Sleep(1500); Log.Write("testui after Nudge(200): volume=" + ip.Get("volume"));
                    });
                };
                tu.Start();
            }
            if (argSeconds > 0)
            {
                var t = new System.Windows.Forms.Timer { Interval = argSeconds * 1000 };
                t.Tick += (s, e) => { t.Stop(); Log.Write("test-end: " + lastReport + "  video=" + videoPanel.Width + "x" + videoPanel.Height); StopMpv(false); Close(); };
                t.Start();
            }
        }

        void PlaySelected()
        {
            if (lv.SelectedIndices.Count == 0) return;
            SaveSettings();
            StartMpv(view[lv.SelectedIndices[0]], preset);
        }

        void StopMpv(bool msg)
        {
            token++;
            try { if (mpv != null && !mpv.HasExited) mpv.Kill(); } catch { }
            foreach (var p in Process.GetProcessesByName("mpv")) { try { if (string.Equals(p.MainModule.FileName, mpvExe, StringComparison.OrdinalIgnoreCase)) p.Kill(); } catch { } p.Dispose(); }
            mpv = null; curIpc = null; mpvWnd = IntPtr.Zero; ExitPlaying(); if (msg) SetStatus("Stopped");
        }

        static string Q(string s) { return "\"" + s.Replace("\"", "\\\"") + "\""; }

        void StartMpv(Channel c, int presetIdx)
        {
            if (!File.Exists(mpvExe)) { ThemedBox.Info(this, "mpv not found", "mpv\\mpv.exe is missing from the folder."); return; }
            if (Process.GetProcessesByName("next").Length > 0)
            {
                if (!ThemedBox.Ask(this, "NEXT+ is running", "Your subscription allows one connection only, so NEXT+ must be closed to stream here.\n\nClose it?")) return;
                foreach (var p in Process.GetProcessesByName("next")) { try { p.CloseMainWindow(); } catch { } }
                Thread.Sleep(2000);
                foreach (var p in Process.GetProcessesByName("next")) { try { p.Kill(); } catch { } }
            }
            token++;
            try { if (mpv != null && !mpv.HasExited) mpv.Kill(); } catch { }
            foreach (var p in Process.GetProcessesByName("mpv")) { try { if (string.Equals(p.MainModule.FileName, mpvExe, StringComparison.OrdinalIgnoreCase)) p.Kill(); } catch { } p.Dispose(); }
            Thread.Sleep(400);

            EnterPlaying();   // يوسّع النافذة ويطلّع منطقة الفيديو
            string url = acct.Get("host", "") + "/live/" + acct.Get("username", "") + "/" + acct.Get("password", "") + "/" + c.Id + ".m3u8";
            pipeName = "nextboost-" + Process.GetCurrentProcess().Id + "-" + (++token);
            var a = new StringBuilder();
            a.Append("--no-border --auto-window-resize=no --keepaspect-window=no --geometry=640x360+0+0 --title=NextBoostVideo");
            a.Append(" --cursor-autohide=1000 --input-ipc-server=").Append(pipeName);
            a.Append(" --force-media-title=").Append(Q(c.Name));
            string sh = Presets.Shaders[presetIdx]; activeShader = sh != null ? Path.Combine(shaderDir, sh) : null; shaderOff = false;
            a.Append(sh != null ? " --glsl-shaders=" + Q(Path.Combine(shaderDir, sh)) : " --glsl-shaders=");
            if (Presets.Fps[presetIdx] > 0) a.Append(" --vf=fps=" + Presets.Fps[presetIdx]);
            a.Append(' ').Append(Q(url));
            mpv = Process.Start(new ProcessStartInfo(mpvExe, a.ToString()) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(mpvExe) });
            try { File.WriteAllText(lastPath, c.Id + "|" + c.Name, Encoding.UTF8); } catch { }
            AddRecent(c.Id);
            try { Thread.Sleep(400); mpv.PriorityClass = ProcessPriorityClass.High; Native.NoThrottle(mpv); } catch { }
            Log.Write("play " + c.Id + " '" + c.Name + "' preset=" + presetIdx + " (owned overlay window)");
            Process mpAttach = mpv; int myAttach = token; mpvWnd = IntPtr.Zero;
            new Thread(() =>
            {
                for (int i = 0; i < 150 && token == myAttach && !mpAttach.HasExited; i++)
                {
                    IntPtr w = Native.FindMpvWindow(mpAttach.Id);
                    if (w != IntPtr.Zero) { try { BeginInvoke(new Action(() => { if (token != myAttach) return; Native.OwnWindow(w, Handle); mpvWnd = w; SyncVideoWindow(); Log.Write("mpv window attached"); })); } catch { } return; }
                    Thread.Sleep(100);
                }
            }) { IsBackground = true }.Start();
            SetStatus(c.Name + " — opening…");
            int my = token; Process mp = mpv; string pn = pipeName;
            new Thread(() => Watch(mp, pn, my, presetIdx, c.Name)) { IsBackground = true }.Start();
        }

        // ---------- مراقبة الأداء لحظياً + أحداث mpv (الشاشة الكاملة / ESC) ----------
        void Watch(Process p, string pipe, int my, int startPreset, string title)
        {
            var ipc = new MpvIpc();
            if (!ipc.Connect(pipe, 20000)) { SetStatus("Could not connect to mpv"); return; }
            ipc.Line += l =>
            {
                if (my != token) return;
                try {
                
                if (l.Contains("\"name\":\"fullscreen\"") && l.Contains("\"data\":true"))
                { ipc.Send("{\"command\":[\"set_property\",\"fullscreen\",false]}"); BeginInvoke(new Action(ToggleVideoFull)); }
                else if (l.Contains("nb-esc")) { if (videoFull) BeginInvoke(new Action(ToggleVideoFull)); }
                else if (l.Contains("\"name\":\"volume\""))
                {
                    var m = Regex.Match(l, "\"data\":(-?[\\d.]+)"); double v;
                    if (m.Success && double.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v)) BeginInvoke(new Action(() => vol.SetSilent((float)v)));
                }
                else if (l.Contains("\"name\":\"mute\""))
                {
                    bool mu = l.Contains("\"data\":true"); BeginInvoke(new Action(() => { muted = mu; btnMute.Glyph = mu ? "mute" : "vol"; btnMute.Invalidate(); }));
                }
                } catch (Exception ex) { Log.Write("line handler error: " + ex.Message); }
            };
            curIpc = ipc;
            ipc.Send("{\"command\":[\"observe_property\",1,\"fullscreen\"]}");
            ipc.Send("{\"command\":[\"observe_property\",2,\"volume\"]}");
            ipc.Send("{\"command\":[\"observe_property\",3,\"mute\"]}");
            var hist = new Queue<KeyValuePair<DateTime, long>>(); DateTime warm = DateTime.Now.AddSeconds(14); string gpu = "";
            DateTime lastGpu = DateTime.MinValue; string vinfo = ""; int cur = startPreset; bool infoDone = false;
            try
            {
                while (!p.HasExited && token == my && !ipc.Closed)
                {
                    Thread.Sleep(3000);
                    if (WindowState == FormWindowState.Minimized) continue;
                    long drops = 0, decd = 0; long.TryParse(ipc.Get("frame-drop-count") ?? "0", out drops); long.TryParse(ipc.Get("decoder-frame-drop-count") ?? "0", out decd);
                    if (!infoDone && DateTime.Now > warm.AddSeconds(-8))
                    {
                        string w = ipc.Get("video-params/w"), h = ipc.Get("video-params/h"), fps = ipc.Get("estimated-vf-fps"), hw = ipc.Get("hwdec-current");
                        if (w != null && h != null && fps != null && hw != null && hw != "no")
                        { infoDone = true; double f; double.TryParse(fps, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out f); vinfo = string.Format("{0}×{1} @{2:0}fps", w, h, f); }
                    }
                    hist.Enqueue(new KeyValuePair<DateTime, long>(DateTime.Now, drops + decd));
                    while (hist.Count > 0 && (DateTime.Now - hist.Peek().Key).TotalSeconds > 10) hist.Dequeue();
                    long recentDrops = hist.Count > 1 ? hist.Last().Value - hist.Peek().Value : 0;
                    lastReport = string.Format("preset={0} drops_total={1} recent10s={2} {3} {4}", cur, drops + decd, recentDrops, vinfo, gpu);
                    SetStatus(string.Format("{0}   |   {1}   |   Dropped frames: {2}{3}", title, vinfo, drops + decd, cur != startPreset ? "   (filter stepped down automatically)" : ""));

                    bool auto = false; try { auto = (bool)Invoke(new Func<bool>(() => tgAuto.On)); } catch { }
                    if (auto && DateTime.Now > warm && recentDrops >= 12 && cur < Presets.Shaders.Length - 1)
                    {
                        cur++; string sh = Presets.Shaders[cur]; activeShader = sh != null ? Path.Combine(shaderDir, sh) : null;
                        if (sh != null) ipc.Send("{\"command\":[\"change-list\",\"glsl-shaders\",\"set\"," + new JavaScriptSerializer().Serialize(Path.Combine(shaderDir, sh)) + "]}");
                        else ipc.Send("{\"command\":[\"change-list\",\"glsl-shaders\",\"clr\",\"\"]}");
                        if (Presets.Fps[cur] == 0 && Presets.Fps[cur - 1] > 0) ipc.Send("{\"command\":[\"change-list\",\"vf\",\"clr\",\"\"]}");
                        Log.Write("auto step-down -> preset " + cur + " (recent drops " + recentDrops + ")");
                        warm = DateTime.Now.AddSeconds(14); hist.Clear();
                    }
                }
            }
            catch (Exception ex) { Log.Write("monitor ended: " + ex.Message); }
            if (token == my) { try { BeginInvoke(new Action(() => { if (token == my) { mpvWnd = IntPtr.Zero; ExitPlaying(); SetStatus("Stream ended / mpv closed"); } })); } catch { } }
        }
    }

    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            bool created;
            using (var mx = new Mutex(true, @"Local\NextBoost_Single", out created))
            {
                if (!created) return 0;
                Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm(args));
            }
            return 0;
        }
    }
}
