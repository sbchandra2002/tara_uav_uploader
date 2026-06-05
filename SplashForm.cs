using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace TARAFlasher
{
    public class SplashForm : Form
    {
        // ── Timing (1 tick = 40 ms) ──────────────────────────────────────────
        private const int T_FadeIn    = 12;
        private const int T_LogoIn    = 32;
        private const int T_TitleType = 58;
        private const int T_Status    = 80;
        private const int T_Progress  = 102;
        private const int T_Hold      = 115;
        private const int T_FadeOut   = 130;
        private const int T_Done      = 131;

        private readonly Timer _timer = new Timer { Interval = 40 };
        private int   _tick;
        private float _logoScale;
        private float _glowPulse;
        private int   _titleChars;
        private int   _statusCount;
        private float _progress;

        // JARVIS ring angles
        private float _r1;   // inner segments, CW
        private float _r2;   // middle data ring, CCW
        private float _r3;   // outer scan sweep, CW

        private const string FullTitle = "TARA-Flasher";
        private static readonly string[] BootLines =
        {
            "Initializing hardware interface...",
            "Loading firmware profiles...",
            "Verifying CLI configuration...",
            "All systems ready."
        };

        private Image _logo;

        private readonly Color _accent = Color.FromArgb(32, 185, 255);
        private readonly Color _green  = Color.FromArgb(0, 220, 150);
        private readonly Color _bg     = Color.FromArgb(18, 22, 28);
        private readonly Color _dim    = Color.FromArgb(70, 110, 140);

        public SplashForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition   = FormStartPosition.CenterScreen;
            ClientSize      = new Size(740, 400);
            BackColor       = _bg;
            ShowInTaskbar   = false;
            Opacity         = 0;
            TopMost         = true;
            DoubleBuffered  = true;     // eliminates all flicker

            try { Icon = new Icon("logo.ico"); } catch { }
            try { _logo = Image.FromFile("logo.png"); } catch { }

            MouseClick += (s, e) => { _timer.Stop(); Close(); };
            _timer.Tick += OnTick;
            _timer.Start();
        }

        // ── Elastic-out easing ───────────────────────────────────────────────
        private static float ElasticOut(float t)
        {
            if (t <= 0) return 0f;
            if (t >= 1) return 1f;
            const float p = 0.40f;
            return (float)(Math.Pow(2, -10 * t) * Math.Sin((t - p / 4f) * 2 * Math.PI / p) + 1);
        }

        // ── Timer tick ───────────────────────────────────────────────────────
        private void OnTick(object sender, EventArgs e)
        {
            _tick++;

            if (_tick <= T_FadeIn)
                Opacity = (double)_tick / T_FadeIn;

            if (_tick > T_FadeIn && _tick <= T_LogoIn)
                _logoScale = ElasticOut((float)(_tick - T_FadeIn) / (T_LogoIn - T_FadeIn));
            else if (_tick > T_LogoIn)
                _logoScale = 1f;

            if (_tick > T_LogoIn)
            {
                _glowPulse = (float)(0.42 + 0.30 * Math.Sin(_tick * 0.11));

                // Advance ring angles
                _r1 = (_r1 + 1.4f) % 360f;
                _r2 = (_r2 - 0.65f + 360f) % 360f;
                _r3 = (_r3 + 0.38f) % 360f;
            }

            if (_tick > T_LogoIn && _tick <= T_TitleType)
            {
                int ph = _tick - T_LogoIn, tot = T_TitleType - T_LogoIn;
                _titleChars = Math.Min((ph * FullTitle.Length + tot - 1) / tot, FullTitle.Length);
            }
            else if (_tick > T_TitleType)
                _titleChars = FullTitle.Length;

            if (_tick > T_TitleType && _tick <= T_Status)
            {
                int ph = _tick - T_TitleType, tot = T_Status - T_TitleType;
                _statusCount = Math.Min(ph * BootLines.Length / tot + 1, BootLines.Length);
            }
            else if (_tick > T_Status)
                _statusCount = BootLines.Length;

            if (_tick > T_Status && _tick <= T_Progress)
                _progress = (float)(_tick - T_Status) / (T_Progress - T_Status);
            else if (_tick > T_Progress)
                _progress = 1f;

            if (_tick > T_Hold && _tick <= T_FadeOut)
                Opacity = 1.0 - (double)(_tick - T_Hold) / (T_FadeOut - T_Hold);

            if (_tick >= T_Done) { _timer.Stop(); Close(); return; }
            Invalidate();
        }

        // ── Paint (directly on form — no child panel, no extra erase) ────────
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode     = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.CompositingQuality = CompositingQuality.HighQuality;

            int w = Width, h = Height;
            DrawBackground(g, w, h);
            DrawLogoArea(g, cx: 205, cy: h / 2, size: 200);
            DrawRightPanel(g, x: 430, w: w, h: h);
            DrawBorder(g, w, h);

        }

        private void DrawBackground(Graphics g, int w, int h)
        {
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(-80, -80, w + 160, h + 160);
                using (var pgb = new PathGradientBrush(path))
                {
                    pgb.CenterPoint    = new PointF(w * 0.27f, h * 0.5f);
                    pgb.CenterColor    = Color.FromArgb(28, 36, 50);
                    pgb.SurroundColors = new[] { Color.FromArgb(8, 12, 18) };
                    g.FillRectangle(pgb, 0, 0, w, h);
                }
            }
        }

        private void DrawLogoArea(Graphics g, int cx, int cy, int size)
        {
            if (_logoScale < 0.01f) return;
            int half = (int)(size / 2 * _logoScale);
            if (half < 2) return;

            // Ambient glow layers
            for (int layer = 4; layer >= 1; layer--)
            {
                int r     = half + layer * 18;
                int alpha = (int)(_glowPulse * (28 - layer * 5));
                if (alpha <= 0) continue;
                using (var gp = new GraphicsPath())
                {
                    gp.AddEllipse(cx - r, cy - r, r * 2, r * 2);
                    using (var pgb = new PathGradientBrush(gp))
                    {
                        pgb.CenterColor    = Color.FromArgb(alpha, 32, 185, 255);
                        pgb.SurroundColors = new[] { Color.Transparent };
                        g.FillPath(pgb, gp);
                    }
                }
            }

            // JARVIS rings (fade in after logo)
            if (_tick > T_LogoIn)
            {
                float ringAlpha = Math.Min(1f, (float)(_tick - T_LogoIn) / 18f);
                DrawJarvisRings(g, cx, cy, half, ringAlpha);
            }

            // Logo clipped to circle
            if (_logo != null)
            {
                using (var clip = new GraphicsPath())
                {
                    clip.AddEllipse(cx - half, cy - half, half * 2, half * 2);
                    g.SetClip(clip, CombineMode.Intersect);
                    g.DrawImage(_logo, cx - half, cy - half, half * 2, half * 2);
                    g.ResetClip();
                }
                using (var pen = new Pen(Color.FromArgb(160, 32, 185, 255), 2f))
                    g.DrawEllipse(pen, cx - half, cy - half, half * 2, half * 2);
            }
            else
            {
                using (var fb = new SolidBrush(Color.FromArgb(36, 41, 51)))
                    g.FillEllipse(fb, cx - half, cy - half, half * 2, half * 2);
                using (var fp = new Pen(_accent, 3))
                    g.DrawEllipse(fp, cx - half, cy - half, half * 2, half * 2);
            }
        }

        // ── JARVIS HUD rings ─────────────────────────────────────────────────
        private void DrawJarvisRings(Graphics g, int cx, int cy, int half, float fade)
        {
            int A(int b) => Math.Max(0, (int)(b * fade));

            // ── Ring 1 · inner 4-segment ring (CW) ──────────────────────────
            {
                int r = half + 18;
                var rect = new RectangleF(cx - r, cy - r, r * 2, r * 2);
                using (var pen = new Pen(Color.FromArgb(A(210), 32, 185, 255), 2.2f))
                    for (int i = 0; i < 4; i++)
                        g.DrawArc(pen, rect, _r1 + i * 90f, 68f);

                // Accent dots at segment start points
                using (var db = new SolidBrush(Color.FromArgb(A(255), 170, 240, 255)))
                    for (int i = 0; i < 4; i++)
                    {
                        double rad = (_r1 + i * 90f) * Math.PI / 180.0;
                        g.FillEllipse(db,
                            cx + (float)(r * Math.Cos(rad)) - 3,
                            cy + (float)(r * Math.Sin(rad)) - 3, 6, 6);
                    }
            }

            // ── Ring 2 · data ring with tick marks (CCW) ────────────────────
            {
                int r = half + 42;
                var rect = new RectangleF(cx - r, cy - r, r * 2, r * 2);
                // 6 segments of 48°, gap of 12°
                using (var pen = new Pen(Color.FromArgb(A(150), 32, 185, 255), 1.6f))
                    for (int i = 0; i < 6; i++)
                        g.DrawArc(pen, rect, _r2 + i * 60f, 48f);

                // Major tick marks every 30°
                using (var tp = new Pen(Color.FromArgb(A(90), 32, 185, 255), 1f))
                    for (int i = 0; i < 12; i++)
                    {
                        double rad = (_r2 + i * 30f) * Math.PI / 180.0;
                        bool major = i % 3 == 0;
                        float ri = r - (major ? 9 : 5), ro = r + (major ? 5 : 3);
                        g.DrawLine(tp,
                            cx + (float)(ri * Math.Cos(rad)), cy + (float)(ri * Math.Sin(rad)),
                            cx + (float)(ro * Math.Cos(rad)), cy + (float)(ro * Math.Sin(rad)));
                    }
            }

            // ── Ring 3 · scanning sweep ring (CW) ───────────────────────────
            {
                int r = half + 70;
                var rect = new RectangleF(cx - r, cy - r, r * 2, r * 2);

                // Full base ring (faint)
                using (var pen = new Pen(Color.FromArgb(A(35), 32, 185, 255), 1f))
                    g.DrawEllipse(pen, rect);

                // Bright leading sweep arc
                using (var pen = new Pen(Color.FromArgb(A(220), 0, 210, 255), 2.5f))
                    g.DrawArc(pen, rect, _r3, 75f);

                // Fading tail
                using (var pen = new Pen(Color.FromArgb(A(65), 0, 170, 220), 1.5f))
                    g.DrawArc(pen, rect, _r3 + 75f, 55f);

                // Tick marks every 15°
                using (var tp = new Pen(Color.FromArgb(A(55), 32, 185, 255), 1f))
                    for (int i = 0; i < 24; i++)
                    {
                        double rad = (_r3 + i * 15f) * Math.PI / 180.0;
                        bool major = i % 6 == 0;
                        float ri = r - (major ? 10 : 5);
                        g.DrawLine(tp,
                            cx + (float)(ri * Math.Cos(rad)), cy + (float)(ri * Math.Sin(rad)),
                            cx + (float)(r  * Math.Cos(rad)), cy + (float)(r  * Math.Sin(rad)));
                    }

                // Bright node dots at cardinal points
                using (var db = new SolidBrush(Color.FromArgb(A(190), 32, 185, 255)))
                    for (int i = 0; i < 4; i++)
                    {
                        double rad = (_r3 + i * 90f + 38f) * Math.PI / 180.0;
                        g.FillEllipse(db,
                            cx + (float)(r * Math.Cos(rad)) - 3,
                            cy + (float)(r * Math.Sin(rad)) - 3, 6, 6);
                    }
            }

            // ── Ring 4 · outermost twin arcs (CCW, slow) ────────────────────
            {
                int r = half + 92;
                var rect = new RectangleF(cx - r, cy - r, r * 2, r * 2);
                float a4 = _r2 * 0.45f;
                using (var pen = new Pen(Color.FromArgb(A(75), 32, 185, 255), 1f))
                {
                    g.DrawArc(pen, rect, a4, 115f);
                    g.DrawArc(pen, rect, a4 + 180f, 115f);
                }
                // Small bright notch at each arc end
                using (var nb = new SolidBrush(Color.FromArgb(A(160), 32, 185, 255)))
                    for (int i = 0; i < 4; i++)
                    {
                        double rad = (a4 + i * 90f + (i % 2 == 0 ? 0 : 115f)) * Math.PI / 180.0;
                        g.FillEllipse(nb,
                            cx + (float)(r * Math.Cos(rad)) - 2,
                            cy + (float)(r * Math.Sin(rad)) - 2, 5, 5);
                    }
            }
        }

        // ── Right panel: title, status, progress ─────────────────────────────
        private void DrawRightPanel(Graphics g, int x, int w, int h)
        {
            int titleY = 68;

            if (_titleChars > 0)
            {
                string display = FullTitle.Substring(0, _titleChars);
                using (var font = new Font("Segoe UI", 30F, FontStyle.Bold))
                {
                    using (var shadow = new SolidBrush(Color.FromArgb(50, 0, 0, 0)))
                        g.DrawString(display, font, shadow, x + 2f, titleY + 2f);
                    using (var tb = new LinearGradientBrush(
                        new RectangleF(x, titleY, 280, 50),
                        Color.FromArgb(165, 230, 255), _accent,
                        LinearGradientMode.Vertical))
                        g.DrawString(display, font, tb, x, titleY);
                }

                using (var sf = new Font("Segoe UI", 9F))
                using (var sb = new SolidBrush(Color.FromArgb(68, 115, 150)))
                    g.DrawString("Firmware Flasher  ·  v1.0", sf, sb, (float)x, titleY + 50f);

                using (var dp = new Pen(Color.FromArgb(38, 32, 185, 255), 1))
                    g.DrawLine(dp, x, titleY + 72, x + 255, titleY + 72);
            }

            // Boot status lines
            using (var sf = new Font("Consolas", 8.2F))
                for (int i = 0; i < _statusCount && i < BootLines.Length; i++)
                {
                    bool cur = i == _statusCount - 1;
                    using (var sb = new SolidBrush(cur ? _green : Color.FromArgb(38, 98, 72)))
                        g.DrawString((cur ? ">> " : "   ") + BootLines[i],
                                     sf, sb, (float)x, titleY + 84f + i * 19f);
                }

            // Progress bar
            int bx = x, by = h - 52, bw = w - x - 32, bh = 5;
            using (var track = new SolidBrush(Color.FromArgb(16, 40, 58)))
                g.FillRectangle(track, bx, by, bw, bh);

            int fw = (int)(bw * _progress);
            if (fw > 1)
            {
                using (var fill = new LinearGradientBrush(
                    new Rectangle(bx, by, Math.Max(1, fw), bh),
                    Color.FromArgb(0, 210, 255), _accent, LinearGradientMode.Horizontal))
                    g.FillRectangle(fill, bx, by, fw, bh);

                int tw = Math.Min(32, fw);
                using (var tip = new LinearGradientBrush(
                    new Rectangle(bx + fw - tw, by - 2, tw + 2, bh + 4),
                    Color.FromArgb(200, 255, 255, 255), Color.Transparent,
                    LinearGradientMode.Horizontal))
                    g.FillRectangle(tip, bx + fw - tw, by - 2, tw + 2, bh + 4);
            }

            if (_progress > 0.02f)
                using (var pf = new Font("Consolas", 7.5F))
                using (var pb = new SolidBrush(_dim))
                    g.DrawString($"{(int)(_progress * 100)}%", pf, pb, bx + bw + 6f, by - 3f);

        }

        private void DrawBorder(Graphics g, int w, int h)
        {
            using (var pen = new Pen(Color.FromArgb(42, 32, 185, 255), 1.5f))
                g.DrawRectangle(pen, 1, 1, w - 3, h - 3);

            // Top & bottom glow lines
            using (var lb = new LinearGradientBrush(new Point(0, 0), new Point(w, 0),
                       Color.Transparent, Color.Transparent))
            {
                var cb = new ColorBlend(3)
                {
                    Colors    = new[] { Color.Transparent, Color.FromArgb(115, 32, 185, 255), Color.Transparent },
                    Positions = new[] { 0f, 0.5f, 1f }
                };
                lb.InterpolationColors = cb;
                g.FillRectangle(lb, 0, 0, w, 2);
                g.FillRectangle(lb, 0, h - 2, w, 2);
            }
        }

        // ── Suppress default background erase (prevents the white flash) ─────
        protected override void OnPaintBackground(PaintEventArgs e) { /* intentionally empty */ }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE
                return cp;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _timer.Dispose(); _logo?.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
