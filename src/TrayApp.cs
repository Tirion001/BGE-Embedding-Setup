using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace BgeTray
{
    internal static class Program
    {
        public static string Root;
        public static int Port = 18080;

        [STAThread]
        private static void Main(string[] args)
        {
            Root = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
            Port = ReadPort();
            LoadUi();

            bool uninstall = false;
            bool force = false;
            bool autostart = false;
            foreach (string a in args)
            {
                if (a == "--uninstall") uninstall = true;
                if (a == "--yes" || a == "-y") force = true;
                if (a == "--autostart") autostart = true;
            }

            if (uninstall)
            {
                KillOtherInstances();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                try
                {
                    Application.Run(new TrayContext(true, force, false));
                }
                catch (Exception ex)
                {
                    Log("uninstall fatal: " + ex);
                }
                return;
            }

            bool created;
            Mutex mutex = new Mutex(true, "Local\\BGE-Embedding-Tray", out created);
            if (!created)
            {
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            try
            {
                Application.Run(new TrayContext(false, false, autostart));
            }
            catch (Exception ex)
            {
                Log("fatal: " + ex);
            }
            GC.KeepAlive(mutex);
        }

        private static bool _showGuide = true;

        public static bool ShowGuideOnStart
        {
            get { return _showGuide; }
            set
            {
                _showGuide = value;
                try
                {
                    File.WriteAllText(
                        Path.Combine(Root, "ui.json"),
                        "{\"show_guide_on_start\": " + (value ? "true" : "false") + "}",
                        Encoding.UTF8);
                }
                catch { }
            }
        }

        private static void LoadUi()
        {
            try
            {
                string f = Path.Combine(Root, "ui.json");
                if (!File.Exists(f)) return;
                string t = File.ReadAllText(f, Encoding.UTF8);
                if (t.IndexOf("\"show_guide_on_start\": false", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _showGuide = false;
                }
            }
            catch { }
        }

        private static void KillOtherInstances()
        {
            try
            {
                foreach (Process p in Process.GetProcessesByName("tray"))
                {
                    if (p.Id == Process.GetCurrentProcess().Id) continue;
                    try
                    {
                        if (p.MainModule != null &&
                            string.Equals(p.MainModule.FileName, Application.ExecutablePath, StringComparison.OrdinalIgnoreCase))
                        {
                            p.Kill();
                            p.WaitForExit(4000);
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        public static int ReadPort()
        {
            try
            {
                string cfg = Path.Combine(Root, "server", "config.json");
                if (File.Exists(cfg))
                {
                    string text = File.ReadAllText(cfg, Encoding.UTF8);
                    Match m = Regex.Match(text, "\"port\"\\s*:\\s*(\\d+)");
                    if (m.Success)
                    {
                        return int.Parse(m.Groups[1].Value);
                    }
                }
            }
            catch { }
            return 18080;
        }

        public static string BaseUrl
        {
            get { return "http://127.0.0.1:" + Port; }
        }

        public static void Log(string msg)
        {
            try
            {
                string dir = Path.Combine(Root, "logs");
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "tray.log"),
                    "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] " + msg + Environment.NewLine,
                    Encoding.UTF8);
            }
            catch { }
        }
    }

    internal class TrayContext : ApplicationContext
    {
        private readonly NotifyIcon _tray;
        private readonly ContextMenuStrip _menu;
        private readonly System.Windows.Forms.Timer _timer;
        private readonly Icon _iconOn;
        private readonly Icon _iconOff;
        private Process _server;
        private bool _lastHealthy;
        private int _startAttempts;
        private DateTime _lastStart = DateTime.MinValue;
        private readonly ToolStripMenuItem _statusItem;
        private readonly ToolStripMenuItem _autoStartItem;

        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValue = "BGE-Embedding";
        private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\BGE-Embedding";

        private readonly bool _force;
        private GuideForm _guide;

        public TrayContext(bool uninstall, bool force, bool autostart)
        {
            _force = force;
            _iconOn = LoadIcon("on.ico") ?? SystemIcons.Application;
            _iconOff = LoadIcon("off.ico") ?? SystemIcons.Warning;

            _menu = new ContextMenuStrip();
            _menu.Font = new Font("Microsoft YaHei UI", 9f);
            _menu.ShowImageMargin = false;

            _statusItem = new ToolStripMenuItem("BGE 嵌入服务 · 检测中…");
            _statusItem.Enabled = false;
            _menu.Items.Add(_statusItem);
            _menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem openItem = new ToolStripMenuItem("打开 WebUI");
            openItem.Click += delegate { OpenUrl(Program.BaseUrl + "/"); };
            _menu.Items.Add(openItem);

            ToolStripMenuItem copyItem = new ToolStripMenuItem("复制接口地址");
            copyItem.Click += delegate
            {
                try { Clipboard.SetText(Program.BaseUrl + "/v1"); Balloon("接口地址已复制", Program.BaseUrl + "/v1"); }
                catch { }
            };
            _menu.Items.Add(copyItem);
            _menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem restartItem = new ToolStripMenuItem("重启服务");
            restartItem.Click += delegate { RestartServer(); };
            _menu.Items.Add(restartItem);

            ToolStripMenuItem logItem = new ToolStripMenuItem("查看日志");
            logItem.Click += delegate { OpenLog(); };
            _menu.Items.Add(logItem);

            ToolStripMenuItem dirItem = new ToolStripMenuItem("打开安装目录");
            dirItem.Click += delegate { try { Process.Start("explorer.exe", "\"" + Program.Root + "\""); } catch { } };
            _menu.Items.Add(dirItem);
            _menu.Items.Add(new ToolStripSeparator());

            _autoStartItem = new ToolStripMenuItem("开机自启");
            _autoStartItem.CheckOnClick = true;
            _autoStartItem.Checked = IsAutoStartEnabled();
            _autoStartItem.CheckedChanged += delegate { SetAutoStart(_autoStartItem.Checked); };
            _menu.Items.Add(_autoStartItem);
            _menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem guideItem = new ToolStripMenuItem("使用说明 / 接入指南");
            guideItem.Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold);
            guideItem.Click += delegate { ShowGuide(); };
            _menu.Items.Add(guideItem);
            _menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem aboutItem = new ToolStripMenuItem("关于 / 版本 1.1.0");
            aboutItem.Click += delegate
            {
                MessageBox.Show(
                    "BGE 本地嵌入服务 1.1.0\r\n\r\n" +
                    "模型：BGE-small-zh-v1.5（512 维，CPU，不吃显存）\r\n" +
                    "接口：" + Program.BaseUrl + "/v1/embeddings\r\n" +
                    "安装目录：" + Program.Root + "\r\n\r\n" +
                    "AstrBot 中把嵌入模型指向 local-bge / bge-small-zh-v1.5。",
                    "关于", MessageBoxButtons.OK, MessageBoxIcon.Information);
            };
            _menu.Items.Add(aboutItem);

            ToolStripMenuItem exitItem = new ToolStripMenuItem("退出");
            exitItem.Click += delegate { ExitApp(); };
            _menu.Items.Add(exitItem);

            _tray = new NotifyIcon();
            _tray.Icon = _iconOff;
            _tray.Text = "BGE 嵌入服务";
            _tray.Visible = true;
            _tray.ContextMenuStrip = _menu;
            _tray.DoubleClick += delegate { ShowGuide(); };

            if (uninstall)
            {
                DoUninstall();
                return;
            }

            StartServer(false);
            _timer = new System.Windows.Forms.Timer();
            _timer.Interval = 4000;
            _timer.Tick += delegate { RefreshState(); };
            _timer.Start();

            RefreshState();

            if (!autostart && Program.ShowGuideOnStart)
            {
                _tray.ShowBalloonTip(2500, "BGE 嵌入服务已就绪", "端口 " + Program.Port + "，右键图标可随时打开使用说明。", ToolTipIcon.Info);
                System.Windows.Forms.Timer opening = new System.Windows.Forms.Timer();
                opening.Interval = 500;
                opening.Tick += delegate
                {
                    opening.Stop();
                    opening.Dispose();
                    ShowGuide();
                };
                opening.Start();
            }
        }

        private static Icon LoadIcon(string name)
        {
            try
            {
                Assembly asm = Assembly.GetExecutingAssembly();
                using (Stream s = asm.GetManifestResourceStream(name))
                {
                    if (s != null) return new Icon(s);
                }
                string file = Path.Combine(Program.Root, name);
                if (File.Exists(file)) return new Icon(file);
            }
            catch { }
            return null;
        }

        private void Balloon(string title, string text)
        {
            try { _tray.ShowBalloonTip(2500, title, text, ToolTipIcon.Info); } catch { }
        }

        private void ShowGuide()
        {
            try
            {
                if (_guide != null && !_guide.IsDisposed)
                {
                    if (_guide.WindowState == FormWindowState.Minimized) _guide.WindowState = FormWindowState.Normal;
                    _guide.Activate();
                    _guide.BringToFront();
                    return;
                }
                _guide = new GuideForm();
                _guide.FormClosed += delegate { _guide = null; };
                _guide.Show();
                _guide.Activate();
            }
            catch (Exception e)
            {
                Program.Log("show guide failed: " + e.Message);
            }
        }

        private static void OpenUrl(string url)
        {
            try { Process.Start(url); } catch (Exception e) { Program.Log("open url failed: " + e.Message); }
        }

        private void OpenLog()
        {
            string log = Path.Combine(Program.Root, "logs", "server.log");
            try
            {
                if (!File.Exists(log))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(log));
                    File.WriteAllText(log, "");
                }
                Process.Start("notepad.exe", "\"" + log + "\"");
            }
            catch (Exception e) { Program.Log("open log failed: " + e.Message); }
        }

        private bool Healthy()
        {
            try
            {
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(Program.BaseUrl + "/health");
                req.Timeout = 1500;
                req.ReadWriteTimeout = 1500;
                req.Proxy = null;
                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                {
                    return (int)resp.StatusCode == 200;
                }
            }
            catch { return false; }
        }

        private void StartServer(bool restart)
        {
            try
            {
                if (restart) KillServer();
                if (Healthy()) return;

                string py = Path.Combine(Program.Root, "runtime", "Tray-Py.exe");
                string script = Path.Combine(Program.Root, "server", "server.py");
                if (!File.Exists(py) || !File.Exists(script))
                {
                    Program.Log("missing runtime or server script");
                    return;
                }

                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = py;
                psi.Arguments = "\"" + script + "\"";
                psi.WorkingDirectory = Program.Root;
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.WindowStyle = ProcessWindowStyle.Hidden;
                _server = Process.Start(psi);
                _lastStart = DateTime.Now;
                Program.Log("server started pid=" + (_server != null ? _server.Id.ToString() : "?"));
            }
            catch (Exception e)
            {
                Program.Log("start failed: " + e);
            }
        }

        private void KillServer()
        {
            try
            {
                if (_server != null && !_server.HasExited)
                {
                    _server.Kill();
                    _server.WaitForExit(4000);
                }
            }
            catch { }
            // 兜底：清理指向本目录 server.py 的残留进程
            try
            {
                string needle = Path.Combine(Program.Root, "server", "server.py");
                foreach (string pname in new string[] { "Tray-Py", "pythonw" })
                {
                    foreach (Process p in Process.GetProcessesByName(pname))
                    {
                        try
                        {
                            if (p.MainModule != null && p.MainModule.FileName.StartsWith(Program.Root, StringComparison.OrdinalIgnoreCase))
                            {
                                p.Kill();
                            }
                        }
                        catch { }
                    }
                }
                GC.KeepAlive(needle);
            }
            catch { }
            _server = null;
        }

        private void RestartServer()
        {
            _tray.Icon = _iconOff;
            _statusItem.Text = "BGE 嵌入服务 · 重启中…";
            StartServer(true);
            ThreadPool.QueueUserWorkItem(delegate
            {
                Thread.Sleep(2500);
                try { RefreshState(); } catch { }
            });
        }

        private void RefreshState()
        {
            bool ok = Healthy();
            _lastHealthy = ok;
            if (ok) { _startAttempts = 0; }
            _tray.Icon = ok ? _iconOn : _iconOff;
            _statusItem.Text = ok
                ? "BGE 嵌入服务 · 运行中（端口 " + Program.Port + "）"
                : "BGE 嵌入服务 · 已停止（右键重启）";
            string tip = ok ? "BGE 嵌入服务运行中 · " + Program.Port : "BGE 嵌入服务已停止";
            if (tip.Length > 62) tip = tip.Substring(0, 62);
            _tray.Text = tip;

            if (ok) return;

            bool dead = (_server == null) || _server.HasExited;
            if (!dead) return;

            if (_server != null)
            {
                try { Program.Log("server exited code=" + _server.ExitCode); } catch { }
                _server = null;
            }

            if (_startAttempts >= 6) return;
            if ((DateTime.Now - _lastStart).TotalSeconds < 5.0) return;

            _startAttempts++;
            Program.Log("try start #" + _startAttempts);
            StartServer(false);
        }

        private static bool IsAutoStartEnabled()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey, false))
                {
                    if (k == null) return false;
                    object v = k.GetValue(RunValue);
                    return v != null;
                }
            }
            catch { return false; }
        }

        private void SetAutoStart(bool on)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (k == null) return;
                    if (on)
                    {
                        k.SetValue(RunValue, "\"" + Application.ExecutablePath + "\" --autostart");
                    }
                    else
                    {
                        k.DeleteValue(RunValue, false);
                    }
                }
                Balloon("开机自启", on ? "已开启：登录后自动启动服务" : "已关闭");
            }
            catch (Exception e)
            {
                Program.Log("set autostart failed: " + e.Message);
                MessageBox.Show("设置开机自启失败：" + e.Message, "BGE 嵌入服务", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void ExitApp()
        {
            DialogResult r = MessageBox.Show(
                "退出将停止本地嵌入服务，AstrBot 的知识库将无法向量化。\r\n\r\n确定退出吗？",
                "BGE 嵌入服务", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (r != DialogResult.Yes) return;

            _timer.Stop();
            KillServer();
            _tray.Visible = false;
            _tray.Dispose();
            ExitThread();
        }

        private void DoUninstall()
        {
            if (!_force)
            {
                DialogResult r = MessageBox.Show(
                    "将卸载 BGE 嵌入服务并删除 " + Program.Root + " 下的所有文件。\r\n\r\n确定继续吗？",
                    "卸载", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (r != DialogResult.Yes)
                {
                    _tray.Visible = false;
                    ExitThread();
                    return;
                }
            }

            KillServer();
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (k != null) k.DeleteValue(RunValue, false);
                }
            }
            catch { }

            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(UninstallKey, true))
                {
                    if (k != null) Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false);
                }
            }
            catch { }

            try
            {
                string startMenu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "BGE 嵌入服务.lnk");
                if (File.Exists(startMenu)) File.Delete(startMenu);
                string desktop = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "BGE 嵌入服务.lnk");
                if (File.Exists(desktop)) File.Delete(desktop);
            }
            catch { }

            _tray.Visible = false;
            try
            {
                string root = Program.Root;
                string cmd = "/c timeout /t 2 /nobreak >nul & del /f /q \"" + root + "\\tray.exe\" >nul 2>&1 & " +
                             "rmdir /s /q \"" + root + "\" >nul 2>&1 & timeout /t 1 /nobreak >nul & " +
                             "rmdir /s /q \"" + root + "\" >nul 2>&1";
                ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", cmd);
                psi.CreateNoWindow = true;
                psi.WindowStyle = ProcessWindowStyle.Hidden;
                psi.UseShellExecute = false;
                Process.Start(psi);
            }
            catch { }
            Environment.Exit(0);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_tray != null) _tray.Dispose();
                if (_menu != null) _menu.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
