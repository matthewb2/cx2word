using System.Windows.Forms;

namespace CX2Word
{
    /// <summary>Translates keyboard input into EditorSession commands.</summary>
    public sealed class KeyboardController
    {
        private readonly EditorSession _session;

        public KeyboardController(EditorSession session) => _session = session;

        public bool HandleKeyDown(Keys key, bool shift, bool control)
        {
            switch (key)
            {
                case Keys.Left: _session.MoveLeft(shift); return true;
                case Keys.Right: _session.MoveRight(shift); return true;
                case Keys.Up: _session.MoveUp(shift); return true;
                case Keys.Down: _session.MoveDown(shift); return true;

                case Keys.Home:
                    if (control) _session.MoveDocumentStart(shift);
                    else _session.MoveLineStart(shift);
                    return true;

                case Keys.End:
                    if (control) _session.MoveDocumentEnd(shift);
                    else _session.MoveLineEnd(shift);
                    return true;

                case Keys.PageUp: _session.MovePageUp(shift); return true;
                case Keys.PageDown: _session.MovePageDown(shift); return true;

                case Keys.Back: _session.Backspace(); return true;
                case Keys.Delete: _session.DeleteForward(); return true;
                case Keys.Enter: _session.InsertParagraph(); return true;
            }

            return false;
        }

        public bool HandleKeyPress(char ch)
        {
            if (char.IsControl(ch)) return false;
            _session.InsertText(ch.ToString());
            return true;
        }
    }
}
