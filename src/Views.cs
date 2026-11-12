using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;

namespace WeishuHelper
{
    // ======================= 攻略（暂时不在界面上显示，以后恢复） =======================
    public class GuideView : DockPanel, IDisposable
    {
        public void Dispose() { _fsw.EnableRaisingEvents = false; _fsw.Dispose(); _debounce.Stop(); }

        const string MapItem = "本图攻略（跟随布阵页选的地图）";
        readonly ComboBox _files = new() { MinWidth = 160 };
        readonly FlowDocumentScrollViewer _viewer = new()
        {
            IsToolBarVisible = false,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        readonly FileSystemWatcher _fsw;
        readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(400) };
        bool _filling;

        public GuideView()
        {
            var bar = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
            var right = new StackPanel { Orientation = Orientation.Horizontal };
            right.Children.Add(UI.Btn("编辑", Edit, "用记事本打开当前攻略；保存后这里自动刷新"));
            right.Children.Add(UI.Btn("文件夹", () => App.OpenPath(Paths.Guides), "打开「攻略」文件夹"));
            DockPanel.SetDock(right, Dock.Right);
            bar.Children.Add(right);
            bar.Children.Add(_files);
            SetDock(bar, Dock.Top);
            Children.Add(bar);
            Children.Add(_viewer);

            _files.SelectionChanged += (_, _) =>
            {
                if (_filling) return;
                App.S.攻略文件 = _files.SelectedItem as string == MapItem ? "" : _files.SelectedItem as string ?? "";
                Refresh();
            };
            FillFiles();

            Directory.CreateDirectory(Paths.Guides);
            _fsw = new FileSystemWatcher(Paths.Guides, "*.md") { IncludeSubdirectories = true, EnableRaisingEvents = true };
            FileSystemEventHandler h = (_, _) => Dispatcher.BeginInvoke(() => { _debounce.Stop(); _debounce.Start(); });
            _fsw.Changed += h; _fsw.Created += h; _fsw.Deleted += h;
            _fsw.Renamed += (_, _) => Dispatcher.BeginInvoke(() => { _debounce.Stop(); _debounce.Start(); });
            _debounce.Tick += (_, _) => { _debounce.Stop(); FillFiles(); Refresh(); };
            Refresh();
        }

        void FillFiles()
        {
            _filling = true;
            var items = new List<string> { MapItem };
            if (Directory.Exists(Paths.Guides))
                items.AddRange(Directory.GetFiles(Paths.Guides, "*.md").Select(Path.GetFileName).OrderBy(n => n));
            _files.ItemsSource = items;
            var want = string.IsNullOrEmpty(App.S.攻略文件) ? MapItem : App.S.攻略文件;
            _files.SelectedItem = items.Contains(want) ? want : MapItem;
            _filling = false;
        }

        string MapGuidePath => Path.Combine(Paths.MapGuides, App.S.地图 + ".md");

        public void Refresh()
        {
            double fs = App.S.字号;
            FlowDocument doc;
            if (string.IsNullOrEmpty(App.S.攻略文件))
            {
                doc = Markdown.Render(MapHeader(), fs);
                if (File.Exists(MapGuidePath))
                    Markdown.AppendTo(doc.Blocks, ReadText(MapGuidePath), fs);
                else
                    Markdown.AppendTo(doc.Blocks, "> 这张图还没有写攻略。点右上角「编辑」会新建 `攻略/地图/" + App.S.地图 + ".md`，写好保存后这里立刻显示。", fs);
            }
            else
            {
                var p = Path.Combine(Paths.Guides, App.S.攻略文件);
                doc = Markdown.Render(File.Exists(p) ? ReadText(p) : "找不到文件：" + p, fs);
            }
            _viewer.Document = doc;
        }

        static string ReadText(string p)
        {
            for (int i = 0; i < 3; i++)
            {
                try { return File.ReadAllText(p); }
                catch (IOException) { System.Threading.Thread.Sleep(80); }   // 编辑器还在写
            }
            return "（文件被占用，稍后自动刷新）";
        }

        static string MapHeader()
        {
            var m = GameData.Map(App.S.地图);
            var s = $"# 战场 {App.S.地图}\n";
            if (m == null) return s;
            foreach (var n in m.说明) s += $"- {n}\n";
            if (m.装置.Count > 0) s += $"- 专属装置：{string.Join("、", m.装置)}\n";
            foreach (var t in m.地形) s += $"- 特殊地形：{t}\n";
            s += $"- 共通：{string.Join("；", GameData.Maps.共通)}\n\n---\n";
            return s;
        }

        void Edit()
        {
            string path;
            if (string.IsNullOrEmpty(App.S.攻略文件))
            {
                path = MapGuidePath;
                if (!File.Exists(path))
                {
                    Directory.CreateDirectory(Paths.MapGuides);
                    File.WriteAllText(path,
$@"## {App.S.地图} 打法

### 场地1
- 敌人从哪边来：
- 推荐站位：
- 适合的盟约：

### 场地2
-

### 场地3
-

### 领袖轮
-
");
                }
            }
            else path = Path.Combine(Paths.Guides, App.S.攻略文件);
            try { Process.Start(new ProcessStartInfo("notepad.exe", $"\"{path}\"") { UseShellExecute = false }); }
            catch { App.OpenPath(path); }
        }
    }

    // ======================= 设置 =======================
    public class SettingsView : ScrollViewer
    {
        public SettingsView(OverlayWindow w)
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            var s = App.S;
            var sp = new StackPanel();
            Content = sp;
            void H(string t) => sp.Children.Add(UI.T(t, 1.0, Theme.Acc, true, new Thickness(0, 8, 0, 4)));
            void Note(string t) => sp.Children.Add(UI.T(t, 0.82, "#9AA6AE", false, new Thickness(0, 0, 0, 4)));

            H("外观");
            sp.Children.Add(ColorRow("主题色", Theme.AccentPresets, s.主题色, hex => { s.主题色 = hex; w.RefreshTheme(); }, w));
            sp.Children.Add(ColorRow("面板底色", Theme.PanelPresets, s.面板色, hex => { s.面板色 = hex; w.RefreshTheme(); }, w));
            sp.Children.Add(ColorRow("悬浮球光圈", new[] { ("跟随主题色", "") }.Concat(Theme.AccentPresets), s.悬浮球色,
                hex => { s.悬浮球色 = hex; w.RefreshTheme(); }, w));

            // 悬浮球图片
            sp.Children.Add(UI.T("悬浮球图片", 0.88, "#C9D1D6", false, new Thickness(0, 2, 0, 3)));
            var icons = new WrapPanel();
            var choices = Enumerable.Range(1, 8).Select(i => ($"图片/通用/奖杯{i}.png", $"联合成就奖杯 {i} 级")).Append(("图片/通用/卫戍节点.png", "卫戍节点"));
            foreach (var (rel, tip) in choices)
            {
                bool sel = s.悬浮球图片 == rel;
                var pic = UI.Img(rel, 34, "", disc: true, border: sel ? "#FFFFFF" : null);
                pic.Margin = new Thickness(0, 0, 6, 6);
                pic.Cursor = System.Windows.Input.Cursors.Hand;
                pic.ToolTip = tip;
                var r = rel;
                pic.MouseLeftButtonUp += (_, _) => { s.悬浮球图片 = r; w.RefreshTheme(); };
                icons.Children.Add(pic);
            }
            sp.Children.Add(icons);
            var opPick = new ComboBox { MinWidth = 150, FontSize = UI.F(0.88) };
            var ops = GameData.Ops.OrderBy(o => o.等阶).ThenBy(o => o.名称).ToList();
            opPick.ItemsSource = new[] { "用干员头像…" }.Concat(ops.Select(o => $"{o.等阶字} {o.名称}")).ToList();
            int cur = ops.FindIndex(o => o.图片 == s.悬浮球图片);
            opPick.SelectedIndex = cur + 1;
            opPick.SelectionChanged += (_, _) =>
            {
                if (opPick.SelectedIndex <= 0) return;
                s.悬浮球图片 = ops[opPick.SelectedIndex - 1].图片;
                w.RefreshTheme();
            };
            sp.Children.Add(UI.Wrap(opPick, UI.Btn("选择图片文件…", () => PickBallImage(w), "用自己的图片（会复制到 数据\\图片\\自定义\\）")));
            sp.Children.Add(Slider("悬浮球大小", 44, 88, s.悬浮球大小, v => { s.悬浮球大小 = Math.Round(v); w.ApplyLook(); w.Relayout(); }, "0"));
            var edge = new CheckBox { Content = "悬浮球拖到屏幕边缘时半隐藏、变半透明", IsChecked = s.贴边隐藏 };
            edge.Click += (_, _) => { s.贴边隐藏 = edge.IsChecked == true; if (!s.贴边隐藏) s.收起贴边 = ""; w.Relayout(); };
            sp.Children.Add(edge);
            sp.Children.Add(UI.Btn("恢复默认外观", () =>
            {
                var d = new Settings();
                s.主题色 = d.主题色; s.面板色 = d.面板色; s.悬浮球色 = d.悬浮球色; s.悬浮球图片 = d.悬浮球图片;
                s.悬浮球大小 = d.悬浮球大小; s.不透明度 = d.不透明度;
                w.RefreshTheme();
            }, "配色、悬浮球图片和大小、不透明度恢复默认"));

            H("显示");
            sp.Children.Add(Slider("宽度", 300, 760, s.宽度, v => { s.宽度 = v; w.Relayout(); }, "0"));
            sp.Children.Add(Slider("不透明度", 0.4, 1.0, s.不透明度, v => { s.不透明度 = v; w.ApplyLook(); }, "0.00"));
            sp.Children.Add(Slider("字号", 11, 18, s.字号, v => { s.字号 = Math.Round(v); }, "0", () => w.Rebuild()));
            var cb = new CheckBox { Content = "只在游戏窗口在最前面时显示", IsChecked = s.仅游戏前台显示 };
            cb.Click += (_, _) => s.仅游戏前台显示 = cb.IsChecked == true;
            sp.Children.Add(cb);
            var order = new CheckBox { Content = "布阵页部署顺序：先按列，同一列再从上到下", IsChecked = s.先列后行 };
            order.Click += (_, _) => { s.先列后行 = order.IsChecked == true; w.Rebuild(); };
            sp.Children.Add(order);
            Note("游戏内说明是「从左至右、从上到下」依次部署。实际顺序以游戏为准，不对就取消勾选改成按行。");

            H("快捷键（全局）");
            sp.Children.Add(UI.T("Ctrl+Shift+F1　显示 / 隐藏\nCtrl+Shift+F2　鼠标穿透（穿透时只有「穿透」按钮能点，再点一次恢复）\nCtrl+Shift+F3　切换 布阵 / 资料", 0.9));
            if (!string.IsNullOrEmpty(w.HotkeyError)) Note("！" + w.HotkeyError);

            H("数据");
            sp.Children.Add(UI.T($"{GameData.Season}　干员 {GameData.Ops.Count}　盟约 {GameData.Bonds.Count}　策略 {GameData.Strategies.Count}　装备 {GameData.Items.Count}\n更新于 {GameData.Updated}（来源 PRTS）", 0.88));
            sp.Children.Add(UI.Wrap(
                UI.Btn("从 PRTS 更新数据", () =>
                {
                    var py = Path.Combine(Paths.Tools, "更新数据.py");
                    try { Process.Start(new ProcessStartInfo("cmd.exe", $"/k python -X utf8 \"{py}\" && echo. && echo 更新完成，回到悬浮窗点「重新载入数据」") { UseShellExecute = true }); }
                    catch (Exception e) { MessageBox.Show(w, "启动失败：" + e.Message); }
                }, "需要本机装了 Python 和 requests。序列Ⅱ / 新赛季上线后跑一次"),
                UI.Btn("重新载入数据", () => { GameData.Load(); w.Rebuild(); }),
                UI.Btn("打开程序文件夹", () => App.OpenPath(Paths.Root))));

            H("关于");
            Note("外置悬浮窗：只读取游戏窗口的位置和大小来贴靠，不读写游戏内存、不注入、不改游戏文件。\n游戏若是「独占全屏」，悬浮窗可能盖不上去，请在游戏设置里用窗口化或无边框全屏。");
            sp.Children.Add(UI.Btn("退出卫戍协议助手", () => ((App)Application.Current).Quit()));
        }

        class Owner : System.Windows.Forms.IWin32Window { public IntPtr Handle { get; init; } }

        /// <summary>一排预设色块 + 「自定义…」（系统取色器）。hex 为空的预设表示「跟随主题色」。</summary>
        static UIElement ColorRow(string name, IEnumerable<(string name, string hex)> presets, string current, Action<string> set, Window owner)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 2, 0, 6) };
            var head = new TextBlock { FontSize = UI.F(0.88), Foreground = UI.B("#C9D1D6") };
            head.Inlines.Add(new Run(name + "　"));
            head.Inlines.Add(new Run(string.IsNullOrEmpty(current) ? "跟随主题色" : current.ToUpper()) { Foreground = UI.B("#8A969E"), FontSize = UI.F(0.78) });
            sp.Children.Add(head);
            var wrap = new WrapPanel { Margin = new Thickness(0, 3, 0, 0) };
            foreach (var (n, hex) in presets)
            {
                bool sel = string.Equals(current ?? "", hex, StringComparison.OrdinalIgnoreCase);
                var sw = new Border
                {
                    Width = 24, Height = 24, CornerRadius = new CornerRadius(12), Margin = new Thickness(0, 0, 7, 5),
                    Background = hex == "" ? new LinearGradientBrush(Theme.Accent, Theme.Mix(Theme.Accent, Colors.Black, 0.5), 45) : UI.B(hex),
                    BorderBrush = UI.B(sel ? "#FFFFFF" : "#50FFFFFF"), BorderThickness = new Thickness(sel ? 2.5 : 1),
                    Cursor = System.Windows.Input.Cursors.Hand, ToolTip = n,
                };
                if (hex == "") sw.Child = new TextBlock { Text = "随", FontSize = 11, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.Bold };
                var h = hex;
                sw.MouseLeftButtonUp += (_, _) => set(h);
                wrap.Children.Add(sw);
            }
            var custom = UI.Btn("自定义…", () =>
            {
                var dlg = new System.Windows.Forms.ColorDialog { FullOpen = true, AnyColor = true };
                try { dlg.Color = System.Drawing.ColorTranslator.FromHtml(string.IsNullOrEmpty(current) ? Theme.Acc : current); } catch { }
                if (dlg.ShowDialog(new Owner { Handle = new System.Windows.Interop.WindowInteropHelper(owner).Handle }) == System.Windows.Forms.DialogResult.OK)
                    set($"#{dlg.Color.R:X2}{dlg.Color.G:X2}{dlg.Color.B:X2}");
            }, "打开取色器选任意颜色", 0.8);
            custom.Margin = new Thickness(0, 0, 0, 5);
            wrap.Children.Add(custom);
            sp.Children.Add(wrap);
            return sp;
        }

        static void PickBallImage(OverlayWindow w)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "图片|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp", Title = "选择悬浮球图片" };
            if (dlg.ShowDialog(w) != true) return;
            try
            {
                var dir = Path.Combine(Paths.Data, "图片", "自定义");
                Directory.CreateDirectory(dir);
                foreach (var old in Directory.GetFiles(dir, "悬浮球_*")) { try { File.Delete(old); } catch { } }
                var name = $"悬浮球_{DateTime.Now:yyyyMMddHHmmss}{Path.GetExtension(dlg.FileName).ToLower()}";
                File.Copy(dlg.FileName, Path.Combine(dir, name), true);
                App.S.悬浮球图片 = "图片/自定义/" + name;
                w.RefreshTheme();
            }
            catch (Exception e) { MessageBox.Show(w, "图片复制失败：" + e.Message, "卫戍协议助手"); }
        }

        static UIElement Slider(string name, double min, double max, double val, Action<double> set, string fmt, Action done = null)
        {
            var label = UI.T($"{name}：{val.ToString(fmt)}", 0.9);
            var sl = new Slider { Minimum = min, Maximum = max, Value = val };
            sl.ValueChanged += (_, e) => { label.Text = $"{name}：{e.NewValue.ToString(fmt)}"; set(e.NewValue); };
            if (done != null) sl.PreviewMouseLeftButtonUp += (_, _) => done();
            var sp = new StackPanel();
            sp.Children.Add(label);
            sp.Children.Add(sl);
            return sp;
        }
    }
}
