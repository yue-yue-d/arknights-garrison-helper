using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace WeishuHelper
{
    // ---------- 存档结构：布阵/#01.json（每个战场一份，底图用场地2的截图） ----------
    public class Placed
    {
        public string 干员 = "";
        public int x = -1, y = -1;
        public bool 精锐;
        public int 等阶;   // 支援（甄选）干员放在哪一阶的甄选槽位：5 或 6；名单干员为 0（用名单里的等阶）
    }

    public class Plan
    {
        public string 名称 = "方案1";
        public List<Placed> 场上 = new();
        public List<Placed> 整备 = new();
        public List<string> 禁用盟约 = new();
        public string 策略 = "";
        public string 备注 = "";
    }

    public class Terrain
    {
        public int 列 = 12, 行 = 6;
        public List<string> 格 = new();
        /// <summary>网格四个角在地图截图上的位置（0～1）：左上x,y 右上x,y 右下x,y 左下x,y。空 = 默认。</summary>
        public List<double> 角点 = new();
        public static readonly double[] 默认角点 = { 0.17, 0.25, 0.97, 0.25, 1.0, 0.80, 0.02, 0.80 };
        public double[] Corners() => 角点.Count == 8 ? 角点.ToArray() : (double[])默认角点.Clone();
        public char Get(int x, int y) => y < 格.Count && x < 格[y].Length ? 格[y][x] : '.';
        public void Set(int x, int y, char c)
        {
            Fix();
            var a = 格[y].ToCharArray();
            a[x] = c;
            格[y] = new string(a);
        }
        public void Fix()
        {
            while (格.Count < 行) 格.Add("");
            if (格.Count > 行) 格.RemoveRange(行, 格.Count - 行);
            for (int i = 0; i < 行; i++)
                格[i] = 格[i].Length >= 列 ? 格[i][..列] : 格[i].PadRight(列, '.');
        }
    }

    public class SiteFile
    {
        public Terrain 地形 = new();
        public List<Plan> 方案 = new();
        public string 当前 = "";
    }

    // ---------- 盟约计算 ----------
    public record BondState(Bond Bond, int Count, int Next, string State, bool Disabled, string Extra);

    public static class BondCalc
    {
        public static List<BondState> Compute(Plan p)
        {
            var onBoard = p.场上.Select(x => x.干员).Distinct().Where(GameData.AnyBy.ContainsKey).ToList();
            var onBench = p.整备.Select(x => x.干员).Distinct().Where(GameData.AnyBy.ContainsKey).ToList();
            bool harmony = onBoard.Any(n => GameData.AnyBy[n].盟约.Contains("调和"));
            var res = new List<BondState>();
            foreach (var b in GameData.Bonds)
            {
                int c;
                string extra = "";
                if (b.名称 == "绝技") c = p.场上.Count(x => x.精锐);
                else
                {
                    var names = new HashSet<string>(onBoard.Where(n => GameData.AnyBy[n].盟约.Contains(b.名称)));
                    if (b.整备区计入) foreach (var n in onBench) if (GameData.AnyBy[n].盟约.Contains(b.名称)) names.Add(n);
                    c = names.Count;
                    if (b.核心 && harmony && c > 0) { c += 1; extra = "（含调和 +1）"; }
                }
                if (c == 0) continue;
                int t1 = b.激活人数, t2 = b.强化人数 ?? t1;
                string state;
                int next;
                if (b.名称 == "独行")
                {
                    state = c == 1 ? "激活" : "失效";
                    next = 1;
                    if (c > 1) extra = "（场上 2 名以上独行会失效）";
                }
                else
                {
                    state = c >= t2 && t2 > t1 ? "强化" : c >= t1 ? "激活" : "未激活";
                    next = c < t1 ? t1 : (c < t2 ? t2 : 0);
                }
                res.Add(new BondState(b, c, next, state, p.禁用盟约.Contains(b.名称), extra));
            }
            int Rank(string s) => s == "强化" ? 0 : s == "激活" ? 1 : 2;
            return res.OrderBy(r => r.Disabled).ThenBy(r => Rank(r.State)).ThenByDescending(r => r.Bond.核心).ThenByDescending(r => r.Count).ToList();
        }
    }

    // ======================= 布阵页 =======================
    public class PlannerView : DockPanel
    {
        public event Action MapChanged;

        static readonly (char c, string name, string color)[] Tiles =
        {
            ('.', "未标注", "#22FFFFFF"), ('G', "地面", "#5E6E61"), ('H', "高台", "#3F79A0"),
            ('X', "不可部署", "#151515"), ('R', "敌人入口", "#A8382C"), ('B', "保护点", "#2C62A8"), ('S', "特殊地块", "#7A5BA6"),
        };
        static string TileColor(char c) => Tiles.FirstOrDefault(t => t.c == c).color ?? "#22FFFFFF";
        static string TileName(char c) => Tiles.FirstOrDefault(t => t.c == c).name ?? "未标注";

        readonly int _limit;
        SiteFile _site = new();
        Plan _plan = new();
        Placed _hold;
        string _holdSrc = "";
        bool _editTerrain;
        char _brush = 'H';
        int _tierFilter;
        string _bondFilter = "";
        bool _showBondToggles;
        bool _showGrid;
        ToggleButton _editBtn;
        // 底图模式的几何对象（拖角点时就地更新，不整块重建）
        readonly List<(Polygon poly, int x, int y)> _cellPolys = new();
        readonly List<(FrameworkElement el, int x, int y)> _avatars = new();
        readonly List<Ellipse> _handles = new();
        Polygon _frame;
        double _bw, _bh;
        int _dragCorner = -1;

        readonly StackPanel _root = new();
        readonly ScrollViewer _scroll;
        readonly ComboBox _plans = new() { MinWidth = 120 };
        readonly Grid _board = new() { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 2, 0, 4) };
        readonly WrapPanel _palette = new() { Margin = new Thickness(0, 0, 0, 2) };
        readonly TextBlock _status = new() { Margin = new Thickness(0, 0, 0, 4), TextWrapping = TextWrapping.Wrap };
        readonly TextBlock _flash = new() { Foreground = UI.B("#FFB02E"), FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap };
        readonly WrapPanel _bonds = new();
        readonly Border _benchBox = new() { MinHeight = 30, Background = UI.B("#14FFFFFF"), CornerRadius = new CornerRadius(4), Padding = new Thickness(4, 4, 4, 1), Margin = new Thickness(0, 0, 0, 6) };
        readonly WrapPanel _bench = new();
        readonly ComboBox _strategy = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
        readonly TextBlock _strategyText = new() { Margin = new Thickness(0, 0, 0, 6), TextWrapping = TextWrapping.Wrap };
        readonly WrapPanel _bondToggles = new();
        readonly TextBox _search;
        readonly WrapPanel _tierBar = new();
        // 支援（甄选）
        bool _supportMode;
        string _supportCat = "";
        readonly WrapPanel _supportBar = new();
        readonly TextBlock _pickHint = new() { TextWrapping = TextWrapping.Wrap, Foreground = UI.B("#9AA6AE"), Margin = new Thickness(0, 0, 0, 4) };
        readonly WrapPanel _bondFilterBar = new();
        readonly StackPanel _picker = new();
        readonly TextBox _notes = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 56, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        readonly DispatcherTimer _save = new() { Interval = TimeSpan.FromMilliseconds(600) };
        readonly DispatcherTimer _flashTimer = new() { Interval = TimeSpan.FromSeconds(3) };
        bool _loading;

        public PlannerView()
        {
            _limit = ParseLimit();
            Focusable = true;
            _scroll = UI.Scroll(_root);
            Children.Add(_scroll);
            _save.Tick += (_, _) => { _save.Stop(); SaveNow(); };
            _flashTimer.Tick += (_, _) => { _flashTimer.Stop(); _flash.Text = ""; };
            PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { _hold = null; RenderPlan(); } };
            _scroll.SizeChanged += (_, e) => { if (Math.Abs(e.NewSize.Width - e.PreviousSize.Width) > 1) RenderBoard(); };

            var fs = UI.F(0.95);
            _status.FontSize = fs; _flash.FontSize = fs; _strategyText.FontSize = UI.F(0.85);
            _notes.FontSize = fs; _plans.FontSize = fs; _strategy.FontSize = UI.F(0.88);

            // 地图选择
            var maps = new WrapPanel();
            foreach (var m in GameData.Maps.战场)
            {
                var id = m.编号;
                maps.Children.Add(UI.Radio(id, "map", id == App.S.地图, () => SwitchMap(id), 0.82).Tip(string.Join("\n", m.说明.Concat(m.地形))));
            }
            _root.Children.Add(maps);
            var sites = new WrapPanel();
            var editBtn = _editBtn = UI.Toggle("编辑地形", false, on => { _editTerrain = on; _hold = null; _palette.Visibility = on ? Visibility.Visible : Visibility.Collapsed; RenderPlan(); }, 0.82)
                .Tip("照着地图把格子标成 地面 / 高台 / 不可部署 等。每张图标一次，以后一直能用。");
            sites.Children.Add(editBtn);
            sites.Children.Add(UI.Toggle("显示格子", false, on => { _showGrid = on; RenderBoard(); }, 0.82)
                .Tip("平时格子是透明的，藏在地图图片里。打开后会淡淡地显示标记过的格子。"));
            _root.Children.Add(sites);

            // 方案
            var planRow = new WrapPanel();
            planRow.Children.Add(_plans);
            planRow.Children.Add(UI.Btn("新建", () => NewPlan(false), "新建空白方案"));
            planRow.Children.Add(UI.Btn("复制", () => NewPlan(true), "复制当前方案"));
            planRow.Children.Add(UI.Btn("改名", RenamePlan));
            planRow.Children.Add(UI.Btn("删除", DeletePlan));
            _root.Children.Add(planRow);
            _plans.SelectionChanged += (_, _) =>
            {
                if (_loading || _plans.SelectedIndex < 0) return;
                _plan = _site.方案[_plans.SelectedIndex];
                _site.当前 = _plan.名称;
                _hold = null;
                LoadPlanFields();
                RenderAll();
                QueueSave();
            };

            // 地形画笔
            _palette.Visibility = Visibility.Collapsed;
            BuildPalette();
            _root.Children.Add(_palette);

            _root.Children.Add(_board);
            _root.Children.Add(_flash);
            _root.Children.Add(_status);

            // 盟约
            _root.Children.Add(Section("盟约"));
            _root.Children.Add(_bonds);

            // 整备区
            _root.Children.Add(Section("整备区（点干员拿起，点格子放下；拿着场上干员点这里＝放回整备区）"));
            _benchBox.Child = _bench;
            _benchBox.MouseLeftButtonDown += (_, e) =>
            {
                if (_hold == null || _holdSrc == "bench") return;
                if (_holdSrc == "board") _plan.场上.Remove(_hold);
                _hold.x = _hold.y = -1;
                _plan.整备.Add(_hold);
                _hold = null;
                Changed();
            };
            _root.Children.Add(_benchBox);

            // 策略
            _root.Children.Add(Section("策略"));
            _strategy.ItemsSource = new[] { "（未选）" }.Concat(GameData.Strategies.Select(s => $"{s.策略}　{s.发起人}·{s.血量}血")).ToList();
            _strategy.SelectionChanged += (_, _) =>
            {
                if (_loading) return;
                _plan.策略 = _strategy.SelectedIndex > 0 ? GameData.Strategies[_strategy.SelectedIndex - 1].策略 : "";
                ShowStrategy();
                QueueSave();
            };
            _root.Children.Add(_strategy);
            _root.Children.Add(_strategyText);

            // 本局盟约
            var toggleBonds = UI.Toggle("本局可用盟约 ▾", false, on => { _showBondToggles = on; _bondToggles.Visibility = on ? Visibility.Visible : Visibility.Collapsed; }, 0.85)
                .Tip("每局只随机载入部分盟约。开局时把这局没有的盟约点灰，下面的干员列表就只显示这局能拿到的人。");
            _root.Children.Add(toggleBonds);
            _bondToggles.Visibility = Visibility.Collapsed;
            _root.Children.Add(_bondToggles);

            // 干员列表
            var pickTabs = new WrapPanel { Margin = new Thickness(0, 6, 0, 2) };
            pickTabs.Children.Add(UI.Radio("干员", "picktab", true, () => SetSupportMode(false), 0.92));
            pickTabs.Children.Add(UI.Radio("支援", "picktab", false, () => SetSupportMode(true), 0.92)
                .Tip("甄选：第Ⅴ、Ⅵ阶各 2 个槽位，可以放名单外的 6★ 干员和罗德岛特派高级资深干员（精英干员）"));
            _root.Children.Add(pickTabs);
            _pickHint.FontSize = UI.F(0.8);
            _root.Children.Add(_pickHint);
            var sb = UI.SearchBox(out _search, "搜名字 / 盟约 / 职业 / 特质…");
            _root.Children.Add(sb);
            _search.TextChanged += (_, _) => RenderPicker();
            _tierBar.Children.Add(UI.Radio("全部", "tier", true, () => { _tierFilter = 0; RenderPicker(); }, 0.82));
            for (int t = 1; t <= 6; t++)
            {
                int k = t;
                var r = UI.Radio(Tier.Roman(t), "tier", false, () => { _tierFilter = k; RenderPicker(); }, 0.82);
                r.Foreground = UI.B(Tier.Color(t));
                _tierBar.Children.Add(r);
            }
            _root.Children.Add(_tierBar);
            _supportBar.Visibility = Visibility.Collapsed;
            foreach (var (label, cat) in new[] { ("全部", ""), ("六星", "六星"), ("精英干员", "精英干员") })
            {
                var c = cat;
                _supportBar.Children.Add(UI.Radio(label, "supcat", cat == "", () => { _supportCat = c; RenderPicker(); }, 0.82));
            }
            _root.Children.Add(_supportBar);
            _root.Children.Add(_bondFilterBar);
            _root.Children.Add(new Border
            {
                BorderBrush = UI.B("#30FFFFFF"), BorderThickness = new Thickness(0, 1, 0, 1), Margin = new Thickness(0, 0, 0, 6),
                Child = new ScrollViewer { Content = _picker, MaxHeight = 300, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
            });

            // 备注
            _root.Children.Add(Section("备注"));
            _notes.TextChanged += (_, _) => { if (_loading) return; _plan.备注 = _notes.Text; QueueSave(); };
            _root.Children.Add(_notes);

            LoadSite();
        }

        static int ParseLimit()
        {
            foreach (var s in GameData.Maps.共通)
            {
                var m = Regex.Match(s, @"部署上限[：:]\s*(\d+)");
                if (m.Success) return int.Parse(m.Groups[1].Value);
            }
            return 8;
        }

        static TextBlock Section(string s) => UI.T(s, 0.85, Theme.Acc, true, new Thickness(0, 6, 0, 3));

        void Flash(string s) { _flash.Text = s; _flashTimer.Stop(); _flashTimer.Start(); }

        // ---------- 存取 ----------
        string SitePath => System.IO.Path.Combine(Paths.Plans, $"{App.S.地图}.json");
        bool _dirty;

        /// <summary>把网格设成这张图的默认位置（数据/网格默认.json）。</summary>
        void ApplyGridDefault(Terrain t, bool size)
        {
            if (!GameData.Grids.TryGetValue(App.S.地图, out var g)) return;
            if (g.角点.Count == 8) t.角点 = g.角点.ToList();
            if (size) { t.列 = g.列; t.行 = g.行; }
            t.Fix();
        }

        void LoadSite()
        {
            bool fresh = !File.Exists(SitePath);
            _site = Json.Read<SiteFile>(SitePath);
            if (fresh || _site.地形.角点.Count != 8)
                ApplyGridDefault(_site.地形, fresh || _site.地形.格.All(r => r.All(c => c == '.')));
            _site.地形.Fix();
            _dirty = false;
            if (_site.方案.Count == 0) _site.方案.Add(new Plan());
            _plan = _site.方案.FirstOrDefault(p => p.名称 == _site.当前) ?? _site.方案[0];
            _hold = null;
            FillPlans();
            LoadPlanFields();
            RenderAll();
        }

        void FillPlans()
        {
            _loading = true;
            _plans.ItemsSource = _site.方案.Select(p => p.名称).ToList();
            _plans.SelectedIndex = _site.方案.IndexOf(_plan);
            _loading = false;
        }

        void LoadPlanFields()
        {
            _loading = true;
            _notes.Text = _plan.备注;
            int si = GameData.Strategies.FindIndex(s => s.策略 == _plan.策略);
            _strategy.SelectedIndex = si + 1;
            _loading = false;
            ShowStrategy();
        }

        void QueueSave() { _dirty = true; _save.Stop(); _save.Start(); }

        public void SaveNow()
        {
            _save.Stop();
            if (!_dirty) return;   // 只是翻看过的地图不存文件
            try
            {
                _dirty = false;
                _site.当前 = _plan.名称;
                Json.Write(SitePath, _site);
            }
            catch (Exception e) { App.Log("保存布阵失败: " + e.Message); }
        }

        void SwitchMap(string map)
        {
            if (map == App.S.地图) return;
            SaveNow();
            App.S.地图 = map;
            App.S.Save();
            LoadSite();
            MapChanged?.Invoke();
        }

        void NewPlan(bool copy)
        {
            int n = _site.方案.Count + 1;
            while (_site.方案.Any(p => p.名称 == $"方案{n}")) n++;
            var p = copy
                ? System.Text.Json.JsonSerializer.Deserialize<Plan>(System.Text.Json.JsonSerializer.Serialize(_plan, Json.Opt), Json.Opt)
                : new Plan();
            p.名称 = copy ? _plan.名称 + " 副本" : $"方案{n}";
            _site.方案.Add(p);
            _plan = p;
            _hold = null;
            FillPlans();
            LoadPlanFields();
            RenderAll();
            QueueSave();
        }

        void RenamePlan()
        {
            var box = new TextBox { Text = _plan.名称, MinWidth = 140, FontSize = UI.F(0.95) };
            var parent = (WrapPanel)_plans.Parent;
            int idx = parent.Children.IndexOf(_plans);
            parent.Children.RemoveAt(idx);
            parent.Children.Insert(idx, box);
            box.Focus();
            box.SelectAll();
            bool done = false;
            void Commit()
            {
                if (done) return;
                done = true;
                var name = box.Text.Trim();
                if (name.Length > 0 && !_site.方案.Any(p => p != _plan && p.名称 == name)) _plan.名称 = name;
                parent.Children.Remove(box);
                parent.Children.Insert(idx, _plans);
                FillPlans();
                QueueSave();
            }
            box.KeyDown += (_, e) => { if (e.Key == Key.Enter) Commit(); if (e.Key == Key.Escape) { box.Text = _plan.名称; Commit(); } };
            box.LostKeyboardFocus += (_, _) => Commit();
        }

        void DeletePlan()
        {
            var owner = Window.GetWindow(this);
            if (MessageBox.Show(owner, $"删除方案「{_plan.名称}」？", "卫戍协议助手", MessageBoxButton.OKCancel) != MessageBoxResult.OK) return;
            _site.方案.Remove(_plan);
            if (_site.方案.Count == 0) _site.方案.Add(new Plan());
            _plan = _site.方案[0];
            _hold = null;
            FillPlans();
            LoadPlanFields();
            RenderAll();
            QueueSave();
        }

        void Changed() { RenderPlan(); QueueSave(); if (_supportMode) RenderPicker(); }

        // ---------- 画面 ----------
        void RenderAll()
        {
            RenderBondToggles();
            RenderPicker();
            RenderPlan();
        }

        void RenderPlan()
        {
            RenderBoard();
            RenderBench();
            RenderBonds();
            RenderStatus();
        }

        void BuildPalette()
        {
            _palette.Children.Clear();
            var align = UI.Radio("对齐网格", "brush", _brush == '#', () => { _brush = '#'; RenderPlan(); }, 0.8);
            align.BorderBrush = UI.B("#FFB02E");
            align.BorderThickness = new Thickness(0, 0, 0, 3);
            _palette.Children.Add(align);
            foreach (var (c, name, color) in Tiles)
            {
                var ch = c;
                var r = UI.Radio(name, "brush", c == _brush, () => { bool was = _brush == '#'; _brush = ch; if (was) RenderPlan(); }, 0.8);
                r.BorderBrush = UI.B(color);
                r.BorderThickness = new Thickness(0, 0, 0, 3);
                _palette.Children.Add(r);
            }
            _palette.Children.Add(UI.Btn("列−", () => Resize(-1, 0), null, 0.8));
            _palette.Children.Add(UI.Btn("列＋", () => Resize(1, 0), null, 0.8));
            _palette.Children.Add(UI.Btn("行−", () => Resize(0, -1), null, 0.8));
            _palette.Children.Add(UI.Btn("行＋", () => Resize(0, 1), null, 0.8));
            _palette.Children.Add(UI.Btn("清空", () =>
            {
                for (int y = 0; y < _site.地形.行; y++) _site.地形.格[y] = new string('.', _site.地形.列);
                Changed();
            }, null, 0.8));
            _palette.Children.Add(UI.Btn("恢复默认网格", () => { ApplyGridDefault(_site.地形, true); Resize(0, 0); }, "网格位置和列数、行数恢复成这张图的默认值（已标记的格子保留）", 0.8));
            _palette.Children.Add(UI.T("网格已经按截图对好了，一般直接选 地面 / 高台 / 不可部署 等点格子标记即可（按住可以连续涂，右键擦掉）。没对准时选「对齐网格」拖四个黄色圆点微调。地形是这张图所有方案共用的。", 0.78, "#9AA6AE"));
        }

        void Resize(int dc, int dr)
        {
            var t = _site.地形;
            t.列 = Math.Clamp(t.列 + dc, 3, 20);
            t.行 = Math.Clamp(t.行 + dr, 2, 12);
            t.Fix();
            foreach (var p in _plan.场上.Where(p => p.x >= t.列 || p.y >= t.行).ToList())
            {
                _plan.场上.Remove(p);
                p.x = p.y = -1;
                _plan.整备.Add(p);
            }
            Changed();
        }

        List<Placed> DeployOrder() => (App.S.先列后行
            ? _plan.场上.OrderBy(p => p.x).ThenBy(p => p.y)
            : _plan.场上.OrderBy(p => p.y).ThenBy(p => p.x)).ToList();

        void RenderBoard()
        {
            var t = _site.地形;
            _board.Children.Clear();
            _board.RowDefinitions.Clear();
            _board.ColumnDefinitions.Clear();
            var mapRel = MapImageRel();
            if (mapRel != null) { RenderImageBoard(mapRel); return; }
            double avail = Math.Max(200, (_scroll.ViewportWidth > 0 ? _scroll.ViewportWidth : _scroll.ActualWidth) - 4);
            double cell = Math.Floor(Math.Min(40, avail / t.列));
            for (int i = 0; i < t.列; i++) _board.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(cell) });
            for (int i = 0; i < t.行; i++) _board.RowDefinitions.Add(new RowDefinition { Height = new GridLength(cell) });
            var order = DeployOrder();

            for (int y = 0; y < t.行; y++)
                for (int x = 0; x < t.列; x++)
                {
                    char c = t.Get(x, y);
                    var occ = _plan.场上.FirstOrDefault(p => p.x == x && p.y == y);
                    var b = new Border
                    {
                        Background = UI.B(TileColor(c)),
                        BorderBrush = UI.B("#40000000"),
                        BorderThickness = new Thickness(0.5),
                        Cursor = Cursors.Hand,
                    };
                    if (c == 'R' || c == 'B')
                        b.Child = new TextBlock { Text = c == 'R' ? "入" : "守", FontSize = cell * 0.36, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = UI.B("#CCFFFFFF") };
                    if (occ != null) b.Child = OpTile(occ, cell, order.IndexOf(occ) + 1, c);
                    int cx = x, cy = y;
                    b.MouseLeftButtonDown += (_, e) => { e.Handled = true; Focus(); CellClick(cx, cy); };
                    b.MouseEnter += (_, e) => { if (_editTerrain && e.LeftButton == MouseButtonState.Pressed) Paint(cx, cy); };
                    b.MouseRightButtonUp += (_, e) =>
                    {
                        e.Handled = true;
                        if (_editTerrain) { Paint(cx, cy, '.'); return; }
                        if (occ != null) ShowMenu(b, occ, true);
                        else if (_hold != null) { _hold = null; RenderPlan(); }
                    };
                    b.ToolTip = occ != null ? OpTip(occ) : TileName(c);
                    Grid.SetColumn(b, x);
                    Grid.SetRow(b, y);
                    _board.Children.Add(b);
                }
        }

        // ---------- 底图模式：地图截图 + 透视网格 ----------
        string MapImageRel()
        {
            foreach (var site in new[] { 2, 1, 3 })   // 默认用场地2的截图
            {
                var rel = $"图片/地图/{App.S.地图.TrimStart('#')}_场地{site}.png";
                if (File.Exists(System.IO.Path.Combine(Paths.Data, rel))) return rel;
            }
            return null;
        }

        /// <summary>单位正方形 → 四边形的透视变换（Heckbert），q = 左上 右上 右下 左下。</summary>
        static Point Proj(double[] q, double u, double v)
        {
            double x0 = q[0], y0 = q[1], x1 = q[2], y1 = q[3], x2 = q[4], y2 = q[5], x3 = q[6], y3 = q[7];
            double dx1 = x1 - x2, dx2 = x3 - x2, dx3 = x0 - x1 + x2 - x3;
            double dy1 = y1 - y2, dy2 = y3 - y2, dy3 = y0 - y1 + y2 - y3;
            double a, b, c, d, e, f, g, h;
            if (Math.Abs(dx3) < 1e-9 && Math.Abs(dy3) < 1e-9)
            {
                a = x1 - x0; b = x2 - x1; c = x0; d = y1 - y0; e = y2 - y1; f = y0; g = 0; h = 0;
            }
            else
            {
                double den = dx1 * dy2 - dx2 * dy1;
                if (Math.Abs(den) < 1e-12) den = 1e-12;
                g = (dx3 * dy2 - dx2 * dy3) / den;
                h = (dx1 * dy3 - dx3 * dy1) / den;
                a = x1 - x0 + g * x1; b = x3 - x0 + h * x3; c = x0;
                d = y1 - y0 + g * y1; e = y3 - y0 + h * y3; f = y0;
            }
            double w = g * u + h * v + 1;
            return new Point((a * u + b * v + c) / w, (d * u + e * v + f) / w);
        }

        Point P(double[] q, double u, double v) { var p = Proj(q, u, v); return new Point(p.X * _bw, p.Y * _bh); }

        PointCollection CellPoints(double[] q, int x, int y)
        {
            var t = _site.地形;
            double u0 = (double)x / t.列, u1 = (double)(x + 1) / t.列, v0 = (double)y / t.行, v1 = (double)(y + 1) / t.行;
            return new PointCollection { P(q, u0, v0), P(q, u1, v0), P(q, u1, v1), P(q, u0, v1) };
        }

        /// <summary>格子中心和大约的边长（透视下近处的格子更大）。</summary>
        (Point c, double size) CellCenter(double[] q, int x, int y)
        {
            var t = _site.地形;
            double um = (x + 0.5) / t.列, vm = (y + 0.5) / t.行;
            var c = P(q, um, vm);
            var l = P(q, (double)x / t.列, vm); var r = P(q, (double)(x + 1) / t.列, vm);
            var up = P(q, um, (double)y / t.行); var dn = P(q, um, (double)(y + 1) / t.行);
            double w = (r - l).Length, h = (dn - up).Length;
            return (c, Math.Max(12, Math.Min(w, h * 1.15) * 0.9));
        }

        static Brush Alpha(string hex, byte a)
        {
            var c = (Color)ColorConverter.ConvertFromString(hex);
            return new SolidColorBrush(Color.FromArgb(a, c.R, c.G, c.B));
        }

        static bool CanPlace(Operator op, char t) => t switch
        {
            'X' or 'R' or 'B' => false,
            'H' => op == null || !op.近战,
            _ => true,
        };

        void RenderImageBoard(string rel)
        {
            var t = _site.地形;
            double avail = Math.Max(200, (_scroll.ViewportWidth > 0 ? _scroll.ViewportWidth : _scroll.ActualWidth) - 4);
            var img = UI.Picture(rel, avail) as Image;
            var src = img?.Source as BitmapSource;
            if (img == null || src == null) return;
            _bw = avail;
            _bh = avail * src.PixelHeight / src.PixelWidth;
            img.Width = _bw; img.Height = _bh; img.Stretch = Stretch.Fill;

            var host = new Grid { Width = _bw, Height = _bh, ClipToBounds = true };
            host.Children.Add(img);
            var cv = new Canvas { Width = _bw, Height = _bh, Background = Brushes.Transparent };
            host.Children.Add(cv);
            _cellPolys.Clear(); _avatars.Clear(); _handles.Clear();
            var q = t.Corners();
            bool align = _editTerrain && _brush == '#';
            GameData.AnyBy.TryGetValue(_hold?.干员 ?? "", out var heldOp);

            for (int y = 0; y < t.行; y++)
                for (int x = 0; x < t.列; x++)
                {
                    char c = t.Get(x, y);
                    var occ = _plan.场上.FirstOrDefault(p => p.x == x && p.y == y);
                    Brush fill, stroke = null;
                    double st = 0;
                    if (_editTerrain)
                    {
                        fill = c == '.' ? Brushes.Transparent : Alpha(TileColor(c), 0x99);
                        stroke = UI.B(align ? "#CCFFB02E" : "#99FFFFFF"); st = align ? 1 : 0.7;
                    }
                    else
                    {
                        fill = _showGrid && c != '.' ? Alpha(TileColor(c), 0x55) : Brushes.Transparent;
                        if (_showGrid) { stroke = UI.B("#55FFFFFF"); st = 0.5; }
                        if (_hold != null && c != '.' && CanPlace(heldOp, c) && occ == null)
                        {
                            fill = UI.B(Theme.A(0x33)); stroke = UI.B(Theme.A(0xAA)); st = 1;   // 拿着干员时，能放的格子微微亮起
                        }
                    }
                    var poly = new Polygon { Points = CellPoints(q, x, y), Fill = fill, Stroke = stroke, StrokeThickness = st, Cursor = align ? Cursors.Arrow : Cursors.Hand };
                    int cx = x, cy = y;
                    var baseFill = fill;
                    if (!align)
                    {
                        poly.MouseLeftButtonDown += (_, e) => { e.Handled = true; Focus(); CellClick(cx, cy); };
                        poly.MouseEnter += (_, e) =>
                        {
                            if (_editTerrain && e.LeftButton == MouseButtonState.Pressed) { Paint(cx, cy); return; }
                            if (!_editTerrain) poly.Fill = UI.B("#38FFFFFF");
                        };
                        poly.MouseLeave += (_, _) => { if (!_editTerrain) poly.Fill = baseFill; };
                        poly.MouseRightButtonUp += (_, e) =>
                        {
                            e.Handled = true;
                            if (_editTerrain) { Paint(cx, cy, '.'); return; }
                            if (occ != null) ShowMenu(poly, occ, true);
                            else if (_hold != null) { _hold = null; RenderPlan(); }
                        };
                        if (_editTerrain || c != '.') poly.ToolTip = TileName(c);
                    }
                    else poly.IsHitTestVisible = false;
                    cv.Children.Add(poly);
                    _cellPolys.Add((poly, x, y));
                }

            var order = DeployOrder();
            foreach (var p in _plan.场上)
            {
                if (p.x < 0 || p.x >= t.列 || p.y < 0 || p.y >= t.行) continue;
                var (center, size) = CellCenter(q, p.x, p.y);
                var av = (FrameworkElement)OpTile(p, size + 2, order.IndexOf(p) + 1, t.Get(p.x, p.y));
                av.Margin = new Thickness(0);
                Canvas.SetLeft(av, center.X - size / 2);
                Canvas.SetTop(av, center.Y - size / 2);
                if (_editTerrain) { av.Opacity = 0.45; av.IsHitTestVisible = false; }
                else
                {
                    var pp = p;
                    av.Cursor = Cursors.Hand;
                    av.ToolTip = OpTip(pp);
                    av.MouseLeftButtonDown += (_, e) => { e.Handled = true; Focus(); CellClick(pp.x, pp.y); };
                    av.MouseRightButtonUp += (_, e) => { e.Handled = true; ShowMenu(av, pp, true); };
                }
                cv.Children.Add(av);
                _avatars.Add((av, p.x, p.y));
            }

            if (align)
            {
                var frame = new Polygon { Stroke = UI.B("#FFB02E"), StrokeThickness = 1.6, IsHitTestVisible = false };
                cv.Children.Add(frame);
                _frame = frame;
                for (int i = 0; i < 4; i++)
                {
                    int k = i;
                    var hd = new Ellipse
                    {
                        Width = 16, Height = 16, Fill = UI.B("#EEFFB02E"), Stroke = Brushes.Black, StrokeThickness = 1.2,
                        Cursor = Cursors.SizeAll, ToolTip = new[] { "左上角", "右上角", "右下角", "左下角" }[i],
                    };
                    Point start = default;
                    double q0x = 0, q0y = 0;
                    hd.MouseLeftButtonDown += (_, e) =>
                    {
                        e.Handled = true;
                        _dragCorner = k;
                        start = e.GetPosition(cv);
                        var c0 = _site.地形.Corners();
                        q0x = c0[k * 2]; q0y = c0[k * 2 + 1];
                        hd.CaptureMouse();
                    };
                    hd.MouseMove += (_, e) =>
                    {
                        if (_dragCorner != k) return;
                        // 按鼠标移动的距离挪角点（圆点可能画在图片边上，不直接跳到鼠标位置）
                        var pos = e.GetPosition(cv);
                        var qq = _site.地形.Corners();
                        qq[k * 2] = Math.Clamp(q0x + (pos.X - start.X) / _bw, -0.1, 1.1);
                        qq[k * 2 + 1] = Math.Clamp(q0y + (pos.Y - start.Y) / _bh, -0.1, 1.1);
                        _site.地形.角点 = qq.ToList();
                        UpdateGeometry();
                    };
                    hd.MouseLeftButtonUp += (_, _) => { _dragCorner = -1; hd.ReleaseMouseCapture(); QueueSave(); };
                    cv.Children.Add(hd);
                    _handles.Add(hd);
                }
            }
            else _frame = null;
            UpdateGeometry();
            _board.Children.Add(host);
        }

        /// <summary>拖角点时就地更新所有格子、头像和圆点的位置。</summary>
        void UpdateGeometry()
        {
            var q = _site.地形.Corners();
            foreach (var (poly, x, y) in _cellPolys) poly.Points = CellPoints(q, x, y);
            foreach (var (el, x, y) in _avatars)
            {
                var (center, size) = CellCenter(q, x, y);
                el.Width = size; el.Height = size;
                Canvas.SetLeft(el, center.X - size / 2);
                Canvas.SetTop(el, center.Y - size / 2);
            }
            for (int i = 0; i < _handles.Count; i++)
            {
                // 角点可能在图片外（格子区域伸出截图边缘），圆点画在边上，保证抓得到
                Canvas.SetLeft(_handles[i], Math.Clamp(q[i * 2] * _bw, 8, _bw - 8) - 8);
                Canvas.SetTop(_handles[i], Math.Clamp(q[i * 2 + 1] * _bh, 8, _bh - 8) - 8);
            }
            if (_frame != null) _frame.Points = new PointCollection { P(q, 0, 0), P(q, 1, 0), P(q, 1, 1), P(q, 0, 1) };
        }

        UIElement OpTile(Placed p, double cell, int order, char tile)
        {
            GameData.AnyBy.TryGetValue(p.干员, out var o);
            bool wrong = o != null && tile == 'H' && o.近战;
            var t = Avatar(p, cell - 2, order, p == _hold, wrong);
            t.Margin = new Thickness(1);
            return t;
        }

        /// <summary>
        /// 干员头像方块：等阶色边框、左上角部署顺序、右上角精锐★。
        /// 鼠标移上去时盖一层半透明黑底并显示名字。
        /// </summary>
        static FrameworkElement Avatar(Placed p, double size, int order, bool held, bool wrong)
        {
            GameData.AnyBy.TryGetValue(p.干员, out var o);
            int tier = p.等阶 > 0 ? p.等阶 : o?.等阶 ?? 0;
            var g = new Grid { Width = size, Height = size };
            var img = UI.Picture(o?.图片, size);
            var face = new Border
            {
                CornerRadius = new CornerRadius(4),
                Background = UI.B("#E0101418"),
                BorderBrush = UI.B(wrong ? "#F05A4A" : held ? Theme.Acc : Tier.Color(tier)),
                BorderThickness = new Thickness(wrong || held ? 2.2 : 1.5),
                ClipToBounds = true,
            };
            var inner = new Grid();
            if (img is Image im) { im.Width = double.NaN; im.Stretch = Stretch.UniformToFill; inner.Children.Add(im); }
            else inner.Children.Add(new TextBlock
            {
                Text = p.干员.Length <= 2 ? p.干员 : p.干员[..2], FontSize = Math.Max(9, size * 0.3), FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.White,
            });
            if (held) inner.Children.Add(new Border { Background = UI.B(Theme.A(0x55)) });
            face.Child = inner;
            g.Children.Add(face);

            if (order > 0)
                g.Children.Add(new Border
                {
                    Background = UI.B("#C0000000"), CornerRadius = new CornerRadius(0, 0, 4, 0),
                    HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                    Padding = new Thickness(3, 0, 3, 0), Margin = new Thickness(1.5),
                    Child = new TextBlock { Text = order.ToString(), FontSize = Math.Max(8, size * 0.26), Foreground = UI.B("#FFB02E"), FontWeight = FontWeights.Bold },
                });
            if (p.精锐)
                g.Children.Add(new TextBlock
                {
                    Text = "★", FontSize = Math.Max(9, size * 0.3), Foreground = UI.B("#F6C60F"),
                    HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0, 2, 0),
                    Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 3, ShadowDepth = 0, Opacity = 1 },
                });

            if (o != null && o.支援)
                g.Children.Add(new Border
                {
                    Background = UI.B("#D0000000"), CornerRadius = new CornerRadius(4, 0, 4, 0),
                    HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
                    Padding = new Thickness(2, 0, 2, 0), Margin = new Thickness(1.5),
                    Child = new TextBlock { Text = "甄", FontSize = Math.Max(8, size * 0.22), Foreground = UI.B(Tier.Color(tier)), FontWeight = FontWeights.Bold },
                });

            // 悬停：半透明底 + 名字
            var hover = new Border
            {
                CornerRadius = new CornerRadius(4),
                Background = UI.B("#B0000000"),
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = false,
                Child = new TextBlock
                {
                    Text = p.干员, Foreground = Brushes.White, FontWeight = FontWeights.Bold,
                    FontSize = Math.Max(9, Math.Min(13, size * (p.干员.Length <= 2 ? 0.32 : 0.24))),
                    TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(1),
                },
            };
            g.Children.Add(hover);
            g.Background = Brushes.Transparent;
            g.MouseEnter += (_, _) => hover.Visibility = Visibility.Visible;
            g.MouseLeave += (_, _) => hover.Visibility = Visibility.Collapsed;
            return g;
        }

        static string OpTip(Placed p)
        {
            if (!GameData.AnyBy.TryGetValue(p.干员, out var o)) return p.干员;
            if (o.支援)
                return $"{Tier.Roman(p.等阶)} {o.名称}（甄选 · {o.类别}）{(p.精锐 ? "（精锐）" : "")}\n{o.职业}·{o.分支}　{o.位置}　阵营：{(o.阵营 == "" ? "无" : o.阵营)}\n盟约：{string.Join("、", o.盟约)}\n甄选干员没有特质\n\n右键：精锐 / 改甄选阶级 / 放回整备区 / 移除";
            return $"{o.等阶字} {o.名称}{(p.精锐 ? "（精锐）" : "")}\n{o.职业}·{o.分支}　{o.位置}\n盟约：{string.Join("、", o.盟约)}\n{o.特质类型}：{(p.精锐 && o.精锐特质.Length > 0 ? o.精锐特质 : o.特质)}\n\n右键：精锐 / 放回整备区 / 移除";
        }

        void Paint(int x, int y, char? c = null)
        {
            var ch = c ?? _brush;
            if (ch == '#') return;
            if (_site.地形.Get(x, y) == ch) return;
            _site.地形.Set(x, y, ch);
            Changed();
        }

        void CellClick(int x, int y)
        {
            if (_editTerrain) { if (_brush != '#') Paint(x, y); return; }
            var occ = _plan.场上.FirstOrDefault(p => p.x == x && p.y == y);
            if (_hold == null)
            {
                if (occ != null) { _hold = occ; _holdSrc = "board"; RenderPlan(); }
                return;
            }
            if (_hold == occ) { _hold = null; RenderPlan(); return; }
            GameData.AnyBy.TryGetValue(_hold.干员, out var op);
            char t = _site.地形.Get(x, y);
            if (t == 'X' || t == 'R' || t == 'B') { Flash("这一格不能部署"); return; }
            if (t == 'H' && op != null && op.近战) { Flash($"{op.名称} 是近战位，不能放在高台上"); return; }
            if (_holdSrc != "board" && occ == null && _plan.场上.Count >= _limit) { Flash($"场上已经 {_limit} 人了（部署上限）"); return; }
            if (occ != null)
            {
                if (_holdSrc == "board") { occ.x = _hold.x; occ.y = _hold.y; }
                else { _plan.场上.Remove(occ); occ.x = occ.y = -1; _plan.整备.Add(occ); }
            }
            if (_holdSrc == "bench") _plan.整备.Remove(_hold);
            _hold.x = x;
            _hold.y = y;
            if (!_plan.场上.Contains(_hold)) _plan.场上.Add(_hold);
            _hold = null;
            Changed();
        }

        void ShowMenu(FrameworkElement target, Placed p, bool onBoard)
        {
            var m = new ContextMenu();
            void Item(string text, Action a)
            {
                var mi = new MenuItem { Header = text };
                mi.Click += (_, _) => a();
                m.Items.Add(mi);
            }
            Item(p.精锐 ? "取消精锐 ★" : "设为精锐 ★（三合一后）", () => { p.精锐 = !p.精锐; Changed(); });
            if (onBoard)
                Item("放回整备区", () => { _plan.场上.Remove(p); p.x = p.y = -1; _plan.整备.Add(p); if (_hold == p) _hold = null; Changed(); });
            Item("移除", () => { _plan.场上.Remove(p); _plan.整备.Remove(p); if (_hold == p) _hold = null; Changed(); });
            if (GameData.AnyBy.TryGetValue(p.干员, out var mo) && mo.支援)
            {
                int other = p.等阶 == 5 ? 6 : 5;
                Item($"改放到 {Tier.Roman(other)} 阶甄选槽位", () =>
                {
                    if (!CanSelect(p.干员, other, ignoreSelf: true)) { Flash($"{Tier.Roman(other)} 阶的 2 个甄选槽位已经满了"); return; }
                    foreach (var q in _plan.场上.Concat(_plan.整备).Where(q => q.干员 == p.干员)) q.等阶 = other;
                    Changed();
                    RenderPicker();
                });
            }
            else Item("在资料页查看", () => (Window.GetWindow(this) as OverlayWindow)?.ShowRef("干员", p.干员));
            m.PlacementTarget = target;
            m.IsOpen = true;
        }

        void RenderBench()
        {
            _bench.Children.Clear();
            if (_plan.整备.Count == 0)
                _bench.Children.Add(UI.T("空", 0.8, "#66FFFFFF"));
            foreach (var p in _plan.整备)
            {
                var chip = Avatar(p, 38, 0, p == _hold, false);
                chip.Margin = new Thickness(0, 0, 4, 4);
                chip.Cursor = Cursors.Hand;
                chip.ToolTip = OpTip(p);
                var pp = p;
                chip.MouseLeftButtonDown += (_, e) =>
                {
                    e.Handled = true;
                    Focus();
                    if (_hold == pp) _hold = null; else { _hold = pp; _holdSrc = "bench"; }
                    RenderPlan();
                };
                chip.MouseRightButtonUp += (_, e) => { e.Handled = true; ShowMenu(chip, pp, false); };
                _bench.Children.Add(chip);
            }
        }

        void RenderBonds()
        {
            _bonds.Children.Clear();
            var list = BondCalc.Compute(_plan);
            if (list.Count == 0) _bonds.Children.Add(UI.T("场上还没有干员", 0.82, "#66FFFFFF"));
            foreach (var s in list)
            {
                string bg = s.Disabled ? "#40F05A4A" : s.State switch { "强化" => "#B0B08A1A", "激活" => "#9020906E", "失效" => "#40F05A4A", _ => "#26FFFFFF" };
                string txt = s.Bond.名称 + " " + (s.Next > 0 ? $"{s.Count}/{s.Next}" : $"{s.Count} 满");
                var chip = UI.Chip(txt, bg, s.State == "未激活" ? "#B0FFFFFF" : "#FFFFFF", 0.88, s.Bond.核心 ? "#7FC8FF" : null);
                ((TextBlock)chip.Child).FontWeight = s.Bond.核心 ? FontWeights.Bold : FontWeights.Normal;
                if (s.Disabled) ((TextBlock)chip.Child).TextDecorations = TextDecorations.Strikethrough;
                chip.ToolTip = BondTip(s);
                chip.Cursor = Cursors.Hand;
                var name = s.Bond.名称;
                chip.MouseLeftButtonUp += (_, _) => SetBondFilter(name);
                _bonds.Children.Add(chip);
            }
        }

        string BondTip(BondState s)
        {
            var b = s.Bond;
            var owned = new HashSet<string>(_plan.场上.Select(p => p.干员).Concat(b.整备区计入 ? _plan.整备.Select(p => p.干员) : Enumerable.Empty<string>()));
            var members = b.干员.Where(GameData.AnyBy.ContainsKey).Select(n => GameData.AnyBy[n]).OrderBy(o => o.等阶)
                .Select(o => (owned.Contains(o.名称) ? "✓" : "　") + o.等阶字 + " " + o.名称);
            string need = $"激活 {b.激活人数} 人" + (b.强化人数 is int t2 && t2 != b.激活人数 ? $"，强化 {t2} 人" : "") + (b.整备区计入 ? "，整备区也计入" : "");
            return $"【{b.名称}】{b.类型}盟约　{s.State}{s.Extra}{(s.Disabled ? "（本局禁用）" : "")}\n{need}\n\n{b.效果}\n\n成员：\n{string.Join("\n", members)}\n\n点一下：只看这个盟约的干员";
        }

        void RenderStatus()
        {
            var order = DeployOrder();
            var active = BondCalc.Compute(_plan).Count(s => !s.Disabled && (s.State == "激活" || s.State == "强化"));
            _status.Inlines.Clear();
            _status.Inlines.Add(new Run($"场上 {_plan.场上.Count}/{_limit}") { Foreground = UI.B(_plan.场上.Count > _limit ? "#F05A4A" : "#E8ECEF"), FontWeight = FontWeights.Bold });
            _status.Inlines.Add(new Run($"　整备 {_plan.整备.Count}　激活盟约 {active} 个"));
            var wrong = _plan.场上.Where(p => GameData.AnyBy.TryGetValue(p.干员, out var o) && o.近战 && _site.地形.Get(p.x, p.y) == 'H').Select(p => p.干员).ToList();
            if (wrong.Count > 0) _status.Inlines.Add(new Run($"\n近战放在了高台：{string.Join("、", wrong)}") { Foreground = UI.B("#F05A4A") });
            if (_hold != null && GameData.AnyBy.TryGetValue(_hold.干员, out var h))
                _status.Inlines.Add(new Run($"\n手上：{h.等阶字} {h.名称}（{h.位置}）→ 点格子放下，右键空格 / Esc 取消") { Foreground = UI.B(Theme.Acc) });
            else if (_editTerrain && _brush == '#')
                _status.Inlines.Add(new Run("\n正在对齐网格：拖动四个黄色圆点，对准地图上格子区域的四个角") { Foreground = UI.B("#FFB02E") });
            else if (_editTerrain)
                _status.Inlines.Add(new Run("\n正在编辑地形：左键涂，右键擦") { Foreground = UI.B(Theme.Acc) });
            else if (order.Count > 0)
                _status.Inlines.Add(new Run("\n部署顺序：" + string.Join(" → ", order.Select(p => p.干员))) { Foreground = UI.B("#9AA6AE"), FontSize = UI.F(0.82) });
        }

        void ShowStrategy()
        {
            var s = GameData.Strategies.FirstOrDefault(x => x.策略 == _plan.策略);
            _strategyText.Inlines.Clear();
            if (s == null) { _strategyText.Inlines.Add(new Run("开局选的策略记在这里，方便对照效果。") { Foreground = UI.B("#77FFFFFF") }); return; }
            _strategyText.Inlines.Add(new Run($"初始生命 {s.血量}　") { Foreground = UI.B("#FFB02E") });
            Markdown.Inline(_strategyText.Inlines, s.效果);
        }

        void RenderBondToggles()
        {
            _bondToggles.Children.Clear();
            foreach (var b in GameData.Bonds)
            {
                var name = b.名称;
                bool off = _plan.禁用盟约.Contains(name);
                var chip = UI.Chip(name, off ? "#20FFFFFF" : (b.核心 ? "#55337FB5" : "#4420906E"), off ? "#66FFFFFF" : "#FFFFFF", 0.82);
                if (off) ((TextBlock)chip.Child).TextDecorations = TextDecorations.Strikethrough;
                chip.Cursor = Cursors.Hand;
                chip.ToolTip = off ? "本局没有（点一下恢复）" : "点一下：标记为本局没有";
                chip.MouseLeftButtonUp += (_, _) =>
                {
                    if (!_plan.禁用盟约.Remove(name)) _plan.禁用盟约.Add(name);
                    RenderBondToggles();
                    RenderPicker();
                    Changed();
                };
                _bondToggles.Children.Add(chip);
            }
            _bondToggles.Children.Add(UI.T("规则：核心盟约没开，它的干员就不会出现；附加盟约全灰的干员也不会出现。", 0.78, "#9AA6AE"));
        }

        bool Available(Operator o)
        {
            var off = _plan.禁用盟约;
            var core = o.盟约.Where(b => GameData.BondBy.TryGetValue(b, out var bd) && bd.核心).ToList();
            var extra = o.盟约.Where(b => !core.Contains(b)).ToList();
            if (core.Any(off.Contains)) return false;
            if (extra.Count > 0 && core.Count == 0 && extra.All(off.Contains)) return false;
            return true;
        }

        void SetBondFilter(string bond)
        {
            _bondFilter = _bondFilter == bond ? "" : bond;
            RenderPicker();
        }

        void RenderPicker()
        {
            _bondFilterBar.Children.Clear();
            if (_bondFilter != "")
            {
                var chip = UI.Chip($"只看盟约：{_bondFilter}　✕", Theme.A(0x55));
                chip.Cursor = Cursors.Hand;
                chip.MouseLeftButtonUp += (_, _) => SetBondFilter(_bondFilter);
                _bondFilterBar.Children.Add(chip);
            }
            _picker.Children.Clear();
            var q = _search?.Text.Trim() ?? "";
            int hidden = 0;
            if (_supportMode)
            {
                _pickHint.Text = $"甄选槽位　Ⅴ阶 {SelCount(5)}/2　Ⅵ阶 {SelCount(6)}/2　·　点「Ⅴ」「Ⅵ」放进整备区，点一下整行直接拿起。按阵营给核心盟约，没有就给【协防干员】；甄选干员没有特质。";
                foreach (var o in GameData.Support)
                {
                    if (_supportCat != "" && o.类别 != _supportCat) continue;
                    if (_bondFilter != "" && !o.盟约.Contains(_bondFilter)) continue;
                    if (q.Length > 0 && !q.Split(' ', StringSplitOptions.RemoveEmptyEntries).All(w =>
                            o.名称.Contains(w, StringComparison.OrdinalIgnoreCase) || o.盟约.Any(b => b.Contains(w)) || o.职业.Contains(w) || o.分支.Contains(w) || o.位置.Contains(w) || o.阵营.Contains(w)))
                        continue;
                    if (!Available(o)) { hidden++; continue; }
                    _picker.Children.Add(PickerRow(o));
                }
                if (_picker.Children.Count == 0) _picker.Children.Add(UI.T("没有符合条件的干员", 0.85, "#9AA6AE"));
                if (hidden > 0) _picker.Children.Add(UI.T($"另有 {hidden} 人因为本局盟约没开被隐藏", 0.78, "#9AA6AE", false, new Thickness(0, 4, 0, 4)));
                return;
            }
            _pickHint.Text = "点一下拿起，再点格子放下；＋ 直接放进整备区。";
            foreach (var o in GameData.Ops.OrderBy(o => o.等阶).ThenBy(o => o.名称))
            {
                if (_tierFilter > 0 && o.等阶 != _tierFilter) continue;
                if (_bondFilter != "" && !o.盟约.Contains(_bondFilter)) continue;
                if (q.Length > 0 && !q.Split(' ', StringSplitOptions.RemoveEmptyEntries).All(w =>
                        o.名称.Contains(w, StringComparison.OrdinalIgnoreCase) || o.盟约.Any(b => b.Contains(w)) || o.职业.Contains(w) || o.分支.Contains(w) || o.位置.Contains(w) || o.特质.Contains(w)))
                    continue;
                if (!Available(o)) { hidden++; continue; }
                _picker.Children.Add(PickerRow(o));
            }
            if (_picker.Children.Count == 0) _picker.Children.Add(UI.T("没有符合条件的干员", 0.85, "#9AA6AE"));
            if (hidden > 0) _picker.Children.Add(UI.T($"另有 {hidden} 人因为本局盟约没开被隐藏", 0.78, "#9AA6AE", false, new Thickness(0, 4, 0, 4)));
        }

        UIElement PickerRow(Operator o)
        {
            var g = new Grid { Background = UI.B("#01000000"), Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 0, 1) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var face = UI.Img(o.图片, 30, o.名称, border: o.支援 ? "#F6C60F" : Tier.Color(o.等阶));
            face.Margin = new Thickness(0, 2, 7, 2);
            face.VerticalAlignment = VerticalAlignment.Center;
            g.Children.Add(face);
            var line = new TextBlock { FontSize = UI.F(0.92), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 2, 0, 2) };
            if (o.支援)
                line.Inlines.Add(new Run(o.类别 == "精英干员" ? "精英 " : "6★ ") { Foreground = UI.B(o.类别 == "精英干员" ? "#E88C1A" : "#F6C60F"), FontWeight = FontWeights.Bold, FontSize = UI.F(0.82) });
            else
                line.Inlines.Add(new Run(o.等阶字 + " ") { Foreground = UI.B(Tier.Color(o.等阶)), FontWeight = FontWeights.Bold });
            line.Inlines.Add(new Run(o.名称) { FontWeight = FontWeights.Bold });
            line.Inlines.Add(new Run($"  {o.职业}{(o.近战 ? "·近" : "·远")}  ") { Foreground = UI.B("#9AA6AE"), FontSize = UI.F(0.8) });
            foreach (var b in o.盟约)
            {
                bool core = GameData.BondBy.TryGetValue(b, out var bd) && bd.核心;
                line.Inlines.Add(new Run(b + " ") { Foreground = UI.B(core ? "#7FC8FF" : Theme.Acc), FontSize = UI.F(0.82) });
            }
            Grid.SetColumn(line, 1);
            g.Children.Add(line);
            if (o.支援)
            {
                var btns = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                foreach (var t in new[] { 5, 6 })
                {
                    int tt = t;
                    var b = UI.Btn(Tier.Roman(t), () =>
                    {
                        if (!CanSelect(o.名称, tt)) { Flash(SelectBlockReason(o.名称, tt)); return; }
                        _plan.整备.Add(new Placed { 干员 = o.名称, 等阶 = tt });
                        Changed();
                        RenderPicker();
                    }, $"放进整备区，占 {Tier.Roman(t)} 阶甄选槽位", 0.85);
                    b.Foreground = UI.B(Tier.Color(t));
                    b.Padding = new Thickness(6, 0, 6, 0);
                    b.Margin = new Thickness(2, 1, 0, 1);
                    b.IsEnabled = CanSelect(o.名称, tt);
                    btns.Children.Add(b);
                }
                Grid.SetColumn(btns, 2);
                g.Children.Add(btns);
                g.ToolTip = $"{o.名称}（{o.类别}）　{o.职业}·{o.分支}　{o.位置}\n阵营：{(o.阵营 == "" ? "无" : o.阵营)}　→　盟约：{string.Join("、", o.盟约)}\n甄选干员没有特质";
            }
            else
            {
                var add = UI.Btn("＋", () =>
                {
                    _plan.整备.Add(new Placed { 干员 = o.名称 });
                    Changed();
                }, "放进整备区", 0.85);
                add.Padding = new Thickness(6, 0, 6, 0);
                add.Margin = new Thickness(2, 1, 0, 1);
                Grid.SetColumn(add, 2);
                g.Children.Add(add);
                g.ToolTip = $"{o.等阶字} {o.名称}　{o.职业}·{o.分支}　{o.位置}\n盟约：{string.Join("、", o.盟约)}\n{o.特质类型}：{o.特质}\n精锐：{o.精锐特质}";
            }
            g.MouseEnter += (_, _) => g.Background = UI.B("#22FFFFFF");
            g.MouseLeave += (_, _) => g.Background = UI.B("#01000000");
            g.MouseLeftButtonDown += (_, e) =>
            {
                if (e.OriginalSource is DependencyObject d && FindParent<Button>(d) != null) return;
                Focus();
                _editTerrain = false;
                _editBtn.IsChecked = false;
                _palette.Visibility = Visibility.Collapsed;
                int tier = 0;
                if (o.支援)
                {
                    tier = ExistingTier(o.名称) ?? (CanSelect(o.名称, 6) ? 6 : CanSelect(o.名称, 5) ? 5 : 0);
                    if (tier == 0) { Flash("Ⅴ、Ⅵ阶的甄选槽位都满了"); return; }
                }
                _hold = new Placed { 干员 = o.名称, 等阶 = tier };
                _holdSrc = "new";
                RenderPlan();
                Flash(o.支援 ? $"拿起了 {o.名称}（{Tier.Roman(tier)} 阶甄选），点上面的格子放下" : $"拿起了 {o.名称}，点上面的格子放下");
            };
            return g;
        }

        // ---------- 甄选槽位：Ⅴ、Ⅵ阶各 2 个 ----------
        IEnumerable<Placed> AllPlaced() => _plan.场上.Concat(_plan.整备);

        static bool IsSupport(string name) => GameData.AnyBy.TryGetValue(name, out var o) && o.支援;

        /// <summary>这个阶级已经占了几个甄选槽位（同名多份只算一个）。</summary>
        int SelCount(int tier) => AllPlaced().Where(p => p.等阶 == tier && IsSupport(p.干员)).Select(p => p.干员).Distinct().Count();

        int? ExistingTier(string name) => AllPlaced().FirstOrDefault(p => p.干员 == name && p.等阶 > 0)?.等阶;

        bool CanSelect(string name, int tier, bool ignoreSelf = false)
        {
            var cur = ExistingTier(name);
            if (cur != null && !ignoreSelf) return cur == tier;          // 已经甄选过的，只能继续放在同一阶
            return AllPlaced().Where(p => p.等阶 == tier && IsSupport(p.干员) && p.干员 != name).Select(p => p.干员).Distinct().Count() < 2;
        }

        string SelectBlockReason(string name, int tier)
        {
            var cur = ExistingTier(name);
            if (cur != null && cur != tier) return $"{name} 已经放在 {Tier.Roman(cur.Value)} 阶甄选槽位了（右键可以改）";
            return $"{Tier.Roman(tier)} 阶的 2 个甄选槽位已经满了";
        }

        void SetSupportMode(bool on)
        {
            _supportMode = on;
            _tierBar.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
            _supportBar.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            RenderPicker();
        }

        static T FindParent<T>(DependencyObject d) where T : DependencyObject
        {
            while (d != null && d is not T) d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
            return d as T;
        }
    }
}
