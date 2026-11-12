using System;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using WF = System.Windows.Forms;
using System.Windows.Threading;

namespace WeishuHelper
{
    public partial class OverlayWindow : Window
    {
        IntPtr _hwnd, _game;
        readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(200) };
        int _tick;
        readonly DispatcherTimer _hotTimer = new() { Interval = TimeSpan.FromMilliseconds(40) };
        bool? _transparentNow;
        bool _userHidden, _through, _dragging, _shown = true;
        bool _ballPeek, _suppressSettings;
        readonly DispatcherTimer _ballLeave = new() { Interval = TimeSpan.FromMilliseconds(450) };
        const double BallPad = 6;   // 悬浮球四周留给光晕的边距
        Native.RECT _last;
        GuideView _guide;
        PlannerView _plan;
        RefView _ref;
        public string HotkeyError = "";

        static Settings S => App.S;
        /// <summary>攻略页暂时下线；以后恢复时改成 true，并把 XAML 里 TabGuide 的 Visibility 去掉。</summary>
        const bool GuideEnabled = false;

        public OverlayWindow()
        {
            InitializeComponent();
            Theme.Load(S);
            Theme.ApplyResources();
            Build();
            ApplyLook();
            SourceInitialized += OnSourceInitialized;
            _timer.Tick += (_, _) => Tick();
            _hotTimer.Tick += (_, _) => HotZoneTick();
            _ballLeave.Tick += (_, _) =>
            {
                _ballLeave.Stop();
                if (Ball.IsMouseOver || _dragging) return;
                _ballPeek = false;
                _last = default;
                Place(false);
            };
            Closing += (_, e) => { e.Cancel = true; ToggleUserHidden(); };
        }

        // ---------- 页面 ----------
        void Build()
        {
            FontSize = S.字号;
            _guide?.Dispose();
            _guide = GuideEnabled ? new GuideView() : null;
            _plan = new PlannerView();
            _plan.MapChanged += () => { _guide?.Refresh(); UpdateTitle(); };
            _ref = new RefView();
            var tab = S.页签;
            if (tab == "攻略" && !GuideEnabled) tab = S.页签 = "布阵";
            TabGuide.IsChecked = false; TabPlan.IsChecked = false; TabRef.IsChecked = false;
            (tab == "布阵" ? TabPlan : tab == "资料" ? TabRef : TabGuide).IsChecked = true;
            ShowTab();
            UpdateTitle();
        }

        /// <summary>字号、数据变了以后整个重建。</summary>
        public void Rebuild()
        {
            _plan?.SaveNow();
            BtnSettings.IsChecked = false;
            Build();
        }

        void Tab_Checked(object sender, RoutedEventArgs e)
        {
            if (_plan == null) return;
            BtnSettings.IsChecked = false;
            S.页签 = TabPlan.IsChecked == true ? "布阵" : TabRef.IsChecked == true ? "资料" : "攻略";
            ShowTab();
        }

        void ShowTab()
        {
            Body.Content = S.页签 switch { "资料" => _ref, "攻略" when _guide != null => _guide, _ => _plan };
        }

        void Settings_Click(object sender, RoutedEventArgs e)
        {
            if (_suppressSettings) return;
            if (BtnSettings.IsChecked == true) Body.Content = new SettingsView(this);
            else { ShowTab(); S.Save(); }
        }

        public void ShowRef(string cat, string query)
        {
            TabRef.IsChecked = true;
            _ref.Show(cat, query);
        }

        void CycleTab()
        {
            if (TabPlan.IsChecked == true) TabRef.IsChecked = true;
            else if (TabRef.IsChecked == true && GuideEnabled) TabGuide.IsChecked = true;
            else TabPlan.IsChecked = true;
        }

        void UpdateTitle() => TitleText.Text = $"卫戍协议·规程　{S.地图}";

        /// <summary>改了配色 / 悬浮球以后调用：刷新颜色并重建页面，设置页保持打开。</summary>
        public void RefreshTheme()
        {
            Theme.Load(S);
            Theme.ApplyResources();
            ApplyLook();
            _plan?.SaveNow();
            bool settingsOpen = BtnSettings.IsChecked == true;
            double offset = (Body.Content as System.Windows.Controls.ScrollViewer)?.VerticalOffset ?? 0;
            _suppressSettings = true;
            Build();
            if (settingsOpen)
            {
                BtnSettings.IsChecked = true;
                var sv = new SettingsView(this);
                Body.Content = sv;
                sv.Loaded += (_, _) => sv.ScrollToVerticalOffset(offset);
            }
            _suppressSettings = false;
            S.Save();
        }

        public void ApplyLook()
        {
            Panel.Background = Theme.PanelBrush(S.不透明度);
            Header.Background = Theme.HeaderBrush();
            ApplyBall();
            bool c = S.收起;
            Panel.Visibility = c ? Visibility.Collapsed : Visibility.Visible;
            Grip.Visibility = c ? Visibility.Collapsed : Visibility.Visible;
            Ball.Visibility = c ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>悬浮球：图片 + 主题色光圈。</summary>
        void ApplyBall()
        {
            double size = Math.Clamp(S.悬浮球大小, 40, 96);
            Ball.Width = Ball.Height = size;
            Ball.Margin = new Thickness(BallPad);
            var ballBrush = new SolidColorBrush(Theme.Ball);
            BallRing.Stroke = ballBrush;
            BallBack.Fill = new RadialGradientBrush(Theme.Mix(Theme.Ball, Colors.Black, 0.45), Theme.Mix(Theme.Panel, Colors.Black, 0.35));
            BallBack.Effect = new DropShadowEffect { Color = Theme.Ball, BlurRadius = 12, ShadowDepth = 0, Opacity = 0.9 };
            // 图标类（奖杯、卫戍节点、盟约图标）留边居中；头像、自定义图片铺满
            bool icon = S.悬浮球图片.StartsWith("图片/通用/") || S.悬浮球图片.StartsWith("图片/盟约/");
            var bmp = UI.Bitmap(S.悬浮球图片, (int)(size * 3)) ?? UI.Bitmap("图片/通用/奖杯8.png", (int)(size * 3));
            BallFace.Margin = new Thickness(icon ? size * 0.13 : 2.5);
            BallFace.Fill = bmp == null ? Brushes.Transparent
                : new ImageBrush(bmp) { Stretch = icon ? Stretch.Uniform : Stretch.UniformToFill };
            // 标题栏左边的小图标用同一张图
            TitleIcon.Fill = bmp == null ? Brushes.Transparent
                : new ImageBrush(bmp) { Stretch = icon ? Stretch.Uniform : Stretch.UniformToFill, Viewbox = icon ? new Rect(-0.12, -0.12, 1.24, 1.24) : new Rect(0, 0, 1, 1) };
        }

        double BallOuter => Math.Clamp(S.悬浮球大小, 40, 96) + BallPad * 2;

        void FadeIn(UIElement e, double ms = 160)
            => e.BeginAnimation(OpacityProperty, new DoubleAnimation(0, e.Opacity, TimeSpan.FromMilliseconds(ms)) { EasingFunction = new QuadraticEase(), FillBehavior = FillBehavior.Stop });

        public void SaveAll()
        {
            _plan?.SaveNow();
            S.Save();
        }

        // ---------- 窗口、热键 ----------
        void OnSourceInitialized(object sender, EventArgs e)
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            Native.SetToolWindow(_hwnd);
            HwndSource.FromHwnd(_hwnd).AddHook(WndProc);
            uint mod = Native.MOD_CONTROL | Native.MOD_SHIFT | Native.MOD_NOREPEAT;
            var failed = new System.Collections.Generic.List<string>();
            for (int i = 0; i < 3; i++)
                if (!Native.RegisterHotKey(_hwnd, i + 1, mod, (uint)(0x70 + i)))   // F1 F2 F3
                    failed.Add($"Ctrl+Shift+F{i + 1}");
            if (failed.Count > 0)
            {
                HotkeyError = $"快捷键 {string.Join("、", failed)} 被别的程序占用了，只能用托盘图标操作。";
                App.Log(HotkeyError);
            }
            _timer.Start();
            Tick();
        }

        IntPtr WndProc(IntPtr hwnd, int msg, IntPtr w, IntPtr l, ref bool handled)
        {
            if (msg == Native.WM_HOTKEY)
            {
                switch (w.ToInt32())
                {
                    case 1: ToggleUserHidden(); break;
                    case 2: ToggleClickThrough(); break;
                    case 3: if (_userHidden) ToggleUserHidden(); CycleTab(); break;
                }
                handled = true;
            }
            return IntPtr.Zero;
        }

        public void ToggleUserHidden()
        {
            _userHidden = !_userHidden;
            Tick();
        }

        public void ToggleClickThrough()
        {
            _through = !_through;
            BtnThrough.IsChecked = _through;
            Panel.Opacity = _through ? 0.75 : 1;
            _transparentNow = null;
            if (_through) { _hotTimer.Start(); HotZoneTick(); }
            else { _hotTimer.Stop(); Native.SetClickThrough(_hwnd, false); }
            Tick();
        }

        /// <summary>
        /// 穿透时整个窗口对鼠标透明，只有「穿透」按钮（收起时是小球）例外：
        /// 鼠标移到按钮上时临时取消穿透，这样再点一次按钮就能恢复。
        /// </summary>
        void HotZoneTick()
        {
            if (!_through || _hwnd == IntPtr.Zero) return;
            FrameworkElement zone = S.收起 ? Ball : BtnThrough;
            bool inside = false;
            if (zone.IsVisible && Native.GetCursorPos(out var pt))
            {
                var tl = zone.PointToScreen(new Point(0, 0));
                var br = zone.PointToScreen(new Point(zone.ActualWidth, zone.ActualHeight));
                inside = pt.X >= tl.X - 2 && pt.X <= br.X + 2 && pt.Y >= tl.Y - 2 && pt.Y <= br.Y + 2;
            }
            bool transparent = !inside;
            if (_transparentNow == transparent) return;
            _transparentNow = transparent;
            Native.SetClickThrough(_hwnd, transparent);
        }

        void Through_Click(object sender, RoutedEventArgs e) => ToggleClickThrough();
        void Hide_Click(object sender, RoutedEventArgs e) => ToggleUserHidden();
        void Collapse_Click(object sender, RoutedEventArgs e)
        {
            if (S.收起X < 0 || S.收起Y < 0)
            {
                Native.GetWindowRect(_hwnd, out var r);   // 第一次收起：悬浮球放在悬浮窗原来的左上角
                S.收起X = r.L; S.收起Y = r.T; S.收起贴边 = "";
            }
            S.收起 = true; ApplyLook(); Relayout(); S.Save();
            FadeIn(Ball);
        }

        void Expand()
        {
            S.收起 = false; _ballPeek = false;
            ApplyLook(); Relayout(); S.Save();
            FadeIn(Panel, 180);
        }

        void Ball_MouseEnter(object sender, MouseEventArgs e)
        {
            _ballLeave.Stop();
            var grow = new DoubleAnimation(1.07, TimeSpan.FromMilliseconds(120));
            BallScale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
            BallScale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
            if (!_ballPeek && S.贴边隐藏 && S.收起贴边 != "")
            {
                _ballPeek = true;   // 贴边半隐藏时，鼠标移上去整个滑出来
                _last = default;
                Place(false);
            }
        }

        void Ball_MouseLeave(object sender, MouseEventArgs e)
        {
            var back = new DoubleAnimation(1, TimeSpan.FromMilliseconds(150));
            BallScale.BeginAnimation(ScaleTransform.ScaleXProperty, back);
            BallScale.BeginAnimation(ScaleTransform.ScaleYProperty, back);
            if (_ballPeek) { _ballLeave.Stop(); _ballLeave.Start(); }
        }

        /// <summary>拖完小球：离屏幕边缘很近就贴过去。</summary>
        void SnapBall(Native.RECT r)
        {
            var (sx, _) = Scale();
            int w = r.W, h = r.H;
            var wa = WF.Screen.FromPoint(new System.Drawing.Point(r.L + w / 2, r.T + h / 2)).WorkingArea;
            int th = (int)(28 * sx);
            int x = r.L, y = r.T;
            string edge = "";
            if (S.贴边隐藏)
            {
                if (x <= wa.Left + th) { edge = "左"; x = wa.Left; }
                else if (x + w >= wa.Right - th) { edge = "右"; x = wa.Right - w; }
                else if (y <= wa.Top + th) { edge = "上"; y = wa.Top; }
                else if (y + h >= wa.Bottom - th) { edge = "下"; y = wa.Bottom - h; }
            }
            S.收起X = x; S.收起Y = y; S.收起贴边 = edge;
            _ballPeek = edge != "" && Ball.IsMouseOver;
            _last = default;
            Place(false);
            S.Save();
        }

        /// <summary>小球：拖动 = 移动位置；几乎没动 = 单击展开。</summary>
        void Ball_MouseDown(object sender, MouseButtonEventArgs e)
        {
            Native.GetWindowRect(_hwnd, out var before);
            _dragging = true;
            try { DragMove(); } catch { }
            _dragging = false;
            Native.GetWindowRect(_hwnd, out var after);
            if (Math.Abs(after.L - before.L) + Math.Abs(after.T - before.T) < 6) { Expand(); return; }
            SnapBall(after);
        }

        /// <summary>每 0.2 秒：找游戏窗口、决定显示还是隐藏、贴过去。</summary>
        void Tick()
        {
            if (_hwnd == IntPtr.Zero) return;
            if (_tick++ % 10 == 0 || (_game != IntPtr.Zero && !Native.IsWindow(_game)))
                _game = Native.FindGame();
            bool game = _game != IntPtr.Zero && Native.IsWindow(_game);
            bool minimized = game && Native.IsIconic(_game);
            var fg = Native.GetForegroundWindow();
            bool fgOk = !game || fg == _game || Native.IsOwnWindow(fg);
            bool show = !_userHidden && !minimized && (!S.仅游戏前台显示 || fgOk);

            if (show != _shown)
            {
                _shown = show;
                if (show) { Show(); _last = default; FadeIn(this, 140); }
                else Hide();
            }
            if (!show) return;

            Status.Text = (_through ? "鼠标穿透中（再点「穿透」恢复）　" : "") +
                          (game ? (S.停靠 == "自由" ? "已找到游戏 · 自由摆放" : "已贴靠游戏窗口") : "没开游戏 · 可以先规划");
            if (!_dragging) Place(game);
            if (_tick % 5 == 0)
                Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }

        public void Relayout()
        {
            _last = default;
            bool game = _game != IntPtr.Zero && Native.IsWindow(_game);
            Place(game);
            S.Save();
        }

        (double sx, double sy) Scale()
        {
            var d = VisualTreeHelper.GetDpi(this);
            return (d.DpiScaleX, d.DpiScaleY);
        }

        void Place(bool game)
        {
            var (sx, sy) = Scale();
            int x, y, h, w;
            if (S.收起 && S.收起X >= 0 && S.收起Y >= 0)
            {
                // 收起后是自由移动的悬浮球，不跟着游戏窗口走
                x = S.收起X; y = S.收起Y;
                w = (int)Math.Round(BallOuter * sx); h = (int)Math.Round(BallOuter * sy);
                string edge = S.贴边隐藏 ? S.收起贴边 : "";
                bool hidden = edge != "" && !_ballPeek && !_dragging;
                if (edge != "")
                {
                    var wa = WF.Screen.FromPoint(new System.Drawing.Point(x + w / 2, y + h / 2)).WorkingArea;
                    // 贴着的那条边按当前屏幕重新对齐（球大小变了也不会离开边缘）；藏一半进屏幕外
                    switch (edge)
                    {
                        case "左": x = wa.Left - (hidden ? w / 2 : 0); break;
                        case "右": x = wa.Right - w + (hidden ? w / 2 : 0); break;
                        case "上": y = wa.Top - (hidden ? h / 2 : 0); break;
                        case "下": y = wa.Bottom - h + (hidden ? h / 2 : 0); break;
                    }
                }
                Ball.Opacity = hidden ? 0.5 : 1;
                SetPos(x, y, w, h);
                return;
            }
            w = (int)Math.Round((S.收起 ? BallOuter : S.宽度) * sx);
            if (game && S.停靠 != "自由")
            {
                var c = Native.ClientOnScreen(_game);
                if (c.W < 100 || c.H < 100) return;
                y = c.T + (int)(S.上边距 * sy);
                h = c.H - (int)((S.上边距 + S.下边距) * sy);
                if (S.停靠 == "外侧")
                {
                    Native.GetWindowRect(_game, out var wr);
                    x = wr.L - w;
                }
                else x = c.L + (int)(S.左边距 * sx);
            }
            else
            {
                x = S.自由X; y = S.自由Y; h = S.自由高;
            }
            h = Math.Max(h, (int)(260 * sy));
            if (S.收起) h = (int)Math.Round(BallOuter * sy);
            SetPos(x, y, w, h);
        }

        void SetPos(int x, int y, int w, int h)
        {
            var r = new Native.RECT { L = x, T = y, R = x + w, B = y + h };
            if (r.L == _last.L && r.T == _last.T && r.R == _last.R && r.B == _last.B) return;
            _last = r;
            Native.SetWindowPos(_hwnd, IntPtr.Zero, x, y, w, h, Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
        }

        // ---------- 拖动、缩放 ----------
        void Header_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2) { Collapse_Click(sender, e); return; }
            _dragging = true;
            try { DragMove(); } catch { }
            _dragging = false;
            Native.GetWindowRect(_hwnd, out var r);
            var (sx, sy) = Scale();
            bool game = _game != IntPtr.Zero && Native.IsWindow(_game);
            if (game && S.停靠 != "自由")
            {
                var c = Native.ClientOnScreen(_game);
                S.上边距 = Math.Max(0, (r.T - c.T) / sy);
                if (S.停靠 == "内侧") S.左边距 = Math.Max(0, (r.L - c.L) / sx);
            }
            else { S.自由X = r.L; S.自由Y = r.T; }
            Relayout();
        }

        void Grip_DragDelta(object sender, DragDeltaEventArgs e)
        {
            var (_, sy) = Scale();
            S.宽度 = Math.Clamp(S.宽度 + e.HorizontalChange, 300, 760);
            bool game = _game != IntPtr.Zero && Native.IsWindow(_game);
            if (game && S.停靠 != "自由") S.下边距 = Math.Max(0, S.下边距 - e.VerticalChange);
            else S.自由高 = Math.Max(260, S.自由高 + (int)(e.VerticalChange * sy));
            _last = default;
            Place(game);
        }

        void Grip_DragCompleted(object sender, DragCompletedEventArgs e) => S.Save();
    }
}
