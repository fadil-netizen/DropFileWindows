using System;
using System.Collections.Specialized;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DropFile
{
    static class Program
    {
        private const string AppName = "DropFile";
        private static readonly Color InstallAccent = Color.FromArgb(66, 133, 244);
        private static readonly Color UninstallAccent = Color.FromArgb(220, 50, 50);

        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            string exePath;
            try { exePath = Assembly.GetExecutingAssembly().Location; }
            catch { exePath = Application.ExecutablePath; }

            string installDir = InstallDir();
            string installedPath = Path.Combine(installDir, "DropFile.exe");
            bool runningFromInstallDir = SameDir(Path.GetDirectoryName(exePath), installDir);

            string arg = args.Length > 0 ? args[0].Trim().ToLowerInvariant() : "";
            bool wantInstall = arg == "/install" || arg == "/i" || arg == "--install" || arg == "-i";
            bool wantUninstall = arg == "/uninstall" || arg == "/u" || arg == "--uninstall" || arg == "-u";
            bool autostart = arg == "/autostart" || arg == "--autostart" || arg == "-autostart";

            if (wantUninstall)
            {
                RunUninstallAnimation(installDir, installedPath);
                return;
            }

            if (wantInstall || !runningFromInstallDir)
            {
                RunInstallAnimation(installDir, installedPath);
                return;
            }

            RunApp(autostart);
        }

        private static string InstallDir()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DropFile");
        }

        private static bool SameDir(string a, string b)
        {
            try
            {
                string fa = Path.GetFullPath(a).TrimEnd('\\', '/') + "\\";
                string fb = Path.GetFullPath(b).TrimEnd('\\', '/') + "\\";
                return string.Equals(fa, fb, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static void RunApp(bool autostart)
        {
            bool createdNew;
            using (var mutex = new Mutex(true, "DropFileApp_SingleInstance", out createdNew))
            {
                if (!createdNew)
                {
                    // Sudah ada instance berjalan → minta instance itu membuka panel utama.
                    try
                    {
                        using (var evt = new EventWaitHandle(false, EventResetMode.AutoReset, "DropFileApp_ShowMain"))
                        {
                            evt.Set();
                        }
                    }
                    catch { }
                    return;
                }
                using (var showEvt = new EventWaitHandle(false, EventResetMode.AutoReset, "DropFileApp_ShowMain"))
                {
                    try
                    {
                        var ctx = new AppContext(!autostart);
                        var waiter = new Thread(delegate()
                        {
                            while (true)
                            {
                                try { showEvt.WaitOne(); }
                                catch { break; }
                                try { ctx.RequestShowMain(); }
                                catch { }
                            }
                        });
                        waiter.IsBackground = true;
                        waiter.Start();
                        Application.Run(ctx);
                    }
                    catch (Exception ex)
                    {
                        try
                        {
                            MessageBox.Show("DropFile: " + ex.Message, "DropFile", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }
                        catch { }
                    }
                }
            }
        }

        private static void RunInstallAnimation(string installDir, string installedPath)
        {
            string[] steps = new string[]
            {
                "Menyiapkan instalasi...",
                "Menyalin file aplikasi...",
                "Membuat pintasan di Start Menu...",
                "Mendaftarkan di Windows...",
                "Mengatur autostart saat login..."
            };

            string info =
                "Lokasi instalasi:\n" + installDir +
                "\n\nBerkas yang akan dipasang:\n• DropFile.exe (aplikasi utama)\n• logo-preview.png (gambar pratinjau drag)\n\n" +
                "Klik \"Lanjut\" untuk mulai memasang, atau \"Batal\" untuk membatalkan.";

            var form = new AnimatedSetupForm(
                IconFactory.Create(),
                InstallAccent,
                info,
                steps,
                label => DoInstallStep(label, installDir, installedPath),
                () =>
                {
                    try { Process.Start(installedPath); }
                    catch { }
                });

            form.Start();
            Application.Run(form);
        }

        private static void DoInstallStep(string label, string installDir, string installedPath)
        {
            switch (label)
            {
                case "Menyiapkan instalasi...":
                    Directory.CreateDirectory(installDir);
                    break;

                case "Menyalin file aplikasi...":
                    KillOtherInstances();
                    File.Copy(Assembly.GetExecutingAssembly().Location, installedPath, true);
                    try
                    {
                        string srcPng = Path.Combine(
                            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
                            "logo-preview.png");
                        if (File.Exists(srcPng))
                        {
                            File.Copy(srcPng, Path.Combine(installDir, "logo-preview.png"), true);
                        }
                    }
                    catch { }
                    break;

                case "Membuat pintasan di Start Menu...":
                    CreateShortcut(installDir);
                    break;

                case "Mendaftarkan di Windows...":
                    RegisterUninstall(installDir);
                    break;

                case "Mengatur autostart saat login...":
                    SetAutostart(installedPath, true);
                    break;
            }
        }

        private static void RunUninstallAnimation(string installDir, string installedPath)
        {
            string[] steps = new string[]
            {
                "Menyiapkan penghapusan...",
                "Menutup DropFile...",
                "Menghapus pintasan...",
                "Menghapus file aplikasi...",
                "Membersihkan registri Windows..."
            };

            var form = new AnimatedSetupForm(
                IconFactory.Create(),
                UninstallAccent,
                steps,
                label => DoUninstallStep(label, installDir),
                () => SelfDelete(installDir));

            form.Start();
            Application.Run(form);
        }

        private static void DoUninstallStep(string label, string installDir)
        {
            switch (label)
            {
                case "Menyiapkan penghapusan...":
                    break;

                case "Menutup DropFile...":
                    KillOtherInstances();
                    break;

                case "Menghapus pintasan...":
                    string lnk = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "Microsoft\\Windows\\Start Menu\\Programs\\DropFile.lnk");
                    if (File.Exists(lnk)) File.Delete(lnk);
                    break;

                case "Menghapus file aplikasi...":
                    if (Directory.Exists(installDir))
                    {
                        string self = Application.ExecutablePath;
                        foreach (string f in Directory.GetFiles(installDir))
                        {
                            if (!string.Equals(f, self, StringComparison.OrdinalIgnoreCase))
                                File.Delete(f);
                        }
                        foreach (string d in Directory.GetDirectories(installDir))
                        {
                            Directory.Delete(d, true);
                        }
                    }
                    break;

                case "Membersihkan registri Windows...":
                    string cfg = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "DropFile");
                    if (Directory.Exists(cfg)) Directory.Delete(cfg, true);

                    string temp = Path.Combine(Path.GetTempPath(), "DropFile");
                    if (Directory.Exists(temp)) Directory.Delete(temp, true);

                    using (RegistryKey rk = Registry.CurrentUser.OpenSubKey(
                        "Software\\Microsoft\\Windows\\CurrentVersion\\Run", true))
                    {
                        if (rk != null) rk.DeleteValue("DropFile", false);
                    }
                    Registry.CurrentUser.DeleteSubKeyTree(
                        "Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\DropFile", false);
                    break;
            }
        }

        private static void KillOtherInstances()
        {
            int self = Process.GetCurrentProcess().Id;
            foreach (Process p in Process.GetProcessesByName("DropFile"))
            {
                if (p.Id != self)
                {
                    try { p.Kill(); }
                    catch { }
                    p.Dispose();
                }
            }
        }

        private static void CreateShortcut(string installDir)
        {
            string lnkPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Microsoft\\Windows\\Start Menu\\Programs\\DropFile.lnk");

            Type t = Type.GetTypeFromProgID("WScript.Shell");
            dynamic ws = Activator.CreateInstance(t);
            dynamic sc = ws.CreateShortcut(lnkPath);
            sc.TargetPath = Path.Combine(installDir, "DropFile.exe");
            sc.WorkingDirectory = installDir;
            sc.Description = "DropFile - salin file dengan drag & drop";
            sc.Save();
        }

        private static void RegisterUninstall(string installDir)
        {
            string un = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\DropFile";
            string exe = Path.Combine(installDir, "DropFile.exe");
            RegistryKey key = Registry.CurrentUser.CreateSubKey(un);
            key.SetValue("DisplayName", "DropFile", RegistryValueKind.String);
            key.SetValue("DisplayVersion", "1.1.0", RegistryValueKind.String);
            key.SetValue("Publisher", "DropFile", RegistryValueKind.String);
            key.SetValue("DisplayIcon", exe, RegistryValueKind.String);
            key.SetValue("InstallLocation", installDir, RegistryValueKind.String);
            key.SetValue("UninstallString", "\"" + exe + "\" /uninstall", RegistryValueKind.String);
            key.SetValue("QuietUninstallString", "\"" + exe + "\" /uninstall", RegistryValueKind.String);
            key.SetValue("NoModify", 1, RegistryValueKind.DWord);
            key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            key.SetValue("EstimatedSize", 150, RegistryValueKind.DWord);
            key.Close();
        }

        private static void SetAutostart(string exePath, bool enable)
        {
            using (RegistryKey rk = Registry.CurrentUser.OpenSubKey(
                "Software\\Microsoft\\Windows\\CurrentVersion\\Run", true))
            {
                if (rk == null) return;
                if (enable) rk.SetValue("DropFile", "\"" + exePath + "\" /autostart");
                else rk.DeleteValue("DropFile", false);
            }
        }

        private static void SelfDelete(string installDir)
        {
            string self = Application.ExecutablePath;
            string tmp = Path.GetTempPath();
            string cmd = string.Format(
                "/c ping 127.0.0.1 -n 3 > nul & del /f /q \"{0}\" & rmdir /s /q \"{1}\"",
                self, installDir);
            try
            {
                Process.Start(new ProcessStartInfo("cmd.exe", cmd)
                {
                    WindowStyle = ProcessWindowStyle.Hidden,
                    CreateNoWindow = true,
                    WorkingDirectory = tmp
                });
            }
            catch { }
        }
    }

    internal static class Native
    {
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        public delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll")]
        public static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        public static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr hIcon);

        public static IntPtr SetHook(int idHook, LowLevelMouseProc lpfn)
        {
            return SetWindowsHookEx(idHook, lpfn, GetModuleHandle(null), 0);
        }

        public static bool Unhook(IntPtr hhk) { return UnhookWindowsHookEx(hhk); }
        public static IntPtr CallNext(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam) { return CallNextHookEx(hhk, nCode, wParam, lParam); }

        public static POINT GetPoint(IntPtr lParam)
        {
            MSLLHOOKSTRUCT msll = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(MSLLHOOKSTRUCT));
            return msll.pt;
        }

        public static void DestroyIconHandle(IntPtr hIcon) { if (hIcon != IntPtr.Zero) DestroyIcon(hIcon); }
    }

    internal static class Shell
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
            public string szTypeName;
        }

        private const uint SHGFI_ICON = 0x000000100;
        private const uint SHGFI_LARGEICON = 0x000000000;
        private const uint FILE_ATTRIBUTE_DIRECTORY = 0x00000010;
        private const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SHGetFileInfo(
            string pszPath, uint dwFileAttributes,
            out SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

        public static Bitmap ExtractIcon(string path, bool isDir)
        {
            try
            {
                var shfi = new SHFILEINFO();
                uint attr = isDir ? FILE_ATTRIBUTE_DIRECTORY : FILE_ATTRIBUTE_NORMAL;
                IntPtr r = SHGetFileInfo(
                    path, attr, out shfi,
                    (uint)Marshal.SizeOf(typeof(SHFILEINFO)),
                    SHGFI_ICON | SHGFI_LARGEICON);
                if (r == IntPtr.Zero || shfi.hIcon == IntPtr.Zero) return null;
                try
                {
                    using (Icon ico = Icon.FromHandle(shfi.hIcon))
                    {
                        var bmp = new Bitmap(ico.Width, ico.Height);
                        using (Graphics g = Graphics.FromImage(bmp))
                        {
                            g.Clear(Color.Transparent);
                            g.DrawIcon(ico, 0, 0);
                        }
                        return bmp;
                    }
                }
                finally
                {
                    Native.DestroyIconHandle(shfi.hIcon);
                }
            }
            catch
            {
                return null;
            }
        }
    }

    internal static class IconFactory
    {
        public static Icon Create()
        {
            try
            {
                Assembly asm = Assembly.GetExecutingAssembly();
                using (Stream s = asm.GetManifestResourceStream("app.ico"))
                {
                    if (s != null && s.Length > 0) return new Icon(s);
                }
            }
            catch { }
            return DrawFallback();
        }

        private static Icon DrawFallback()
        {
            using (Bitmap bmp = new Bitmap(32, 32))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);
                    using (var bg = new SolidBrush(Color.FromArgb(41, 98, 255)))
                    using (var pen = new Pen(Color.White, 3f))
                    using (var tri = new SolidBrush(Color.White))
                    {
                        g.FillEllipse(bg, 1, 1, 30, 30);
                        g.DrawLine(pen, 16, 6, 16, 19);
                        PointF[] pts = { new PointF(9f, 16f), new PointF(23f, 16f), new PointF(16f, 27f) };
                        g.FillPolygon(tri, pts);
                    }
                }
                IntPtr h = bmp.GetHicon();
                Icon icon;
                using (Icon temp = Icon.FromHandle(h))
                {
                    icon = (Icon)temp.Clone();
                }
                Native.DestroyIconHandle(h);
                return icon;
            }
        }
    }

    internal class Settings
    {
        public bool FeatureEnabled = true;
        public bool AlwaysOnTop = true;
        public bool StartWithWindows = true;
        public bool IsFirstRun;
        public string AccentMode = "system";   // "system" = ikuti warna Windows, "custom" = hex
        public string AccentHex = "#34B4FF";

        private readonly string configFile;

        public Settings()
        {
            string configDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "DropFile");
            Directory.CreateDirectory(configDir);
            configFile = Path.Combine(configDir, "config.ini");

            IsFirstRun = !File.Exists(configFile);
            if (IsFirstRun)
            {
                Save();
            }
            else
            {
                Load();
            }
        }

        private void Load()
        {
            try
            {
                foreach (string raw in File.ReadAllLines(configFile))
                {
                    string l = raw.Trim();
                    if (l.Length == 0 || l.StartsWith("#")) continue;
                    int idx = l.IndexOf('=');
                    if (idx <= 0) continue;
                    string k = l.Substring(0, idx).Trim().ToLowerInvariant();
                    string v = l.Substring(idx + 1).Trim();
                    if (k == "enabled") FeatureEnabled = ParseBool(v, true);
                    else if (k == "ontop") AlwaysOnTop = ParseBool(v, true);
                    else if (k == "startup") StartWithWindows = ParseBool(v, true);
                    else if (k == "accentmode") AccentMode = v;
                    else if (k == "accenthex") AccentHex = v;
                }
            }
            catch { }
        }

        private static bool ParseBool(string v, bool dflt)
        {
            bool r;
            return bool.TryParse(v, out r) ? r : dflt;
        }

        public void Save()
        {
            try
            {
                var sb = new StringBuilder();
                sb.Append("enabled=").AppendLine(FeatureEnabled ? "True" : "False");
                sb.Append("ontop=").AppendLine(AlwaysOnTop ? "True" : "False");
                sb.Append("startup=").AppendLine(StartWithWindows ? "True" : "False");
                sb.Append("accentmode=").AppendLine(AccentMode);
                sb.Append("accenthex=").AppendLine(AccentHex);
                File.WriteAllText(configFile, sb.ToString());
            }
            catch { }
        }

        // Warna seed: ikuti aksen Windows, atau hex custom.
        public Color SeedColor()
        {
            if (AccentMode == "system")
            {
                Color? sys = ReadSystemAccent();
                if (sys.HasValue) return sys.Value;
            }
            return ParseHex(AccentHex);
        }

        private static Color? ReadSystemAccent()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(
                    "Software\\Microsoft\\Windows\\DWM"))
                {
                    if (key != null)
                    {
                        object v = key.GetValue("ColorizationColor");
                        if (v is int)
                        {
                            uint val = (uint)(int)v; // format 0xAABBGGRR
                            return Color.FromArgb(
                                (int)(val & 0xFF),
                                (int)((val >> 8) & 0xFF),
                                (int)((val >> 16) & 0xFF));
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        private static Color ParseHex(string hex)
        {
            try
            {
                string s = hex.TrimStart('#');
                if (s.Length == 6)
                {
                    return Color.FromArgb(
                        Convert.ToInt32(s.Substring(0, 2), 16),
                        Convert.ToInt32(s.Substring(2, 2), 16),
                        Convert.ToInt32(s.Substring(4, 2), 16));
                }
            }
            catch { }
            return Color.FromArgb(52, 180, 255);
        }
    }

    internal class MouseShaker : IDisposable
    {
        public event EventHandler ShakeDetected;

        private const int WH_MOUSE_LL = 14;
        private const int WM_MOUSEMOVE = 0x0200;

        private const int MinSampleIntervalMs = 10;
        private const int MinStepPx = 6;
        private const int StrokePx = 16;
        private const int MaxSpanMs = 600;
        private const int MinReversals = 6;
        private const double MinDistancePx = 130;

        private static readonly Stopwatch Clock = Stopwatch.StartNew();

        private IntPtr hook;
        private Native.LowLevelMouseProc proc;
        private bool running;

        private long lastEventTime;
        private int lastX;
        private int lastY;
        private int turnX;
        private int turnY;
        private int dominantAxis;
        private int lastSign;
        private int reversals;
        private double distance;
        private long windowStart;
        private long cooldownUntil;
        private bool hasStart;

        public bool IsRunning { get { return running; } }

        public void Start()
        {
            if (running) return;
            proc = HookCallback;
            hook = Native.SetHook(WH_MOUSE_LL, proc);
            running = hook != IntPtr.Zero;
            Reset();
        }

        public void Stop()
        {
            if (!running) return;
            Native.Unhook(hook);
            hook = IntPtr.Zero;
            running = false;
            proc = null;
        }

        private void Reset()
        {
            hasStart = false;
            turnX = lastX;
            turnY = lastY;
            dominantAxis = 0;
            lastSign = 0;
            reversals = 0;
            distance = 0;
            windowStart = Clock.ElapsedMilliseconds;
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && wParam == (IntPtr)WM_MOUSEMOVE)
            {
                Native.POINT pt = Native.GetPoint(lParam);
                ProcessMove(pt.X, pt.Y);
            }
            return Native.CallNext(hook, nCode, wParam, lParam);
        }

        private void ProcessMove(int x, int y)
        {
            long now = Clock.ElapsedMilliseconds;
            if (now - lastEventTime < MinSampleIntervalMs) return;
            lastEventTime = now;

            if (!hasStart)
            {
                lastX = x;
                lastY = y;
                turnX = x;
                turnY = y;
                hasStart = true;
                windowStart = now;
                return;
            }

            int dx = x - lastX;
            int dy = y - lastY;
            double dist = Math.Abs((double)dx) + Math.Abs((double)dy);
            if (dist < MinStepPx) return;

            if (now - windowStart > MaxSpanMs)
            {
                Reset();
                now = Clock.ElapsedMilliseconds;
            }

            lastX = x;
            lastY = y;

            if (dominantAxis == 0)
            {
                int sx = Math.Abs(x - turnX);
                int sy = Math.Abs(y - turnY);
                if (sx >= StrokePx && sx > sy)
                {
                    dominantAxis = 1;
                    lastSign = Math.Sign(x - turnX);
                    reversals = 1;
                    distance += sx;
                    turnX = x;
                    turnY = y;
                }
                else if (sy >= StrokePx && sy >= sx)
                {
                    dominantAxis = 2;
                    lastSign = Math.Sign(y - turnY);
                    reversals = 1;
                    distance += sy;
                    turnX = x;
                    turnY = y;
                }
            }
            else if (dominantAxis == 1)
            {
                int run = x - turnX;
                if (Math.Abs(run) >= StrokePx)
                {
                    int s = Math.Sign(run);
                    if (lastSign != 0 && s != lastSign) reversals++;
                    lastSign = s;
                    distance += Math.Abs(run);
                    turnX = x;
                    turnY = y;
                }
            }
            else
            {
                int run = y - turnY;
                if (Math.Abs(run) >= StrokePx)
                {
                    int s = Math.Sign(run);
                    if (lastSign != 0 && s != lastSign) reversals++;
                    lastSign = s;
                    distance += Math.Abs(run);
                    turnX = x;
                    turnY = y;
                }
            }

            if (reversals >= MinReversals && distance >= MinDistancePx && now >= cooldownUntil)
            {
                cooldownUntil = now + 2000;
                Reset();
                if (ShakeDetected != null)
                {
                    ShakeDetected(this, EventArgs.Empty);
                }
            }
        }

        public void Dispose() { Stop(); }
    }

    internal class AppContext : ApplicationContext
    {
        private const int MaxBoxes = 8;

        private readonly Settings settings;
        private readonly System.Collections.Generic.List<DropWindow> boxes =
            new System.Collections.Generic.List<DropWindow>();
        private readonly MouseShaker shaker;
        private NotifyIcon tray;
        private Icon appIcon;
        private bool isClosing;
        private MainWindow mainWindow;
        private bool showMainRequested;

        public Settings AppSettings { get { return settings; } }

        public bool IsClosing { get { return isClosing; } }

        internal void RequestShowMain()
        {
            if (isClosing) return;
            showMainRequested = true;
        }

        internal bool ConsumeShowMainRequest()
        {
            bool r = showMainRequested;
            showMainRequested = false;
            return r;
        }

        public void ShowMainWindow()
        {
            if (isClosing) return;
            if (mainWindow == null || mainWindow.IsDisposed)
            {
                mainWindow = new MainWindow(this);
            }
            mainWindow.RefreshUi();
            mainWindow.Show();
            mainWindow.BringToFront();
            mainWindow.Activate();
        }

        internal void CloseAllBoxes()
        {
            var arr = boxes.ToArray();
            foreach (DropWindow b in arr)
            {
                try { b.CloseBox(); }
                catch { }
            }
        }

        public void ApplyTheme()
        {
            MaterialColors.Apply(settings.SeedColor());
            foreach (DropWindow b in boxes)
            {
                try { b.Invalidate(); } catch { }
            }
        }

        internal void ShowColorMenu(Control owner, Point location)
        {
            Settings s = settings;
            var menu = new ContextMenuStrip();
            menu.Renderer = new DropWindow.MaterialMenuRenderer();
            menu.ShowImageMargin = true;
            menu.Font = new Font("Segoe UI", 9f);

            var header = new ToolStripMenuItem("Warna Kotak Drop") { Enabled = false };
            menu.Items.Add(header);
            menu.Items.Add(new ToolStripSeparator());

            var sys = new ToolStripMenuItem("Ikuti warna Windows") { Checked = s.AccentMode == "system" };
            sys.ToolTipText = "Warna kotak mengikuti aksen warna sistem operasi";
            sys.Click += delegate
            {
                s.AccentMode = "system";
                s.Save();
                ApplyTheme();
            };
            menu.Items.Add(sys);

            menu.Items.Add(new ToolStripSeparator());

            string[] presets =
            {
                "Biru", "#34B4FF",
                "Indigo", "#5B8DEF",
                "Ungu", "#A884F5",
                "Merah Muda", "#F075A8",
                "Merah", "#F2555A",
                "Oranye", "#FF9F43",
                "Kuning", "#F7C948",
                "Hijau", "#4CC38A",
                "Toska", "#2EC4B6"
            };

            bool isCustom = s.AccentMode == "custom";
            for (int i = 0; i < presets.Length; i += 2)
            {
                string label = presets[i];
                string hex = presets[i + 1];
                string norm = hex.TrimStart('#').ToUpperInvariant();
                bool active = isCustom && string.Equals(
                    s.AccentHex.TrimStart('#').ToUpperInvariant(), norm,
                    StringComparison.OrdinalIgnoreCase);

                Color swatchColor = Color.FromArgb(
                    Convert.ToInt32(norm.Substring(0, 2), 16),
                    Convert.ToInt32(norm.Substring(2, 2), 16),
                    Convert.ToInt32(norm.Substring(4, 2), 16));

                var mi = new ToolStripMenuItem((active ? "✓ " : "") + label)
                {
                    Image = DropWindow.MakeSwatch(swatchColor),
                    ToolTipText = "Gunakan warna " + label.ToLowerInvariant()
                };
                string capturedHex = hex;
                mi.Click += delegate
                {
                    s.AccentMode = "custom";
                    s.AccentHex = capturedHex;
                    s.Save();
                    ApplyTheme();
                };
                menu.Items.Add(mi);
            }

            menu.Items.Add(new ToolStripSeparator());

            var reset = new ToolStripMenuItem("Reset ke warna bawaan");
            reset.Click += delegate
            {
                s.AccentMode = "system";
                s.AccentHex = "#34B4FF";
                s.Save();
                ApplyTheme();
            };
            menu.Items.Add(reset);

            menu.Show(owner, location);
        }

        public AppContext(bool showMainOnLaunch)
        {
            settings = new Settings();
            shaker = new MouseShaker();
            shaker.ShakeDetected += OnShake;
            appIcon = IconFactory.Create();
            BuildTray();
            ApplyTheme();

            CleanupTempStaging();
            ApplyStartupKey();
            ApplyFeatureState();

            var poll = new System.Windows.Forms.Timer { Interval = 250 };
            poll.Tick += delegate
            {
                if (ConsumeShowMainRequest())
                {
                    ShowMainWindow();
                }
            };
            poll.Start();

            if (settings.IsFirstRun && settings.FeatureEnabled)
            {
                tray.ShowBalloonTip(6000, "DropFile aktif",
                    "Goyangkan kursor (geser cepat bolak-balik 6x) untuk membuat kotak drop baru. Klik kanan kotak/ikon tray untuk menu.",
                    ToolTipIcon.Info);
            }

            if (showMainOnLaunch)
            {
                ShowMainWindow();
            }
        }

        private void OnShake(object sender, EventArgs e)
        {
            if (!settings.FeatureEnabled) return;
            CreateBox();
        }

        internal void CreateBox()
        {
            if (boxes.Count >= MaxBoxes) return;
            var w = new DropWindow(this);
            boxes.Add(w);
            w.ShowWindow();
        }

        internal void RemoveBox(DropWindow b)
        {
            if (isClosing) return;
            boxes.Remove(b);
        }

        private static void CleanupTempStaging()
        {
            try
            {
                string root = Path.Combine(Path.GetTempPath(), "DropFile");
                if (Directory.Exists(root))
                {
                    foreach (string d in Directory.GetDirectories(root, "box-*"))
                    {
                        try { Directory.Delete(d, true); }
                        catch { }
                    }
                }
            }
            catch { }
        }

        public void ToggleFeature()
        {
            settings.FeatureEnabled = !settings.FeatureEnabled;
            settings.Save();
            ApplyFeatureState();
            if (mainWindow != null && !mainWindow.IsDisposed) mainWindow.RefreshUi();
            if (settings.FeatureEnabled)
            {
                tray.ShowBalloonTip(2000, "DropFile", "Fitur drop aktif.", ToolTipIcon.Info);
            }
            else
            {
                tray.ShowBalloonTip(2000, "DropFile", "Fitur drop dimatikan. Semua kotak drop ditutup dan file sementara dihapus.", ToolTipIcon.Info);
            }
        }

        private void ApplyFeatureState()
        {
            if (settings.FeatureEnabled)
            {
                shaker.Start();
            }
            else
            {
                shaker.Stop();
                var arr = boxes.ToArray();
                foreach (DropWindow b in arr)
                {
                    b.CloseBox();
                }
            }
        }

        public void ToggleStartup()
        {
            settings.StartWithWindows = !settings.StartWithWindows;
            settings.Save();
            ApplyStartupKey();
        }

        private void ApplyStartupKey()
        {
            if (!IsInstalled()) return;
            using (RegistryKey rk = Registry.CurrentUser.OpenSubKey(
                "Software\\Microsoft\\Windows\\CurrentVersion\\Run", true))
            {
                if (rk == null) return;
                if (settings.StartWithWindows)
                {
                    rk.SetValue("DropFile", "\"" + Path.Combine(InstallDir(), "DropFile.exe") + "\" /autostart");
                }
                else
                {
                    rk.DeleteValue("DropFile", false);
                }
            }
        }

        public void ToggleAlwaysOnTop()
        {
            settings.AlwaysOnTop = !settings.AlwaysOnTop;
            settings.Save();
            foreach (DropWindow b in boxes)
            {
                b.TopMost = settings.AlwaysOnTop;
            }
        }

        public void Uninstall()
        {
            if (!IsInstalled())
            {
                MessageBox.Show("DropFile belum terinstal. Jalankan DropFile.exe untuk menginstal.",
                    "DropFile", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            string self = Application.ExecutablePath;
            try
            {
                Process.Start(new ProcessStartInfo(self, "/uninstall")
                {
                    WorkingDirectory = Path.GetDirectoryName(self)
                });
            }
            catch { }
            Exit();
        }

        public void Exit()
        {
            if (isClosing) return;
            isClosing = true;
            shaker.Stop();
            foreach (DropWindow b in boxes)
            {
                try { b.Dispose(); }
                catch { }
            }
            boxes.Clear();
            if (mainWindow != null && !mainWindow.IsDisposed)
            {
                try { mainWindow.Dispose(); }
                catch { }
            }
            mainWindow = null;
            tray.Visible = false;
            tray.Dispose();
            if (appIcon != null) appIcon.Dispose();
            Application.Exit();
        }

        public ContextMenuStrip MakeMenu(DropWindow owner)
        {
            var m = new ContextMenuStrip { ShowImageMargin = false };
            m.Opening += delegate { ConfigureMenu(m, owner); };
            return m;
        }

        private void ConfigureMenu(ContextMenuStrip m, DropWindow owner)
        {
            m.Items.Clear();

            var main = new ToolStripMenuItem("Buka Panel Utama");
            main.Click += delegate { ShowMainWindow(); };
            m.Items.Add(main);

            var create = new ToolStripMenuItem("Buat kotak drop baru");
            create.Click += delegate { CreateBox(); };
            m.Items.Add(create);

            var close = new ToolStripMenuItem("Tutup kotak ini (hapus file sementara)") { Enabled = owner != null };
            close.Click += delegate { if (owner != null) owner.CloseBox(); };
            m.Items.Add(close);

            m.Items.Add(new ToolStripSeparator());

            var feat = new ToolStripMenuItem("Aktifkan fitur drop") { Checked = settings.FeatureEnabled };
            feat.Click += delegate { ToggleFeature(); };
            m.Items.Add(feat);

            var top = new ToolStripMenuItem("Selalu di atas") { Checked = settings.AlwaysOnTop };
            top.Click += delegate { ToggleAlwaysOnTop(); };
            m.Items.Add(top);

            var start = new ToolStripMenuItem("Mulai saat login") { Checked = settings.StartWithWindows, Enabled = IsInstalled() };
            start.Click += delegate { ToggleStartup(); };
            m.Items.Add(start);

            m.Items.Add(new ToolStripSeparator());

            var un = new ToolStripMenuItem("Hapus instalasi DropFile...") { Enabled = IsInstalled() };
            un.Click += delegate { Uninstall(); };
            m.Items.Add(un);

            var exit = new ToolStripMenuItem("Keluar");
            exit.Click += delegate { Exit(); };
            m.Items.Add(exit);
        }

        private void BuildTray()
        {
            tray = new NotifyIcon();
            tray.Icon = appIcon;
            tray.Text = "DropFile — drag & drop file";
            tray.Visible = true;
            tray.ContextMenuStrip = MakeMenu(boxes.Count > 0 ? boxes[0] : null);
            tray.DoubleClick += delegate { ShowMainWindow(); };
        }

        private static string InstallDir()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DropFile");
        }

        private bool IsInstalled()
        {
            string dir = InstallDir();
            return string.Equals(Path.GetDirectoryName(Application.ExecutablePath), dir, StringComparison.OrdinalIgnoreCase)
                && File.Exists(Path.Combine(dir, "DropFile.exe"));
        }
    }

    // Tampilan utama aplikasi — muncul saat DropFile diklik/dijalankan.
    internal class MainWindow : Form
    {
        private const int MaxButtons = 6;

        private readonly AppContext app;
        private bool closeHover;
        private bool closePressed;
        private int buttonHover = -1;
        private int buttonPressed = -1;
        private Rectangle CloseBtnRect;

        private readonly List<Rectangle> _buttonRects = new List<Rectangle>();
        private readonly List<Action> _buttonActions = new List<Action>();
        private readonly float[] buttonGlow = new float[MaxButtons];
        private readonly float[] buttonPress = new float[MaxButtons];
        private System.Windows.Forms.Timer btnAnim;

        // Font dik-cache agar tidak dialokasikan ulang per frame.
        private readonly Font titleFont;
        private readonly Font subFont;
        private readonly Font statusFont;
        private readonly Font hintFont;
        private readonly Font[] buttonFonts = new Font[3];

        public MainWindow(AppContext app)
        {
            this.app = app;
            Text = "DropFile — Panel Utama";
            Icon = IconFactory.Create();
            ClientSize = new Size(380, 470);
            MinimumSize = new Size(380, 470);
            MaximumSize = new Size(380, 470);
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = MaterialColors.SurfaceContainerLow;
            DoubleBuffered = true;
            ShowInTaskbar = true;
            CloseBtnRect = new Rectangle(ClientSize.Width - 36, 10, 26, 26);

            titleFont = new Font("Segoe UI", 15f, FontStyle.Bold);
            subFont = new Font("Segoe UI", 9f);
            statusFont = new Font("Segoe UI", 11f, FontStyle.Bold);
            hintFont = new Font("Segoe UI", 8.5f);
            buttonFonts[0] = new Font("Segoe UI", 10f, FontStyle.Bold);
            buttonFonts[1] = new Font("Segoe UI", 10f);
            buttonFonts[2] = new Font("Segoe UI", 10f);

            btnAnim = new System.Windows.Forms.Timer { Interval = 20 };
            btnAnim.Tick += OnButtonAnimTick;
        }

        public void RefreshUi()
        {
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            Rectangle rect = new Rectangle(1, 1, Width - 3, Height - 3);
            using (var brush = new LinearGradientBrush(rect, MaterialColors.SurfaceContainerLow, MaterialColors.SurfaceDim, 90f))
            {
                g.FillRounded(rect, 16, brush);
            }
            using (Pen border = new Pen(Color.FromArgb(120, MaterialColors.OutlineVariant), 1.5f))
            {
                g.DrawRounded(rect, 16, border);
            }
            using (var stripBrush = new LinearGradientBrush(new Rectangle(0, 0, Width, 3), Accent, AccentLight, 0f))
            {
                g.FillRounded(new Rectangle(14, 2, Width - 28, 3), 2, stripBrush);
            }

            // Header: ikon + judul
            if (app != null)
            {
                try
                {
                    using (Icon icon = IconFactory.Create())
                    {
                        g.DrawIcon(icon, new Rectangle(20, 18, 40, 40));
                    }
                }
                catch { }
            }

            using (var white = new SolidBrush(MaterialColors.OnSurface))
            using (var gray = new SolidBrush(MaterialColors.OnSurfaceVariant))
            {
                g.DrawString("DropFile", titleFont, white, 72, 18);
                g.DrawString("Panel utama — seret & tempel file", subFont, gray, 72, 46);
            }

            DrawCloseButton(g);

            // Status fitur
            Settings s = app != null ? app.AppSettings : null;
            bool on = s != null && s.FeatureEnabled;
            float pulse = 0.5f + 0.5f * (float)Math.Sin(uiTime * 3.2);
            using (var statBrush = new SolidBrush(on ? Accent : MaterialColors.Outline))
            using (var glowBrush = new SolidBrush(on ? Color.FromArgb((int)(40 + 35 * pulse), Accent) : Color.Transparent))
            {
                int dotSize = on ? 10 + (int)(3 * pulse) : 10;
                int dotX = 24, dotY = 96;
                if (glowBrush != null && on)
                {
                    g.FillEllipse(glowBrush, dotX - 5, dotY - 5, dotSize + 10, dotSize + 10);
                }
                g.DrawString(on ? "● Aktif" : "○ Nonaktif", statusFont, statBrush, 40, 88);
            }
            using (var gray = new SolidBrush(MaterialColors.OnSurfaceVariant))
            {
                g.DrawString(on
                    ? "Kotak drop siap dipanggil: goyangkan kursor (6x cepat) untuk memunculkan kotak baru."
                    : "Fitur dimatikan. Klik tombol \"Nyalakan\" di bawah untuk mengaktifkan kotak drop.",
                    hintFont, gray, new RectangleF(24, 112, Width - 48, 36));
            }

            // Tombol
            _buttonRects.Clear();
            _buttonActions.Clear();
            int y = 156;
            if (on) y = DrawButton(g, y, "Matikan Fitur", ButtonKind.Primary, new Action(ToggleOff));
            else y = DrawButton(g, y, "Nyalakan Fitur", ButtonKind.Primary, new Action(ToggleOn));
            y = DrawButton(g, y, "Buat Kotak Drop Baru", ButtonKind.Secondary, new Action(CreateBox));
            y = DrawButton(g, y, "Pengaturan Warna...", ButtonKind.Secondary, new Action(ShowColor));
            y = DrawButton(g, y, "Tutup Semua Kotak", ButtonKind.Secondary, new Action(CloseAllBoxes));
            y = DrawButton(g, y, "Hapus Instalasi DropFile...", ButtonKind.Danger, new Action(Uninstall));
            DrawButton(g, y, "Keluar", ButtonKind.Secondary, new Action(ExitApp));
        }

        private enum ButtonKind { Primary, Secondary, Danger }

        private float uiTime;

        private void OnButtonAnimTick(object sender, EventArgs e)
        {
            uiTime += 0.05f;
            bool animating = false;
            for (int i = 0; i < MaxButtons; i++)
            {
                float targetGlow = (buttonHover == i) ? 1f : 0f;
                float targetPress = (buttonPressed == i) ? 1f : 0f;
                if (Math.Abs(buttonGlow[i] - targetGlow) > 0.001f)
                {
                    buttonGlow[i] += (targetGlow - buttonGlow[i]) * 0.28f;
                    animating = true;
                }
                else buttonGlow[i] = targetGlow;
                if (Math.Abs(buttonPress[i] - targetPress) > 0.001f)
                {
                    buttonPress[i] += (targetPress - buttonPress[i]) * 0.3f;
                    animating = true;
                }
                else buttonPress[i] = targetPress;
            }
            if (!animating) btnAnim.Stop();
            Invalidate();
        }

        private int DrawButton(Graphics g, int y, string text, ButtonKind kind, Action onClick)
        {
            Rectangle r = new Rectangle(24, y, Width - 48, 40);
            Color fill = MaterialColors.SurfaceContainerHigh;
            Color fg = MaterialColors.OnSurface;
            if (kind == ButtonKind.Primary)
            {
                fill = Accent;
                fg = MaterialColors.OnPrimary;
            }
            else if (kind == ButtonKind.Danger)
            {
                fill = Color.FromArgb(40, 40, 46);
                fg = MaterialColors.Error;
            }

            int idx = _buttonRects.Count;
            float glow = buttonGlow[idx];
            float press = buttonPress[idx];

            // Efek tekan: ukuran menyusut sedikit + lebih terang.
            int inset = (int)(press * 3f);
            Rectangle dr = r;
            if (inset > 0) dr.Inflate(-inset, -inset);

            using (var b = new SolidBrush(fill))
            {
                g.FillRounded(dr, 10, b);
            }
            using (Pen p = new Pen(Color.FromArgb(60, 255, 255, 255), 1f))
            {
                g.DrawRounded(dr, 10, p);
            }

            // Efek sentuh (hover): overlay membesar pelan.
            if (glow > 0.02f)
            {
                using (var b = new SolidBrush(Color.FromArgb((int)(38 * glow), 255, 255, 255)))
                {
                    g.FillRounded(dr, 10, b);
                }
            }
            if (press > 0.02f)
            {
                Color pressFill = Color.FromArgb((int)(55 * press), kind == ButtonKind.Primary ? Color.White : MaterialColors.Primary);
                using (var b = new SolidBrush(pressFill))
                {
                    g.FillRounded(dr, 10, b);
                }
            }

            using (var fgBrush = new SolidBrush(fg))
            {
                var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                g.DrawString(text, buttonFonts[(int)kind], fgBrush, dr, sf);
            }
            _buttonRects.Add(r);
            _buttonActions.Add(onClick);
            return y + 50;
        }

        private void StartButtonAnim()
        {
            if (!btnAnim.Enabled) btnAnim.Start();
        }

        private void DrawCloseButton(Graphics g)
        {
            Rectangle r = CloseBtnRect;
            using (var bg = new SolidBrush(closeHover ? Color.FromArgb(70, 255, 255, 255) : Color.FromArgb(30, 255, 255, 255)))
            {
                g.FillEllipse(bg, r);
            }
            using (var pen = new Pen(Color.FromArgb(210, 255, 255, 255), 2f))
            {
                g.DrawLine(pen, r.Left + 8, r.Top + 8, r.Right - 8, r.Bottom - 8);
                g.DrawLine(pen, r.Right - 8, r.Top + 8, r.Left + 8, r.Bottom - 8);
            }
        }

        private static Color Accent { get { return MaterialColors.Primary; } }
        private static Color AccentLight { get { return MaterialColors.AccentLight; } }

        private void ToggleOn()
        {
            if (app != null && !app.AppSettings.FeatureEnabled) app.ToggleFeature();
            RefreshUi();
        }

        private void ToggleOff()
        {
            if (app != null && app.AppSettings.FeatureEnabled) app.ToggleFeature();
            RefreshUi();
        }

        private void CreateBox()
        {
            if (app != null) app.CreateBox();
        }

        private void ShowColor()
        {
            if (app != null) app.ShowColorMenu(this, new Point(24, 24));
        }

        private void CloseAllBoxes()
        {
            if (app != null) app.CloseAllBoxes();
            RefreshUi();
        }

        private void Uninstall()
        {
            if (app != null) app.Uninstall();
        }

        private void ExitApp()
        {
            if (app != null) app.Exit();
            else Close();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;

            if (CloseBtnRect.Contains(e.Location))
            {
                closePressed = true;
                closeHover = true;
                buttonPressed = -1;
                Invalidate();
                return;
            }

            // Cek tombol (dari OnPaint terakhir)
            for (int i = 0; i < _buttonRects.Count; i++)
            {
                if (_buttonRects[i].Contains(e.Location))
                {
                    buttonPressed = i;
                    buttonHover = i;
                    StartButtonAnim();
                    Invalidate();
                    return;
                }
            }

            // Area header → drag jendela. Area bawah (non-tombol) jangan didrag.
            if (e.Y < 150)
            {
                Native.ReleaseCapture();
                Native.SendMessage(Handle, WM_NCLBUTTONDOWN, new IntPtr(HTCAPTION), IntPtr.Zero);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool h = CloseBtnRect.Contains(e.Location);
            if (h != closeHover) { closeHover = h; Invalidate(); }

            int hover = -1;
            for (int i = 0; i < _buttonRects.Count; i++)
            {
                if (_buttonRects[i].Contains(e.Location)) { hover = i; break; }
            }
            if (hover != buttonHover) { buttonHover = hover; StartButtonAnim(); Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            closeHover = false;
            if (buttonHover != -1) { buttonHover = -1; StartButtonAnim(); Invalidate(); }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;

            if (closePressed)
            {
                closePressed = false;
                Invalidate();
                if (CloseBtnRect.Contains(e.Location))
                {
                    // Tutup panel utama → sembunyikan ke tray, aplikasi tetap jalan.
                    Hide();
                }
                return;
            }

            if (buttonPressed >= 0 && buttonPressed < _buttonActions.Count)
            {
                int idx = buttonPressed;
                buttonPressed = -1;
                StartButtonAnim();
                Invalidate();
                if (_buttonRects[idx].Contains(e.Location))
                {
                    try { _buttonActions[idx](); }
                    catch { }
                }
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape) Hide();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            // Panel utama tidak menutup aplikasi — hanya sembunyi ke tray.
            if (!app.IsClosing)
            {
                e.Cancel = true;
                Hide();
            }
            else
            {
                DisposeFonts();
            }
        }

        private void DisposeFonts()
        {
            try
            {
                if (titleFont != null) titleFont.Dispose();
                if (subFont != null) subFont.Dispose();
                if (statusFont != null) statusFont.Dispose();
                if (hintFont != null) hintFont.Dispose();
                foreach (Font f in buttonFonts)
                {
                    if (f != null) f.Dispose();
                }
            }
            catch { }
        }

        private const int WM_NCLBUTTONDOWN = 0xA1;
        private const int HTCAPTION = 0x2;
    }

    internal static class MaterialColors
    {
        // Material You (M3) dark tonal palette — dihasilkan dari seed warna.
        public static Color SurfaceDim = Color.FromArgb(20, 18, 24);
        public static Color SurfaceContainerLowest = Color.FromArgb(15, 13, 19);
        public static Color SurfaceContainerLow = Color.FromArgb(29, 27, 32);
        public static Color SurfaceContainer = Color.FromArgb(33, 31, 38);
        public static Color SurfaceContainerHigh = Color.FromArgb(43, 41, 48);
        public static Color SurfaceContainerHighest = Color.FromArgb(54, 52, 59);
        public static Color OnSurface = Color.FromArgb(230, 225, 229);
        public static Color OnSurfaceVariant = Color.FromArgb(202, 196, 208);
        public static Color OutlineVariant = Color.FromArgb(73, 69, 79);
        public static Color Outline = Color.FromArgb(147, 143, 153);
        public static Color Primary = Color.FromArgb(174, 203, 250);
        public static Color AccentLight = Color.FromArgb(205, 224, 252);
        public static Color OnPrimary = Color.FromArgb(0, 50, 88);
        public static Color PrimaryContainer = Color.FromArgb(37, 78, 119);
        public static Color OnPrimaryContainer = Color.FromArgb(211, 228, 255);
        public static Color SecondaryContainer = Color.FromArgb(74, 68, 88);
        public static Color OnSecondaryContainer = Color.FromArgb(233, 222, 255);
        public static Color Error = Color.FromArgb(242, 184, 187);
        public static Color OnError = Color.FromArgb(105, 25, 25);
        public static Color ErrorContainer = Color.FromArgb(140, 29, 24);
        public static Color OnErrorContainer = Color.FromArgb(249, 222, 220);

        // Regenerasi palet tonal dari satu warna seed (gaya Material You).
        public static void Apply(Color seed)
        {
            float h, s, l;
            RgbToHsl(seed, out h, out s, out l);
            if (s < 0.12f) { h = 0.57f; s = 0.55f; }  // seed netral → fallback azure

            Primary = Hsl(h, Math.Max(0.35f, s * 0.85f), 0.74f);
            AccentLight = Hsl(h, Math.Min(1f, s * 0.65f), 0.85f);
            OnPrimary = Hsl(h, Math.Min(1f, s * 0.6f), 0.12f);
            PrimaryContainer = Hsl(h, Math.Max(0.30f, s * 0.6f), 0.30f);
            OnPrimaryContainer = Hsl(h, Math.Min(1f, s * 0.75f), 0.90f);
            SecondaryContainer = Hsl(h, Math.Max(0.20f, s * 0.30f), 0.28f);
            OnSecondaryContainer = Hsl(h, Math.Min(1f, s * 0.6f), 0.90f);

            Error = Color.FromArgb(242, 184, 187);
            OnError = Color.FromArgb(105, 25, 25);
            ErrorContainer = Color.FromArgb(140, 29, 24);
            OnErrorContainer = Color.FromArgb(249, 222, 220);

            SurfaceContainerLowest = Hsl(h, Math.Max(0.25f, s * 0.55f), 0.06f);
            SurfaceDim = Hsl(h, Math.Max(0.25f, s * 0.55f), 0.09f);
            SurfaceContainerLow = Hsl(h, Math.Max(0.28f, s * 0.5f), 0.12f);
            SurfaceContainer = Hsl(h, Math.Max(0.30f, s * 0.5f), 0.15f);
            SurfaceContainerHigh = Hsl(h, Math.Max(0.28f, s * 0.45f), 0.19f);
            SurfaceContainerHighest = Hsl(h, Math.Max(0.25f, s * 0.4f), 0.23f);
            OnSurface = Hsl(h, Math.Min(1f, s * 0.12f), 0.90f);
            OnSurfaceVariant = Hsl(h, Math.Min(1f, s * 0.15f), 0.76f);
            OutlineVariant = Hsl(h, Math.Min(1f, s * 0.18f), 0.30f);
            Outline = Hsl(h, Math.Min(1f, s * 0.15f), 0.55f);
        }

        private static void RgbToHsl(Color c, out float h, out float s, out float l)
        {
            float r = c.R / 255f, g = c.G / 255f, b = c.B / 255f;
            float max = Math.Max(r, Math.Max(g, b));
            float min = Math.Min(r, Math.Min(g, b));
            l = (max + min) / 2f;
            float d = max - min;
            if (d < 0.0001f) { h = 0f; s = 0f; return; }
            s = l > 0.5f ? d / (2f - max - min) : d / (max + min);
            if (max == r) h = (g - b) / d + (g < b ? 6f : 0f);
            else if (max == g) h = (b - r) / d + 2f;
            else h = (r - g) / d + 4f;
            h /= 6f;
        }

        private static Color Hsl(float h, float s, float l)
        {
            if (s <= 0f)
            {
                byte gr = (byte)(l * 255f);
                return Color.FromArgb(gr, gr, gr);
            }
            float q = l < 0.5f ? l * (1f + s) : l + s - l * s;
            float p = 2f * l - q;
            return Color.FromArgb(
                (int)(Hue2Rgb(p, q, h + 1f / 3f) * 255f),
                (int)(Hue2Rgb(p, q, h) * 255f),
                (int)(Hue2Rgb(p, q, h - 1f / 3f) * 255f));
        }

        private static float Hue2Rgb(float p, float q, float t)
        {
            if (t < 0f) t += 1f;
            if (t > 1f) t -= 1f;
            if (t < 1f / 6f) return p + (q - p) * 6f * t;
            if (t < 1f / 2f) return q;
            if (t < 2f / 3f) return p + (q - p) * (2f / 3f - t) * 6f;
            return p;
        }
    }

    internal class DropWindow : Form
    {
        private readonly AppContext app;
        private string statusText;
        private bool hovering;
        private System.Windows.Forms.Timer statusTimer;
        private System.Windows.Forms.Timer animTimer;
        private ToolTip toolTip;
        private bool xHover;
        private bool xPressed;
        private bool basketHover;
        private bool basketPressed;
        private bool settingsHover;
        private bool settingsPressed;
        private bool selfDrag;
        private bool draggingOut;
        private Bitmap dragPreview;
        private Bitmap dragGhost;
        private Rectangle dragGhostRect;

        private readonly List<string> items = new List<string>();
        private readonly Dictionary<string, ShelfAnimItem> itemAnims = new Dictionary<string, ShelfAnimItem>();
        private float shelfBounce = 0f;

        private Point pressPoint;
        private readonly string stagingDir;
        private bool closed;

        private const int FixedWidth = 200;
        private const int FixedHeight = 250;

        private readonly Dictionary<string, Bitmap> thumbCache = new Dictionary<string, Bitmap>();
        private readonly Dictionary<string, Bitmap> iconCache = new Dictionary<string, Bitmap>();

        private static Color Accent { get { return MaterialColors.Primary; } }
        private static Color AccentLight { get { return MaterialColors.AccentLight; } }
        private static Color BgTop { get { return MaterialColors.SurfaceContainerLow; } }
        private static Color BgBottom { get { return MaterialColors.SurfaceDim; } }

        private static readonly Random rng = new Random();

        // File mana saja yang boleh ditarik keluar (default: semua).
        private readonly Dictionary<string, bool> dragEnabled = new Dictionary<string, bool>();

        private const int WM_NCLBUTTONDOWN = 0xA1;
        private const int HTCAPTION = 0x2;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool ChangeWindowMessageFilterEx(IntPtr hWnd, uint msg, uint action, IntPtr pChangeFilterStruct);

        private const uint MSGFLT_ALLOW = 1;
        private const uint WM_DROPFILES = 0x0233;
        private const uint WM_COPYDATA = 0x004A;
        private const uint WM_COPYGLOBALDATA = 0x0049;

        private class ShelfAnimItem
        {
            public float CurrentY = 0f;
            public float TargetY = 0f;
            public float VelocityY = 0f;
            public float Scale = 1.25f;
            public float Alpha = 0f;
            public float Tilt = 0f;
            public float PinGlow = 0f;
            public int Delay = 0;

            public bool Step()
            {
                if (Delay > 0)
                {
                    Delay--;
                    return false;
                }

                bool active = false;

                // "Slap" kertas ke dinding: gravitasi + pantulan pegas yang cepat meredam.
                float diff = TargetY - CurrentY;
                VelocityY = (VelocityY + diff * 0.30f) * 0.45f;
                CurrentY += VelocityY;
                if (Math.Abs(diff) < 0.5f && Math.Abs(VelocityY) < 0.5f)
                {
                    CurrentY = TargetY;
                    VelocityY = 0f;
                }
                else active = true;

                if (Scale > 1.01f)
                {
                    Scale += (1f - Scale) * 0.30f;
                    active = true;
                }
                else Scale = 1f;

                if (Alpha < 254f)
                {
                    Alpha += (255f - Alpha) * 0.36f;
                    active = true;
                }
                else Alpha = 255f;

                if (Math.Abs(Tilt) > 0.15f)
                {
                    Tilt += (0f - Tilt) * 0.22f;
                    active = true;
                }
                else Tilt = 0f;

                if (PinGlow < 1f)
                {
                    PinGlow += 0.25f;
                    if (PinGlow > 1f) PinGlow = 1f;
                    active = true;
                }

                return active;
            }
        }

        private Rectangle XRect()
        {
            return new Rectangle(Width - 30, 6, 22, 22);
        }

        private Rectangle BasketRect()
        {
            return new Rectangle(Width - 92, 11, 60, 20);
        }

        private Rectangle SettingsRect()
        {
            return new Rectangle(10, 10, 17, 17);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                ChangeWindowMessageFilterEx(Handle, WM_DROPFILES, MSGFLT_ALLOW, IntPtr.Zero);
                ChangeWindowMessageFilterEx(Handle, WM_COPYDATA, MSGFLT_ALLOW, IntPtr.Zero);
                ChangeWindowMessageFilterEx(Handle, WM_COPYGLOBALDATA, MSGFLT_ALLOW, IntPtr.Zero);
            }
            catch { }
        }

        public DropWindow(AppContext app)
        {
            this.app = app;
            stagingDir = Path.Combine(
                Path.GetTempPath(), "DropFile", "box-" + Guid.NewGuid().ToString("N"));

            Text = "DropFile";
            Icon = IconFactory.Create();
            ClientSize = new Size(FixedWidth, FixedHeight);
            MinimumSize = new Size(200, 240);
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            TopMost = app.AppSettings.AlwaysOnTop;
            ShowInTaskbar = false;
            AllowDrop = true;
            MaterialColors.Apply(app.AppSettings.SeedColor());
            BackColor = MaterialColors.SurfaceContainerLow;
            DoubleBuffered = true;
            Visible = false;
            ContextMenuStrip = app.MakeMenu(this);
            SetRegion();

            try {
                string previewPath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "logo-preview.png");
                if (File.Exists(previewPath)) dragPreview = new Bitmap(previewPath);
            } catch { }

            statusTimer = new System.Windows.Forms.Timer { Interval = 3000 };
            statusTimer.Tick += delegate
            {
                statusTimer.Stop();
                statusText = null;
                Invalidate();
            };

            animTimer = new System.Windows.Forms.Timer { Interval = 30 };
            animTimer.Tick += OnAnimTick;

            toolTip = new ToolTip();
            UpdateTooltip();

            this.GiveFeedback += DropWindow_GiveFeedback;
        }

        private void OnAnimTick(object sender, EventArgs e)
        {
            bool needsRedraw = false;

            if (shelfBounce > 0.1f)
            {
                shelfBounce *= 0.75f;
                needsRedraw = true;
            }
            else shelfBounce = 0f;

            foreach (var kv in itemAnims)
            {
                if (kv.Value.Step()) needsRedraw = true;
            }

            if (needsRedraw) Invalidate();
            else if (shelfBounce == 0f)
            {
                bool allDone = true;
                foreach (var kv in itemAnims)
                {
                    if (kv.Value.Delay > 0 ||
                        Math.Abs(kv.Value.CurrentY - kv.Value.TargetY) > 0.4f ||
                        kv.Value.Scale > 1.01f || kv.Value.Alpha < 254f)
                    {
                        allDone = false;
                        break;
                    }
                }
                if (allDone) animTimer.Stop();
            }
        }

        private void UpdateTooltip()
        {
            toolTip.SetToolTip(this,
                "Rak Dokumen DropFile:\n" +
                "• Seret file, gambar, atau halaman web ke rak ini (dari aplikasi apa pun).\n" +
                "• File yang masuk tersimpan di keranjang; bisa terus menambahkan file baru.\n" +
                "• Klik tombol ▾ di kanan atas untuk memilih (centang) file mana yang boleh ditarik keluar.\n" +
                "• Tarik file dari rak ke folder/aplikasi lain untuk memindahkan yang tercentang.\n" +
                "• Klik ✕ untuk menutup dan membersihkan rak.");
        }

        public void ShowStatus(string text)
        {
            statusText = text;
            statusTimer.Stop();
            statusTimer.Start();
            Invalidate();
        }

        public void ShowWindow()
        {
            if (!app.AppSettings.FeatureEnabled) return;
            PositionNearCursor();
            if (!Visible)
            {
                Show();
            }
            Invalidate();
        }

        public void HideWindow()
        {
            statusTimer.Stop();
            animTimer.Stop();
            if (Visible)
            {
                Hide();
            }
        }

        private void AddItem(string path)
        {
            if (!HasItem(path))
            {
                items.Insert(0, path);
                dragEnabled[path] = true;
                itemAnims[path] = new ShelfAnimItem
                {
                    CurrentY = -56f,
                    Tilt = (float)rng.Next(-10, 11),
                    Scale = 1.16f,
                    Delay = itemAnims.Count * 5
                };
                shelfBounce = 8f;
                if (!animTimer.Enabled) animTimer.Start();
            }
        }

        private bool HasItem(string path)
        {
            foreach (string it in items)
            {
                if (string.Equals(it, path, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private void PositionNearCursor()
        {
            Point c = Cursor.Position;
            Rectangle wa = Screen.FromPoint(c).WorkingArea;
            int x = c.X - Width / 2;
            int y = c.Y - Height / 2;
            x = Math.Max(wa.Left + 4, Math.Min(x, wa.Right - Width - 4));
            y = Math.Max(wa.Top + 4, Math.Min(y, wa.Bottom - Height - 4));
            SetDesktopLocation(x, y);
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape) HideWindow();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            Rectangle rect = new Rectangle(1, 1, Width - 3, Height - 3);

            using (var brush = new LinearGradientBrush(rect, BgTop, BgBottom, 90f))
            {
                g.FillRounded(rect, 16, brush);
            }

            // HD soft sheen at top for depth
            using (var sheen = new LinearGradientBrush(rect, Color.FromArgb(22, 255, 255, 255), Color.Transparent, 90f))
            {
                g.FillRounded(new Rectangle(1, 1, Width - 3, 72), 16, sheen);
            }

            using (Pen border = hovering ? new Pen(Accent, 3f) : new Pen(Color.FromArgb(120, MaterialColors.OutlineVariant), 1.5f))
            {
                g.DrawRounded(rect, 16, border);
            }

            using (var stripBrush = new LinearGradientBrush(new Rectangle(0, 0, Width, 3), Accent, AccentLight, 0f))
            {
                g.FillRounded(new Rectangle(14, 2, Width - 28, 3), 2, stripBrush);
            }

            using (var titleFont = new Font("Segoe UI", 9f, FontStyle.Bold))
            using (var subFont = new Font("Segoe UI", 7.5f))
            using (var white = new SolidBrush(MaterialColors.OnSurface))
            using (var gray = new SolidBrush(Color.FromArgb(200, MaterialColors.OnSurfaceVariant)))
            using (var accentBrush = new SolidBrush(Accent))
            using (var sfCenter = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                Rectangle sRect = SettingsRect();
                using (var glow = new SolidBrush(settingsHover ? Color.FromArgb(150, Accent) : Color.FromArgb(70, Accent)))
                {
                    int gd = settingsHover ? 22 : 15;
                    g.FillEllipse(glow, sRect.Left + sRect.Width / 2 - gd / 2, sRect.Top + sRect.Height / 2 - gd / 2, gd, gd);
                }
                if (settingsHover)
                {
                    using (var ring = new Pen(Color.FromArgb(200, Accent), 1.4f))
                    {
                        g.DrawEllipse(ring, sRect.Left + 2, sRect.Top + 2, sRect.Width - 4, sRect.Height - 4);
                    }
                }
                g.FillEllipse(accentBrush, 14, 14, 8, 8);
                using (var shine = new SolidBrush(Color.FromArgb(90, Color.White)))
                {
                    g.FillEllipse(shine, 15, 15, 3, 2);
                }

                Rectangle titleRect = new Rectangle(28, 9, Width - 120, 20);
                g.DrawString(hovering ? "LEPASKAN DI RAK!" : "RAK DOKUMEN", titleFont, hovering ? accentBrush : white, titleRect);

                if (items.Count > 0)
                {
                    Rectangle br = BasketRect();
                    using (var bBg = new SolidBrush(basketHover || basketPressed ? MaterialColors.PrimaryContainer : MaterialColors.SurfaceContainerHighest))
                    using (var bPen = new Pen(basketHover ? MaterialColors.Primary : MaterialColors.OutlineVariant, 1f))
                    using (var bFg = new SolidBrush(basketHover || basketPressed ? MaterialColors.OnPrimaryContainer : MaterialColors.OnSurface))
                    using (var bFont = new Font("Segoe UI", 8f, FontStyle.Bold))
                    {
                        g.FillRounded(br, 10, bBg);
                        g.DrawRounded(br, 10, bPen);
                        g.DrawString("▾ " + items.Count, bFont, bFg, br, sfCenter);
                    }
                }

                Rectangle xr = XRect();
                using (var xBg = new SolidBrush(xHover ? MaterialColors.ErrorContainer : Color.FromArgb(30, MaterialColors.OnSurface)))
                using (var xPen = new Pen(xHover ? MaterialColors.OnErrorContainer : MaterialColors.OnSurfaceVariant, 1.6f))
                {
                    g.FillEllipse(xBg, xr);
                    g.DrawLine(xPen, xr.Left + 7, xr.Top + 7, xr.Right - 7, xr.Bottom - 7);
                    g.DrawLine(xPen, xr.Right - 7, xr.Top + 7, xr.Left + 7, xr.Bottom - 7);
                }

                if (items.Count > 0 && draggingOut)
                {
                    using (var dashPen = new Pen(Color.FromArgb(150, Accent), 2f) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash })
                    {
                        g.DrawRounded(ShelfRect(), 8, dashPen);
                    }
                }

                using (var hair = new Pen(Color.FromArgb(45, MaterialColors.OnSurfaceVariant), 1f))
                {
                    g.DrawLine(hair, 14, 42, Width - 14, 42);
            // Update basket hover state for mouse move
            // (handled in OnMouseMove override)

                }
                int stackTop = 48 + (int)shelfBounce;
                int stackBottom = Height - 42;
                int stackH = stackBottom - stackTop;

                if (items.Count > 0)
                {
                    DrawPaperStack(g, stackTop, stackBottom, stackH);
                }
                else
                {
                    Rectangle zone = new Rectangle(Width / 2 - 68, stackTop + 14, 136, stackH - 30);
                    using (var panel = new SolidBrush(Color.FromArgb(36, MaterialColors.SurfaceContainerHighest)))
                    {
                        g.FillRounded(zone, 12, panel);
                    }
                    using (var zonePen = new Pen(Color.FromArgb(150, Accent), 1.2f))
                    {
                        zonePen.DashStyle = System.Drawing.Drawing2D.DashStyle.Dash;
                        g.DrawRounded(zone, 12, zonePen);
                    }
                    DrawInboxIcon(g, zone, accentBrush);
                    using (var emptyFont = new Font("Segoe UI", 8f))
                    {
                        g.DrawString(hovering ? "Lepaskan di sini!" : "Keranjang kosong\nSeret file / gambar / web ke sini", emptyFont, hovering ? accentBrush : gray, new Rectangle(12, zone.Top + zone.Height - 44, Width - 24, 34), sfCenter);
                    }
                }
                if (draggingOut && items.Count > 0)
                {
                    using (var overlay = new SolidBrush(Color.FromArgb(30, Accent)))
                    {
                        g.FillRounded(ShelfRect(), 8, overlay);
                    }
                }

                Rectangle hintRect = new Rectangle(8, Height - 26, Width - 16, 20);
                string textToShow = !string.IsNullOrEmpty(statusText) ? statusText : "Seret keluar untuk memindahkan";
                g.DrawString(Shorten(textToShow, hintRect.Width, subFont, g), subFont, !string.IsNullOrEmpty(statusText) ? accentBrush : gray, hintRect, sfCenter);
            }
        }

        private void DrawPaperStack(Graphics g, int stackTop, int stackBottom, int stackH)
        {
            int maxVisible = 4;
            int count = Math.Min(items.Count, maxVisible);
            int peek = 12;
            int cardW = Width - 48;
            int cardH = stackH - (count - 1) * peek - 10;
            int centerX = Width / 2;

            for (int i = count - 1; i >= 0; i--)
            {
                string path = items[i];
                ShelfAnimItem anim;
                if (!itemAnims.TryGetValue(path, out anim))
                {
                    anim = new ShelfAnimItem { CurrentY = 0, Scale = 1, Alpha = 255, Tilt = 0, PinGlow = 1 };
                }

                int alpha = (int)anim.Alpha;
                if (alpha > 255) alpha = 255;
                if (alpha < 0) alpha = 0;
                alpha = (int)(alpha * (1f - 0.06f * i));   // kedalaman: kartu bawah sedikit meredup
                if (alpha > 255) alpha = 255;
                if (alpha < 0) alpha = 0;

                int cardY = stackTop + i * peek + (int)anim.CurrentY;
                int cardX = centerX - cardW / 2;
                Rectangle cardRect = new Rectangle(cardX, cardY, cardW, cardH);

                // Mikro-kemiringan alami agar tersusun rapih tapi tetap hidup.
                float restTilt = i == 0 ? 0f : (i % 2 == 0 ? -1.3f : 1.1f);
                float drawTilt = anim.Tilt + restTilt * (anim.Alpha / 255f);

                GraphicsState gs = g.Save();

                if (Math.Abs(drawTilt) > 0.05f || anim.Scale > 1.01f)
                {
                    g.TranslateTransform(centerX, cardY + cardH);
                    g.RotateTransform(drawTilt);
                    g.ScaleTransform(anim.Scale, anim.Scale);
                    g.TranslateTransform(-centerX, -(cardY + cardH));
                }

                DrawDocumentCard(g, cardRect, path, alpha, i == 0, anim.PinGlow);

                g.Restore(gs);
            }
        }

        private void DrawDocumentCard(Graphics g, Rectangle r, string path, int alpha, bool isTopItem, float pinGlow)
        {
            string ext = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
            if (string.IsNullOrEmpty(ext)) ext = Directory.Exists(path) ? "folder" : "file";
            Color headerColor = GetExtHeaderColor(ext);
            string displayTag = ext.Length > 4 ? ext.Substring(0, 4).ToUpperInvariant() : ext.ToUpperInvariant();
            string name = Path.GetFileName(path.TrimEnd('\\', '/'));
            if (string.IsNullOrEmpty(name)) name = "(folder)";

            using (var s1 = new SolidBrush(Color.FromArgb((int)(alpha * 0.25f), 0, 0, 0)))
            {
                g.FillRounded(new Rectangle(r.Left + 4, r.Top + 5, r.Width, r.Height), 10, s1);
            }

            using (var paper = new LinearGradientBrush(r, Color.FromArgb(alpha, 255, 255, 255), Color.FromArgb(alpha, 244, 247, 251), 90f))
            using (var borderPen = new Pen(Color.FromArgb(isTopItem ? alpha : (int)(alpha * 0.6f), isTopItem ? Accent : Color.FromArgb(214, 218, 226)), isTopItem ? 1.8f : 1f))
            {
                g.FillRounded(r, 10, paper);
                g.DrawRounded(r, 10, borderPen);
            }

            using (var hl = new Pen(Color.FromArgb((int)(alpha * 0.55f), 255, 255, 255), 1f))
            {
                g.DrawLine(hl, r.Left + 6, r.Top + 2, r.Right - 6, r.Top + 2);
            }

            int fold = 10;
            Point[] foldPoly = new Point[]
            {
                new Point(r.Right - fold, r.Top),
                new Point(r.Right, r.Top + fold),
                new Point(r.Right - fold, r.Top + fold)
            };
            using (var foldBrush = new SolidBrush(Color.FromArgb(alpha, 216, 220, 228)))
            using (var foldEdge = new Pen(Color.FromArgb((int)(alpha * 0.55f), 150, 155, 165), 1f))
            {
                g.FillPolygon(foldBrush, foldPoly);
                g.DrawLine(foldEdge, r.Right - fold, r.Top + 1, r.Right - 1, r.Top + fold);
            }

            using (var strip = new SolidBrush(Color.FromArgb(alpha, headerColor)))
            {
                g.FillRounded(new Rectangle(r.Left + 8, r.Top + 8, 4, r.Height - 34), 2, strip);
            }

            Rectangle body = new Rectangle(r.Left + 16, r.Top + 10, r.Width - 30, r.Height - 40);

            if (File.Exists(path) && IsImageExt(path))
            {
                Bitmap t = GetThumb(path, body.Width, body.Height);
                if (t != null)
                {
                    int tx = body.Left + (body.Width - t.Width) / 2;
                    int ty = body.Top + (body.Height - t.Height) / 2;
                    using (var clip = RoundedPath(new Rectangle(tx, ty, t.Width, t.Height), 6))
                    {
                        g.SetClip(clip);
                        g.DrawImage(t, tx, ty, t.Width, t.Height);
                        g.ResetClip();
                    }
                    using (var imgBorder = new Pen(Color.FromArgb((int)(alpha * 0.35f), 150, 155, 165), 1f))
                    {
                        g.DrawRounded(new Rectangle(tx, ty, t.Width - 1, t.Height - 1), 6, imgBorder);
                    }
                }
                else
                {
                    DrawDocPreviewLines(g, body, alpha);
                }
            }
            else if (Directory.Exists(path))
            {
                DrawFolderGlyph(g, body, alpha);
            }
            else
            {
                Rectangle tagRect = new Rectangle(body.Left, body.Top, 34, 15);
                using (var tagBrush = new SolidBrush(Color.FromArgb(alpha, headerColor)))
                using (var tagFont = new Font("Segoe UI", 6.5f, FontStyle.Bold))
                using (var whiteBrush = new SolidBrush(Color.FromArgb(alpha, Color.White)))
                using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                {
                    g.FillRounded(tagRect, 3, tagBrush);
                    g.DrawString(displayTag, tagFont, whiteBrush, tagRect, sf);
                }
                DrawDocPreviewLines(g, new Rectangle(body.Left + 42, body.Top + 1, body.Width - 42, body.Height - 1), alpha);
            }

            using (var nameFont = new Font("Segoe UI", 8f, FontStyle.Bold))
            using (var nameBrush = new SolidBrush(Color.FromArgb(alpha, 56, 64, 78)))
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                Rectangle nameRect = new Rectangle(r.Left + 12, r.Bottom - 26, r.Width - 24, 18);
                g.DrawString(Shorten(name, nameRect.Width, nameFont, g), nameFont, nameBrush, nameRect, sf);
            }

            DrawPin(g, r, alpha, pinGlow);
        }

        private static void DrawPin(Graphics g, Rectangle r, int alpha, float pinGlow)
        {
            int cx = r.Left + r.Width / 2;
            int pinY = r.Top + 6;

            using (var halo = new SolidBrush(Color.FromArgb(
                (int)(alpha * 0.55f * (1f - pinGlow)), MaterialColors.Primary)))
            {
                int d = 16 + (int)((1f - pinGlow) * 14);
                g.FillEllipse(halo, cx - d / 2, pinY - d / 2, d, d);
            }

            using (var sh = new SolidBrush(Color.FromArgb((int)(alpha * 0.30f), 20, 20, 30)))
            {
                g.FillEllipse(sh, cx - 5, pinY + 5, 10, 4);
            }

            using (var grad = new LinearGradientBrush(
                new Rectangle(cx - 5, pinY - 5, 10, 10),
                Color.FromArgb(alpha, 255, 255, 255),
                Color.FromArgb(alpha, 150, 158, 172), 60f))
            {
                g.FillEllipse(grad, cx - 5, pinY - 5, 10, 10);
            }

            using (var edge = new Pen(Color.FromArgb((int)(alpha * 0.55f), 85, 95, 112), 1f))
            {
                g.DrawEllipse(edge, cx - 5, pinY - 5, 10, 10);
            }

            using (var stem = new SolidBrush(Color.FromArgb(alpha, 96, 106, 124)))
            {
                g.FillEllipse(stem, cx - 1, pinY + 3, 2, 4);
            }

            using (var hl = new SolidBrush(Color.FromArgb(alpha, 255, 255, 255)))
            {
                g.FillEllipse(hl, cx - 3, pinY - 3, 3, 2);
            }
        }

        private void DrawDocPreviewLines(Graphics g, Rectangle body, int alpha)
        {
            int[] widths = { -4, -10, -6, -12, -8, -14 };
            int y = body.Top + 4;
            using (var linePen = new Pen(Color.FromArgb((int)(alpha * 0.5f), 130, 140, 156), 2f))
            {
                for (int i = 0; i < widths.Length && y < body.Bottom - 6; i++)
                {
                    g.DrawLine(linePen, body.Left, y, body.Right + widths[i], y);
                    y += 12;
                }
            }
        }

        private void DrawFolderGlyph(Graphics g, Rectangle body, int alpha)
        {
            int fw = Math.Min(body.Width - 24, 100);
            int fh = fw * 3 / 4;
            int fx = body.Left + (body.Width - fw) / 2;
            int fy = body.Top + (body.Height - fh) / 2;
            using (var tab = new SolidBrush(Color.FromArgb(alpha, 232, 182, 58)))
            using (var folder = new SolidBrush(Color.FromArgb(alpha, 255, 208, 88)))
            {
                g.FillRounded(new Rectangle(fx + 4, fy, fw / 3, 10), 3, tab);
                g.FillRounded(new Rectangle(fx, fy + 8, fw, fh - 8), 5, folder);
            }
        }

        private void DrawInboxIcon(Graphics g, Rectangle zone, Brush accent)
        {
            int cx = zone.Left + zone.Width / 2;
            int cy = zone.Top + 34;
            int w = 40;
            int h = 24;
            using (var pen = new Pen(Color.FromArgb(165, 255, 255, 255), 1.6f))
            using (var penA = new Pen(Color.FromArgb(210, Accent), 2f))
            {
                g.DrawLine(penA, cx, cy - 17, cx, cy - 6);
                g.DrawLine(penA, cx - 6, cy - 11, cx, cy - 6);
                g.DrawLine(penA, cx + 6, cy - 11, cx, cy - 6);
                g.DrawLine(pen, cx - w / 2 + 6, cy, cx + w / 2 - 6, cy);
                Rectangle tray = new Rectangle(cx - w / 2, cy + 1, w, h);
                using (var gp = new GraphicsPath())
                {
                    int rad = 6;
                    gp.AddArc(tray.Left, tray.Top + 2, rad * 2, rad * 2, 180, 90);
                    gp.AddArc(tray.Right - rad * 2, tray.Top + 2, rad * 2, rad * 2, 270, 90);
                    gp.AddArc(tray.Right - rad * 2, tray.Bottom - rad * 2, rad * 2, rad * 2, 0, 90);
                    gp.AddArc(tray.Left, tray.Bottom - rad * 2, rad * 2, rad * 2, 90, 90);
                    gp.CloseFigure();
                    g.DrawPath(pen, gp);
                }
            }
        }

        private static GraphicsPath RoundedPath(Rectangle rect, int radius)
        {
            var gp = new GraphicsPath();
            gp.AddArc(rect.X, rect.Y, radius * 2, radius * 2, 180, 90);
            gp.AddArc(rect.Right - radius * 2, rect.Y, radius * 2, radius * 2, 270, 90);
            gp.AddArc(rect.Right - radius * 2, rect.Bottom - radius * 2, radius * 2, radius * 2, 0, 90);
            gp.AddArc(rect.X, rect.Bottom - radius * 2, radius * 2, radius * 2, 90, 90);
            gp.CloseFigure();
            return gp;
        }

        private static Color GetExtHeaderColor(string ext)
        {
            switch (ext)
            {
                case "png":
                case "jpg":
                case "jpeg":
                case "gif":
                case "webp":
                case "bmp":
                    return Color.FromArgb(0, 188, 212);
                case "url":
                case "html":
                case "htm":
                case "http":
                    return Color.FromArgb(33, 150, 243);
                case "txt":
                case "cs":
                case "json":
                case "js":
                    return Color.FromArgb(76, 175, 80);
                case "pdf":
                case "doc":
                case "docx":
                    return Color.FromArgb(244, 67, 54);
                case "xls":
                case "xlsx":
                case "csv":
                    return Color.FromArgb(46, 125, 50);
                default:
                    return Color.FromArgb(156, 39, 176);
            }
        }

        private static string Shorten(string text, int maxWidth, Font font, Graphics g)
        {
            if (g.MeasureString(text, font).Width <= maxWidth) return text;
            int len = text.Length;
            while (len > 1 && g.MeasureString(text.Substring(0, len) + "...", font).Width > maxWidth)
            {
                len--;
            }
            return text.Substring(0, len) + "...";
        }

        private static bool IsImageExt(string path)
        {
            switch (Path.GetExtension(path).ToLowerInvariant())
            {
                case ".png":
                case ".jpg":
                case ".jpeg":
                case ".gif":
                case ".bmp":
                case ".ico":
                case ".tif":
                case ".tiff":
                    return true;
                default:
                    return false;
            }
        }

        private Bitmap GetThumb(string path, int maxW, int maxH)
        {
            Bitmap bmp;
            if (thumbCache.TryGetValue(path, out bmp)) return bmp;
            Bitmap made = null;
            try
            {
                using (Image img = Image.FromFile(path))
                {
                    int w = img.Width;
                    int h = img.Height;
                    double scale = Math.Min((double)maxW / w, (double)maxH / h);
                    int nw = Math.Max(1, (int)(w * scale));
                    int nh = Math.Max(1, (int)(h * scale));
                    made = new Bitmap(nw, nh);
                    using (Graphics gr = Graphics.FromImage(made))
                    {
                        gr.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        gr.SmoothingMode = SmoothingMode.AntiAlias;
                        gr.DrawImage(img, 0, 0, nw, nh);
                    }
                }
            }
            catch
            {
                made = null;
            }
            if (made != null)
            {
                if (thumbCache.Count > 24) ClearThumbCache();
                thumbCache[path] = made;
            }
            return made;
        }

        private void ClearThumbCache()
        {
            foreach (var kv in thumbCache) kv.Value.Dispose();
            thumbCache.Clear();
        }

        private Rectangle ShelfRect()
        {
            return new Rectangle(12, 48, Width - 24, Height - 90);
        }

        private void ShowBasketMenu()
        {
            if (items.Count == 0) return;
            var menu = new ContextMenuStrip();
            menu.Renderer = new MaterialMenuRenderer();
            menu.ShowImageMargin = true;
            menu.Font = new Font("Segoe UI", 9f);

            var header = new ToolStripMenuItem("Keranjang — " + items.Count + " file — centang yang boleh ditarik keluar") { Enabled = false };
            menu.Items.Add(header);
            menu.Items.Add(new ToolStripSeparator());

            var checks = new Dictionary<ToolStripMenuItem, string>();
            for (int i = 0; i < items.Count; i++)
            {
                string path = items[i];
                string name = Path.GetFileName(path.TrimEnd('\\', '/'));
                if (string.IsNullOrEmpty(name)) name = "(folder)";

                var item = new ToolStripMenuItem(name)
                {
                    CheckOnClick = true,
                    Tag = path,
                    ToolTipText = "Centang agar file ini ikut ditarik keluar"
                };

                bool on;
                item.Checked = !dragEnabled.TryGetValue(path, out on) || on;

                string captured = path;
                item.CheckedChanged += delegate { dragEnabled[captured] = item.Checked; };
                checks[item] = path;
                menu.Items.Add(item);
            }

            menu.Items.Add(new ToolStripSeparator());

            var selAll = new ToolStripMenuItem("Pilih Semua");
            selAll.Click += delegate { foreach (var kv in checks) kv.Key.Checked = true; };
            menu.Items.Add(selAll);

            var selNone = new ToolStripMenuItem("Kosongkan Pilihan");
            selNone.Click += delegate { foreach (var kv in checks) kv.Key.Checked = false; };
            menu.Items.Add(selNone);

            menu.Items.Add(new ToolStripSeparator());

            var dragAll = new ToolStripMenuItem("Seret Semua Keluar");
            dragAll.Click += delegate { StartDragOut(new List<string>(items)); };
            menu.Items.Add(dragAll);

            var dragSel = new ToolStripMenuItem("Seret Keluar (yang Terpilih)");
            dragSel.Click += delegate
            {
                var sel = new List<string>();
                foreach (var kv in checks) if (kv.Key.Checked) sel.Add(kv.Value);
                if (sel.Count > 0) StartDragOut(sel);
            };
            menu.Items.Add(dragSel);

            menu.Items.Add(new ToolStripSeparator());

            var clearItem = new ToolStripMenuItem("Bersihkan Semua");
            clearItem.ForeColor = MaterialColors.Error;
            clearItem.Click += delegate
            {
                items.Clear();
                itemAnims.Clear();
                dragEnabled.Clear();
                Invalidate();
            };
            menu.Items.Add(clearItem);

            Rectangle br = BasketRect();
            menu.Show(this, new Point(br.Left, br.Bottom + 2));
        }

        private void ShowSettingsMenu()
        {
            Rectangle sr = SettingsRect();
            app.ShowColorMenu(this, new Point(sr.Left, sr.Bottom + 2));
        }

        internal static Bitmap MakeSwatch(Color c)
        {
            var bmp = new Bitmap(14, 14);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var b = new SolidBrush(c))
                {
                    g.FillRounded(new Rectangle(1, 1, 12, 12), 4, b);
                }
                using (var p = new Pen(Color.FromArgb(90, 255, 255, 255), 1f))
                {
                    g.DrawRounded(new Rectangle(1, 1, 12, 12), 4, p);
                }
            }
            return bmp;
        }

        private void DropWindow_GiveFeedback(object sender, GiveFeedbackEventArgs e)
        {
            if (dragPreview == null)
            {
                e.UseDefaultCursors = true;
                return;
            }
            e.UseDefaultCursors = false;

            Point pos = Cursor.Position;
            int w = 64;
            int h = (int)(dragPreview.Height * ((float)w / dragPreview.Width));
            if (h > 64) { h = 64; w = (int)(dragPreview.Width * ((float)h / dragPreview.Height)); }
            if (w < 16) w = 16;
            if (h < 16) h = 16;
            Rectangle r = new Rectangle(pos.X - w / 2, pos.Y - h / 2, w, h);

            IntPtr hdc = Native.GetDC(IntPtr.Zero);
            if (hdc == IntPtr.Zero) return;
            try
            {
                using (Graphics g = Graphics.FromHdc(hdc))
                {
                    if (dragGhost != null)
                    {
                        g.DrawImageUnscaled(dragGhost, dragGhostRect.X, dragGhostRect.Y);
                    }
                    if (dragGhost == null || dragGhost.Width != r.Width || dragGhost.Height != r.Height)
                    {
                        if (dragGhost != null) dragGhost.Dispose();
                        dragGhost = new Bitmap(r.Width, r.Height);
                    }
                    using (var bg = Graphics.FromImage(dragGhost))
                    {
                        bg.CopyFromScreen(r.X, r.Y, 0, 0, r.Size);
                    }
                    using (var ia = new System.Drawing.Imaging.ImageAttributes())
                    {
                        var cm = new System.Drawing.Imaging.ColorMatrix();
                        cm.Matrix33 = 0.85f;
                        ia.SetColorMatrix(cm);
                        g.DrawImage(dragPreview, r, 0, 0, dragPreview.Width, dragPreview.Height, GraphicsUnit.Pixel, ia);
                    }
                    dragGhostRect = r;
                }
            }
            finally
            {
                Native.ReleaseDC(IntPtr.Zero, hdc);
            }
        }

        private void ClearDragGhost()
        {
            if (dragGhost == null) return;
            IntPtr hdc = Native.GetDC(IntPtr.Zero);
            if (hdc != IntPtr.Zero)
            {
                try
                {
                    using (Graphics g = Graphics.FromHdc(hdc))
                    {
                        g.DrawImageUnscaled(dragGhost, dragGhostRect.X, dragGhostRect.Y);
                    }
                }
                finally { Native.ReleaseDC(IntPtr.Zero, hdc); }
            }
            dragGhost.Dispose();
            dragGhost = null;
        }

        private void StartDragOut()
        {
            // Hanya file yang dicentang (dragEnabled = true) yang ikut ditarik keluar.
            var enabled = new List<string>();
            foreach (string p in items)
            {
                bool on;
                if (!dragEnabled.TryGetValue(p, out on) || on) enabled.Add(p);
            }
            if (enabled.Count == 0) enabled = new List<string>(items);
            StartDragOut(enabled);
        }

        private void StartDragOut(List<string> paths)
        {
            var valid = new List<string>();
            foreach (string p in paths)
            {
                if (File.Exists(p) || Directory.Exists(p)) valid.Add(p);
            }
            if (valid.Count == 0) return;

            var data = new DataObject();

            var fileList = new StringCollection();
            foreach (string p in valid) fileList.Add(p);
            data.SetFileDropList(fileList);

            string topPath = valid[0];
            string topName = Path.GetFileName(topPath);
            data.SetData(DataFormats.Text, topName);
            data.SetData(DataFormats.UnicodeText, topName);
            data.SetData(DataFormats.Html, string.Format("<a href=\"file:///{0}\">{1}</a>", topPath.Replace('\\', '/'), topName));


            // Attempt to use preview image if available
            Image previewImg = null;
            try
            {
                string appDir = AppDomain.CurrentDomain.BaseDirectory;
                string previewFile = Path.Combine(appDir, "logo-preview.png");
                if (File.Exists(previewFile))
                {
                    previewImg = Image.FromFile(previewFile);
                    data.SetData(DataFormats.Bitmap, true, previewImg);
                }
            }
            catch { }

            selfDrag = true;
            try
            {
                DoDragDrop(data, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
            }
            finally
            {
                selfDrag = false;
                ClearDragGhost();
                if (previewImg != null) previewImg.Dispose();
                foreach (string p in valid)
                {
                    items.Remove(p);
                    itemAnims.Remove(p);
                    dragEnabled.Remove(p);
                }
                Invalidate();
            }
        }

        private sealed class MaterialMenuColorTable : ProfessionalColorTable        {
            public override Color MenuItemSelected { get { return MaterialColors.SecondaryContainer; } }
            public override Color MenuItemBorder { get { return MaterialColors.SecondaryContainer; } }
            public override Color ToolStripDropDownBackground { get { return MaterialColors.SurfaceContainer; } }
            public override Color ImageMarginGradientBegin { get { return MaterialColors.SurfaceContainer; } }
            public override Color ImageMarginGradientMiddle { get { return MaterialColors.SurfaceContainer; } }
            public override Color ImageMarginGradientEnd { get { return MaterialColors.SurfaceContainer; } }
            public override Color MenuBorder { get { return MaterialColors.OutlineVariant; } }
            public override Color SeparatorDark { get { return MaterialColors.OutlineVariant; } }
            public override Color SeparatorLight { get { return MaterialColors.SurfaceContainer; } }
            public override Color CheckBackground { get { return MaterialColors.PrimaryContainer; } }
            public override Color CheckSelectedBackground { get { return MaterialColors.PrimaryContainer; } }
            public override Color CheckPressedBackground { get { return MaterialColors.PrimaryContainer; } }
        }

        internal sealed class MaterialMenuRenderer : ToolStripProfessionalRenderer
        {
            public MaterialMenuRenderer() : base(new MaterialMenuColorTable()) { }

            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
            {
                e.TextColor = e.Item.Enabled ? MaterialColors.OnSurface : MaterialColors.OnSurfaceVariant;
                base.OnRenderItemText(e);
            }

            protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
            {
                if (e.Item.Image != null) return; // item swatch warna: biarkan gambar tampil
                Rectangle r = e.ImageRectangle;
                bool isChecked = e.Item is ToolStripMenuItem && ((ToolStripMenuItem)e.Item).Checked;
                if (isChecked)
                {
                    using (var bg = new SolidBrush(MaterialColors.Primary))
                    {
                        e.Graphics.FillRounded(r, 3, bg);
                    }
                    using (var pen = new Pen(MaterialColors.OnPrimary, 1.8f))
                    {
                        e.Graphics.DrawLine(pen, r.Left + 3, r.Top + r.Height / 2, r.Left + 5, r.Top + r.Height - 3);
                        e.Graphics.DrawLine(pen, r.Left + 5, r.Top + r.Height - 3, r.Right - 3, r.Top + 2);
                    }
                }
                else
                {
                    using (var bg = new SolidBrush(MaterialColors.SurfaceContainerHigh))
                    using (var p = new Pen(MaterialColors.OutlineVariant, 1f))
                    {
                        e.Graphics.FillRounded(r, 3, bg);
                        e.Graphics.DrawRounded(r, 3, p);
                    }
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ClearThumbCache();
                foreach (var kv in iconCache) kv.Value.Dispose();
                iconCache.Clear();
                if (dragPreview != null) { dragPreview.Dispose(); dragPreview = null; }
                if (dragGhost != null) { dragGhost.Dispose(); dragGhost = null; }
                try { animTimer.Stop(); animTimer.Dispose(); } catch { }
                try
                {
                    if (Directory.Exists(stagingDir)) Directory.Delete(stagingDir, true);
                }
                catch { }
            }
            base.Dispose(disposing);
        }

        protected override void OnDragEnter(DragEventArgs e)
        {
            base.OnDragEnter(e);
            if (selfDrag)
            {
                e.Effect = DragDropEffects.None;
                return;
            }
            hovering = IsDragDataSupported(e.Data);
            if (hovering)
            {
                e.Effect = (e.AllowedEffect & DragDropEffects.Copy) != 0 ? DragDropEffects.Copy :
                           ((e.AllowedEffect & DragDropEffects.Link) != 0 ? DragDropEffects.Link :
                           (e.AllowedEffect != DragDropEffects.None ? e.AllowedEffect : DragDropEffects.Copy));
                Invalidate();
            }
            else
            {
                e.Effect = DragDropEffects.None;
            }
        }

        protected override void OnDragOver(DragEventArgs e)
        {
            base.OnDragOver(e);
            if (selfDrag)
            {
                e.Effect = DragDropEffects.None;
                return;
            }
            if (hovering)
            {
                e.Effect = (e.AllowedEffect & DragDropEffects.Copy) != 0 ? DragDropEffects.Copy :
                           ((e.AllowedEffect & DragDropEffects.Link) != 0 ? DragDropEffects.Link :
                           (e.AllowedEffect != DragDropEffects.None ? e.AllowedEffect : DragDropEffects.Copy));
            }
            else
            {
                e.Effect = DragDropEffects.None;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool needRedraw = false;
            bool nowBasketHover = items.Count > 0 && BasketRect().Contains(e.Location);
            if (nowBasketHover != basketHover) { basketHover = nowBasketHover; needRedraw = true; }
            bool nowXHover = XRect().Contains(e.Location);
            if (nowXHover != xHover) { xHover = nowXHover; needRedraw = true; }
            bool nowSettingsHover = SettingsRect().Contains(e.Location);
            if (nowSettingsHover != settingsHover) { settingsHover = nowSettingsHover; needRedraw = true; }
            if (needRedraw) Invalidate();
            
            // Initiate drag-out if left button held and moved enough
            if (selfDrag && draggingOut)
            {
                if (Math.Abs(e.X - pressPoint.X) > 5 || Math.Abs(e.Y - pressPoint.Y) > 5)
                {
                    selfDrag = false;
                    draggingOut = false;
                    StartDragOut();
                }
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
            {
                if (XRect().Contains(e.Location))
                {
                    xPressed = true;
                }
                else if (SettingsRect().Contains(e.Location))
                {
                    settingsPressed = true;
                }
                else if (BasketRect().Contains(e.Location))
                {
                    basketPressed = true;
                }
                else if (ShelfRect().Contains(e.Location) && items.Count > 0)
                {
                    selfDrag = true;
                    draggingOut = true;
                    pressPoint = e.Location;
                }
                else
                {
                    Native.ReleaseCapture();
                    Native.SendMessage(Handle, WM_NCLBUTTONDOWN, new IntPtr(HTCAPTION), IntPtr.Zero);
                }
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left)
            {
                if (settingsPressed)
                {
                    settingsPressed = false;
                    Invalidate();
                    if (SettingsRect().Contains(e.Location)) ShowSettingsMenu();
                }
                if (basketPressed)
                {
                    basketPressed = false;
                    Invalidate();
                    if (BasketRect().Contains(e.Location)) ShowBasketMenu();
                }
                if (xPressed)
                {
                    xPressed = false;
                    Invalidate();
                    if (XRect().Contains(e.Location)) CloseBox();
                }
                selfDrag = false;
                draggingOut = false;
            }
        }

        protected override void OnDragLeave(EventArgs e)
        {
            base.OnDragLeave(e);
            hovering = false;
            Invalidate();
        }

        protected override void OnDragDrop(DragEventArgs e)
        {
            base.OnDragDrop(e);
            hovering = false;
            Invalidate();

            Directory.CreateDirectory(stagingDir);

            List<string> added = ProcessDroppedData(e.Data, stagingDir);

            int copied = 0;
            foreach (string target in added)
            {
                if (File.Exists(target) || Directory.Exists(target))
                {
                    copied++;
                    AddItem(target);
                }
            }

            if (copied > 0)
            {
                statusText = "✓ " + copied + " item masuk ke rak";
            }
            else
            {
                statusText = "Gagal mengambil data";
            }

            statusTimer.Stop();
            if (!string.IsNullOrEmpty(statusText)) statusTimer.Start();
            Invalidate();
        }


        private static bool IsDragDataSupported(IDataObject data)
        {
            if (data == null) return false;
            try
            {
                if (data.GetDataPresent(DataFormats.FileDrop)) return true;
                if (data.GetDataPresent("FileGroupDescriptor") || data.GetDataPresent("FileGroupDescriptorW")) return true;
                if (data.GetDataPresent(DataFormats.Bitmap) || data.GetDataPresent("DeviceIndependentBitmap")) return true;
                if (data.GetDataPresent("HTML Format") || data.GetDataPresent(DataFormats.Html)) return true;
                if (data.GetDataPresent("UniformResourceLocator") || data.GetDataPresent("UniformResourceLocatorW") || data.GetDataPresent("URL")) return true;
                if (data.GetDataPresent(DataFormats.UnicodeText) || data.GetDataPresent(DataFormats.Text)) return true;

                string[] formats = data.GetFormats();
                return formats != null && formats.Length > 0;
            }
            catch
            {
                return false;
            }
        }

        private List<string> ProcessDroppedData(IDataObject data, string targetStagingDir)
        {
            var results = new List<string>();
            if (data == null) return results;

            if (data.GetDataPresent(DataFormats.FileDrop))
            {
                try
                {
                    string[] paths = data.GetData(DataFormats.FileDrop) as string[];
                    if (paths != null && paths.Length > 0)
                    {
                        foreach (string path in paths)
                        {
                            if (Directory.Exists(path) || File.Exists(path))
                            {
                                string target = CopyToStaging(path);
                                results.Add(target);
                            }
                        }
                    }
                }
                catch { }
            }

            if (data.GetDataPresent("FileGroupDescriptorW") || data.GetDataPresent("FileGroupDescriptor"))
            {
                try
                {
                    var vfiles = ExtractVirtualFiles(data, targetStagingDir);
                    results.AddRange(vfiles);
                }
                catch { }
            }

            if (data.GetDataPresent(DataFormats.Bitmap) || data.GetDataPresent("DeviceIndependentBitmap"))
            {
                try
                {
                    Image img = data.GetData(DataFormats.Bitmap) as Image;
                    if (img == null)
                    {
                        object bobj = data.GetData("DeviceIndependentBitmap");
                        Image bi = bobj as Image;
                        if (bi != null) img = bi;
                        else
                        {
                            Bitmap bm = bobj as Bitmap;
                            if (bm != null) img = bm;
                        }
                    }

                    if (img != null)
                    {
                        string name = "dropped_image_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".png";
                        string target = GetUniquePath(targetStagingDir, name);
                        img.Save(target, System.Drawing.Imaging.ImageFormat.Png);
                        results.Add(target);
                    }
                }
                catch { }
            }

            string htmlContent = null;
            if (data.GetDataPresent("HTML Format"))
            {
                try { htmlContent = data.GetData("HTML Format") as string; } catch { }
            }
            else if (data.GetDataPresent(DataFormats.Html))
            {
                try { htmlContent = data.GetData(DataFormats.Html) as string; } catch { }
            }

            if (!string.IsNullOrEmpty(htmlContent))
            {
                try
                {
                    var htmlFiles = ExtractFromHtmlContent(htmlContent, targetStagingDir);
                    if (htmlFiles.Count == 0)
                    {
                        string clean = StripHtmlClipboardHeader(htmlContent);
                        if (!string.IsNullOrWhiteSpace(clean) && clean.IndexOf('<') >= 0)
                        {
                            string name = "web_page_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".html";
                            string target = GetUniquePath(targetStagingDir, name);
                            File.WriteAllText(target, clean, Encoding.UTF8);
                            results.Add(target);
                        }
                    }
                    results.AddRange(htmlFiles);
                }
                catch { }
            }

            string urlStr = ExtractUrlString(data);
            if (!string.IsNullOrEmpty(urlStr))
            {
                try
                {
                    var urlFiles = ProcessUrl(urlStr, targetStagingDir);
                    results.AddRange(urlFiles);
                }
                catch { }
            }

            if (results.Count == 0 && (data.GetDataPresent(DataFormats.UnicodeText) || data.GetDataPresent(DataFormats.Text)))
            {
                try
                {
                    string txt = data.GetData(DataFormats.UnicodeText) as string;
                    if (string.IsNullOrEmpty(txt)) txt = data.GetData(DataFormats.Text) as string;

                    if (!string.IsNullOrWhiteSpace(txt))
                    {
                        txt = txt.Trim();
                        Uri uriResult;
                        if (Uri.TryCreate(txt, UriKind.Absolute, out uriResult) &&
                            (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps))
                        {
                            var urlFiles = ProcessUrl(txt, targetStagingDir);
                            results.AddRange(urlFiles);
                        }
                        else
                        {
                            string safeSnippetName = GetSafeFileName(txt.Length > 20 ? txt.Substring(0, 20) : txt);
                            if (string.IsNullOrWhiteSpace(safeSnippetName)) safeSnippetName = "snippet";
                            string name = safeSnippetName + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt";
                            string target = GetUniquePath(targetStagingDir, name);
                            File.WriteAllText(target, txt, Encoding.UTF8);
                            results.Add(target);
                        }
                    }
                }
                catch { }
            }

            return results;
        }

        private static string ExtractUrlString(IDataObject data)
        {
            try
            {
                if (data.GetDataPresent("UniformResourceLocatorW"))
                {
                    object obj = data.GetData("UniformResourceLocatorW");
                    string s = obj as string;
                    if (s != null) return s.Trim('\0', ' ', '\r', '\n');
                    MemoryStream ms = obj as MemoryStream;
                    if (ms != null)
                    {
                        string s2 = Encoding.Unicode.GetString(ms.ToArray()).Trim('\0', ' ', '\r', '\n');
                        if (!string.IsNullOrEmpty(s2)) return s2;
                    }
                }
                if (data.GetDataPresent("UniformResourceLocator"))
                {
                    object obj = data.GetData("UniformResourceLocator");
                    string s = obj as string;
                    if (s != null) return s.Trim('\0', ' ', '\r', '\n');
                    MemoryStream ms = obj as MemoryStream;
                    if (ms != null)
                    {
                        string s2 = Encoding.Default.GetString(ms.ToArray()).Trim('\0', ' ', '\r', '\n');
                        if (!string.IsNullOrEmpty(s2)) return s2;
                    }
                }
                if (data.GetDataPresent("URL"))
                {
                    object obj = data.GetData("URL");
                    string s = obj as string;
                    if (s != null) return s.Trim('\0', ' ', '\r', '\n');
                }
            }
            catch { }
            return null;
        }

        private static List<string> ProcessUrl(string url, string targetStagingDir)
        {
            var results = new List<string>();
            if (string.IsNullOrEmpty(url)) return results;

            Uri uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri)) return results;
            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return results;

            string ext = Path.GetExtension(uri.LocalPath).ToLowerInvariant();
            bool isImage = ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".gif" ||
                           ext == ".webp" || ext == ".svg" || ext == ".bmp" || ext == ".ico";

            if (isImage)
            {
                string downloaded = DownloadFileFromWeb(url, targetStagingDir);
                if (!string.IsNullOrEmpty(downloaded))
                {
                    results.Add(downloaded);
                }
            }
            else
            {
                string domain = uri.Host;
                string shortcutName = GetSafeFileName(domain) + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".url";
                string shortcutPath = GetUniquePath(targetStagingDir, shortcutName);
                string urlContent = "[InternetShortcut]\r\nURL=" + url + "\r\n";
                File.WriteAllText(shortcutPath, urlContent, Encoding.ASCII);
                results.Add(shortcutPath);

                string htmlPath = DownloadFileFromWeb(url, targetStagingDir);
                if (!string.IsNullOrEmpty(htmlPath))
                {
                    results.Add(htmlPath);
                }
            }

            return results;
        }

        private static string DownloadFileFromWeb(string url, string targetStagingDir)
        {
            try
            {
                Uri uri = new Uri(url);
                string fileName = Path.GetFileName(uri.LocalPath);
                if (string.IsNullOrEmpty(fileName) || !fileName.Contains("."))
                {
                    fileName = "web_page_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".html";
                }
                fileName = GetSafeFileName(fileName);
                string targetPath = GetUniquePath(targetStagingDir, fileName);

                using (var wc = new WebClient())
                {
                    wc.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
                    wc.DownloadFile(uri, targetPath);
                }
                if (File.Exists(targetPath) && new FileInfo(targetPath).Length > 0)
                {
                    return targetPath;
                }
            }
            catch { }
            return null;
        }

        private static string StripHtmlClipboardHeader(string html)
        {
            if (string.IsNullOrEmpty(html)) return html;
            int idx = html.IndexOf("<html", StringComparison.OrdinalIgnoreCase);
            if (idx < 0) idx = html.IndexOf("<!--StartFragment-->", StringComparison.OrdinalIgnoreCase);
            if (idx < 0) idx = html.IndexOf("<body", StringComparison.OrdinalIgnoreCase);
            return idx >= 0 ? html.Substring(idx) : html;
        }

        private static List<string> ExtractFromHtmlContent(string html, string targetStagingDir)
        {
            var results = new List<string>();
            if (string.IsNullOrEmpty(html)) return results;

            var b64Matches = Regex.Matches(
                html,
                @"data:image/(?<type>png|jpeg|jpg|gif|webp|bmp);base64,(?<data>[A-Za-z0-9+/=]+)",
                RegexOptions.IgnoreCase);

            foreach (Match match in b64Matches)
            {
                try
                {
                    string type = match.Groups["type"].Value.ToLowerInvariant();
                    string b64 = match.Groups["data"].Value;
                    byte[] bytes = Convert.FromBase64String(b64);
                    string name = "web_img_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + "." + type;
                    string target = GetUniquePath(targetStagingDir, name);
                    File.WriteAllBytes(target, bytes);
                    results.Add(target);
                }
                catch { }
            }

            var imgMatches = Regex.Matches(
                html,
                @"<img[^>]+src=[""'](?<url>https?://[^""']+)[""']",
                RegexOptions.IgnoreCase);

            foreach (Match match in imgMatches)
            {
                try
                {
                    string imgUrl = match.Groups["url"].Value;
                    string downloaded = DownloadFileFromWeb(imgUrl, targetStagingDir);
                    if (!string.IsNullOrEmpty(downloaded) && !results.Contains(downloaded))
                    {
                        results.Add(downloaded);
                    }
                }
                catch { }
            }

            return results;
        }

        private static List<string> ExtractVirtualFiles(IDataObject data, string targetStagingDir)
        {
            var results = new List<string>();
            string formatName = data.GetDataPresent("FileGroupDescriptorW") ? "FileGroupDescriptorW" :
                               (data.GetDataPresent("FileGroupDescriptor") ? "FileGroupDescriptor" : null);

            if (formatName == null) return results;

            try
            {
                MemoryStream stream = data.GetData(formatName) as MemoryStream;
                if (stream == null) return results;

                byte[] bytes = stream.ToArray();
                if (bytes.Length < 4) return results;

                int fileCount = BitConverter.ToInt32(bytes, 0);
                bool isUnicode = formatName == "FileGroupDescriptorW";
                int descriptorSize = isUnicode ? 592 : 332;
                int fileNameOffset = 72;

                for (int i = 0; i < fileCount; i++)
                {
                    int offset = 4 + i * descriptorSize;
                    if (offset + descriptorSize > bytes.Length) break;

                    string fileName = "";
                    if (isUnicode)
                    {
                        fileName = Encoding.Unicode.GetString(bytes, offset + fileNameOffset, 520).Split('\0')[0];
                    }
                    else
                    {
                        fileName = Encoding.Default.GetString(bytes, offset + fileNameOffset, 260).Split('\0')[0];
                    }

                    fileName = GetSafeFileName(Path.GetFileName(fileName));
                    if (string.IsNullOrEmpty(fileName))
                    {
                        fileName = "virtual_file_" + (i + 1) + ".dat";
                    }

                    object contentObj = null;
                    try { contentObj = data.GetData("FileContents"); } catch { }
                    if (contentObj is Array)
                    {
                        Array arr = (Array)contentObj;
                        if (i < arr.Length) contentObj = arr.GetValue(i);
                    }

                    Stream contentStream = contentObj as Stream;
                    if (contentStream != null)
                    {
                        string targetPath = GetUniquePath(targetStagingDir, fileName);
                        using (var dest = File.Create(targetPath))
                        {
                            contentStream.CopyTo(dest);
                        }
                        results.Add(targetPath);
                    }
                    else
                    {
                        MemoryStream ms = contentObj as MemoryStream;
                        if (ms != null)
                        {
                            string targetPath = GetUniquePath(targetStagingDir, fileName);
                            File.WriteAllBytes(targetPath, ms.ToArray());
                            results.Add(targetPath);
                        }
                    }
                }
            }
            catch { }

            return results;
        }

        private static string GetSafeFileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "file";
            char[] invalid = Path.GetInvalidFileNameChars();
            StringBuilder sb = new StringBuilder();
            foreach (char c in name)
            {
                if (Array.IndexOf(invalid, c) < 0 && c != '\r' && c != '\n' && c != '\t')
                {
                    sb.Append(c);
                }
                else
                {
                    sb.Append('_');
                }
            }
            string res = sb.ToString().Trim();
            return res.Length > 50 ? res.Substring(0, 50) : (string.IsNullOrEmpty(res) ? "file" : res);
        }

        private static string GetUniquePath(string dir, string name)
        {
            Directory.CreateDirectory(dir);
            string candidate = Path.Combine(dir, name);
            int i = 1;
            string stem = Path.GetFileNameWithoutExtension(name);
            string ext = Path.GetExtension(name);
            while (File.Exists(candidate) || Directory.Exists(candidate))
            {
                candidate = Path.Combine(dir, stem + " (" + i + ")" + ext);
                i++;
            }
            return candidate;
        }

        private string CopyToStaging(string source)
        {
            string target = GetUniqueStagingTarget(source);
            if (Directory.Exists(source))
            {
                CopyDirectory(source, target);
            }
            else
            {
                File.Copy(source, target);
            }
            return target;
        }

        private string GetUniqueStagingTarget(string source)
        {
            string name = Path.GetFileName(source);
            string candidate = Path.Combine(stagingDir, name);
            int i = 1;
            string stem = Path.GetFileNameWithoutExtension(name);
            string ext = Path.GetExtension(name);
            while (File.Exists(candidate) || Directory.Exists(candidate))
            {
                candidate = Path.Combine(stagingDir, stem + " (" + i + ")" + ext);
                i++;
            }
            return candidate;
        }

        private static void CopyDirectory(string sourceDir, string destDir)
        {
            Directory.CreateDirectory(destDir);
            foreach (string file in Directory.GetFiles(sourceDir))
            {
                File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)), false);
            }
            foreach (string dir in Directory.GetDirectories(sourceDir))
            {
                CopyDirectory(dir, Path.Combine(destDir, Path.GetFileName(dir)));
            }
        }




        private void RemoveItems(List<string> paths)
        {
            bool changed = false;
            foreach (string p in paths)
            {
                if (items.Remove(p) || itemAnims.Remove(p) || dragEnabled.Remove(p))
                {
                    changed = true;
                }
                try
                {
                    if (Directory.Exists(p)) Directory.Delete(p, true);
                    else if (File.Exists(p)) File.Delete(p);
                }
                catch { }
            }
            if (changed)
            {
                Invalidate();
            }
        }

        public void CloseBox()
        {
            if (closed) return;
            closed = true;
            statusTimer.Stop();
            items.Clear();
            Hide();
            app.RemoveBox(this);
            Dispose();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (xHover || basketHover || settingsHover)
            {
                xHover = false;
                basketHover = false;
                settingsHover = false;
                Invalidate();
            }
        }

        private void SetRegion()
        {
            int radius = 16;
            using (var gp = new GraphicsPath())
            {
                gp.AddArc(0, 0, radius * 2, radius * 2, 180, 90);
                gp.AddArc(Width - radius * 2 - 1, 0, radius * 2, radius * 2, 270, 90);
                gp.AddArc(Width - radius * 2 - 1, Height - radius * 2 - 1, radius * 2, radius * 2, 0, 90);
                gp.AddArc(0, Height - radius * 2 - 1, radius * 2, radius * 2, 90, 90);
                gp.CloseFigure();
                Region = new Region(gp);
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            SetRegion();
            Invalidate();
        }
    }

    internal static class GraphicsExtensions
    {
        public static void FillRounded(this Graphics g, Rectangle rect, int radius, Brush brush)
        {
            using (var gp = Rounded(rect, radius))
            {
                g.FillPath(brush, gp);
            }
        }

        public static void DrawRounded(this Graphics g, Rectangle rect, int radius, Pen pen)
        {
            using (var gp = Rounded(rect, radius))
            {
                g.DrawPath(pen, gp);
            }
        }

        private static GraphicsPath Rounded(Rectangle rect, int radius)
        {
            var gp = new GraphicsPath();
            gp.AddArc(rect.X, rect.Y, radius * 2, radius * 2, 180, 90);
            gp.AddArc(rect.Right - radius * 2, rect.Y, radius * 2, radius * 2, 270, 90);
            gp.AddArc(rect.Right - radius * 2, rect.Bottom - radius * 2, radius * 2, radius * 2, 0, 90);
            gp.AddArc(rect.X, rect.Bottom - radius * 2, radius * 2, radius * 2, 90, 90);
            gp.CloseFigure();
            return gp;
        }
    }

    public class AnimatedSetupForm : Form
    {
        private readonly Icon appIcon;
        private readonly Color accent;
        private readonly string[] steps;
        private readonly Action<string> doWork;
        private readonly Action onFinished;

        private int currentStep;
        private float progress;
        private float t;
        private bool success;
        private float successT;
        private bool fadingOut;
        private bool finishedCalled;
        private bool cancelled;

        private readonly System.Windows.Forms.Timer animTimer = new System.Windows.Forms.Timer { Interval = 33 };
        private readonly System.Windows.Forms.Timer fadeTimer = new System.Windows.Forms.Timer { Interval = 16 };

        private const int FormWidth = 420;
        private const int FormHeight = 270;
        private const int FormHeightInfo = 360;

        private string statusText = "Memulai...";
        private readonly string info;
        private bool waitingForConfirm;
        private Button btnLanjut;
        private Button btnBatal;

        private readonly Font titleFont = new Font("Segoe UI", 10.5f, FontStyle.Bold);
        private readonly Font subFont = new Font("Segoe UI", 8f);
        private readonly Font infoFont = new Font("Segoe UI", 8.5f);

        public AnimatedSetupForm(Icon appIcon, Color accent, string[] steps, Action<string> doWork, Action onFinished)
            : this(appIcon, accent, null, steps, doWork, onFinished)
        {
        }

        public AnimatedSetupForm(Icon appIcon, Color accent, string info, string[] steps, Action<string> doWork, Action onFinished)
        {
            this.appIcon = appIcon;
            this.accent = accent;
            this.info = info;
            this.steps = steps;
            this.doWork = doWork;
            this.onFinished = onFinished;

            bool hasInfo = info != null;
            int formHeight = hasInfo ? FormHeightInfo : FormHeight;

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(FormWidth, formHeight);
            BackColor = Color.FromArgb(30, 34, 42);
            DoubleBuffered = true;
            ShowInTaskbar = true;
            KeyPreview = true;
            KeyDown += OnKeyDown;

            using (var gp = new GraphicsPath())
            {
                int r = 14;
                gp.AddArc(0, 0, r * 2, r * 2, 180, 90);
                gp.AddArc(FormWidth - r * 2 - 1, 0, r * 2, r * 2, 270, 90);
                gp.AddArc(FormWidth - r * 2 - 1, formHeight - r * 2 - 1, r * 2, r * 2, 0, 90);
                gp.AddArc(0, formHeight - r * 2 - 1, r * 2, r * 2, 90, 90);
                gp.CloseFigure();
                Region = new Region(gp);
            }

            animTimer.Tick += OnAnimTick;
            fadeTimer.Tick += OnFadeTick;

            if (info != null)
            {
                waitingForConfirm = true;
                CreateButtons();
            }
        }

        // Geser hue warna (efek Material You warna-warni).
        private static Color ShiftHue(Color c, float degrees)
        {
            float h, s, l;
            RgbToHsl(c, out h, out s, out l);
            h += degrees / 360f;
            h -= (float)Math.Floor(h);
            return HslToRgb(h, s, l);
        }

        private static void RgbToHsl(Color c, out float h, out float s, out float l)
        {
            float r = c.R / 255f, g = c.G / 255f, b = c.B / 255f;
            float max = Math.Max(r, Math.Max(g, b));
            float min = Math.Min(r, Math.Min(g, b));
            l = (max + min) / 2f;
            float d = max - min;
            if (d < 0.0001f) { h = 0f; s = 0f; return; }
            s = l > 0.5f ? d / (2f - max - min) : d / (max + min);
            if (max == r) h = (g - b) / d + (g < b ? 6f : 0f);
            else if (max == g) h = (b - r) / d + 2f;
            else h = (r - g) / d + 4f;
            h /= 6f;
        }

        private static Color HslToRgb(float h, float s, float l)
        {
            if (s <= 0f)
            {
                byte gr = (byte)(l * 255f);
                return Color.FromArgb(gr, gr, gr);
            }
            float q = l < 0.5f ? l * (1f + s) : l + s - l * s;
            float p = 2f * l - q;
            return Color.FromArgb(
                (int)(Hue2Rgb(p, q, h + 1f / 3f) * 255f),
                (int)(Hue2Rgb(p, q, h) * 255f),
                (int)(Hue2Rgb(p, q, h - 1f / 3f) * 255f));
        }

        private static float Hue2Rgb(float p, float q, float tt)
        {
            if (tt < 0f) tt += 1f;
            if (tt > 1f) tt -= 1f;
            if (tt < 1f / 6f) return p + (q - p) * 6f * tt;
            if (tt < 1f / 2f) return q;
            if (tt < 2f / 3f) return p + (q - p) * (2f / 3f - tt) * 6f;
            return p;
        }

        private void CreateButtons()
        {
            btnLanjut = new Button
            {
                Text = "Lanjut",
                FlatStyle = FlatStyle.Flat,
                BackColor = accent,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                Cursor = Cursors.Hand,
                Size = new Size(100, 34),
                Location = new Point(FormWidth - 30 - 100, FormHeightInfo - 62),
                UseVisualStyleBackColor = false
            };
            btnLanjut.FlatAppearance.BorderSize = 0;
            btnLanjut.FlatAppearance.MouseOverBackColor = ShiftHue(accent, 12f);
            btnLanjut.FlatAppearance.MouseDownBackColor = ShiftHue(accent, -12f);
            btnLanjut.Click += (s, ev) => BeginInstallSteps();

            btnBatal = new Button
            {
                Text = "Batal",
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(45, 50, 60),
                ForeColor = Color.FromArgb(220, 220, 220),
                Font = new Font("Segoe UI", 9f),
                Cursor = Cursors.Hand,
                Size = new Size(100, 34),
                Location = new Point(FormWidth - 30 - 100 - 108, FormHeightInfo - 62),
                UseVisualStyleBackColor = false
            };
            btnBatal.FlatAppearance.BorderSize = 1;
            btnBatal.FlatAppearance.BorderColor = Color.FromArgb(90, 95, 105);
            btnBatal.FlatAppearance.MouseOverBackColor = Color.FromArgb(58, 63, 74);
            btnBatal.FlatAppearance.MouseDownBackColor = Color.FromArgb(70, 76, 88);
            btnBatal.Click += (s, ev) => { cancelled = true; Close(); };

            Controls.Add(btnLanjut);
            Controls.Add(btnBatal);
        }

        private void BeginInstallSteps()
        {
            waitingForConfirm = false;
            btnLanjut.Visible = false;
            btnBatal.Visible = false;
            statusText = "Memulai...";
            Invalidate();
        }

        public void Start()
        {
            Opacity = 0;
            Show();
            fadeTimer.Start();
            animTimer.Start();
        }

        private void OnFadeTick(object sender, EventArgs e)
        {
            if (fadingOut)
            {
                Opacity -= 0.06;
                if (Opacity <= 0)
                {
                    fadeTimer.Stop();
                    Close();
                    if (!finishedCalled && onFinished != null)
                    {
                        finishedCalled = true;
                        onFinished();
                    }
                }
            }
            else
            {
                Opacity += 0.05;
                if (Opacity >= 1) Opacity = 1;
            }
        }

        private void OnAnimTick(object sender, EventArgs e)
        {
            t += 1f / 30f;

            if (waitingForConfirm)
            {
                Invalidate();
                return;
            }

            if (success)
            {
                successT += 1f / 30f;
                if (successT > 1.8 && !fadingOut) BeginFadeOut();
            }
            else if (currentStep < steps.Length)
            {
                progress += 0.015f;
                if (progress >= 1f)
                {
                    progress = 1f;
                    Invalidate();
                    try
                    {
                        doWork(steps[currentStep]);
                    }
                    catch (Exception ex)
                    {
                        statusText = "Gagal: " + ex.Message;
                        Invalidate();
                        return;
                    }
                    currentStep++;
                    if (currentStep < steps.Length) progress = 0f;
                    else BeginSuccess();
                }
            }

            Invalidate();
        }

        private void BeginSuccess()
        {
            success = true;
            successT = 0;
            progress = 1f;
            statusText = "Selesai!";
            Invalidate();
        }

        private void BeginFadeOut()
        {
            fadingOut = true;
            fadeTimer.Start();
            animTimer.Stop();
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape && !success && currentStep == 0)
            {
                cancelled = true;
                Close();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            Rectangle r = ClientRectangle;

            // Latar: gradien gelap bergetar halus + kilau warna-warni (Material You).
            float shift = (float)Math.Sin(t * 0.4) * 6f;
            using (var bg = new LinearGradientBrush(r, Color.FromArgb(30, 34, 42), Color.FromArgb(20, 23, 29), 90f + shift))
                g.FillRectangle(bg, r);

            int cx = r.Width / 2;

            // Kilau latar berputar hue — warna-warni kedap-kedip.
            for (int i = 0; i < 3; i++)
            {
                float ang = t * 0.8f + i * 2.09f;
                float bx = cx + (float)Math.Cos(ang) * 130f;
                float by = 38 + (float)Math.Sin(ang * 1.4f) * 70f;
                int bd = 60 + (int)(20 * (float)Math.Sin(t * 3 + i));
                Color bc = ShiftHue(accent, t * 60f + i * 120f);
                using (var b = new SolidBrush(Color.FromArgb(28, bc)))
                {
                    g.FillEllipse(b, bx - bd / 2f, by - bd / 2f, bd, bd);
                }
            }

            // Lingkaran cahaya di belakang ikon — membesar-mengecil + warna berganti.
            float pulse = 1f + 0.12f * (float)Math.Sin(t * 2.5);
            Color glowCol = ShiftHue(accent, t * 80f);
            using (var glow = new SolidBrush(Color.FromArgb(60, glowCol)))
            {
                int d = (int)(110 * pulse);
                g.FillEllipse(glow, cx - d / 2, 38 - d / 2, d, d);
            }
            // Cincin luar berdenyut.
            float ringPulse = 1f + 0.15f * (float)Math.Sin(t * 1.8);
            int ringD = (int)(150 * ringPulse);
            using (var ringPen = new Pen(Color.FromArgb(55, ShiftHue(accent, t * 50f + 40f)), 2.5f))
            {
                g.DrawEllipse(ringPen, cx - ringD / 2f, 38 - ringD / 2f, ringD, ringD);
            }

            // Ikon: ukuran berubah-ubah + memantul.
            float bob = (float)Math.Sin(t * 2) * 4f;
            float iconScale = 1f + 0.10f * (float)Math.Sin(t * 3.2);
            int iconSize = (int)(52 * iconScale);
            if (appIcon != null)
            {
                g.DrawIcon(appIcon, cx - iconSize / 2, (int)(38 - iconSize / 2 + bob));
            }

            // Judul dengan warna berkilau halus.
            using (var white = new SolidBrush(Color.FromArgb(245, 255, 255, 255)))
            {
                var sf = new StringFormat { Alignment = StringAlignment.Center };
                g.DrawString("DropFile", titleFont, white, new RectangleF(0, 96, r.Width, 22), sf);
            }

            // Partikel kecil kedap-kedip warna-warni.
            for (int i = 0; i < 6; i++)
            {
                float pa = t * (0.5f + (i % 3) * 0.15f) + i * 1.7f;
                float px = 30 + (i * 71) % (r.Width - 60);
                float py = 110 + (i * 37) % (r.Height - 130);
                float alpha = 60f + 90f * (float)Math.Sin(pa);
                if (alpha < 0) alpha = 0;
                if (alpha > 0)
                {
                    Color pc = ShiftHue(accent, t * 90f + i * 60f);
                    using (var b = new SolidBrush(Color.FromArgb((int)alpha, pc)))
                    {
                        g.FillEllipse(b, px, py, 4, 4);
                    }
                }
            }

            if (waitingForConfirm)
            {
                DrawInfoPanel(g, r);
            }
            else if (!success)
            {
                DrawProgressBar(g, r);
            }
            else
            {
                DrawCheckmark(g, r);
            }

            if (waitingForConfirm)
            {
                using (var gray = new SolidBrush(Color.FromArgb(200, 255, 255, 255)))
                {
                    var sf = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Near };
                    g.DrawString(info, infoFont, gray, new RectangleF(44, 128, r.Width - 88, 190), sf);
                }
            }
            else
            {
                using (var gray = new SolidBrush(Color.FromArgb(180, 255, 255, 255)))
                {
                    var sf = new StringFormat { Alignment = StringAlignment.Center };
                    g.DrawString(statusText, subFont, gray, new RectangleF(0, 230, r.Width, 20), sf);
                }
            }
        }

        private void DrawInfoPanel(Graphics g, Rectangle r)
        {
            using (var panelBg = new SolidBrush(Color.FromArgb(45, 255, 255, 255)))
            {
                DrawRounded(g, new Rectangle(30, 118, r.Width - 60, 208), 10, panelBg);
            }
        }

        private void DrawProgressBar(Graphics g, Rectangle r)
        {
            int barX = 40, barY = 180, barW = r.Width - 80, barH = 12;

            using (var track = new SolidBrush(Color.FromArgb(60, 255, 255, 255)))
            {
                DrawRounded(g, new Rectangle(barX, barY, barW, barH), 6, track);
            }

            int fillW = (int)(barW * progress);
            if (fillW > 0)
            {
                // Gradien Material You — warna bergeser seiring waktu.
                Color fillA = ShiftHue(accent, t * 90f);
                Color fillB = ShiftHue(accent, t * 90f + 50f);
                using (var fillGrad = new LinearGradientBrush(
                    new Rectangle(barX, barY, fillW, barH),
                    fillA,
                    fillB,
                    0f))
                using (var path = CreateRoundedPath(new Rectangle(barX, barY, fillW, barH), 6))
                {
                    g.FillPath(fillGrad, path);
                }

                // Kilau melintas.
                float shine = ((t * 80f) % (barW + 60)) - 30;
                if (shine > barX && shine < barX + barW)
                {
                    int sx = (int)shine - 20;
                    int sw = 40;
                    if (sx < barX) { sw -= (barX - sx); sx = barX; }
                    if (sx + sw > barX + barW) sw = barX + barW - sx;
                    if (sw > 0)
                    {
                        using (var shineBrush = new SolidBrush(Color.FromArgb(90, 255, 255, 255)))
                        {
                            g.FillRectangle(shineBrush, sx, barY, sw, barH);
                        }
                    }
                }
            }
        }

        private void DrawCheckmark(Graphics g, Rectangle r)
        {
            float k = Math.Min(1f, successT * 2f);
            int cx = r.Width / 2;
            int cy = 170;
            int size = (int)(44 * k);
            if (size < 2) return;

            int half = size / 2;
            Color ck = ShiftHue(accent, t * 60f);
            using (var pen = new Pen(ck, Math.Max(1, 5 * k)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                g.DrawLine(pen, cx - half + 4, cy, cx - 2, cy + half - 4);
                g.DrawLine(pen, cx - 2, cy + half - 4, cx + half - 4, cy - half / 2);
            }
        }

        private static void DrawRounded(Graphics g, Rectangle rect, int radius, Brush brush)
        {
            using (var gp = CreateRoundedPath(rect, radius))
            {
                g.FillPath(brush, gp);
            }
        }

        private static GraphicsPath CreateRoundedPath(Rectangle rect, int radius)
        {
            var gp = new GraphicsPath();
            gp.AddArc(rect.X, rect.Y, radius * 2, radius * 2, 180, 90);
            gp.AddArc(rect.Right - radius * 2, rect.Y, radius * 2, radius * 2, 270, 90);
            gp.AddArc(rect.Right - radius * 2, rect.Bottom - radius * 2, radius * 2, radius * 2, 0, 90);
            gp.AddArc(rect.X, rect.Bottom - radius * 2, radius * 2, radius * 2, 90, 90);
            gp.CloseFigure();
            return gp;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            animTimer.Stop();
            fadeTimer.Stop();
            try
            {
                if (titleFont != null) titleFont.Dispose();
                if (subFont != null) subFont.Dispose();
                if (infoFont != null) infoFont.Dispose();
            }
            catch { }
        }

        public bool Cancelled { get { return cancelled; } }
    }
}
