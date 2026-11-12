using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace WeishuHelper
{
    /// <summary>
    /// 够攻略用的 Markdown 渲染：# 标题、- 列表、1. 编号、> 引用、| 表格、--- 分隔线、
    /// **加粗**、`代码`、==高亮==、【盟约名】自动着色。
    /// </summary>
    public static class Markdown
    {
        static Brush Accent => UI.B(Theme.Acc);
        static readonly Brush Warn = new SolidColorBrush(Color.FromRgb(0xFF, 0xB0, 0x2E));
        static readonly Brush Dim = new SolidColorBrush(Color.FromRgb(0x9A, 0xA6, 0xAE));
        static readonly Brush Fg = new SolidColorBrush(Color.FromRgb(0xE8, 0xEC, 0xEF));
        static readonly Brush Bond = new SolidColorBrush(Color.FromRgb(0x7F, 0xC8, 0xFF));

        public static FlowDocument Render(string md, double fontSize)
        {
            var doc = new FlowDocument
            {
                FontFamily = new FontFamily("Microsoft YaHei UI"),
                FontSize = fontSize,
                Foreground = Fg,
                PagePadding = new Thickness(2, 0, 6, 8),
                TextAlignment = TextAlignment.Left,
                LineHeight = fontSize * 1.55,
            };
            AppendTo(doc.Blocks, md, fontSize);
            return doc;
        }

        public static void AppendTo(BlockCollection blocks, string md, double fs)
        {
            var lines = md.Replace("\r", "").Split('\n');
            List list = null;
            int listIndent = 0;
            Table table = null;
            for (int i = 0; i < lines.Length; i++)
            {
                var raw = lines[i];
                var line = raw.TrimEnd();
                var t = line.TrimStart();
                int indent = line.Length - t.Length;

                if (!t.StartsWith("|")) table = null;
                if (t.Length == 0) { list = null; continue; }

                var h = Regex.Match(t, @"^(#{1,4})\s+(.*)$");
                if (h.Success)
                {
                    list = null;
                    int lv = h.Groups[1].Length;
                    var p = new Paragraph
                    {
                        FontSize = fs * (lv == 1 ? 1.45 : lv == 2 ? 1.22 : 1.06),
                        FontWeight = FontWeights.Bold,
                        Foreground = lv <= 2 ? Accent : Fg,
                        Margin = new Thickness(0, lv == 1 ? 4 : 10, 0, 4),
                    };
                    Inline(p.Inlines, h.Groups[2].Value);
                    if (lv == 2) { p.BorderBrush = UI.B(Theme.A(0x40)); p.BorderThickness = new Thickness(0, 0, 0, 1); p.Padding = new Thickness(0, 0, 0, 2); }
                    blocks.Add(p);
                    continue;
                }
                if (Regex.IsMatch(t, @"^(-{3,}|\*{3,})$"))
                {
                    list = null;
                    blocks.Add(new Paragraph { BorderBrush = Dim, BorderThickness = new Thickness(0, 0, 0, 1), Margin = new Thickness(0, 4, 0, 8), FontSize = 2 });
                    continue;
                }
                if (t.StartsWith(">"))
                {
                    list = null;
                    var p = new Paragraph
                    {
                        BorderBrush = Warn, BorderThickness = new Thickness(3, 0, 0, 0), Padding = new Thickness(8, 2, 0, 2),
                        Background = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xB0, 0x2E)), Margin = new Thickness(0, 2, 0, 6),
                    };
                    Inline(p.Inlines, t.TrimStart('>').Trim());
                    blocks.Add(p);
                    continue;
                }
                if (t.StartsWith("|"))
                {
                    list = null;
                    var cells = t.Trim('|').Split('|').Select(c => c.Trim()).ToList();
                    if (cells.All(c => Regex.IsMatch(c, @"^:?-{2,}:?$"))) continue;
                    bool header = false;
                    if (table == null)
                    {
                        table = new Table { CellSpacing = 0, Margin = new Thickness(0, 2, 0, 8), BorderBrush = Dim, BorderThickness = new Thickness(0.5) };
                        for (int c = 0; c < cells.Count; c++) table.Columns.Add(new TableColumn());
                        table.RowGroups.Add(new TableRowGroup());
                        blocks.Add(table);
                        header = i + 1 < lines.Length && Regex.IsMatch(lines[i + 1].Trim(), @"^\|?\s*:?-{2,}");
                    }
                    var row = new TableRow();
                    if (header) row.Background = UI.B(Theme.A(0x40));
                    foreach (var c in cells.Take(table.Columns.Count))
                    {
                        var p = new Paragraph { Margin = new Thickness(4, 2, 4, 2), FontSize = fs * 0.93 };
                        if (header) p.FontWeight = FontWeights.Bold;
                        Inline(p.Inlines, c);
                        row.Cells.Add(new TableCell(p) { BorderBrush = new SolidColorBrush(Color.FromArgb(0x30, 255, 255, 255)), BorderThickness = new Thickness(0, 0, 0, 0.5) });
                    }
                    table.RowGroups[0].Rows.Add(row);
                    continue;
                }
                var li = Regex.Match(t, @"^([-*+]|\d+[.、])\s+(.*)$");
                if (li.Success)
                {
                    bool ordered = char.IsDigit(li.Groups[1].Value[0]);
                    if (list == null || indent != listIndent)
                    {
                        list = new List
                        {
                            MarkerStyle = ordered ? TextMarkerStyle.Decimal : (indent > 0 ? TextMarkerStyle.Circle : TextMarkerStyle.Disc),
                            Margin = new Thickness(indent * 6, 0, 0, 4), Padding = new Thickness(fs * 1.3, 0, 0, 0),
                        };
                        listIndent = indent;
                        blocks.Add(list);
                    }
                    var p = new Paragraph { Margin = new Thickness(0, 1, 0, 1) };
                    Inline(p.Inlines, li.Groups[2].Value);
                    list.ListItems.Add(new ListItem(p));
                    continue;
                }
                list = null;
                var para = new Paragraph { Margin = new Thickness(0, 2, 0, 6) };
                // 连续的普通行合成一段
                var text = t;
                while (i + 1 < lines.Length && IsPlain(lines[i + 1])) text += "\n" + lines[++i].Trim();
                Inline(para.Inlines, text);
                blocks.Add(para);
            }
        }

        static bool IsPlain(string l)
        {
            var t = l.Trim();
            return t.Length > 0 && !Regex.IsMatch(t, @"^(#{1,4}\s|>|\||[-*+]\s|\d+[.、]\s|-{3,}|\*{3,})");
        }

        static readonly Regex Tok = new(@"(\*\*.+?\*\*|`.+?`|==.+?==|【[^】]{1,8}】)");

        public static void Inline(InlineCollection ic, string s)
        {
            var parts = s.Split('\n');
            for (int k = 0; k < parts.Length; k++)
            {
                if (k > 0) ic.Add(new LineBreak());
                foreach (var seg in Tok.Split(parts[k]))
                {
                    if (seg.Length == 0) continue;
                    if (seg.StartsWith("**") && seg.EndsWith("**") && seg.Length > 4)
                        ic.Add(new Bold(new Run(seg[2..^2])) { Foreground = Brushes.White });
                    else if (seg.StartsWith("`") && seg.EndsWith("`") && seg.Length > 2)
                        ic.Add(new Run(seg[1..^1]) { FontFamily = new FontFamily("Consolas, Microsoft YaHei UI"), Background = new SolidColorBrush(Color.FromArgb(0x40, 0, 0, 0)) });
                    else if (seg.StartsWith("==") && seg.EndsWith("==") && seg.Length > 4)
                        ic.Add(new Run(seg[2..^2]) { Foreground = Warn, FontWeight = FontWeights.Bold });
                    else if (seg.StartsWith("【") && GameData.BondBy.ContainsKey(seg[1..^1]))
                        ic.Add(new Run(seg) { Foreground = Bond });
                    else
                        ic.Add(new Run(seg));
                }
            }
        }
    }
}
