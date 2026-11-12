using System;
using System.Windows;
using System.Windows.Media;

namespace WeishuHelper
{
    /// <summary>
    /// 配色：主题色（按钮、标题、高亮）、面板底色、悬浮球颜色。
    /// XAML 里用 DynamicResource 引用这里写进 Application.Resources 的画刷，代码里用 Theme.Acc / Theme.A(alpha)。
    /// </summary>
    public static class Theme
    {
        public static Color Accent = C("#41DBB2");
        public static Color Panel = C("#141A1F");
        public static Color Ball = C("#41DBB2");

        public static readonly (string name, string hex)[] AccentPresets =
        {
            ("薄荷", "#41DBB2"), ("罗德岛蓝", "#3FA9F5"), ("源石橙", "#FF9F43"), ("樱粉", "#FF7EB6"),
            ("鸢尾紫", "#A78BFA"), ("赤红", "#F25F5C"), ("鎏金", "#F6C60F"), ("霜白", "#D8E3EA"),
        };
        public static readonly (string name, string hex)[] PanelPresets =
        {
            ("夜灰", "#141A1F"), ("深海", "#0E1A2B"), ("墨黑", "#0A0A0C"), ("暖褐", "#1F1712"),
            ("暗紫", "#1A1426"), ("森绿", "#0F1C17"), ("酒红", "#24121A"), ("石板", "#263038"),
        };

        public static Color C(string hex)
        {
            try { return (Color)ColorConverter.ConvertFromString(hex); }
            catch { return Colors.Gray; }
        }

        public static string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        public static string Acc => Hex(Accent);
        /// <summary>带透明度的主题色，例如 A(0x55)。</summary>
        public static string A(byte a) => $"#{a:X2}{Accent.R:X2}{Accent.G:X2}{Accent.B:X2}";

        public static Color Mix(Color a, Color b, double t) => Color.FromArgb(
            (byte)(a.A + (b.A - a.A) * t), (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
        public static Color WithA(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);

        public static void Load(Settings s)
        {
            Accent = C(s.主题色);
            Panel = C(s.面板色);
            Ball = string.IsNullOrEmpty(s.悬浮球色) ? Accent : C(s.悬浮球色);
        }

        static SolidColorBrush Br(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

        /// <summary>把配色写进全局资源，XAML 里的 DynamicResource 会立刻跟着变。</summary>
        public static void ApplyResources()
        {
            var r = Application.Current.Resources;
            r["Accent"] = Br(Accent);
            r["AccentSoft"] = Br(WithA(Accent, 0x55));
            r["AccentHover"] = Br(WithA(Accent, 0x44));
            r["AccentFaint"] = Br(WithA(Accent, 0x28));
            r["AccentLine"] = Br(WithA(Accent, 0x66));
            r["AccentGrip"] = Br(WithA(Accent, 0x88));
            r["PopupBg"] = Br(WithA(Mix(Panel, Colors.White, 0.06), 0xF5));
            r["TipBg"] = Br(WithA(Mix(Panel, Colors.White, 0.04), 0xF2));
        }

        /// <summary>面板背景：上浅下深的渐变，透明度跟随设置。</summary>
        public static Brush PanelBrush(double opacity)
        {
            byte a = (byte)Math.Clamp(opacity * 255, 60, 255);
            var top = WithA(Mix(Panel, Colors.White, 0.07), a);
            var bottom = WithA(Mix(Panel, Colors.Black, 0.15), a);
            var g = new LinearGradientBrush(top, bottom, 90);
            g.Freeze();
            return g;
        }

        public static Brush HeaderBrush()
        {
            var g = new LinearGradientBrush(
                WithA(Mix(Panel, Colors.Black, 0.45), 0xE6),
                WithA(Mix(Mix(Panel, Accent, 0.12), Colors.Black, 0.35), 0xE6), 0);
            g.Freeze();
            return g;
        }
    }
}
