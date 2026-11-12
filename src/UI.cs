using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace WeishuHelper
{
    /// <summary>界面小零件。</summary>
    public static class UI
    {
        static readonly BrushConverter Conv = new();
        public static Brush B(string hex) => (Brush)Conv.ConvertFromString(hex);
        public static double F(double k = 1) => App.S.字号 * k;

        public static TextBlock T(string s, double k = 1, string color = null, bool bold = false, Thickness? margin = null)
        {
            var t = new TextBlock { Text = s, FontSize = F(k), TextWrapping = TextWrapping.Wrap };
            if (color != null) t.Foreground = B(color);
            if (bold) t.FontWeight = FontWeights.Bold;
            if (margin != null) t.Margin = margin.Value;
            return t;
        }

        /// <summary>带【盟约】着色和 **加粗** 的文字。</summary>
        public static TextBlock Rich(string s, double k = 1, string color = null)
        {
            var t = new TextBlock { FontSize = F(k), TextWrapping = TextWrapping.Wrap };
            if (color != null) t.Foreground = B(color);
            Markdown.Inline(t.Inlines, s ?? "");
            return t;
        }

        public static Button Btn(string s, Action a, string tip = null, double k = 0.92)
        {
            var b = new Button { Content = s, FontSize = F(k), ToolTip = tip };
            b.Click += (_, _) => a();
            return b;
        }

        public static T Tip<T>(this T e, string tip) where T : FrameworkElement { e.ToolTip = tip; return e; }

        public static Border Chip(string text, string bg, string fg = "#E8ECEF", double k = 0.85, string border = null)
        {
            return new Border
            {
                Background = B(bg),
                BorderBrush = border != null ? B(border) : null,
                BorderThickness = new Thickness(border != null ? 1 : 0),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(5, 1, 5, 1),
                Margin = new Thickness(0, 0, 3, 3),
                Child = new TextBlock { Text = text, FontSize = F(k), Foreground = B(fg), TextWrapping = TextWrapping.NoWrap },
            };
        }

        public static Border Card(params UIElement[] kids)
        {
            var sp = new StackPanel();
            foreach (var k in kids) if (k != null) sp.Children.Add(k);
            return new Border
            {
                Background = B("#1CFFFFFF"),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(8, 6, 8, 6),
                Margin = new Thickness(0, 0, 0, 6),
                Child = sp,
            };
        }

        /// <summary>带灰色提示字的搜索框。</summary>
        public static Grid SearchBox(out TextBox box, string hint)
        {
            var g = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            box = new TextBox { FontSize = F(0.95) };
            var h = new TextBlock { Text = hint, Foreground = B("#77FFFFFF"), FontSize = F(0.9), Margin = new Thickness(7, 4, 0, 0), IsHitTestVisible = false };
            var bx = box;
            bx.TextChanged += (_, _) => h.Visibility = bx.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            g.Children.Add(bx);
            g.Children.Add(h);
            return g;
        }

        public static WrapPanel Wrap(params UIElement[] kids)
        {
            var w = new WrapPanel();
            foreach (var k in kids) if (k != null) w.Children.Add(k);
            return w;
        }

        public static RadioButton Radio(string text, string group, bool on, Action a, double k = 0.88)
        {
            var r = new RadioButton { Content = text, GroupName = group, IsChecked = on, FontSize = F(k), Padding = new Thickness(5, 2, 5, 2) };
            r.Checked += (_, _) => a();
            return r;
        }

        public static ToggleButton Toggle(string text, bool on, Action<bool> a, double k = 0.88)
        {
            var t = new ToggleButton { Content = text, IsChecked = on, FontSize = F(k), Padding = new Thickness(5, 2, 5, 2) };
            t.Checked += (_, _) => a(true);      // 鼠标、键盘、程序切换都会触发
            t.Unchecked += (_, _) => a(false);
            return t;
        }

        static readonly Dictionary<string, BitmapImage> ImgCache = new();

        public static BitmapImage Bitmap(string rel, int decodeWidth) => LoadBitmap(rel, decodeWidth);

        static BitmapImage LoadBitmap(string rel, int decodeWidth)
        {
            if (string.IsNullOrEmpty(rel)) return null;
            var key = rel + "@" + decodeWidth;
            if (ImgCache.TryGetValue(key, out var bmp)) return bmp;
            var path = Path.IsPathRooted(rel) ? rel : Path.Combine(Paths.Data, rel);
            bmp = null;
            if (File.Exists(path))
            {
                try
                {
                    bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(path);
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    if (decodeWidth > 0) bmp.DecodePixelWidth = decodeWidth;
                    bmp.EndInit();
                    bmp.Freeze();
                }
                catch (Exception e) { App.Log("图片读取失败 " + path + ": " + e.Message); bmp = null; }
            }
            ImgCache[key] = bmp;
            return bmp;
        }

        /// <summary>
        /// 资料配图（数据/图片/ 下的相对路径）。没有图时画一个带首字的方块。
        /// disc：给盟约图标这种白色透明图标垫一个深绿圆底。
        /// </summary>
        public static FrameworkElement Img(string rel, double size, string fallbackText = "", bool disc = false, string border = null)
        {
            double px = size * 2;   // 高 DPI 下也清楚
            var bmp = LoadBitmap(rel, (int)px);
            FrameworkElement inner;
            if (bmp != null)
                inner = new Image { Source = bmp, Width = disc ? size * 0.72 : size, Height = disc ? size * 0.72 : size, Stretch = Stretch.Uniform };
            else
                inner = new TextBlock
                {
                    Text = string.IsNullOrEmpty(fallbackText) ? "？" : fallbackText[..Math.Min(2, fallbackText.Length)],
                    FontSize = size * 0.34, FontWeight = FontWeights.Bold, Foreground = B(Theme.Acc),
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                };
            return new Border
            {
                Width = size, Height = size,
                CornerRadius = new CornerRadius(disc ? size / 2 : 4),
                Background = disc || bmp == null
                    ? new RadialGradientBrush(Theme.Mix(Theme.Accent, Colors.Black, 0.35), Theme.Mix(Theme.Panel, Colors.Black, 0.2))
                    : B("#30000000"),
                BorderBrush = border != null ? B(border) : null,
                BorderThickness = new Thickness(border != null ? 1.5 : 0),
                ClipToBounds = true,
                VerticalAlignment = VerticalAlignment.Top,
                Child = inner,
            };
        }

        /// <summary>大图（地图截图等），按宽度缩放。</summary>
        public static FrameworkElement Picture(string rel, double width)
        {
            var bmp = LoadBitmap(rel, (int)(width * 2));
            if (bmp == null) return null;
            return new Image { Source = bmp, Width = width, Stretch = Stretch.Uniform };
        }

        /// <summary>左图右文的卡片。</summary>
        public static Border ImageCard(FrameworkElement img, params UIElement[] kids)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            img.Margin = new Thickness(0, 2, 9, 0);
            g.Children.Add(img);
            var sp = new StackPanel();
            foreach (var k in kids) if (k != null) sp.Children.Add(k);
            Grid.SetColumn(sp, 1);
            g.Children.Add(sp);
            return new Border
            {
                Background = B("#1CFFFFFF"),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(8, 6, 8, 6),
                Margin = new Thickness(0, 0, 0, 6),
                Child = g,
            };
        }

        public static ScrollViewer Scroll(UIElement content) => new()
        {
            Content = content,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
    }
}
