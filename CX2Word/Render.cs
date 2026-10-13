using System.Drawing;
using System.Drawing.Text;

namespace CX2Word
{
    /// <summary>
    /// View layer (AbiWord equivalent: fp_View). Draws the document strictly from
    /// the derived DocumentLayout; it holds no layout state of its own.
    /// </summary>
    public sealed class DocumentRenderer : IDisposable
    {
        private readonly ITextMeasurer _measurer;
        private readonly StringFormat _format;
        private readonly Dictionary<int, Brush> _brushes = new();
        private readonly Font _footerFont = new("Segoe UI", 9f);

        public Color PageBackColor { get; set; } = Color.White;
        public Color PageBorderColor { get; set; } = Color.FromArgb(0xC8, 0xC8, 0xC8);
        public Color SelectionColor { get; set; } = Color.FromArgb(90, 0, 120, 215);

        public DocumentRenderer(ITextMeasurer measurer)
        {
            _measurer = measurer;
            _format = new StringFormat(StringFormat.GenericTypographic)
            {
                FormatFlags = StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.NoClip
            };
        }

        public void Render(Graphics g, EditorSession session, bool caretVisible, float pageLeft)
        {
            var layout = session.Layout;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            DrawPages(g, layout, pageLeft);
            DrawSelection(g, session, pageLeft);
            DrawText(g, layout, pageLeft);
            DrawCaret(g, session, caretVisible, pageLeft);
        }

        private void DrawPages(Graphics g, DocumentLayout layout, float pageLeft)
        {
            foreach (var page in layout.Pages)
            {
                var rect = new RectangleF(pageLeft + page.Left, page.Top, page.Width, page.Height);
                g.FillRectangle(Brushes.White, rect);
                g.DrawRectangle(Pens.Gainsboro, rect.X, rect.Y, rect.Width, rect.Height);

                string footer = $"Page {page.Index + 1} / {layout.Pages.Count}";
                var size = g.MeasureString(footer, _footerFont);
                g.DrawString(footer, _footerFont, Brushes.Gray,
                    rect.X + (rect.Width - size.Width) / 2f, rect.Bottom - 32f);
            }
        }

        private void DrawText(Graphics g, DocumentLayout layout, float pageLeft)
        {
            foreach (var page in layout.Pages)
            {
                foreach (var line in page.Lines)
                {
                    foreach (var run in line.Runs)
                    {
                        if (run.Text.Length == 0) continue;
                        g.DrawString(
                            run.Text,
                            _measurer.GetFont(run.Style),
                            GetBrush(run.Style.Color),
                            pageLeft + line.X + run.X,
                            line.Top,
                            _format);
                    }
                }
            }
        }

        private void DrawSelection(Graphics g, EditorSession session, float pageLeft)
        {
            if (!session.Caret.HasSelection) return;

            var (start, end) = session.GetSelection();
            var layout = session.Layout;
            using var brush = new SolidBrush(SelectionColor);

            foreach (var line in layout.Lines)
            {
                if (line.ParagraphIndex < start.ParagraphIndex || line.ParagraphIndex > end.ParagraphIndex)
                    continue;

                int from = line.ParagraphIndex == start.ParagraphIndex ? start.Offset : 0;
                int to = line.ParagraphIndex == end.ParagraphIndex ? end.Offset : session.Document[line.ParagraphIndex].Length;

                int a = Math.Max(from, line.Start);
                int b = Math.Min(to, line.End);
                if (b <= a) continue;

                float x1 = layout.GetCaretX(line, a - line.Start);
                float x2 = layout.GetCaretX(line, b - line.Start);
                g.FillRectangle(brush, pageLeft + x1, line.Top, x2 - x1, line.Height);
            }
        }

        private void DrawCaret(Graphics g, EditorSession session, bool caretVisible, float pageLeft)
        {
            if (!caretVisible) return;

            var bounds = session.Layout.GetCaretBounds(session.Caret.Position, session.Caret.Affinity);
            if (bounds == RectangleF.Empty) return;

            g.DrawLine(Pens.Black, pageLeft + bounds.X, bounds.Y + 1f, pageLeft + bounds.X, bounds.Bottom - 1f);
        }

        private Brush GetBrush(Color color)
        {
            int key = color.ToArgb();
            if (!_brushes.TryGetValue(key, out var brush))
            {
                brush = new SolidBrush(color);
                _brushes[key] = brush;
            }
            return brush;
        }

        public void Dispose()
        {
            _format.Dispose();
            _footerFont.Dispose();
            foreach (var brush in _brushes.Values) brush.Dispose();
        }
    }
}
