using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace BgeTray
{
    internal class GuideForm : Form
    {
        private static readonly Color C_BG = Color.FromArgb(247, 248, 250);
        private static readonly Color C_HEAD = Color.FromArgb(24, 26, 31);
        private static readonly Color C_TEXT = Color.FromArgb(32, 36, 44);
        private static readonly Color C_MUTED = Color.FromArgb(110, 118, 132);
        private static readonly Color C_CARD = Color.FromArgb(238, 242, 248);
        private static readonly Color C_ACCENT = Color.FromArgb(79, 140, 255);
        private static readonly Color C_BTN = Color.FromArgb(64, 70, 84);
        private static readonly Color C_BODY = Color.FromArgb(60, 66, 78);
        private static readonly Color C_OK = Color.FromArgb(39, 174, 96);

        private static readonly string ConfigSnippet =
            "{\r\n" +
            "  \"provider\": \"local-bge\",\r\n" +
            "  \"type\": \"openai_embedding\",\r\n" +
            "  \"provider_type\": \"embedding\",\r\n" +
            "  \"embedding_api_base\": \"http://127.0.0.1:18080/v1\",\r\n" +
            "  \"embedding_api_key\": \"sk-local\",\r\n" +
            "  \"embedding_model\": \"bge-small-zh-v1.5\",\r\n" +
            "  \"embedding_dimensions\": 512,\r\n" +
            "  \"embedding_dimensions_mode\": \"never\"\r\n" +
            "}";

        private static readonly string GuideText =
            "BGE 本地嵌入服务 v1.1.0\r\n" +
            "==============================\r\n\r\n" +
            "接口地址：http://127.0.0.1:18080/v1\r\n" +
            "健康检查：http://127.0.0.1:18080/health\r\n" +
            "WebUI：  http://127.0.0.1:18080/\r\n" +
            "模型：   bge-small-zh-v1.5（512 维，L2 归一化，CPU 运行，显存 0 MB）\r\n\r\n" +
            "【AstrBot 配置】服务提供商 → 类型 openai_embedding：\r\n" +
            ConfigSnippet + "\r\n\r\n" +
            "然后「知识库 → 新建」，嵌入模型选 local-bge/bge-small-zh-v1.5。\r\n\r\n" +
            "【直接调用】\r\n" +
            "curl -s http://127.0.0.1:18080/v1/embeddings -H \"Content-Type: application/json\" -d \"{\\\"input\\\":\\\"你好\\\"}\"\r\n\r\n" +
            "【调用失败排查】\r\n" +
            "1. 托盘图标绿色=运行中；灰色就右键 →「重启服务」\r\n" +
            "2. 打开 WebUI 看状态，正常显示绿色「运行中」\r\n" +
            "3. 看日志：安装目录\\logs\\server.log\r\n" +
            "4. 仍失败：把这个安装包（或日志）发给 AstrBot，让它按实际配置逐项核对\r\n\r\n" +
            "【特点】ONNX Runtime CPU 执行器，不初始化 CUDA，显存占用恒为 0 MB，\r\n" +
            "常驻内存约 185 MB，单条约 4 ms，空闲时几乎不占 CPU。";

        private readonly CheckBox _noShow;
        private readonly Label _status;
        private readonly Timer _statusTimer;

        public GuideForm()
        {
            Text = "BGE 本地嵌入服务 · 使用说明";
            ClientSize = new Size(860, 716);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            BackColor = C_BG;
            Font = new Font("Microsoft YaHei UI", 9f);
            Icon = LoadAppIcon();

            BuildHeader();
            BuildCards();
            BuildLeftColumn();
            BuildRightColumn();
            BuildFooter();

            _status = new Label();
            _status.Text = "";
            _status.ForeColor = C_OK;
            _status.Bounds = new Rectangle(560, 664, 284, 20);
            _status.TextAlign = ContentAlignment.MiddleRight;
            Controls.Add(_status);

            _statusTimer = new Timer();
            _statusTimer.Interval = 2000;
            _statusTimer.Tick += delegate { _status.Text = ""; _statusTimer.Stop(); };

            _noShow = new CheckBox();
            _noShow.Text = "下次启动不再自动显示本页（可从托盘右键重新打开）";
            _noShow.ForeColor = C_MUTED;
            _noShow.Bounds = new Rectangle(16, 664, 520, 22);
            _noShow.Checked = !Program.ShowGuideOnStart;
            _noShow.CheckedChanged += delegate { Program.ShowGuideOnStart = !_noShow.Checked; };
            Controls.Add(_noShow);
        }

        private static Icon LoadAppIcon()
        {
            try
            {
                using (Stream s = typeof(GuideForm).Assembly.GetManifestResourceStream("on.ico"))
                {
                    if (s != null) return new Icon(s);
                }
            }
            catch { }
            return SystemIcons.Application;
        }

        private void BuildHeader()
        {
            Panel head = new Panel();
            head.Bounds = new Rectangle(0, 0, 860, 92);
            head.BackColor = C_HEAD;
            Controls.Add(head);

            Label title = new Label();
            title.Text = "BGE 本地嵌入服务";
            title.Font = new Font("Microsoft YaHei UI", 16f, FontStyle.Bold);
            title.ForeColor = Color.White;
            title.Bounds = new Rectangle(24, 14, 420, 32);
            title.BackColor = Color.Transparent;
            head.Controls.Add(title);

            Label ver = new Label();
            ver.Text = "v1.1.0";
            ver.Font = new Font("Consolas", 10f);
            ver.ForeColor = Color.FromArgb(140, 148, 162);
            ver.Bounds = new Rectangle(24, 50, 90, 22);
            ver.BackColor = Color.Transparent;
            head.Controls.Add(ver);

            Label sub = new Label();
            sub.Text = "给 AstrBot 知识库用的本地 512 维嵌入服务 · 无需联网 · 无需显卡";
            sub.Font = new Font("Microsoft YaHei UI", 9f);
            sub.ForeColor = Color.FromArgb(158, 166, 180);
            sub.Bounds = new Rectangle(104, 50, 700, 22);
            sub.BackColor = Color.Transparent;
            head.Controls.Add(sub);
        }

        private void BuildCards()
        {
            AddCard(16, "512 维", "BGE-small-zh-v1.5", "shape 1");
            AddCard(300, "0 MB 显存", "纯 CPU 推理，不碰显卡", "shape 2");
            AddCard(584, "约 4 ms / 条", "常驻内存约 185 MB", "shape 3");
        }

        private void AddCard(int x, string big, string small, string tag)
        {
            Panel card = new Panel();
            card.Bounds = new Rectangle(x, 104, 260, 68);
            card.BackColor = C_CARD;
            Controls.Add(card);

            Label a = new Label();
            a.Text = big;
            a.Font = new Font("Microsoft YaHei UI", 14f, FontStyle.Bold);
            a.ForeColor = C_TEXT;
            a.Bounds = new Rectangle(16, 8, 230, 30);
            a.BackColor = Color.Transparent;
            card.Controls.Add(a);

            Label b = new Label();
            b.Text = small;
            b.Font = new Font("Microsoft YaHei UI", 9f);
            b.ForeColor = C_MUTED;
            b.Bounds = new Rectangle(16, 38, 230, 22);
            b.BackColor = Color.Transparent;
            card.Controls.Add(b);
        }

        private void BuildLeftColumn()
        {
            AddTitle(16, 186, 480, "① 接入 AstrBot（推荐，4 步）");
            AddBody(16, 214, 480, 178,
                "1. AstrBot 面板 → 配置 → 服务提供商 → 新增提供商\r\n" +
                "2. 类型选「OpenAI 兼容 / openai_embedding」\r\n" +
                "3. 按下面参数填（点左下角「复制 AstrBot 配置」可直接粘贴）\r\n" +
                "4. 保存后 → 知识库 → 新建 → 嵌入模型选 local-bge/bge-small-zh-v1.5\r\n" +
                "\r\n" +
                "api_base    http://127.0.0.1:18080/v1\r\n" +
                "api_key     sk-local（本地不校验，随便填）\r\n" +
                "model       bge-small-zh-v1.5\r\n" +
                "dimensions  512 ／ dimensions_mode  never");

            AddTitle(16, 404, 480, "③ 为什么不吃显存");
            AddBody(16, 432, 480, 110,
                "服务用 ONNX Runtime 的 CPU 执行器，整个进程从不初始化 CUDA、\r\n" +
                "不打开任何显卡上下文。所以显存占用恒为 0 MB、GPU 占用 0%，\r\n" +
                "和 ComfyUI、游戏、Ollama 都不抢显卡。\r\n" +
                "代价只是速度慢一点：单条 4 ms 左右，知识库检索完全够用。");
        }

        private void BuildRightColumn()
        {
            AddTitle(504, 186, 340, "② 直接调用（不经过 AstrBot）");

            TextBox code = new TextBox();
            code.Multiline = true;
            code.ReadOnly = true;
            code.ScrollBars = ScrollBars.Vertical;
            code.Font = new Font("Consolas", 8.2f);
            code.BackColor = Color.FromArgb(28, 31, 37);
            code.ForeColor = Color.FromArgb(210, 216, 226);
            code.BorderStyle = BorderStyle.None;
            code.Bounds = new Rectangle(504, 214, 340, 186);
            code.Text =
                "# curl（单条）\r\n" +
                "curl -s http://127.0.0.1:18080/v1/embeddings ^\r\n" +
                "  -H \"Content-Type: application/json\" ^\r\n" +
                "  -d \"{\\\"input\\\":\\\"你好\\\"}\"\r\n" +
                "\r\n" +
                "# Python（批量）\r\n" +
                "import requests\r\n" +
                "r = requests.post(\r\n" +
                "  \"http://127.0.0.1:18080/v1/embeddings\",\r\n" +
                "  json={\"input\": [\"你好\", \"再见\"]})\r\n" +
                "print(len(r.json()[\"data\"][0][\"embedding\"]))";
            Controls.Add(code);

            AddTitle(504, 406, 340, "④ 老是调用失败怎么办");
            AddBody(504, 434, 340, 180,
                "1. 托盘图标绿色才算正常，灰色就右键 →「重启服务」\r\n" +
                "2. 点「打开 WebUI」看状态，正常是绿色「运行中」\r\n" +
                "    也可以直接访问 http://127.0.0.1:18080/\r\n" +
                "3. 看日志：安装目录\\logs\\server.log\r\n" +
                "4. 还不行，就把这个安装包（或日志）发给 AstrBot，\r\n" +
                "    让它按你的实际配置逐项核对，比人猜快得多。");
        }

        private void BuildFooter()
        {
            Button copyCfg = MakeButton(16, 616, 168, "复制 AstrBot 配置", C_ACCENT, true);
            copyCfg.Click += delegate
            {
                try { Clipboard.SetText(ConfigSnippet); Toast("已复制 AstrBot 配置 JSON"); }
                catch { }
            };
            Controls.Add(copyCfg);

            Button copyUrl = MakeButton(192, 616, 140, "复制接口地址", C_BTN, false);
            copyUrl.Click += delegate
            {
                try { Clipboard.SetText("http://127.0.0.1:18080/v1"); Toast("已复制接口地址"); }
                catch { }
            };
            Controls.Add(copyUrl);

            Button openWebui = MakeButton(340, 616, 120, "打开 WebUI", C_BTN, false);
            openWebui.Click += delegate
            {
                try { Process.Start("http://127.0.0.1:18080/"); }
                catch (Exception ex) { MessageBox.Show(this, "打开浏览器失败：" + ex.Message); }
            };
            Controls.Add(openWebui);

            Button openLog = MakeButton(480, 616, 110, "查看日志", C_BTN, false);
            openLog.Click += delegate
            {
                try
                {
                    string log = Path.Combine(Program.Root, "logs", "server.log");
                    if (!File.Exists(log)) File.WriteAllText(log, "");
                    Process.Start("notepad.exe", "\"" + log + "\"");
                }
                catch { }
            };
            Controls.Add(openLog);

            Button copyAll = MakeButton(600, 616, 132, "复制本页说明", C_BTN, false);
            copyAll.Click += delegate
            {
                try { Clipboard.SetText(GuideText); Toast("已复制完整说明到剪贴板"); }
                catch { }
            };
            Controls.Add(copyAll);

            Button close = MakeButton(744, 616, 100, "关闭", C_BTN, false);
            close.Click += delegate { Close(); };
            Controls.Add(close);

            Label foot = new Label();
            foot.Text = "常驻托盘 · 右键图标可重启服务、开关开机自启或退出";
            foot.ForeColor = Color.FromArgb(150, 158, 172);
            foot.Bounds = new Rectangle(16, 690, 828, 20);
            foot.TextAlign = ContentAlignment.MiddleLeft;
            Controls.Add(foot);
        }

        private void Toast(string msg)
        {
            _status.Text = msg;
            _statusTimer.Stop();
            _statusTimer.Start();
        }

        private void AddTitle(int x, int y, int w, string text)
        {
            Label l = new Label();
            l.Text = text;
            l.Font = new Font("Microsoft YaHei UI", 10.5f, FontStyle.Bold);
            l.ForeColor = C_TEXT;
            l.Bounds = new Rectangle(x, y, w, 24);
            Controls.Add(l);
        }

        private void AddBody(int x, int y, int w, int h, string text)
        {
            // 用只读 TextBox 而不是 Label：可以选中、Ctrl+C 复制
            TextBox t = new TextBox();
            t.Multiline = true;
            t.ReadOnly = true;
            t.BorderStyle = BorderStyle.None;
            t.WordWrap = true;
            t.ScrollBars = ScrollBars.None;
            t.ShortcutsEnabled = true;
            t.TabStop = false;
            t.Text = text;
            t.Font = new Font("Microsoft YaHei UI", 8.8f);
            t.ForeColor = C_BODY;
            t.BackColor = C_BG;
            t.Bounds = new Rectangle(x, y, w, h);
            Controls.Add(t);
        }

        private Button MakeButton(int x, int y, int w, string text, Color color, bool bold)
        {
            Button b = new Button();
            b.Text = text;
            b.Bounds = new Rectangle(x, y, w, 34);
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 0;
            b.BackColor = color;
            b.ForeColor = Color.White;
            b.Font = new Font("Microsoft YaHei UI", 9f, bold ? FontStyle.Bold : FontStyle.Regular);
            b.Cursor = Cursors.Hand;
            return b;
        }
    }
}
