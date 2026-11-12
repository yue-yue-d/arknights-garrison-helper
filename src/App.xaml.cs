using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows;
using WF = System.Windows.Forms;

namespace WeishuHelper
{
    public partial class App : Application
    {
        public static Settings S = new();
        static Mutex _mutex;
        WF.NotifyIcon _tray;
        OverlayWindow _overlay;

        public static void Log(string msg)
        {
            try { File.AppendAllText(Path.Combine(Paths.Root, "日志.txt"), $"{DateTime.Now:MM-dd HH:mm:ss} {msg}\n"); } catch { }
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            _mutex = new Mutex(true, "WeishuHelper_Overlay_Single", out bool first);
            if (!first)
            {
                MessageBox.Show("卫戍协议助手已经在运行了（看右下角托盘）。", "卫戍协议助手");
                Shutdown();
                return;
            }
            DispatcherUnhandledException += (_, ex) =>
            {
                Log("未处理异常: " + ex.Exception);
                MessageBox.Show("出错了：" + ex.Exception.Message + "\n\n详细信息写在 日志.txt", "卫戍协议助手");
                ex.Handled = true;
            };

            if (!Directory.Exists(Paths.Data) || !File.Exists(Path.Combine(Paths.Data, "干员.json")))
            {
                MessageBox.Show($"找不到「数据」文件夹（在 {Paths.Root} 及上级目录里找过）。\n请先运行 工具\\更新数据.py。", "卫戍协议助手");
                Shutdown();
                return;
            }
            S = Settings.Load();
            GameData.Load();
            Directory.CreateDirectory(Paths.Plans);
            Directory.CreateDirectory(Paths.MapGuides);

            _overlay = new OverlayWindow();
            _overlay.Show();
            SetupTray();
        }

        void SetupTray()
        {
            _tray = new WF.NotifyIcon { Icon = MakeIcon(), Text = "卫戍协议助手（规程）", Visible = true };
            var menu = new WF.ContextMenuStrip();
            menu.Items.Add("显示 / 隐藏　Ctrl+Shift+F1", null, (_, _) => _overlay.ToggleUserHidden());
            menu.Items.Add("鼠标穿透　Ctrl+Shift+F2", null, (_, _) => _overlay.ToggleClickThrough());
            menu.Items.Add("打开程序文件夹", null, (_, _) => OpenPath(Paths.Root));
            menu.Items.Add(new WF.ToolStripSeparator());
            menu.Items.Add("退出", null, (_, _) => Quit());
            _tray.ContextMenuStrip = menu;
            _tray.MouseClick += (_, a) => { if (a.Button == WF.MouseButtons.Left) _overlay.ToggleUserHidden(); };
        }

        static Icon MakeIcon()
        {
            // 优先用 exe 自带的图标（奖杯），取不到再画一个
            try
            {
                var exe = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exe))
                {
                    var ico = Icon.ExtractAssociatedIcon(exe);
                    if (ico != null) return ico;
                }
            }
            catch { }
            using var bmp = new Bitmap(32, 32);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                using var bg = new SolidBrush(System.Drawing.Color.FromArgb(255, 21, 85, 68));
                g.FillEllipse(bg, 0, 0, 31, 31);
                using var f = new Font("Microsoft YaHei", 15, System.Drawing.FontStyle.Bold, GraphicsUnit.Pixel);
                var sz = g.MeasureString("卫", f);
                g.DrawString("卫", f, Brushes.White, (32 - sz.Width) / 2, (32 - sz.Height) / 2 + 1);
            }
            return Icon.FromHandle(bmp.GetHicon());
        }

        public static void OpenPath(string path)
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true }); }
            catch (Exception e) { Log("打开失败 " + path + ": " + e.Message); }
        }

        public void Quit()
        {
            _overlay?.SaveAll();
            S.Save();
            if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
            Shutdown();
        }
    }
}
