using System.Drawing;
using System.Text;
using System.Windows.Forms;
using System.Runtime.InteropServices;

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
        private readonly Panel _viewport;
        private bool _caretVisible = true;
        private bool _initialized;
        private float _zoom = 1f;
        private StatusStrip? _statusStrip;
        private ToolStripStatusLabel? _statusLabel;
        private ToolStripStatusLabel? _zoomLabel;
        private ToolStripButton? _zoomOutButton;
        private ToolStripButton? _zoomInButton;

        private const float MinZoom = 0.4f;
        private const float MaxZoom = 4f;
        private const float ZoomStep = 1.1f;

        // IME (Input Method Editor) support for Korean/other languages
        private const int WM_IME_COMPOSITION = 0x010F;
        private const int GCS_COMPSTR = 0x0008;
        private const int GCS_RESULTSTR = 0x0009;

        [DllImport("imm32.dll")]
        private static extern IntPtr ImmGetContext(IntPtr hwnd);

        [DllImport("imm32.dll")]
        private static extern bool ImmReleaseContext(IntPtr hwnd, IntPtr himc);

        [DllImport("imm32.dll", CharSet = CharSet.Unicode)]
        private static extern int ImmGetCompositionString(IntPtr himc, int dwIndex, StringBuilder lpBuf, int dwBufLen);

        private string _compositionText = string.Empty;
        private TextPosition _compositionStart = TextPosition.Start;

        public Form1()
        {
            InitializeComponent();

            KeyPreview = true;
            ImeMode = ImeMode.On;
            Text = "CX2Word - 구조???�드?�로?�서";
            BackColor = Color.FromArgb(0x5F, 0x5F, 0x5F);

            _measurer = new DirectWriteTextMeasurer();
            _settings = new LayoutSettings();

            _viewport = new DocumentViewport
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = Color.FromArgb(0x5F, 0x5F, 0x5F)
            };

            _renderer = new DocumentRenderer(_measurer, _viewport);

            var engine = new LayoutEngine(_measurer);
            var document = Document.CreateSample();
            var typeStyle = new TextStyle { FontFamily = "Malgun Gothic", FontSize = 14f };

            _session = new EditorSession(document, engine, _settings, typeStyle);
            _session.Changed += (_, _) => { UpdateScrollSize(); EnsureCaretVisible(); UpdateStatusBar(); _viewport.Invalidate(); };

            _keyboard = new KeyboardController(_session);
            _mouse = new MouseController(_session, ScreenToDocument);

            BuildStatusBar();

            Controls.Add(_viewport);
            Controls.Add(_statusStrip!);

            _caretTimer = new System.Windows.Forms.Timer { Interval = 530 };
            _caretTimer.Tick += (_, _) => { _caretVisible = !_caretVisible; _viewport.Invalidate(); };
            _caretTimer.Start();

            _viewport.Paint += (_, _) =>
            {
                if (_session.Layout is null) return;
                _renderer.EnsureInitialized();
                _renderer.Render(_session, _caretVisible, PageLeft, _viewport.AutoScrollPosition.X, _viewport.AutoScrollPosition.Y, _zoom);
            };
            _viewport.Resize += (_, _) =>
            {
                _renderer.Resize(_viewport.ClientSize.Width, _viewport.ClientSize.Height);
                UpdateScrollSize();
                _viewport.Invalidate();
            };
            _viewport.Scroll += (_, _) => _viewport.Invalidate();

            _viewport.MouseDown += OnViewportMouseDown;
            _viewport.MouseMove += OnViewportMouseMove;
            _viewport.MouseUp += OnViewportMouseUp;

            Shown += (_, _) => { _viewport.Focus(); UpdateScrollSize(); UpdateZoomLabel(); UpdateStatusBar(); };

            _initialized = true;
            UpdateScrollSize();
            UpdateZoomLabel();
            UpdateStatusBar();
        }

        private float PageLeft => Math.Max(24f, (_viewport.ClientSize.Width - _settings.PageSize.Width * _zoom) / 2f);

        private PointF ScreenToDocument(Point client) =>
            new((client.X - PageLeft - _viewport.AutoScrollPosition.X) / _zoom, (client.Y - _viewport.AutoScrollPosition.Y) / _zoom);

        // ------------------------------------------------------------ zoom

        private void ZoomIn() => SetZoom(_zoom * ZoomStep);

        private void ZoomOut() => SetZoom(_zoom / ZoomStep);

        private void SetZoom(float zoom)
        {
            _zoom = Math.Clamp(zoom, MinZoom, MaxZoom);
            UpdateZoomLabel();
            UpdateScrollSize();
            EnsureCaretVisible();
            _viewport.Invalidate();
        }

        private void UpdateZoomLabel()
        {
            if (_zoomLabel is null) return;
            _zoomLabel.Text = $"{Math.Round(_zoom * 100)}%";
        }

        // -------------------------------------------------------- status bar

        private void BuildStatusBar()
        {
            _statusStrip = new StatusStrip { Dock = DockStyle.Bottom, SizingGrip = false };
            _statusStrip.BackColor = Color.FromArgb(0xF0, 0xF0, 0xF0);
            _statusStrip.ForeColor = Color.FromArgb(0x40, 0x40, 0x40);

            _statusLabel = new ToolStripStatusLabel
            {
                Spring = true,
                TextAlign = ContentAlignment.MiddleLeft
            };

            _zoomOutButton = new ToolStripButton("-");
            _zoomOutButton.Click += (_, _) => { ZoomOut(); _viewport.Focus(); };

            _zoomLabel = new ToolStripStatusLabel
            {
                TextAlign = ContentAlignment.MiddleRight
            };

            _zoomInButton = new ToolStripButton("+");
            _zoomInButton.Click += (_, _) => { ZoomIn(); _viewport.Focus(); };

            _statusStrip.Items.Add(_statusLabel);
            _statusStrip.Items.Add(_zoomOutButton);
            _statusStrip.Items.Add(_zoomLabel);
            _statusStrip.Items.Add(_zoomInButton);
        }

        private void UpdateStatusBar()
        {
            if (_statusLabel is null || _session is null || _session.Layout is null) return;

            var layout = _session.Layout;
            _statusLabel.Text = $"페이지 {layout.Pages.Count} · 줄 {layout.LineCount}";
        }

        private void UpdateScrollSize()
        {
            if (!_initialized || _session is null || _session.Layout is null) return;

            var layout = _session.Layout;
            float totalHeight = (layout.Pages.Count > 0 ? layout.Pages[^1].Bottom : 0) * _zoom;
            _viewport.AutoScrollMinSize = new Size((int)(_settings.PageSize.Width * _zoom) + 96, (int)(totalHeight + 48));
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_IME_COMPOSITION)
            {
                IntPtr himc = ImmGetContext(Handle);
                if (himc != IntPtr.Zero)
                {
                    try
                    {
                        int resultLen = ImmGetCompositionString(himc, GCS_RESULTSTR, null, 0);
                        if (resultLen > 0)
                        {
                            int oldLen = _compositionText.Length;
                            for (int i = 0; i < oldLen; i++)
                            {
                                _session.Backspace();
                            }
                            StringBuilder resultSb = new StringBuilder(resultLen / 2 + 1);
                            ImmGetCompositionString(himc, GCS_RESULTSTR, resultSb, resultLen);
                            string resultText = resultSb.ToString(0, resultLen / 2);
                            if (!string.IsNullOrEmpty(resultText))
                            {
                                _session.InsertText(resultText);
                            }
                            _compositionText = string.Empty;
                            _caretVisible = true;
                            _viewport.Invalidate();
                            EnsureCaretVisible();
                            UpdateStatusBar();
                            base.WndProc(ref m);
                            return;
                        }

                        int compLen2 = ImmGetCompositionString(himc, GCS_COMPSTR, null, 0);
                        if (compLen2 > 0)
                        {
                            StringBuilder compSb = new StringBuilder(compLen2 / 2 + 1);
                            ImmGetCompositionString(himc, GCS_COMPSTR, compSb, compLen2);
                            string compText = compSb.ToString(0, compLen2 / 2);
                            if (compText != _compositionText)
                            {
                                int oldLen = _compositionText.Length;
                                for (int i = 0; i < oldLen; i++)
                                {
                                    _session.Backspace();
                                }
                                if (!string.IsNullOrEmpty(compText))
                                {
                                    _session.InsertText(compText);
                                }
                                _compositionText = compText;
                                _caretVisible = true;
                                _viewport.Invalidate();
                                EnsureCaretVisible();
                            }
                            base.WndProc(ref m);
                            return;
                        }
                        else if (!string.IsNullOrEmpty(_compositionText))
                        {
                            int oldLen = _compositionText.Length;
                            for (int i = 0; i < oldLen; i++)
                            {
                                _session.Backspace();
                            }
                            _compositionText = string.Empty;
                            _caretVisible = true;
                            _viewport.Invalidate();
                            EnsureCaretVisible();
                        }
                    }
                    finally
                    {
                        ImmReleaseContext(Handle, himc);
                    }
                }
            }
            base.WndProc(ref m);
        }

        /// <summary>Keeps the caret on screen by scrolling the least possible amount.</summary>
        private void EnsureCaretVisible()
        {
            if (!_initialized || _session is null || _session.Layout is null) return;

            var bounds = _session.Layout.GetCaretBounds(_session.Caret.Position, _session.Caret.Affinity);
            if (bounds == RectangleF.Empty) return;

            const int margin = 24;

            float left = bounds.X * _zoom + PageLeft + _viewport.AutoScrollPosition.X;
            float top = bounds.Y * _zoom + _viewport.AutoScrollPosition.Y;

            int deltaX = 0, deltaY = 0;
            if (left < margin) deltaX = (int)(left - margin);
            else if (left > _viewport.ClientSize.Width - margin) deltaX = (int)(left - (_viewport.ClientSize.Width - margin));

            if (top < margin) deltaY = (int)(top - margin);
            else if (top > _viewport.ClientSize.Height - margin) deltaY = (int)(top - (_viewport.ClientSize.Height - margin));

            if (deltaX != 0 || deltaY != 0)
                _viewport.AutoScrollPosition = new Point(_viewport.AutoScrollPosition.X - deltaX, _viewport.AutoScrollPosition.Y - deltaY);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            var key = keyData & Keys.KeyCode;
            bool shift = (keyData & Keys.Shift) == Keys.Shift;
            bool control = (keyData & Keys.Control) == Keys.Control;

            if (control)
            {
                if (key == Keys.Oemplus || key == Keys.Add) { ZoomIn(); return true; }
                if (key == Keys.OemMinus || key == Keys.Subtract) { ZoomOut(); return true; }
            }

            if (_keyboard.HandleKeyDown(key, shift, control))
            {
                _caretVisible = true;
                _viewport.Invalidate();
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
                _viewport.Invalidate();
                e.Handled = true;
            }
        }

        private void OnViewportMouseDown(object? sender, MouseEventArgs e)
        {
            _viewport.Focus();
            _viewport.Capture = true;
            _mouse.OnMouseDown(e.Location, (ModifierKeys & Keys.Shift) == Keys.Shift);
            _caretVisible = true;
            _viewport.Invalidate();
        }

        private void OnViewportMouseMove(object? sender, MouseEventArgs e)
        {
            if (!_mouse.IsDragging) return;
            _mouse.OnMouseMove(e.Location);
            _viewport.Invalidate();
        }

        private void OnViewportMouseUp(object? sender, MouseEventArgs e)
        {
            _mouse.OnMouseUp();
            _viewport.Capture = false;
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            _viewport.Focus();
            _caretTimer.Start();
            _caretVisible = true;
            _viewport.Invalidate();
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            _caretTimer.Stop();
            _caretVisible = false;
            _viewport.Invalidate();
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