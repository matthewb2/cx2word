using System.Drawing;

namespace CX2Word
{
    // ---------------------------------------------------------------------
    // Caret layer  (AbiWord equivalent: fl_DocLayout caret mapping)
    //
    // The caret stores ONLY logical state: a TextPosition, an Affinity that
    // disambiguates soft-wrap boundaries, and an optional GoalX (goal column)
    // used by vertical movement. No line index / page index is ever cached,
    // which is what keeps vertical movement from becoming "땜질식" patched code.
    // ---------------------------------------------------------------------

    /// <summary>Which side of a soft-wrap boundary the caret binds to.</summary>
    public enum CaretAffinity
    {
        Before,
        After
    }

    public sealed class Caret
    {
        public TextPosition Position { get; set; } = TextPosition.Start;
        public CaretAffinity Affinity { get; set; } = CaretAffinity.Before;

        /// <summary>Goal column (document x) remembered across a vertical run. Null = recompute.</summary>
        public double? GoalX { get; set; }

        /// <summary>Selection anchor. Null or equal to Position means a collapsed selection.</summary>
        public TextPosition? Anchor { get; set; }

        public bool HasSelection => Anchor.HasValue && Anchor.Value != Position;

        public void CollapseSelection() => Anchor = null;
    }

    /// <summary>Maps logical caret movement onto the current DocumentLayout.</summary>
    public sealed class CaretNavigator
    {
        private readonly Document _document;

        public DocumentLayout Layout { get; set; }

        public CaretNavigator(Document document, DocumentLayout layout)
        {
            _document = document;
            Layout = layout;
        }

        public void MoveLeft(Caret caret)
        {
            var p = caret.Position;
            if (p.Offset > 0)
                caret.Position = new TextPosition(p.ParagraphIndex, p.Offset - 1);
            else if (p.ParagraphIndex > 0)
                caret.Position = new TextPosition(p.ParagraphIndex - 1, _document[p.ParagraphIndex - 1].Length);

            caret.Affinity = CaretAffinity.Before;
            caret.GoalX = null;
        }

        public void MoveRight(Caret caret)
        {
            var p = caret.Position;
            var paragraph = _document[p.ParagraphIndex];

            if (p.Offset < paragraph.Length)
                caret.Position = new TextPosition(p.ParagraphIndex, p.Offset + 1);
            else if (p.ParagraphIndex < _document.ParagraphCount - 1)
                caret.Position = new TextPosition(p.ParagraphIndex + 1, 0);

            // Landing exactly on a soft-wrap boundary binds the caret to the NEXT line visually.
            caret.Affinity = Layout.IsSoftLineStart(caret.Position) ? CaretAffinity.After : CaretAffinity.Before;
            caret.GoalX = null;
        }

        public void MoveUp(Caret caret) => MoveVertical(caret, -1);

        public void MoveDown(Caret caret) => MoveVertical(caret, 1);

        private void MoveVertical(Caret caret, int direction)
        {
            int idx = Layout.IndexOfLine(caret.Position, caret.Affinity);
            if (idx < 0) return;

            var current = Layout.Lines[idx];
            caret.GoalX ??= Layout.GetCaretX(current, ClampOffset(current, caret.Position.Offset));

            int targetIndex = idx + direction;
            if (targetIndex < 0 || targetIndex >= Layout.LineCount) return;

            var target = Layout.Lines[targetIndex];
            int offsetInLine = Layout.GetOffsetInLineAtX(target, (float)caret.GoalX.Value);
            SetPosition(caret, target, offsetInLine);
            // GoalX intentionally preserved for consecutive vertical moves.
        }

        public void MoveLineStart(Caret caret)
        {
            var line = Layout.LineFor(caret.Position, caret.Affinity);
            if (line is null) return;
            caret.Position = new TextPosition(line.ParagraphIndex, line.Start);
            caret.Affinity = CaretAffinity.Before;
            caret.GoalX = null;
        }

        public void MoveLineEnd(Caret caret)
        {
            var line = Layout.LineFor(caret.Position, caret.Affinity);
            if (line is null) return;
            caret.Position = new TextPosition(line.ParagraphIndex, line.End);
            caret.Affinity = CaretAffinity.Before;
            caret.GoalX = null;
        }

        public void MovePageUp(Caret caret) => MovePage(caret, -1);

        public void MovePageDown(Caret caret) => MovePage(caret, 1);

        private void MovePage(Caret caret, int direction)
        {
            int idx = Layout.IndexOfLine(caret.Position, caret.Affinity);
            if (idx < 0) return;

            var current = Layout.Lines[idx];
            int targetPageIndex = current.PageIndex + direction;
            if (targetPageIndex < 0 || targetPageIndex >= Layout.Pages.Count) return;

            caret.GoalX ??= Layout.GetCaretX(current, ClampOffset(current, caret.Position.Offset));

            var targetPage = Layout.Pages[targetPageIndex];
            LineFragment? best = null;
            float bestDistance = float.MaxValue;
            foreach (var line in targetPage.Lines)
            {
                float distance = Math.Abs(line.Top - current.Top);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = line;
                }
            }
            if (best is null) return;

            int offsetInLine = Layout.GetOffsetInLineAtX(best, (float)caret.GoalX.Value);
            SetPosition(caret, best, offsetInLine);
        }

        public void MoveDocumentStart(Caret caret)
        {
            caret.Position = TextPosition.Start;
            caret.Affinity = CaretAffinity.Before;
            caret.GoalX = null;
        }

        public void MoveDocumentEnd(Caret caret)
        {
            int last = _document.ParagraphCount - 1;
            caret.Position = new TextPosition(last, _document[last].Length);
            caret.Affinity = CaretAffinity.Before;
            caret.GoalX = null;
        }

        public void MoveToPoint(Caret caret, PointF documentPoint)
        {
            var (position, affinity) = Layout.GetPositionFromPoint(documentPoint);
            caret.Position = position;
            caret.Affinity = affinity;
            caret.GoalX = null;
        }

        private void SetPosition(Caret caret, LineFragment line, int offsetInLine)
        {
            caret.Position = new TextPosition(line.ParagraphIndex, line.Start + offsetInLine);
            caret.Affinity = (offsetInLine == 0 && line.Start > 0) ? CaretAffinity.After : CaretAffinity.Before;
        }

        private static int ClampOffset(LineFragment line, int paragraphOffset) =>
            Math.Clamp(paragraphOffset - line.Start, 0, line.Length);
    }
}
