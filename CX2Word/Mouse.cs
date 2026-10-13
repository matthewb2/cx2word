using System.Drawing;

namespace CX2Word
{
    /// <summary>Translates pointer input into caret placement / click-and-drag selection.</summary>
    public sealed class MouseController
    {
        private readonly EditorSession _session;
        private readonly Func<Point, PointF> _toDocumentPoint;
        private bool _dragging;

        public MouseController(EditorSession session, Func<Point, PointF> toDocumentPoint)
        {
            _session = session;
            _toDocumentPoint = toDocumentPoint;
        }

        public bool IsDragging => _dragging;

        public void OnMouseDown(Point clientPoint, bool extendSelection)
        {
            _dragging = true;
            _session.MoveToPoint(_toDocumentPoint(clientPoint), extendSelection);
        }

        public void OnMouseMove(Point clientPoint)
        {
            if (!_dragging) return;
            _session.MoveToPoint(_toDocumentPoint(clientPoint), extend: true);
        }

        public void OnMouseUp() => _dragging = false;
    }
}