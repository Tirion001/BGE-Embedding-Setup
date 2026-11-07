using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace BgeSetup
{
    internal static class Program
    {
        public const string AppName = "BGE 本地嵌入服务";
        public const string Version = "1.1.0";
        public const string RunValue = "BGE-Embedding";
        public const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\BGE-Embedding";
        public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        [STAThread]
        private static void Main(string[] args)
        {
            string silentPath = null;
            bool silent = false;
            bool autoStart = true;
            bool desktop = true;

            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (a == "/S" || a == "--silent" || a == "/silent") silent = true;
                else if (a.StartsWith("/D=")) silentPath = a.Substring(3);
                else if (a == "--no-autostart") autoStart = false;
                else if (a == "--no-desktop") desktop = false;
            }

            if (silent)
            {
                string target = silentPath;
                if (string.IsNullOrEmpty(target)) target = DefaultDir;
                int code = SetupForm.RunSilent(target, autoStart, desktop);
                Environment.Exit(code);
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new SetupForm());
        }

        public static string DefaultDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BGE-Embedding"); }
        }

        public static bool IsInstalled()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(UninstallKey, false))
                {
                    return k != null;
                }
            }
            catch { return false; }
        }
    }

    internal class SetupForm : Form
    {
        private readonly TextBox _pathBox;
        private readonly CheckBox _autoStart;
        private readonly CheckBox _desktop;
        private readonly Button _installBtn;
        private readonly Button _closeBtn;
        private readonly ProgressBar _bar;
        private readonly Label _status;
        private readonly Label _hint;
        private bool _installing;
        private bool _done;

        private static readonly Color Accent = Color.FromArgb(79, 140, 255);
        private static readonly Color TextColor = Color.FromArgb(32, 36, 44);
        private static readonly Color MutedColor = Color.FromArgb(110, 118, 132);

        public SetupForm()
        {
            Text = Program.AppName + " 安装程序";
            ClientSize = new Size(680, 430);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            BackColor = Color.White;
            Font = new Font("Microsoft YaHei UI", 9f);

            Panel header = new Panel();
            header.Bounds = new Rectangle(0, 0, 680, 104);
            header.BackColor = Color.FromArgb(24, 26, 31);
            Controls.Add(header);

            Label title = new Label();
            title.Text = Program.AppName;
            title.Font = new Font("Microsoft YaHei UI", 17f, FontStyle.Bold);
            title.ForeColor = Color.White;
            title.Bounds = new Rectangle(28, 20, 500, 34);
            title.BackColor = Color.Transparent;
            header.Controls.Add(title);

            Label sub = new Label();
            sub.Text = "v1.1.0 · BGE-small-zh-v1.5 · 512 维 · 纯 CPU 不吃显存 · 免管理员";
            sub.Font = new Font("Microsoft YaHei UI", 9f);
            sub.ForeColor = Color.FromArgb(150, 158, 172);
            sub.Bounds = new Rectangle(30, 60, 620, 22);
            sub.BackColor = Color.Transparent;
            header.Controls.Add(sub);

            Label pathLabel = new Label();
            pathLabel.Text = "安装位置";
            pathLabel.ForeColor = TextColor;
            pathLabel.Bounds = new Rectangle(30, 128, 100, 24);
            Controls.Add(pathLabel);

            _pathBox = new TextBox();
            _pathBox.Text = Program.DefaultDir;
            _pathBox.Bounds = new Rectangle(30, 154, 520, 26);
            _pathBox.BorderStyle = BorderStyle.FixedSingle;
            Controls.Add(_pathBox);

            Button browse = new Button();
            browse.Text = "浏览…";
            browse.Bounds = new Rectangle(560, 153, 90, 28);
            browse.FlatStyle = FlatStyle.System;
            browse.Click += delegate
            {
                using (FolderBrowserDialog dlg = new FolderBrowserDialog())
                {
                    dlg.Description = "选择安装位置";
                    if (Directory.Exists(_pathBox.Text)) dlg.SelectedPath = _pathBox.Text;
                    if (dlg.ShowDialog(this) == DialogResult.OK) _pathBox.Text = dlg.SelectedPath;
                }
            };
            Controls.Add(browse);

            _autoStart = new CheckBox();
            _autoStart.Text = "开机自启（登录 Windows 后自动启动服务，可随时在托盘菜单里关闭）";
            _autoStart.Bounds = new Rectangle(30, 198, 620, 24);
            _autoStart.Checked = true;
            Controls.Add(_autoStart);

            _desktop = new CheckBox();
            _desktop.Text = "创建桌面快捷方式";
            _desktop.Bounds = new Rectangle(30, 226, 620, 24);
            _desktop.Checked = true;
            Controls.Add(_desktop);

            Label space = new Label();
            space.Text = "占用空间约 210 MB · 常驻内存约 210 MB · 监听 127.0.0.1:18080";
            space.ForeColor = MutedColor;
            space.Bounds = new Rectangle(30, 258, 620, 22);
            Controls.Add(space);

            _bar = new ProgressBar();
            _bar.Bounds = new Rectangle(30, 294, 620, 18);
            _bar.Style = ProgressBarStyle.Continuous;
            Controls.Add(_bar);

            _status = new Label();
            _status.Text = Program.IsInstalled() ? "检测到已安装，点击「安装」将覆盖升级（服务会被重启）" : "准备就绪";
            _status.ForeColor = MutedColor;
            _status.Bounds = new Rectangle(30, 318, 620, 22);
            Controls.Add(_status);

            _hint = new Label();
            _hint.Text = "安装后会常驻系统托盘，右键托盘图标可查看状态、重启服务或退出。";
            _hint.ForeColor = Color.FromArgb(140, 148, 162);
            _hint.Bounds = new Rectangle(30, 344, 620, 22);
            Controls.Add(_hint);

            _installBtn = new Button();
            _installBtn.Text = Program.IsInstalled() ? "重新安装" : "安装";
            _installBtn.Bounds = new Rectangle(432, 380, 110, 36);
            _installBtn.BackColor = Accent;
            _installBtn.ForeColor = Color.White;
            _installBtn.FlatStyle = FlatStyle.Flat;
            _installBtn.FlatAppearance.BorderSize = 0;
            _installBtn.Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold);
            _installBtn.Click += delegate { StartInstall(); };
            Controls.Add(_installBtn);

            _closeBtn = new Button();
            _closeBtn.Text = "关闭";
            _closeBtn.Bounds = new Rectangle(552, 380, 98, 36);
            _closeBtn.FlatStyle = FlatStyle.System;
            _closeBtn.Click += delegate { Close(); };
            Controls.Add(_closeBtn);
        }

        public static int RunSilent(string target, bool autoStart, bool desktop)
        {
            try
            {
                target = target.TrimEnd('\\');
                KillRunning(target);
                CleanTarget(target);
                Directory.CreateDirectory(target);
                ExtractPayload(target, delegate(int p, string m) { });
                RegisterUninstall(target, Program.Version);
                SetAutoStart(target, autoStart);

                string exe = Path.Combine(target, "tray.exe");
                const string lnkName = "BGE 嵌入服务.lnk";
                const string desc = "BGE 本地嵌入服务（512 维嵌入，托盘运行）";
                string startMenu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), lnkName);
                CreateShortcut(startMenu, exe, target, desc);
                string desktopLnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), lnkName);
                if (desktop)
                {
                    CreateShortcut(desktopLnk, exe, target, desc);
                }
                else if (File.Exists(desktopLnk))
                {
                    try { File.Delete(desktopLnk); } catch { }
                }

                ProcessStartInfo psi = new ProcessStartInfo(exe);
                psi.WorkingDirectory = target;
                Process.Start(psi);
                return 0;
            }
            catch (Exception ex)
            {
                try
                {
                    File.AppendAllText(Path.Combine(Path.GetTempPath(), "bge-setup-error.log"), ex.ToString() + Environment.NewLine);
                }
                catch { }
                return 1;
            }
        }

        private void StartInstall()
        {
            if (_installing) return;
            string target = _pathBox.Text.Trim().TrimEnd('\\');
            if (target.Length < 3 || target.IndexOf(':') != 1)
            {
                MessageBox.Show("请选择一个有效的安装路径（例如 D:\\BGE-Embedding）。", "路径无效", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _installing = true;
            _installBtn.Enabled = false;
            _closeBtn.Enabled = false;
            _pathBox.Enabled = false;
            _autoStart.Enabled = false;
            _desktop.Enabled = false;
            _bar.Value = 0;

            BackgroundWorker worker = new BackgroundWorker();
            worker.WorkerReportsProgress = true;
            worker.DoWork += delegate(object sender, DoWorkEventArgs e)
            {
                BackgroundWorker w = (BackgroundWorker)sender;
                try
                {
                    w.ReportProgress(2, "停止正在运行的服务…");
                    KillRunning(target);
                    CleanTarget(target);

                    w.ReportProgress(5, "创建目录…");
                    Directory.CreateDirectory(target);

                    w.ReportProgress(8, "释放文件（约 210 MB，请稍候）…");
                    ExtractPayload(target, delegate(int pct, string msg) { w.ReportProgress(pct, msg); });

                    w.ReportProgress(88, "写入卸载信息…");
                    RegisterUninstall(target, Program.Version);

                    w.ReportProgress(92, "配置开机自启…");
                    SetAutoStart(target, _autoStart.Checked);

                    w.ReportProgress(95, "创建快捷方式…");
                    string lnkName = "BGE 嵌入服务.lnk";
                    string exe = Path.Combine(target, "tray.exe");
                    string startMenu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), lnkName);
                    CreateShortcut(startMenu, exe, target, "BGE 本地嵌入服务（512 维嵌入，托盘运行）");
                    string desktop = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), lnkName);
                    if (_desktop.Checked)
                    {
                        CreateShortcut(desktop, exe, target, "BGE 本地嵌入服务（512 维嵌入，托盘运行）");
                    }
                    else if (File.Exists(desktop))
                    {
                        try { File.Delete(desktop); } catch { }
                    }

                    w.ReportProgress(100, "完成");
                    e.Result = target;
                }
                catch (Exception ex)
                {
                    e.Result = ex;
                }
            };
            worker.ProgressChanged += delegate(object sender, ProgressChangedEventArgs e)
            {
                if (e.ProgressPercentage > _bar.Maximum) _bar.Value = _bar.Maximum;
                else if (e.ProgressPercentage >= _bar.Minimum) _bar.Value = e.ProgressPercentage;
                if (e.UserState != null) _status.Text = e.UserState.ToString();
            };
            worker.RunWorkerCompleted += delegate(object sender, RunWorkerCompletedEventArgs e)
            {
                _installing = false;
                _closeBtn.Enabled = true;

                if (e.Result is Exception)
                {
                    Exception ex = (Exception)e.Result;
                    _status.Text = "安装失败：" + ex.Message;
                    _installBtn.Enabled = true;
                    _pathBox.Enabled = true;
                    _autoStart.Enabled = true;
                    _desktop.Enabled = true;
                    MessageBox.Show("安装失败：\r\n" + ex.Message, Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                string installDir = (string)e.Result;
                _done = true;
                _bar.Value = 100;
                _status.Text = "安装完成，正在启动托盘程序…";
                _installBtn.Text = "完成";
                _installBtn.Enabled = true;
                _installBtn.Click += delegate { Close(); };

                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo(Path.Combine(installDir, "tray.exe"));
                    psi.WorkingDirectory = installDir;
                    Process.Start(psi);
                }
                catch (Exception ex)
                {
                    _status.Text = "安装完成，但托盘程序启动失败：" + ex.Message;
                    return;
                }

                _status.Text = "安装完成，服务已在托盘运行（端口 18080）。" +
                    (_autoStart.Checked ? "已开启开机自启。" : "未开启开机自启，可随时在托盘菜单里打开。");
            };
            worker.RunWorkerAsync();
        }

        private static void CleanTarget(string target)
        {
            try
            {
                if (File.Exists(Path.Combine(target, "tray.exe")))
                {
                    Directory.Delete(target, true);
                }
            }
            catch { }
        }
        private static void KillRunning(string target)
        {
            string prefix = target.TrimEnd('\\') + "\\";
            foreach (string name in new string[] { "tray", "Tray-Py", "pythonw", "python" })
            {
                foreach (Process p in Process.GetProcessesByName(name))
                {
                    try
                    {
                        if (p.MainModule != null &&
                            p.MainModule.FileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        {
                            p.Kill();
                            p.WaitForExit(4000);
                        }
                    }
                    catch { }
                }
            }
        }

        private static void ExtractPayload(string target, Action<int, string> report)
        {
            Assembly asm = Assembly.GetExecutingAssembly();
            using (Stream s = asm.GetManifestResourceStream("payload.zip"))
            {
                if (s == null) throw new Exception("安装包损坏：找不到内嵌数据");
                using (ZipArchive zip = new ZipArchive(s, ZipArchiveMode.Read))
                {
                    int total = zip.Entries.Count;
                    int i = 0;
                    foreach (ZipArchiveEntry entry in zip.Entries)
                    {
                        i++;
                        string rel = entry.FullName.Replace('/', '\\');
                        string dest = Path.Combine(target, rel);
                        bool isDir = string.IsNullOrEmpty(entry.Name) || entry.FullName.EndsWith("/") || entry.FullName.EndsWith("\\");
                        if (isDir)
                        {
                            Directory.CreateDirectory(dest);
                        }
                        else
                        {
                            string dir = Path.GetDirectoryName(dest);
                            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                            using (Stream es = entry.Open())
                            using (FileStream fs = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None))
                            {
                                es.CopyTo(fs);
                            }
                        }
                        int pct = 8 + (int)(80.0 * i / Math.Max(1, total));
                        if (i % 20 == 0 || i == total)
                        {
                            report(pct, "释放文件 " + i + "/" + total + " …");
                        }
                    }
                }
            }
        }

        private static void RegisterUninstall(string target, string version)
        {
            string exe = Path.Combine(target, "tray.exe");
            long size = 0;
            try
            {
                foreach (string f in Directory.GetFiles(target, "*", SearchOption.AllDirectories))
                {
                    size += new FileInfo(f).Length;
                }
            }
            catch { }

            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(Program.UninstallKey))
            {
                k.SetValue("DisplayName", Program.AppName);
                k.SetValue("DisplayVersion", version);
                k.SetValue("Publisher", "Local BGE");
                k.SetValue("InstallLocation", target);
                k.SetValue("DisplayIcon", exe);
                k.SetValue("UninstallString", "\"" + exe + "\" --uninstall");
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                k.SetValue("EstimatedSize", (int)(size / 1024), RegistryValueKind.DWord);
            }
        }

        private static void SetAutoStart(string target, bool on)
        {
            string exe = Path.Combine(target, "tray.exe");
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(Program.RunKey))
            {
                if (on) k.SetValue(Program.RunValue, "\"" + exe + "\" --autostart");
                else k.DeleteValue(Program.RunValue, false);
            }
        }

        private static void CreateShortcut(string lnkPath, string exe, string workDir, string desc)
        {
            try
            {
                Type shType = Type.GetTypeFromProgID("WScript.Shell");
                object sh = Activator.CreateInstance(shType);
                object lnk = shType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, sh, new object[] { lnkPath });
                Type lnkType = lnk.GetType();
                lnkType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, lnk, new object[] { exe });
                lnkType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, lnk, new object[] { workDir });
                lnkType.InvokeMember("Description", BindingFlags.SetProperty, null, lnk, new object[] { desc });
                lnkType.InvokeMember("IconLocation", BindingFlags.SetProperty, null, lnk, new object[] { exe + ",0" });
                lnkType.InvokeMember("Save", BindingFlags.InvokeMethod, null, lnk, null);
            }
            catch { }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_installing)
            {
                e.Cancel = true;
                return;
            }
            base.OnFormClosing(e);
        }
    }
}
