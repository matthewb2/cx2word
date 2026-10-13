using System.Drawing;
using CX2Word;

namespace CX2Word.Validation;

/// <summary>
/// Deterministic validation of the layout/caret architecture. Uses a fixed-width
/// measurer so wrapping, pagination and caret geometry are exactly predictable.
/// Run with:  dotnet run --project CX2Word.Validation
/// </summary>
internal static class Program
{
    private static int _passed;
    private static int _failed;

    private static int Main()
    {
        Console.WriteLine("CX2Word architecture validation\n");

        ValidateWordWrap();
        ValidateHardBreak();
        ValidatePagination();
        ValidateCaretVerticalWithinParagraph();
        ValidateCaretVerticalAcrossPages();
        ValidateCaretHorizontalAcrossParagraphs();
        ValidateCaretGeometryWithMargins();
        ValidateCaretWrapBoundaryHorizontal();
        ValidateCaretGeometryRoundTrip();
        ValidateEditing();
        ValidateDeleteRangeAcrossParagraphs();

        Console.WriteLine($"\n{_passed} passed, {_failed} failed");
        return _failed == 0 ? 0 : 1;
    }

    // ---------------------------------------------------------------- tests

    private static void ValidateWordWrap()
    {
        var doc = new Document();
        doc.AppendParagraph("aaaa bbbb cccc dddd", new TextStyle());
        var layout = Layout(doc, contentWidth: 100);

        Check("word wrap: produces 2 lines", layout.LineCount == 2);
        Check("word wrap: line0 = [0,10)", layout.Lines[0].Start == 0 && layout.Lines[0].End == 10);
        Check("word wrap: line1 = [10,19)", layout.Lines[1].Start == 10 && layout.Lines[1].End == 19);
        Check("word wrap: trailing space kept on line0", layout.Lines[0].Runs[^1].Text.EndsWith(" "));
    }

    private static void ValidateHardBreak()
    {
        var doc = new Document();
        doc.AppendParagraph(new string('x', 25), new TextStyle());
        var layout = Layout(doc, contentWidth: 100);

        Check("hard break: produces 3 lines", layout.LineCount == 3);
        Check("hard break: [0,10)", layout.Lines[0].Start == 0 && layout.Lines[0].End == 10);
        Check("hard break: [10,20)", layout.Lines[1].Start == 10 && layout.Lines[1].End == 20);
        Check("hard break: [20,25)", layout.Lines[2].Start == 20 && layout.Lines[2].End == 25);
        Check("hard break: each line <= content width", layout.Lines.All(l => l.Runs.Sum(r => r.Width) <= 100 + 0.001f));
    }

    private static void ValidatePagination()
    {
        var doc = new Document();
        doc.AppendParagraph(new string('x', 120), new TextStyle()); // 12 lines of 10 chars
        var layout = Layout(doc, contentWidth: 100, contentHeight: 100, lineHeight: 20);

        Check("pagination: 3 pages", layout.Pages.Count == 3);
        Check("pagination: page0 has 5 lines", layout.Pages[0].Lines.Count == 5);
        Check("pagination: page1 has 5 lines", layout.Pages[1].Lines.Count == 5);
        Check("pagination: page2 has 2 lines", layout.Pages[2].Lines.Count == 2);
        Check("pagination: flat order preserved", layout.Lines.Select(l => l.PageIndex).SequenceEqual(
            Enumerable.Repeat(0, 5).Concat(Enumerable.Repeat(1, 5)).Concat(Enumerable.Repeat(2, 2))));
        Check("pagination: pages do not overlap", layout.Pages[0].Bottom <= layout.Pages[1].Top);
    }

    private static void ValidateCaretVerticalWithinParagraph()
    {
        var doc = new Document();
        doc.AppendParagraph("aaaa bbbb cccc dddd", new TextStyle());
        var layout = Layout(doc, contentWidth: 100);
        var caret = new Caret { Position = new TextPosition(0, 8) };
        var nav = new CaretNavigator(doc, layout);

        double goal = layout.GetCaretBounds(caret.Position, caret.Affinity).X;
        nav.MoveDown(caret);
        Check("caret: down onto next wrapped line", caret.Position.ParagraphIndex == 0 && caret.Position.Offset == 18);
        Check("caret: goal column preserved", caret.GoalX is not null && Math.Abs(caret.GoalX.Value - goal) < 0.001);

        nav.MoveUp(caret);
        Check("caret: up returns to original column", caret.Position.Offset == 8);
    }

    private static void ValidateCaretVerticalAcrossPages()
    {
        var doc = new Document();
        doc.AppendParagraph(new string('x', 120), new TextStyle()); // 12 lines, 3 pages
        var layout = Layout(doc, contentWidth: 100, contentHeight: 100, lineHeight: 20);
        var caret = new Caret { Position = new TextPosition(0, 5) };
        var nav = new CaretNavigator(doc, layout);

        // Move down across the page 0 -> page 1 boundary.
        for (int i = 0; i < 5; i++) nav.MoveDown(caret);

        var line = layout.LineFor(caret.Position, caret.Affinity)!;
        Check("caret/page: crossed onto page 1", line.PageIndex == 1);
        Check("caret/page: landed on first line of page 1", Math.Abs(caret.Position.Offset - 50) < 10);

        // PageDown then PageUp returns to the same row on the original page.
        var lineBefore = layout.LineFor(caret.Position, caret.Affinity)!;
        nav.MovePageDown(caret);
        var linePageDown = layout.LineFor(caret.Position, caret.Affinity)!;
        Check("caret/page: page down changes page", linePageDown.PageIndex == 2);

        nav.MovePageUp(caret);
        var lineBack = layout.LineFor(caret.Position, caret.Affinity)!;
        Check("caret/page: page up returns to page 1", lineBack.PageIndex == 1);
    }

    private static void ValidateCaretHorizontalAcrossParagraphs()
    {
        var doc = new Document();
        doc.AppendParagraph("abc", new TextStyle());
        doc.AppendParagraph("def", new TextStyle());
        var layout = Layout(doc, contentWidth: 100);
        var caret = new Caret { Position = new TextPosition(0, 3) };
        var nav = new CaretNavigator(doc, layout);

        nav.MoveRight(caret);
        Check("caret: right across paragraph break", caret.Position == new TextPosition(1, 0));

        nav.MoveLeft(caret);
        Check("caret: left across paragraph break", caret.Position == new TextPosition(0, 3));
    }

    private static void ValidateCaretGeometryRoundTrip()
    {
        var doc = new Document();
        doc.AppendParagraph("aaaa bbbb cccc dddd", new TextStyle());
        var layout = Layout(doc, contentWidth: 100);
        var nav = new CaretNavigator(doc, layout);

        foreach (int offset in new[] { 0, 3, 8, 12, 18 })
        {
            var pos = new TextPosition(0, offset);
            var bounds = layout.GetCaretBounds(pos, CaretAffinity.Before);
            var point = new PointF(bounds.X, bounds.Y + bounds.Height / 2f);
            var (mapped, _) = layout.GetPositionFromPoint(point);
            Check($"caret: point round-trip offset {offset} -> {mapped.Offset}", mapped.Offset == offset);
        }
    }

    private static void ValidateCaretGeometryWithMargins()
    {
        var doc = new Document();
        doc.AppendParagraph("aaaa bbbb cccc dddd", new TextStyle());
        var settings = new LayoutSettings
        {
            PageSize = new SizeF(120, 1000),
            Margins = new PageMargins(20f),
            PageGap = 0f,
            ParagraphSpacing = 0f
        };
        var layout = new LayoutEngine(new FixedWidthTextMeasurer()).Layout(doc, settings);

        // line.X == Margin.Left == 20, so caret x = 20 + 10*offset.
        var bounds = layout.GetCaretBounds(new TextPosition(0, 3), CaretAffinity.Before);
        Check("caret/margins: caret x includes left margin", Math.Abs(bounds.X - 50f) < 0.001f);

        var (mapped, _) = layout.GetPositionFromPoint(new PointF(50f, bounds.Y + 5f));
        Check("caret/margins: point round-trip with margins", mapped.Offset == 3);
    }

    private static void ValidateCaretWrapBoundaryHorizontal()
    {
        var doc = new Document();
        doc.AppendParagraph("aaaa bbbb cccc dddd", new TextStyle());
        var layout = Layout(doc, contentWidth: 100);
        var caret = new Caret { Position = new TextPosition(0, 9) };
        var nav = new CaretNavigator(doc, layout);

        var before = layout.GetCaretBounds(caret.Position, caret.Affinity);
        nav.MoveRight(caret);
        var after = layout.GetCaretBounds(caret.Position, caret.Affinity);

        Check("caret/wrap: right at wrap boundary moves to next line start",
            caret.Position.Offset == 10 && after.Y > before.Y);

        nav.MoveLeft(caret);
        Check("caret/wrap: left returns across boundary", caret.Position.Offset == 9);
    }

    private static void ValidateDeleteRangeAcrossParagraphs()
    {
        var doc = new Document();
        doc.AppendParagraph("abcd", new TextStyle());
        doc.AppendParagraph("efgh", new TextStyle());
        doc.AppendParagraph("ijkl", new TextStyle());

        // Delete "cd" + "efg" : range (para0,2) .. (para1,3).
        var pos = DocumentEditor.DeleteRange(doc, new TextPosition(0, 2), new TextPosition(1, 3));
        Check("edit: delete range across paragraphs", pos == new TextPosition(0, 2));
        Check("edit: delete range text result", doc.ParagraphCount == 2 && doc[0].Text == "abh" && doc[1].Text == "ijkl");
    }

    private static void ValidateEditing()
    {
        var doc = new Document();
        doc.AppendParagraph("abc", new TextStyle());
        var pos = new TextPosition(0, 3);

        pos = DocumentEditor.InsertText(doc, pos, "de", new TextStyle());
        Check("edit: insert appends", doc[0].Text == "abcde" && pos.Offset == 5);

        pos = DocumentEditor.Backspace(doc, pos);
        Check("edit: backspace removes one", doc[0].Text == "abcd" && pos.Offset == 4);

        pos = DocumentEditor.InsertParagraph(doc, new TextPosition(0, 2));
        Check("edit: enter splits paragraph", doc.ParagraphCount == 2 && doc[0].Text == "ab" && doc[1].Text == "cd");

        pos = DocumentEditor.Backspace(doc, new TextPosition(1, 0));
        Check("edit: backspace at start merges paragraphs", doc.ParagraphCount == 1 && doc[0].Text == "abcd");
    }

    // ---------------------------------------------------------------- helpers

    private static DocumentLayout Layout(Document doc, float contentWidth, float contentHeight = 1000, float lineHeight = 20)
    {
        var settings = new LayoutSettings
        {
            PageSize = new SizeF(contentWidth, contentHeight),
            Margins = new PageMargins(0f),
            PageGap = 0f,
            ParagraphSpacing = 0f
        };
        return new LayoutEngine(new FixedWidthTextMeasurer { LineHeight = lineHeight }).Layout(doc, settings);
    }

    private static void Check(string name, bool condition)
    {
        if (condition)
        {
            _passed++;
            Console.WriteLine($"  PASS  {name}");
        }
        else
        {
            _failed++;
            Console.WriteLine($"  FAIL  {name}");
        }
    }
}

/// <summary>Every character is <see cref="CharWidth"/> wide. Fully deterministic.</summary>
internal sealed class FixedWidthTextMeasurer : ITextMeasurer
{
    public float CharWidth { get; init; } = 10f;
    public float LineHeight { get; set; } = 20f;
    public float Ascent { get; init; } = 16f;

    private readonly Font _font = new("Arial", 12f);

    public float MeasureWidth(string text, TextStyle style) => text.Length * CharWidth;
    public float MeasureCharWidth(char ch, TextStyle style) => CharWidth;
    public float GetLineHeight(TextStyle style) => LineHeight;
    public float GetAscent(TextStyle style) => Ascent;
    public Font GetFont(TextStyle style) => _font;
}
