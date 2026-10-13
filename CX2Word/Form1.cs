using System.Drawing;
using System.Windows.Forms;

namespace CX2Word
{
    public partial class Form1 : Form
    {
        private readonly DirectWriteTextMeasurer _measurer;
        private readonly LayoutSettings _settings;
        private readonly EditorSession _session;
        private readonly DocumentRenderer _renderer;
        private readonly KeyboardController _keyboard;
        private readonly MouseController _mouse;
        private readonly System.Windows.Forms.Timer _caretTimer;
        private bool _caretVisible = true;
        private bool _initialized;

        public Form1()
        {
            InitializeComponent();

            DoubleBuffered = false;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.Opaque | ControlStyles.UserPaint, true);
            KeyPreview = true;
            Text = "CX2Word - 구조적 워드프로세서";
            BackColor = Color.FromArgb(0x5F, 0x5F, 0x5F);
            AutoScroll = true;

            _measurer = new DirectWriteTextMeasurer();
            _settings = new LayoutSettings();
            _renderer = new DocumentRenderer(_measurer, this);

            var engine = new LayoutEngine(_measurer);
            var document = Document.CreateSample();
            var typeStyle = new TextStyle { FontFamily = "Malgun Gothic", FontSize = 14f };

            _session = new EditorSession(document, engine, _settings, typeStyle);
            _session.Changed += (_, _) => { UpdateScrollSize(); EnsureCaretVisible(); Invalidate(); };

            _keyboard = new KeyboardController(_session);
            _mouse = new MouseController(_session, ScreenToDocument);

            _caretTimer = new System.Windows.Forms.Timer { Interval = 530 };
            _caretTimer.Tick += (_, _) => { _caretVisible = !_caretVisible; Invalidate(); };
            _caretTimer.Start();

            _initialized = true;
            UpdateScrollSize();
        }

        private float PageLeft => Math.Max(24f, (ClientSize.Width - _settings.PageSize.Width) / 2f);

        private PointF ScreenToDocument(Point client) =>
            new(client.X - PageLeft - AutoScrollPosition.X, client.Y - AutoScrollPosition.Y);

        private void UpdateScrollSize()
        {
            if (!_initialized || _session is null || _session.Layout is null) return;

            var layout = _session.Layout;
            float totalHeight = layout.Pages.Count > 0 ? layout.Pages[^1].Bottom : 0;
            AutoScrollMinSize = new Size((int)_settings.PageSize.Width + 96, (int)(totalHeight + 48));
        }

        /// <summary>Keeps the caret on screen by scrolling the least possible amount.</summary>
        private void EnsureCaretVisible()
        {
            if (!_initialized || _session is null || _session.Layout is null) return;

            var bounds = _session.Layout.GetCaretBounds(_session.Caret.Position, _session.Caret.Affinity);
            if (bounds == RectangleF.Empty) return;

            const int margin = 24;

            float left = bounds.X + PageLeft + AutoScrollPosition.X;
            float top = bounds.Y + AutoScrollPosition.Y;

            int deltaX = 0, deltaY = 0;
            if (left < margin) deltaX = (int)(left - margin);
            else if (left > ClientSize.Width - margin) deltaX = (int)(left - (ClientSize.Width - margin));

            if (top < margin) deltaY = (int)(top - margin);
            else if (top > ClientSize.Height - margin) deltaY = (int)(top - (ClientSize.Height - margin));

            if (deltaX != 0 || deltaY != 0)
                AutoScrollPosition = new Point(AutoScrollPosition.X - deltaX, AutoScrollPosition.Y - deltaY);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            var key = keyData & Keys.KeyCode;
            bool shift = (keyData & Keys.Shift) == Keys.Shift;
            bool control = (keyData & Keys.Control) == Keys.Control;

            if (_keyboard.HandleKeyDown(key, shift, control))
            {
                _caretVisible = true;
                Invalidate();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            base.OnKeyPress(e);
            if (_keyboard.HandleKeyPress(e.KeyChar))
            {
                _caretVisible = true;
                Invalidate();
                e.Handled = true;
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            Capture = true;
            _mouse.OnMouseDown(e.Location, (ModifierKeys & Keys.Shift) == Keys.Shift);
            _caretVisible = true;
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!_mouse.IsDragging) return;
            _mouse.OnMouseMove(e.Location);
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _mouse.OnMouseUp();
            Capture = false;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (!_initialized || _session is null || _session.Layout is null) return;

            _renderer.EnsureInitialized();
            _renderer.Render(_session, _caretVisible, PageLeft, AutoScrollPosition.X, AutoScrollPosition.Y);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (!_initialized) return;
            _renderer.Resize(Math.Max(ClientSize.Width, 1), Math.Max(ClientSize.Height, 1));
            UpdateScrollSize();
            Invalidate();
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            _caretTimer.Start();
            _caretVisible = true;
            Invalidate();
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            _caretTimer.Stop();
            _caretVisible = false;
            Invalidate();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            _caretTimer.Dispose();
            _renderer.Dispose();
            _measurer.Dispose();
        }
    }
}