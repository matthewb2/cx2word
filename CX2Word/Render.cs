using System.Drawing;
using System.Numerics;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;
using static Vortice.Direct2D1.D2D1;
using Color = System.Drawing.Color;

namespace CX2Word
{
    /// <summary>
    /// View layer (AbiWord equivalent: fp_View). Draws the document strictly from
    /// the derived DocumentLayout with Direct2D + DirectWrite; it holds no layout state.
    /// </summary>
    public sealed class DocumentRenderer : IDisposable
    {
        private readonly DirectWriteTextMeasurer _measurer;
        private readonly Control _control;

        private ID2D1Factory? _factory;
        private ID2D1HwndRenderTarget? _target;
        private ID2D1SolidColorBrush? _pageBrush;
        private ID2D1SolidColorBrush? _borderBrush;
        private ID2D1SolidColorBrush? _footerBrush;
        private ID2D1SolidColorBrush? _selectionBrush;
        private ID2D1SolidColorBrush? _caretBrush;
        private IDWriteTextFormat? _footerFormat;
        private readonly Dictionary<int, ID2D1SolidColorBrush> _ownedBrushes = new();

        public Color PageBackColor { get; set; } = Color.White;
        public Color PageBorderColor { get; set; } = Color.FromArgb(0xC8, 0xC8, 0xC8);
        public Color SelectionColor { get; set; } = Color.FromArgb(90, 0, 120, 215);
        public Color CaretColor { get; set; } = Color.Black;

        public DocumentRenderer(DirectWriteTextMeasurer measurer, Control control)
        {
            _measurer = measurer;
            _control = control;
        }

        /// <summary>Creates the D2D/DWrite resources lazily on first paint, or recreates after device loss.</summary>
        public void EnsureInitialized()
        {
            if (_target != null) return;

            _factory = D2D1CreateFactory<ID2D1Factory>();
            _target = _factory.CreateHwndRenderTarget(
                new RenderTargetProperties(),
                new HwndRenderTargetProperties
                {
                    Hwnd = _control.Handle,
                    PixelSize = new SizeI(Math.Max(_control.ClientSize.Width, 1), Math.Max(_control.ClientSize.Height, 1))
                });

            _pageBrush = _target.CreateSolidColorBrush(ToColor4(PageBackColor));
            _borderBrush = _target.CreateSolidColorBrush(ToColor4(PageBorderColor));
            _footerBrush = _target.CreateSolidColorBrush(ToColor4(Color.FromArgb(0x66, 0x66, 0x66)));
            _selectionBrush = _target.CreateSolidColorBrush(ToColor4(SelectionColor));
            _caretBrush = _target.CreateSolidColorBrush(ToColor4(CaretColor));
            _footerFormat = _measurer.WriteFactory.CreateTextFormat("Segoe UI", 9f);
        }

        public void Resize(int width, int height)
        {
            _target?.Resize(new SizeI(Math.Max(width, 1), Math.Max(height, 1)));
        }

        /// <param name="pageLeft">Horizontal offset of the document space origin (centered page).</param>
        public void Render(EditorSession session, bool caretVisible, float pageLeft, int scrollX, int scrollY)
        {
            if (_target is null) return;

            _target.BeginDraw();
            _target.Clear(ToColor4(Color.FromArgb(0x5F, 0x5F, 0x5F)));
            _target.Transform = Matrix3x2.CreateTranslation(pageLeft + scrollX, scrollY);

            DrawPages(session.Layout);
            DrawSelection(session);
            DrawText(session.Layout);
            DrawCaret(session, caretVisible);

            _target.Transform = Matrix3x2.Identity;

            var result = _target.EndDraw();
            if (result.Failure) DisposeResources();
        }

        private void DrawPages(DocumentLayout layout)
        {
            if (_target is null || _pageBrush is null || _borderBrush is null || _footerBrush is null || _footerFormat is null)
                return;

            foreach (var page in layout.Pages)
            {
                var rect = new Rect(page.Left, page.Top, page.Left + page.Width, page.Top + page.Height);
                _target.FillRectangle(rect, _pageBrush);
                _target.DrawRectangle(rect, _borderBrush, 1f);

                string footer = $"Page {page.Index + 1} / {layout.Pages.Count}";
                using var footerLayout = _measurer.WriteFactory.CreateTextLayout(footer, _footerFormat, 100000f, 40f);
                float fx = page.Left + (page.Width - footerLayout.Metrics.WidthIncludingTrailingWhitespace) / 2f;
                _target.DrawTextLayout(new Vector2(fx, page.Top + page.Height - 32f), footerLayout, _footerBrush);
            }
        }

        private void DrawText(DocumentLayout layout)
        {
            if (_target is null) return;

            foreach (var page in layout.Pages)
            {
                foreach (var line in page.Lines)
                {
                    foreach (var run in line.Runs)
                    {
                        if (run.Text.Length == 0) continue;

                        using var textLayout = _measurer.WriteFactory.CreateTextLayout(
                            run.Text, _measurer.GetFormat(run.Style), 100000f, line.Height);

                        _target.DrawTextLayout(new Vector2(line.X + run.X, line.Top), textLayout, GetTextBrush(run.Style.Color));
                    }
                }
            }
        }

        private void DrawSelection(EditorSession session)
        {
            if (_target is null || _selectionBrush is null || !session.Caret.HasSelection) return;

            var (start, end) = session.GetSelection();
            var layout = session.Layout;

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
                _target.FillRectangle(new Rect(x1, line.Top, x2, line.Bottom), _selectionBrush);
            }
        }

        private void DrawCaret(EditorSession session, bool caretVisible)
        {
            if (_target is null || _caretBrush is null || !caretVisible) return;

            var bounds = session.Layout.GetCaretBounds(session.Caret.Position, session.Caret.Affinity);
            if (bounds == RectangleF.Empty) return;

            _target.DrawLine(
                new Vector2(bounds.X, bounds.Y + 1f),
                new Vector2(bounds.X, bounds.Bottom - 1f),
                _caretBrush, 1.5f);
        }

        private ID2D1SolidColorBrush GetTextBrush(Color color)
        {
            if (_target is null) throw new InvalidOperationException();

            int key = color.ToArgb();
            if (_ownedBrushes.TryGetValue(key, out var brush))
                return brush;

            var created = _target.CreateSolidColorBrush(ToColor4(color));
            _ownedBrushes[key] = created;
            return created;
        }

        private static Color4 ToColor4(Color c) => new(c.R / 255f, c.G / 255f, c.B / 255f, c.A / 255f);

        private void DisposeResources()
        {
            _footerFormat?.Dispose();
            _footerFormat = null;

            _pageBrush?.Dispose();
            _pageBrush = null;
            _borderBrush?.Dispose();
            _borderBrush = null;
            _footerBrush?.Dispose();
            _footerBrush = null;
            _selectionBrush?.Dispose();
            _selectionBrush = null;
            _caretBrush?.Dispose();
            _caretBrush = null;

            foreach (var brush in _ownedBrushes.Values) brush.Dispose();
            _ownedBrushes.Clear();

            _target?.Dispose();
            _target = null;
            _factory?.Dispose();
            _factory = null;
        }

        public void Dispose() => DisposeResources();
    }
}