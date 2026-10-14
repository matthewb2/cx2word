using System.Drawing;
using System.Windows.Forms;

namespace CX2Word
{
    /// <summary>
    /// Scrollable host for the Direct2D surface. Non-selectable so keyboard focus
    /// stays on the form; style flags live in a subclass because SetStyle is protected.
    /// </summary>
    internal sealed class DocumentViewport : Panel
    {
        public DocumentViewport()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.Opaque | ControlStyles.UserPaint, true);
            SetStyle(ControlStyles.Selectable, true);
            TabStop = true;
            DoubleBuffered = false;
        }
    }
}