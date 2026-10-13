using System.Drawing;
using Vortice.DirectWrite;
using static Vortice.DirectWrite.DWrite;

namespace CX2Word
{
    // ---------------------------------------------------------------------
    // Layout layer  (AbiWord equivalent: fl_BlockLayout + fl_LineBreaker + fp_Page)
    //
    // LayoutEngine turns the logical Document into a DocumentLayout consisting of
    // LineFragments flowed onto PageLayouts. The flat Lines[] list is the single
    // source of truth for reading order; pages are just a partition of it.
    // ---------------------------------------------------------------------

    /// <summary>Abstraction over text measurement so the layout engine is deterministic and testable.</summary>
    public interface ITextMeasurer
    {
        float MeasureWidth(string text, TextStyle style);
        float MeasureCharWidth(char ch, TextStyle style);
        float GetLineHeight(TextStyle style);
        float GetAscent(TextStyle style);
    }

    /// <summary>DirectWrite-backed measurer. The formats it creates are directly usable by the D2D renderer.</summary>
    public sealed class DirectWriteTextMeasurer : ITextMeasurer, IDisposable
    {
        private const float LayoutWidth = 100000f;

        private readonly IDWriteFactory _factory;
        private readonly Dictionary<TextStyle, IDWriteTextFormat> _formats = new();
        private readonly Dictionary<(TextStyle, char), float> _charWidths = new();
        private readonly Dictionary<TextStyle, float> _lineHeights = new();

        public DirectWriteTextMeasurer()
        {
            _factory = DWriteCreateFactory<IDWriteFactory>();
        }

        public IDWriteFactory WriteFactory => _factory;

        public IDWriteTextFormat GetFormat(TextStyle style)
        {
            if (!_formats.TryGetValue(style, out var format))
            {
                format = _factory.CreateTextFormat(
                    style.FontFamily,
                    (style.FontStyle & System.Drawing.FontStyle.Bold) != 0 ? Vortice.DirectWrite.FontWeight.Bold : Vortice.DirectWrite.FontWeight.Normal,
                    (style.FontStyle & System.Drawing.FontStyle.Italic) != 0 ? Vortice.DirectWrite.FontStyle.Italic : Vortice.DirectWrite.FontStyle.Normal,
                    Vortice.DirectWrite.FontStretch.Normal,
                    style.FontSize);
                _formats[style] = format;
            }
            return format;
        }

        public float MeasureWidth(string text, TextStyle style)
        {
            if (string.IsNullOrEmpty(text)) return 0f;

            using var layout = _factory.CreateTextLayout(text, GetFormat(style), LayoutWidth, LayoutWidth);
            return layout.Metrics.WidthIncludingTrailingWhitespace;
        }

        public float MeasureCharWidth(char ch, TextStyle style)
        {
            var key = (style, ch);
            if (_charWidths.TryGetValue(key, out var width))
                return width;

            using var layout = _factory.CreateTextLayout(ch.ToString(), GetFormat(style), LayoutWidth, LayoutWidth);
            width = layout.Metrics.WidthIncludingTrailingWhitespace;
            _charWidths[key] = width;
            return width;
        }

        public float GetLineHeight(TextStyle style)
        {
            if (_lineHeights.TryGetValue(style, out var height))
                return height;

            using var layout = _factory.CreateTextLayout("K에9", GetFormat(style), LayoutWidth, LayoutWidth);
            height = layout.Metrics.Height;
            _lineHeights[style] = height;
            return height;
        }

        public float GetAscent(TextStyle style) => GetLineHeight(style) * 0.72f;

        public void Dispose()
        {
            foreach (var format in _formats.Values) format.Dispose();
            _formats.Clear();
        }
    }

    public readonly struct PageMargins
    {
        public readonly float Left, Top, Right, Bottom;
        public PageMargins(float all) { Left = Top = Right = Bottom = all; }
        public PageMargins(float left, float top, float right, float bottom)
        {
            Left = left; Top = top; Right = right; Bottom = bottom;
        }
    }

    public sealed class LayoutSettings
    {
        public SizeF PageSize { get; set; } = new SizeF(794f, 1123f); // A4 @ 96 dpi
        public PageMargins Margins { get; set; } = new PageMargins(72f);
        public float PageGap { get; set; } = 24f;
        public float ParagraphSpacing { get; set; } = 10f;

        public float ContentWidth => PageSize.Width - Margins.Left - Margins.Right;
        public float ContentHeight => PageSize.Height - Margins.Top - Margins.Bottom;
    }

    /// <summary>A styled slice of a line. Prefix widths make caret geometry exact and O(log n).</summary>
    public sealed class LineRun
    {
        public TextStyle Style { get; init; } = new();
        public string Text { get; init; } = string.Empty;
        public int StartInLine { get; init; }
        public float X { get; set; }
        public float Width { get; set; }
        public float[] Prefix { get; init; } = Array.Empty<float>(); // length = Text.Length + 1
    }

    /// <summary>One physical (wrapped) line. Offsets are within its owning paragraph.</summary>
    public sealed class LineFragment
    {
        public int ParagraphIndex { get; init; }
        public int Start { get; init; }
        public int End { get; init; }
        public float Height { get; set; }
        public float X { get; set; }
        public float Top { get; set; }
        public int PageIndex { get; set; }
        public List<LineRun> Runs { get; } = new();

        public int Length => End - Start;
        public float Bottom => Top + Height;
    }

    public sealed class PageLayout
    {
        public int Index { get; init; }
        public float Left { get; set; }
        public float Top { get; set; }
        public float Width { get; set; }
        public float Height { get; set; }
        public RectangleF Bounds => new(Left, Top, Width, Height);
        public float Bottom => Top + Height;
        public List<LineFragment> Lines { get; } = new();
    }

    /// <summary>Result of a layout pass. Visual truth is derived here; nothing else caches it.</summary>
    public sealed class DocumentLayout
    {
        private readonly List<int> _paragraphFirstLine = new();
        private readonly List<int> _paragraphLineCount = new();

        public LayoutSettings Settings { get; }
        public List<PageLayout> Pages { get; } = new();
        public List<LineFragment> Lines { get; } = new();

        public DocumentLayout(LayoutSettings settings) { Settings = settings; }

        internal void AddParagraphLines(int paragraphIndex, List<LineFragment> lines)
        {
            _paragraphFirstLine.Add(Lines.Count);
            _paragraphLineCount.Add(lines.Count);
            Lines.AddRange(lines);
        }

        public int LineCount => Lines.Count;

        public int IndexOfLine(TextPosition pos, CaretAffinity affinity)
        {
            if (Lines.Count == 0) return -1;

            int pi = Math.Clamp(pos.ParagraphIndex, 0, _paragraphFirstLine.Count - 1);
            int first = _paragraphFirstLine[pi];
            int count = _paragraphLineCount[pi];

            for (int i = first; i < first + count; i++)
            {
                var line = Lines[i];
                if (pos.Offset > line.Start && pos.Offset < line.End) return i;

                if (pos.Offset == line.Start)
                {
                    if (i > first && Lines[i - 1].End == line.Start)
                        return affinity == CaretAffinity.After ? i : i - 1;
                    return i;
                }

                if (pos.Offset == line.End)
                {
                    if (i < first + count - 1 && Lines[i + 1].Start == line.End)
                        return affinity == CaretAffinity.Before ? i : i + 1;
                    return i;
                }
            }

            return first + count - 1;
        }

        public LineFragment? LineFor(TextPosition pos, CaretAffinity affinity)
        {
            int idx = IndexOfLine(pos, affinity);
            return idx < 0 ? null : Lines[idx];
        }

        /// <summary>True when <paramref name="pos"/> is the first offset of a continuation line
        /// (i.e. the previous line of the same paragraph ends exactly there).</summary>
        public bool IsSoftLineStart(TextPosition pos)
        {
            int pi = Math.Clamp(pos.ParagraphIndex, 0, _paragraphFirstLine.Count - 1);
            int first = _paragraphFirstLine[pi];
            int count = _paragraphLineCount[pi];

            for (int i = first + 1; i < first + count; i++)
            {
                var line = Lines[i];
                if (line.Start == pos.Offset && Lines[i - 1].End == pos.Offset)
                    return true;
            }
            return false;
        }

        public float GetCaretX(LineFragment line, int offsetInLine)
        {
            if (line.Runs.Count == 0) return line.X;

            offsetInLine = Math.Clamp(offsetInLine, 0, line.Length);

            foreach (var run in line.Runs)
            {
                int runEnd = run.StartInLine + run.Text.Length;
                if (offsetInLine <= runEnd)
                {
                    int local = Math.Clamp(offsetInLine - run.StartInLine, 0, run.Text.Length);
                    return line.X + run.X + run.Prefix[local];
                }
            }

            var last = line.Runs[^1];
            return line.X + last.X + last.Width;
        }

        public int GetOffsetInLineAtX(LineFragment line, float x)
        {
            if (line.Runs.Count == 0) return 0;

            float localX = x - line.X;

            foreach (var run in line.Runs)
            {
                float runEnd = run.X + run.Width;

                if (localX < run.X)
                    return run.StartInLine;

                if (localX < runEnd)
                {
                    int lo = 0, hi = run.Text.Length;
                    while (lo < hi)
                    {
                        int mid = (lo + hi) / 2;
                        if (run.X + run.Prefix[mid] < localX) lo = mid + 1;
                        else hi = mid;
                    }

                    if (lo == 0) return run.StartInLine;

                    float left = run.X + run.Prefix[lo - 1];
                    float right = run.X + run.Prefix[lo];
                    int clamped = (localX - left < right - localX) ? lo - 1 : lo;
                    return Math.Clamp(run.StartInLine + clamped, run.StartInLine, run.StartInLine + run.Text.Length);
                }
            }

            return line.Length;
        }

        public RectangleF GetCaretBounds(TextPosition pos, CaretAffinity affinity)
        {
            var line = LineFor(pos, affinity);
            if (line is null) return RectangleF.Empty;

            int offsetInLine = Math.Clamp(pos.Offset - line.Start, 0, line.Length);
            float x = GetCaretX(line, offsetInLine);
            return new RectangleF(x, line.Top, 1f, line.Height);
        }

        /// <summary>Maps a document-space point to the nearest logical position.</summary>
        public (TextPosition position, CaretAffinity affinity) GetPositionFromPoint(PointF docPoint)
        {
            if (Lines.Count == 0) return (TextPosition.Start, CaretAffinity.Before);

            PageLayout page = FindPageByY(docPoint.Y);

            LineFragment? best = null;
            float bestDist = float.MaxValue;
            foreach (var line in page.Lines)
            {
                float center = line.Top + line.Height / 2f;
                float dist = Math.Abs(center - docPoint.Y);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = line;
                }
            }

            best ??= docPoint.Y < Lines[0].Top ? Lines[0] : Lines[^1];

            int offsetInLine = GetOffsetInLineAtX(best, docPoint.X);
            var affinity = (offsetInLine == 0 && best.Start > 0) ? CaretAffinity.After : CaretAffinity.Before;
            return (new TextPosition(best.ParagraphIndex, best.Start + offsetInLine), affinity);
        }

        private PageLayout FindPageByY(float y)
        {
            foreach (var page in Pages)
                if (y >= page.Top && y < page.Bottom)
                    return page;

            return y < Pages[0].Top ? Pages[0] : Pages[^1];
        }
    }

    /// <summary>Breaks paragraphs into lines and flows them onto pages.</summary>
    public sealed class LayoutEngine
    {
        public ITextMeasurer Measurer { get; }

        public LayoutEngine(ITextMeasurer measurer) { Measurer = measurer; }

        public DocumentLayout Layout(Document document, LayoutSettings settings)
        {
            var result = new DocumentLayout(settings);
            float maxWidth = settings.ContentWidth;

            for (int i = 0; i < document.ParagraphCount; i++)
                result.AddParagraphLines(i, WrapParagraph(i, document[i], settings, maxWidth));

            Paginate(result, settings);
            return result;
        }

        /// <summary>The 줄바꿈 (word wrap) engine. Greedy, breaks after the last space that fits;
        /// falls back to a hard character break for over-long tokens.</summary>
        public List<LineFragment> WrapParagraph(int paragraphIndex, Paragraph paragraph, LayoutSettings settings, float maxWidth)
        {
            var lines = new List<LineFragment>();
            string text = paragraph.Text;
            float lineHeight = Measurer.GetLineHeight(paragraph.Style);

            if (text.Length == 0)
            {
                lines.Add(new LineFragment { ParagraphIndex = paragraphIndex, Start = 0, End = 0, Height = lineHeight });
                return lines;
            }

            var styles = new TextStyle[text.Length];
            var widths = new float[text.Length];
            for (int i = 0; i < text.Length; i++)
            {
                styles[i] = paragraph.StyleAt(i);
                widths[i] = Measurer.MeasureCharWidth(text[i], styles[i]);
            }

            int start = 0;
            while (start < text.Length)
            {
                float acc = 0f;
                int i = start;
                int lastSpace = -1;

                while (i < text.Length)
                {
                    if (text[i] == ' ') lastSpace = i;
                    if (acc + widths[i] > maxWidth && i > start) break;
                    acc += widths[i];
                    i++;
                }

                int end;
                if (i >= text.Length) end = text.Length;
                else if (lastSpace >= start) end = lastSpace + 1; // include the trailing space
                else end = i;                                     // hard break inside a long token

                if (end <= start) end = start + 1;

                lines.Add(BuildLine(paragraphIndex, text, styles, widths, start, end, lineHeight));
                start = end;
            }

            return lines;
        }

        private static LineFragment BuildLine(int paragraphIndex, string text, TextStyle[] styles, float[] widths, int start, int end, float lineHeight)
        {
            var fragment = new LineFragment
            {
                ParagraphIndex = paragraphIndex,
                Start = start,
                End = end,
                Height = lineHeight
            };

            int i = start;
            float x = 0f;
            while (i < end)
            {
                var style = styles[i];
                int runStart = i;
                while (i < end && styles[i].Equals(style)) i++;
                int len = i - runStart;

                var prefix = new float[len + 1];
                for (int k = 0; k < len; k++)
                    prefix[k + 1] = prefix[k] + widths[runStart + k];

                fragment.Runs.Add(new LineRun
                {
                    Style = style,
                    Text = text.Substring(runStart, len),
                    StartInLine = runStart - start,
                    X = x,
                    Width = prefix[len],
                    Prefix = prefix
                });

                x += prefix[len];
            }

            return fragment;
        }

        private void Paginate(DocumentLayout layout, LayoutSettings settings)
        {
            float contentTop = settings.Margins.Top;
            float contentBottom = settings.PageSize.Height - settings.Margins.Bottom;

            var page = NewPage(0, settings);
            layout.Pages.Add(page);
            float y = contentTop;
            int previousParagraph = -1;

            foreach (var line in layout.Lines)
            {
                if (previousParagraph != -1 && line.ParagraphIndex != previousParagraph)
                    y += settings.ParagraphSpacing;

                if (y + line.Height > contentBottom + 0.01f)
                {
                    page = NewPage(layout.Pages.Count, settings);
                    layout.Pages.Add(page);
                    y = contentTop;
                }

                line.X = settings.Margins.Left;
                line.Top = page.Top + y;
                line.PageIndex = page.Index;
                page.Lines.Add(line);

                y += line.Height;
                previousParagraph = line.ParagraphIndex;
            }
        }

        private static PageLayout NewPage(int index, LayoutSettings settings) => new()
        {
            Index = index,
            Left = 0f,
            Top = settings.PageGap + index * (settings.PageSize.Height + settings.PageGap),
            Width = settings.PageSize.Width,
            Height = settings.PageSize.Height
        };
    }
}
