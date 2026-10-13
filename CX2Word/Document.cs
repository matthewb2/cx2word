using System.Drawing;

namespace CX2Word
{
    // ---------------------------------------------------------------------
    // Model layer  (AbiWord equivalent: ptbl / PD_Document)
    //
    // This layer is purely logical. It knows nothing about pages, pixels or
    // wrapping. A position in the document is (ParagraphIndex, Offset) where
    // Offset ranges over [0 .. Paragraph.Length].
    // ---------------------------------------------------------------------

    /// <summary>Immutable character style. Value equality is required so runs can be coalesced.</summary>
    public sealed class TextStyle : IEquatable<TextStyle>
    {
        public string FontFamily { get; init; } = "Malgun Gothic";
        public float FontSize { get; init; } = 12f;
        public FontStyle FontStyle { get; init; } = FontStyle.Regular;
        public Color Color { get; init; } = Color.Black;

        public bool Equals(TextStyle? other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;
            return FontFamily == other.FontFamily
                && Math.Abs(FontSize - other.FontSize) < 0.001f
                && FontStyle == other.FontStyle
                && Color.ToArgb() == other.Color.ToArgb();
        }

        public override bool Equals(object? obj) => Equals(obj as TextStyle);

        public override int GetHashCode() =>
            HashCode.Combine(FontFamily, FontSize, FontStyle, Color.ToArgb());
    }

    /// <summary>A contiguous run of characters sharing one style.</summary>
    public sealed class TextRun
    {
        public string Text { get; set; } = string.Empty;
        public TextStyle Style { get; set; } = new();

        public TextRun() { }

        public TextRun(string text, TextStyle style)
        {
            Text = text;
            Style = style;
        }

        public TextRun Clone() => new(Text, Style);
    }

    /// <summary>A logical paragraph: an ordered list of styled runs, terminated by an implicit hard break.</summary>
    public sealed class Paragraph
    {
        public List<TextRun> Runs { get; } = new();

        /// <summary>Style applied to newly inserted characters / empty paragraphs.</summary>
        public TextStyle Style { get; set; } = new();

        /// <summary>Flattened logical text. The paragraph break itself is not included.</summary>
        public string Text
        {
            get
            {
                if (Runs.Count == 0) return string.Empty;
                if (Runs.Count == 1) return Runs[0].Text;
                return string.Concat(Runs.Select(r => r.Text));
            }
        }

        public int Length => Text.Length;

        public Paragraph() { }

        public Paragraph(TextStyle style) { Style = style; }

        public static Paragraph FromText(string text, TextStyle style)
        {
            var p = new Paragraph(style);
            if (text.Length > 0)
                p.Runs.Add(new TextRun(text, style));
            return p;
        }

        /// <summary>Style of the character at <paramref name="offset"/> (offset may equal Length).</summary>
        public TextStyle StyleAt(int offset)
        {
            if (Runs.Count == 0) return Style;

            int acc = 0;
            foreach (var run in Runs)
            {
                if (offset < acc + run.Text.Length)
                    return run.Style;
                acc += run.Text.Length;
            }
            return Runs[^1].Style;
        }

        public void Insert(int offset, string text, TextStyle style)
        {
            if (string.IsNullOrEmpty(text)) return;
            var chars = ToStyledChars();
            for (int i = 0; i < text.Length; i++)
                chars.Insert(offset + i, (text[i], style));
            FromStyledChars(chars);
        }

        public void Remove(int offset, int count)
        {
            if (count <= 0) return;
            var chars = ToStyledChars();
            chars.RemoveRange(offset, count);
            FromStyledChars(chars);
        }

        /// <summary>Splits at <paramref name="offset"/>; returns the tail paragraph, this keeps the head.</summary>
        public Paragraph Split(int offset)
        {
            var chars = ToStyledChars();
            var tail = new Paragraph(Style);
            tail.FromStyledChars(chars.GetRange(offset, chars.Count - offset));
            FromStyledChars(chars.GetRange(0, offset));
            return tail;
        }

        public void Append(Paragraph other)
        {
            var chars = ToStyledChars();
            chars.AddRange(other.ToStyledChars());
            FromStyledChars(chars);
        }

        internal List<(char ch, TextStyle style)> ToStyledChars()
        {
            var list = new List<(char, TextStyle)>();
            foreach (var run in Runs)
                foreach (var ch in run.Text)
                    list.Add((ch, run.Style));
            return list;
        }

        internal void FromStyledChars(List<(char ch, TextStyle style)> chars)
        {
            Runs.Clear();
            int i = 0;
            while (i < chars.Count)
            {
                var style = chars[i].style;
                int start = i;
                var sb = new System.Text.StringBuilder();
                while (i < chars.Count && chars[i].style.Equals(style))
                {
                    sb.Append(chars[i].ch);
                    i++;
                }
                Runs.Add(new TextRun(sb.ToString(), style));
            }
        }
    }

    /// <summary>Logical document position. Offset is a character index within the paragraph.</summary>
    public readonly struct TextPosition : IEquatable<TextPosition>, IComparable<TextPosition>
    {
        public readonly int ParagraphIndex;
        public readonly int Offset;

        public TextPosition(int paragraphIndex, int offset)
        {
            ParagraphIndex = paragraphIndex;
            Offset = offset;
        }

        public static readonly TextPosition Start = new(0, 0);

        public bool Equals(TextPosition other) =>
            ParagraphIndex == other.ParagraphIndex && Offset == other.Offset;

        public override bool Equals(object? obj) => obj is TextPosition p && Equals(p);

        public override int GetHashCode() => HashCode.Combine(ParagraphIndex, Offset);

        public int CompareTo(TextPosition other)
        {
            int c = ParagraphIndex.CompareTo(other.ParagraphIndex);
            return c != 0 ? c : Offset.CompareTo(other.Offset);
        }

        public static bool operator ==(TextPosition a, TextPosition b) => a.Equals(b);
        public static bool operator !=(TextPosition a, TextPosition b) => !a.Equals(b);
        public static bool operator <(TextPosition a, TextPosition b) => a.CompareTo(b) < 0;
        public static bool operator >(TextPosition a, TextPosition b) => a.CompareTo(b) > 0;
        public static bool operator <=(TextPosition a, TextPosition b) => a.CompareTo(b) <= 0;
        public static bool operator >=(TextPosition a, TextPosition b) => a.CompareTo(b) >= 0;

        public override string ToString() => $"({ParagraphIndex},{Offset})";
    }

    public sealed class Document
    {
        public List<Paragraph> Paragraphs { get; } = new();

        public int ParagraphCount => Paragraphs.Count;

        public Paragraph this[int index] => Paragraphs[index];

        public Document() { }

        public void AppendParagraph(Paragraph paragraph) => Paragraphs.Add(paragraph);

        public void AppendParagraph(string text, TextStyle style) =>
            Paragraphs.Add(Paragraph.FromText(text, style));

        /// <summary>Total number of characters including the implicit paragraph breaks.</summary>
        public int CharCount
        {
            get
            {
                int n = 0;
                foreach (var p in Paragraphs) n += p.Length + 1;
                return n;
            }
        }

        public static Document CreateSample()
        {
            var body = new TextStyle { FontFamily = "Malgun Gothic", FontSize = 14f };
            var heading = new TextStyle { FontFamily = "Malgun Gothic", FontSize = 20f, FontStyle = FontStyle.Bold, Color = Color.FromArgb(0x1F, 0x3B, 0x63) };
            var accent = new TextStyle { FontFamily = "Consolas", FontSize = 14f, FontStyle = FontStyle.Italic, Color = Color.FromArgb(0xB0, 0x30, 0x30) };

            var doc = new Document();
            doc.Paragraphs.Add(Paragraph.FromText("CX2Word - 구조적 워드프로세서 엔진", heading));
            doc.Paragraphs.Add(new Paragraph(body));

            string sentence =
                "이 문단은 자동 줄바꿈(word wrap)과 페이지네이션, 그리고 줄바꿈 경계를 넘나드는 커서 이동을 검증하기 위해 충분히 길게 작성되었습니다. " +
                "커서는 논리적 위치(문단, 오프셋)로만 저장되고 화면 좌표는 레이아웃에서 파생되므로, 위/아래 이동 시 열이 유지되고 페이지 경계를 자연스럽게 넘어갑니다. ";

            for (int i = 1; i <= 4; i++)
            {
                var p = new Paragraph(body);
                p.Runs.Add(new TextRun($"{i}번째 섹션: ", new TextStyle { FontFamily = "Malgun Gothic", FontSize = 14f, FontStyle = FontStyle.Bold }));
                p.Runs.Add(new TextRun(sentence, body));
                p.Runs.Add(new TextRun("LayoutEngine", accent));
                p.Runs.Add(new TextRun(" 는 GDI+ 텍스트 측정 결과를 그대로 사용하므로, 렌더링된 글자 위치와 커서 x 좌표가 항상 일치합니다.", body));
                doc.Paragraphs.Add(p);
            }

            // A long unbroken token to exercise the hard-break path.
            doc.Paragraphs.Add(Paragraph.FromText(
                "Supercalifragilisticexpialidocious" + new string('x', 120),
                new TextStyle { FontFamily = "Consolas", FontSize = 14f }));

            return doc;
        }
    }

    /// <summary>
    /// Mutating operations on the model. Each returns the caret position after the edit.
    /// Kept separate from Document so the model stays a passive data structure.
    /// </summary>
    public static class DocumentEditor
    {
        public static TextPosition InsertText(Document doc, TextPosition pos, string text, TextStyle style)
        {
            if (string.IsNullOrEmpty(text)) return pos;
            var p = doc.Paragraphs[pos.ParagraphIndex];
            p.Insert(pos.Offset, text, style);
            return new TextPosition(pos.ParagraphIndex, pos.Offset + text.Length);
        }

        public static TextPosition InsertParagraph(Document doc, TextPosition pos)
        {
            var p = doc.Paragraphs[pos.ParagraphIndex];
            var tail = p.Split(pos.Offset);
            tail.Style = p.Style;
            doc.Paragraphs.Insert(pos.ParagraphIndex + 1, tail);
            return new TextPosition(pos.ParagraphIndex + 1, 0);
        }

        public static TextPosition Backspace(Document doc, TextPosition pos)
        {
            if (pos.Offset > 0)
            {
                var p = doc.Paragraphs[pos.ParagraphIndex];
                p.Remove(pos.Offset - 1, 1);
                return new TextPosition(pos.ParagraphIndex, pos.Offset - 1);
            }

            if (pos.ParagraphIndex > 0)
            {
                var prev = doc.Paragraphs[pos.ParagraphIndex - 1];
                int joinOffset = prev.Length;
                prev.Append(doc.Paragraphs[pos.ParagraphIndex]);
                doc.Paragraphs.RemoveAt(pos.ParagraphIndex);
                return new TextPosition(pos.ParagraphIndex - 1, joinOffset);
            }

            return pos;
        }

        public static TextPosition DeleteForward(Document doc, TextPosition pos)
        {
            var p = doc.Paragraphs[pos.ParagraphIndex];

            if (pos.Offset < p.Length)
            {
                p.Remove(pos.Offset, 1);
                return pos;
            }

            if (pos.ParagraphIndex < doc.Paragraphs.Count - 1)
            {
                p.Append(doc.Paragraphs[pos.ParagraphIndex + 1]);
                doc.Paragraphs.RemoveAt(pos.ParagraphIndex + 1);
            }

            return pos;
        }

        /// <summary>Deletes every character in the half-open logical range [start, end).</summary>
        public static TextPosition DeleteRange(Document doc, TextPosition start, TextPosition end)
        {
            if (end < start) (start, end) = (end, start);

            var head = doc.Paragraphs[start.ParagraphIndex];

            var remaining = new List<(char ch, TextStyle style)>();
            remaining.AddRange(head.ToStyledChars().GetRange(0, start.Offset));

            if (start.ParagraphIndex != end.ParagraphIndex)
            {
                var last = doc.Paragraphs[end.ParagraphIndex];
                remaining.AddRange(last.ToStyledChars().GetRange(end.Offset, last.Length - end.Offset));
                doc.Paragraphs.RemoveRange(start.ParagraphIndex + 1, end.ParagraphIndex - start.ParagraphIndex);
            }

            head.FromStyledChars(remaining);
            return start;
        }
    }
}
