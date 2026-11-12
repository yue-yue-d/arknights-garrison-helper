using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Threading;

namespace WeishuHelper
{
    // ======================= 资料 =======================
    public class RefView : DockPanel
    {
        static readonly string[] Cats = { "干员", "盟约", "策略", "装备", "机变", "领袖", "地图" };
        // 机变阶段：PRTS 的两大组 → 子分类（顺序即按钮顺序）
        static readonly (string group, string[] subs)[] PhaseGroups =
        {
            ("敌人轮选", new[] { "单只悬赏", "多轮悬赏", "特训", "飞行悬赏", "频次悬赏", "损伤悬赏", "持续悬赏", "隐匿悬赏", "折射悬赏", "特异悬赏" }),
            ("联合增益/减益", new[] { "盟誓", "驰援", "资源", "战斗效果" }),
        };
        const double Pic = 46;

        string _cat = "干员", _phaseGroup = "", _phaseSub = "";
        // 干员快速检索
        readonly HashSet<int> _opTiers = new();
        readonly HashSet<string> _opBonds = new();
        bool _bondAll, _quickOpen = true;
        // 装备等阶
        int _itemTier;
        readonly TextBox _search;
        readonly StackPanel _list = new();
        readonly ScrollViewer _scroll;
        readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(180) };
        readonly WrapPanel _catBar = new();
        readonly StackPanel _subBar = new() { Margin = new Thickness(0, 0, 0, 2) };

        public RefView()
        {
            foreach (var c in Cats)
            {
                var cc = c;
                _catBar.Children.Add(UI.Radio(c, "refcat", c == _cat, () => { _cat = cc; BuildSubBar(); Render(); }));
            }
            SetDock(_catBar, Dock.Top);
            Children.Add(_catBar);
            SetDock(_subBar, Dock.Top);
            Children.Add(_subBar);
            var sb = UI.SearchBox(out _search, "搜索：名字、盟约、效果里的字…");
            SetDock(sb, Dock.Top);
            Children.Add(sb);
            _scroll = UI.Scroll(_list);
            Children.Add(_scroll);
            _search.TextChanged += (_, _) => { _debounce.Stop(); _debounce.Start(); };
            _debounce.Tick += (_, _) => { _debounce.Stop(); Render(); };
            BuildSubBar();
            Render();
        }

        public void Show(string cat, string query)
        {
            _cat = cat;
            foreach (RadioButton r in _catBar.Children) r.IsChecked = (string)r.Content == cat;
            BuildSubBar();
            _search.Text = query;
            Render();
        }

        /// <summary>机变页的子分类按钮：第一行大组，第二行子类。</summary>
        void BuildSubBar()
        {
            _subBar.Children.Clear();
            if (_cat == "干员") { BuildOpFilter(); return; }
            if (_cat == "装备") { BuildItemFilter(); return; }
            if (_cat != "机变") return;
            int Count(string g, string s) => GameData.Phases.Count(p => (g == "" || p.分组 == g) && (s == "" || p.子类 == s));
            var groups = new WrapPanel();
            groups.Children.Add(UI.Radio($"全部 {Count("", "")}", "pgroup", _phaseGroup == "", () => { _phaseGroup = ""; _phaseSub = ""; BuildSubBar(); Render(); }, 0.85));
            foreach (var (g, _) in PhaseGroups)
            {
                var gg = g;
                groups.Children.Add(UI.Radio($"{g} {Count(g, "")}", "pgroup", _phaseGroup == g, () => { _phaseGroup = gg; _phaseSub = ""; BuildSubBar(); Render(); }, 0.85));
            }
            _subBar.Children.Add(groups);
            if (_phaseGroup == "") return;
            var subs = new WrapPanel { Margin = new Thickness(10, 0, 0, 0) };
            subs.Children.Add(UI.Radio("全部", "psub", _phaseSub == "", () => { _phaseSub = ""; Render(); }, 0.8));
            foreach (var s in PhaseGroups.First(x => x.group == _phaseGroup).subs)
            {
                int n = Count(_phaseGroup, s);
                if (n == 0) continue;
                var ss = s;
                subs.Children.Add(UI.Radio($"{s} {n}", "psub", _phaseSub == s, () => { _phaseSub = ss; Render(); }, 0.8));
            }
            // 数据里出现了上面没列到的子类（PRTS 更新后），也给按钮
            foreach (var s in GameData.Phases.Where(p => p.分组 == _phaseGroup).Select(p => p.子类).Distinct()
                         .Where(s => !PhaseGroups.First(x => x.group == _phaseGroup).subs.Contains(s)))
            {
                var ss = s;
                subs.Children.Add(UI.Radio(s, "psub", _phaseSub == s, () => { _phaseSub = ss; Render(); }, 0.8));
            }
            _subBar.Children.Add(subs);
        }

        bool Hit(params string[] fields)
        {
            var q = _search.Text.Trim();
            if (q.Length == 0) return true;
            return q.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .All(w => fields.Any(f => f != null && f.Contains(w, StringComparison.OrdinalIgnoreCase)));
        }

        public void Render()
        {
            _list.Children.Clear();
            int n = 0;
            void Add(UIElement e) { if (n++ < 200) _list.Children.Add(e); }
            switch (_cat)
            {
                case "干员":
                    foreach (var o in GameData.Ops.OrderBy(o => o.等阶).ThenBy(o => o.名称))
                        if (OpPass(o) && Hit(o.名称, string.Join(" ", o.盟约), o.职业, o.分支, o.位置, o.特质, o.精锐特质, o.等阶字 + "阶"))
                            Add(OpCard(o));
                    break;
                case "盟约":
                    foreach (var b in GameData.Bonds)
                        if (Hit(b.名称, b.类型, b.效果, string.Join(" ", b.干员)))
                            Add(BondCard(b));
                    break;
                case "策略":
                    foreach (var s in GameData.Strategies)
                        if (Hit(s.策略, s.发起人, s.效果, s.解锁条件))
                            Add(UI.ImageCard(UI.Img(s.图片, Pic, s.发起人),
                                UI.T($"{s.策略}", 1.05, Theme.Acc, true),
                                UI.T($"发起人 {s.发起人}　初始生命 {s.血量}", 0.85, "#9AA6AE"),
                                UI.Rich(s.效果, 0.92),
                                string.IsNullOrEmpty(s.解锁条件) ? null : UI.T("解锁：" + s.解锁条件, 0.82, "#9AA6AE")));
                    break;
                case "装备":
                    foreach (var i in GameData.Items.OrderBy(i => i.等阶))
                        if ((_itemTier == 0 || i.等阶 == _itemTier) && Hit(i.名称, i.效果, i.描述, i.类型))
                            Add(UI.ImageCard(UI.Img(i.图片, Pic, i.名称, border: Tier.Color(i.等阶 ?? 0)),
                                UI.T($"{Tier.Roman(i.等阶 ?? 0)}　{i.名称}", 1.0, Tier.Color(i.等阶 ?? 0), true),
                                UI.T($"{i.类型}　售价 {i.售价}", 0.82, "#9AA6AE"),
                                UI.Rich(i.效果, 0.92),
                                string.IsNullOrEmpty(i.描述) ? null : UI.T(i.描述, 0.8, "#9AA6AE")));
                    break;
                case "机变":
                    foreach (var p in GameData.Phases)
                        if ((_phaseGroup == "" || p.分组 == _phaseGroup) && (_phaseSub == "" || p.子类 == _phaseSub) && Hit(p.名称, p.说明, p.分组, p.子类, p.敌人))
                            Add(PhaseCard(p));
                    break;
                case "领袖":
                    foreach (var l in GameData.Leaders)
                        if (Hit(l.名称))
                            Add(LeaderCard(l));
                    Add(UI.T("血量为 PRTS 数据。「隐秘核心」是达成条件后在最终攻势后出现的强化领袖。", 0.8, "#9AA6AE"));
                    break;
                case "地图":
                    Add(UI.Card(UI.T("共通", 1.0, Theme.Acc, true), UI.T(string.Join("\n", GameData.Maps.共通), 0.9)));
                    foreach (var m in GameData.Maps.战场)
                        if (Hit(m.编号, string.Join(" ", m.说明), string.Join(" ", m.装置), string.Join(" ", m.地形)))
                            Add(MapCard(m));
                    break;
            }
            if (n == 0) _list.Children.Add(UI.T("没有找到。", 0.9, "#9AA6AE"));
            if (n > 200) _list.Children.Add(UI.T($"还有 {n - 200} 条，换个关键词缩小范围。", 0.85, "#9AA6AE"));
            _scroll.ScrollToTop();
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => _scroll.ScrollToTop()));   // 新内容排版后再回顶部一次
        }

        UIElement OpCard(Operator o)
        {
            var bonds = new WrapPanel { Margin = new Thickness(0, 3, 0, 2) };
            foreach (var b in o.盟约)
            {
                GameData.BondBy.TryGetValue(b, out var bd);
                var chip = new Border
                {
                    Background = UI.B(bd?.核心 == true ? "#55337FB5" : "#33FFFFFF"),
                    CornerRadius = new CornerRadius(3), Padding = new Thickness(2, 1, 6, 1), Margin = new Thickness(0, 0, 3, 3),
                    Cursor = Cursors.Hand, ToolTip = bd?.效果,
                };
                var sp = new StackPanel { Orientation = Orientation.Horizontal };
                var icon = UI.Img(bd?.图片, UI.F(1.25), b, disc: true);
                icon.Margin = new Thickness(0, 0, 3, 0);
                icon.VerticalAlignment = VerticalAlignment.Center;
                sp.Children.Add(icon);
                sp.Children.Add(new TextBlock { Text = b, FontSize = UI.F(0.85), VerticalAlignment = VerticalAlignment.Center });
                chip.Child = sp;
                var name = b;
                chip.MouseLeftButtonUp += (_, _) => Show("盟约", name);
                bonds.Children.Add(chip);
            }
            var title = new TextBlock { FontSize = UI.F(1.02), FontWeight = FontWeights.Bold };
            title.Inlines.Add(new Run(o.等阶字 + "  ") { Foreground = UI.B(Tier.Color(o.等阶)) });
            title.Inlines.Add(new Run(o.名称));
            return UI.ImageCard(UI.Img(o.图片, Pic + 6, o.名称, border: Tier.Color(o.等阶)),
                title,
                UI.T($"{o.职业}·{o.分支}　{o.位置}" + (string.IsNullOrEmpty(o.原型) ? "" : $"　原型补位：{o.原型}"), 0.8, "#9AA6AE"),
                bonds,
                UI.Rich($"**{o.特质类型}**　{o.特质}", 0.88),
                string.IsNullOrEmpty(o.精锐特质) ? null : UI.Rich($"**精锐**　{o.精锐特质}", 0.88, "#F6C60F"));
        }

        static UIElement BondCard(Bond b)
        {
            var members = new WrapPanel { Margin = new Thickness(0, 5, 0, 0) };
            foreach (var o in b.干员.Select(n => GameData.OpBy.TryGetValue(n, out var o) ? o : null).Where(o => o != null).OrderBy(o => o.等阶))
            {
                var img = UI.Img(o.图片, 32, o.名称, border: Tier.Color(o.等阶));
                img.Margin = new Thickness(0, 0, 3, 3);
                img.ToolTip = $"{o.等阶字} {o.名称}　{o.职业}·{o.位置}\n盟约：{string.Join("、", o.盟约)}";
                members.Children.Add(img);
            }
            string need = $"激活 {b.激活人数} 人" + (b.强化人数 is int s2 && s2 != b.激活人数 ? $"　强化 {s2} 人" : "") + (b.整备区计入 ? "　整备区也计入" : "");
            return UI.ImageCard(UI.Img(b.图片, Pic, b.名称, disc: true),
                UI.T($"{b.名称}", 1.05, b.核心 ? "#7FC8FF" : Theme.Acc, true),
                UI.T($"{b.类型}盟约　{need}", 0.82, "#9AA6AE"),
                UI.Rich(b.效果, 0.9),
                b.名称 == "绝技" ? UI.T("（不需要特定干员：所有精锐状态的干员都算）", 0.82, "#9AA6AE") : members);
        }

        static UIElement LeaderCard(Leader l)
        {
            var solo = UI.T(string.Join("\n", l.个人模拟血量.Select(kv => $"{kv.Key}：{kv.Value}")), 0.82, "#C9D1D6");
            solo.Visibility = Visibility.Collapsed;
            UIElement toggle = null;
            if (l.个人模拟血量.Count > 0)
            {
                var t = UI.T("个人模拟（极速 / 规程等级1～20）血量 ▾", 0.8, Theme.Acc, false, new Thickness(0, 3, 0, 0));
                t.Cursor = Cursors.Hand;
                t.MouseLeftButtonUp += (_, _) =>
                {
                    bool open = solo.Visibility != Visibility.Visible;
                    solo.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
                    t.Text = "个人模拟（极速 / 规程等级1～20）血量 " + (open ? "▴" : "▾");
                };
                toggle = t;
            }
            return UI.ImageCard(UI.Img(l.图片, Pic + 8, l.名称, border: "#F05A4A"),
                UI.T(l.名称, 1.02, "#F05A4A", true),
                UI.T("联合模拟", 0.78, "#9AA6AE"),
                UI.T(string.Join("\n", l.血量.Select(kv => $"{kv.Key}：{kv.Value}")), 0.88),
                toggle, solo);
        }

        static UIElement PhaseCard(Phase p)
        {
            bool disc = p.子类 == "驰援";
            return UI.ImageCard(UI.Img(p.图片, Pic, p.名称, disc: disc),
                UI.T(p.名称, 0.98, Theme.Acc, true),
                UI.T($"{p.分组} · {p.子类}" + (string.IsNullOrEmpty(p.敌人) ? "" : $"　敌人：{p.敌人}"), 0.8, "#9AA6AE"),
                UI.Rich(p.说明, 0.9));
        }

        static UIElement MapCard(MapInfo m)
        {
            // 三个场地的截图只放中间那张（场地2），鼠标停上去看大图
            FrameworkElement pic = null;
            if (m.图片.Count > 0)
            {
                int k = m.图片.Count >= 3 ? 1 : 0;
                pic = UI.Picture(m.图片[k], 330);
                if (pic != null)
                {
                    pic.HorizontalAlignment = HorizontalAlignment.Left;
                    pic.Margin = new Thickness(0, 5, 0, 0);
                    pic.ToolTip = new ToolTip { Content = UI.Picture(m.图片[k], 600), MaxWidth = 640 };
                    ToolTipService.SetInitialShowDelay(pic, 200);
                }
            }
            return UI.Card(UI.T("战场 " + m.编号, 1.02, Theme.Acc, true),
                UI.T(string.Join("\n", m.说明.Concat(m.装置.Select(d => "专属装置：" + d)).Concat(m.地形.Select(t => "特殊地形：" + t))), 0.88),
                pic);
        }

        // ---------- 干员快速检索 ----------
        bool OpPass(Operator o)
        {
            if (_opTiers.Count > 0 && !_opTiers.Contains(o.等阶)) return false;
            if (_opBonds.Count > 0)
                return _bondAll ? _opBonds.All(o.盟约.Contains) : _opBonds.Any(o.盟约.Contains);
            return true;
        }

        void BuildOpFilter()
        {
            int active = _opTiers.Count + _opBonds.Count;
            var head = new WrapPanel();
            var toggle = UI.Toggle((_quickOpen ? "快速检索 ▴" : "快速检索 ▾") + (active > 0 ? $"（已选 {active}）" : ""), _quickOpen,
                on => { _quickOpen = on; BuildSubBar(); }, 0.85);
            head.Children.Add(toggle);
            if (active > 0)
                head.Children.Add(UI.Btn("清除", () => { _opTiers.Clear(); _opBonds.Clear(); BuildSubBar(); Render(); }, "清除所有检索条件", 0.82));
            _subBar.Children.Add(head);
            if (!_quickOpen) return;

            var box = new StackPanel { Margin = new Thickness(0, 0, 0, 2) };
            // 阶级
            var tiers = new WrapPanel();
            tiers.Children.Add(UI.T("阶级", 0.8, "#9AA6AE", false, new Thickness(0, 4, 6, 0)));
            for (int t = 1; t <= 6; t++)
            {
                int k = t;
                var b = UI.Toggle(Tier.Roman(t), _opTiers.Contains(t), on => { if (on) _opTiers.Add(k); else _opTiers.Remove(k); BuildSubBar(); Render(); }, 0.85);
                b.Foreground = UI.B(Tier.Color(t));
                b.MinWidth = 30;
                tiers.Children.Add(b);
            }
            box.Children.Add(tiers);
            // 盟约（核心一行、附加一行）
            foreach (var core in new[] { true, false })
            {
                var row = new WrapPanel();
                row.Children.Add(UI.T(core ? "核心" : "附加", 0.8, core ? "#7FC8FF" : Theme.Acc, false, new Thickness(0, 4, 6, 0)));
                foreach (var bd in GameData.Bonds.Where(b => b.核心 == core && GameData.Ops.Any(o => o.盟约.Contains(b.名称))))
                {
                    var name = bd.名称;
                    var sp = new StackPanel { Orientation = Orientation.Horizontal };
                    var icon = UI.Img(bd.图片, UI.F(1.15), name, disc: true);
                    icon.Margin = new Thickness(0, 0, 3, 0);
                    icon.VerticalAlignment = VerticalAlignment.Center;
                    sp.Children.Add(icon);
                    sp.Children.Add(new TextBlock { Text = name, FontSize = UI.F(0.82), VerticalAlignment = VerticalAlignment.Center });
                    var tb = new System.Windows.Controls.Primitives.ToggleButton { Content = sp, IsChecked = _opBonds.Contains(name), Padding = new Thickness(3, 1, 6, 1), ToolTip = bd.效果 };
                    tb.Checked += (_, _) => { _opBonds.Add(name); BuildSubBar(); Render(); };
                    tb.Unchecked += (_, _) => { _opBonds.Remove(name); BuildSubBar(); Render(); };
                    row.Children.Add(tb);
                }
                box.Children.Add(row);
            }
            if (_opBonds.Count > 1)
            {
                var mode = new WrapPanel();
                mode.Children.Add(UI.T("多个盟约：", 0.8, "#9AA6AE", false, new Thickness(0, 4, 6, 0)));
                mode.Children.Add(UI.Radio("有其中任一个", "bondmode", !_bondAll, () => { _bondAll = false; Render(); }, 0.8));
                mode.Children.Add(UI.Radio("同时都有", "bondmode", _bondAll, () => { _bondAll = true; Render(); }, 0.8));
                box.Children.Add(mode);
            }
            _subBar.Children.Add(new Border
            {
                Background = UI.B("#14FFFFFF"), CornerRadius = new CornerRadius(6), Padding = new Thickness(6, 4, 2, 0),
                Margin = new Thickness(0, 0, 0, 4), Child = box,
            });
        }

        void BuildItemFilter()
        {
            var row = new WrapPanel();
            row.Children.Add(UI.Radio($"全部 {GameData.Items.Count}", "itier", _itemTier == 0, () => { _itemTier = 0; Render(); }, 0.85));
            for (int t = 1; t <= 6; t++)
            {
                int k = t, n = GameData.Items.Count(i => i.等阶 == t);
                if (n == 0) continue;
                var r = UI.Radio($"{Tier.Roman(t)} {n}", "itier", _itemTier == t, () => { _itemTier = k; Render(); }, 0.85);
                r.Foreground = UI.B(Tier.Color(t));
                row.Children.Add(r);
            }
            _subBar.Children.Add(row);
        }
    }
}
