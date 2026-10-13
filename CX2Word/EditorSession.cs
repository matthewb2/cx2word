using System.Drawing;

namespace CX2Word
{
    /// <summary>
    /// Orchestrates the model, layout and caret. The view (Form) only talks to
    /// this class; input controllers translate events into these commands.
    /// </summary>
    public sealed class EditorSession
    {
        public Document Document { get; }
        public LayoutSettings Settings { get; }
        public LayoutEngine Engine { get; }
        public DocumentLayout Layout { get; private set; }
        public Caret Caret { get; } = new();
        public CaretNavigator Navigator { get; private set; }

        /// <summary>Style applied to typed characters.</summary>
        public TextStyle CurrentStyle { get; set; }

        public event EventHandler? Changed;

        public EditorSession(Document document, LayoutEngine engine, LayoutSettings settings, TextStyle currentStyle)
        {
            Document = document;
            Engine = engine;
            Settings = settings;
            CurrentStyle = currentStyle;
            Layout = engine.Layout(document, settings);
            Navigator = new CaretNavigator(document, Layout);
        }

        public void Rebuild()
        {
            Layout = Engine.Layout(Document, Settings);
            Navigator.Layout = Layout;
            Notify();
        }

        private void Notify() => Changed?.Invoke(this, EventArgs.Empty);

        // ----------------------------------------------------------- editing

        public void InsertText(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            DeleteSelectionIfAny();
            Caret.Position = DocumentEditor.InsertText(Document, Caret.Position, text, CurrentStyle);
            ResetCaretAfterEdit();
            Rebuild();
        }

        public void InsertParagraph()
        {
            DeleteSelectionIfAny();
            Caret.Position = DocumentEditor.InsertParagraph(Document, Caret.Position);
            ResetCaretAfterEdit();
            Rebuild();
        }

        public void Backspace()
        {
            if (DeleteSelectionIfAny()) { Rebuild(); return; }
            Caret.Position = DocumentEditor.Backspace(Document, Caret.Position);
            ResetCaretAfterEdit();
            Rebuild();
        }

        public void DeleteForward()
        {
            if (DeleteSelectionIfAny()) { Rebuild(); return; }
            Caret.Position = DocumentEditor.DeleteForward(Document, Caret.Position);
            ResetCaretAfterEdit();
            Rebuild();
        }

        private bool DeleteSelectionIfAny()
        {
            if (!Caret.HasSelection) return false;

            var (start, end) = GetSelection();
            Caret.Position = DocumentEditor.DeleteRange(Document, start, end);
            ResetCaretAfterEdit();
            return true;
        }

        private void ResetCaretAfterEdit()
        {
            Caret.CollapseSelection();
            Caret.Affinity = CaretAffinity.Before;
            Caret.GoalX = null;
        }

        // ---------------------------------------------------------- selection

        public (TextPosition start, TextPosition end) GetSelection()
        {
            var a = Caret.Anchor ?? Caret.Position;
            var b = Caret.Position;
            return a <= b ? (a, b) : (b, a);
        }

        // ---------------------------------------------------------- movement

        public void MoveLeft(bool extend = false) => Move(Navigator.MoveLeft, extend);
        public void MoveRight(bool extend = false) => Move(Navigator.MoveRight, extend);
        public void MoveUp(bool extend = false) => Move(Navigator.MoveUp, extend);
        public void MoveDown(bool extend = false) => Move(Navigator.MoveDown, extend);
        public void MoveLineStart(bool extend = false) => Move(Navigator.MoveLineStart, extend);
        public void MoveLineEnd(bool extend = false) => Move(Navigator.MoveLineEnd, extend);
        public void MovePageUp(bool extend = false) => Move(Navigator.MovePageUp, extend);
        public void MovePageDown(bool extend = false) => Move(Navigator.MovePageDown, extend);
        public void MoveDocumentStart(bool extend = false) => Move(Navigator.MoveDocumentStart, extend);
        public void MoveDocumentEnd(bool extend = false) => Move(Navigator.MoveDocumentEnd, extend);

        private void Move(Action<Caret> move, bool extend)
        {
            if (extend) Caret.Anchor ??= Caret.Position;
            else Caret.Anchor = null;

            move(Caret);

            if (Caret.Anchor == Caret.Position) Caret.Anchor = null;
            Notify();
        }

        public void MoveToPoint(PointF documentPoint, bool extend)
        {
            if (extend) Caret.Anchor ??= Caret.Position;
            else Caret.Anchor = null;

            Navigator.MoveToPoint(Caret, documentPoint);

            if (Caret.Anchor == Caret.Position) Caret.Anchor = null;
            Notify();
        }
    }
}
