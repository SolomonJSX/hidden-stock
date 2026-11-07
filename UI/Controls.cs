using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace HiddenStock.UI;

internal class CardPanel : Panel
{
    public int Radius { get; set; } = 16;
    public Color Fill { get; set; } = Theme.Surface;
    public Color BorderColor { get; set; } = Theme.Stroke;
    public bool Dashed { get; set; }

    public CardPanel()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Bg;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(BackColor);

        if (Width < 4 || Height < 4)
            return;

        var rect = new RectangleF(1f, 1f, Width - 3f, Height - 3f);
        using var path = Theme.Rounded(rect, Radius);
        using var brush = new SolidBrush(Fill);
        g.FillPath(brush, path);

        using var pen = new Pen(BorderColor, Dashed ? 1.6f : 1f);
        if (Dashed)
            pen.DashStyle = DashStyle.Dash;
        g.DrawPath(pen, path);
    }
}

internal sealed class DropZone : CardPanel
{
    public event Action<string[]>? Dropped;

    private readonly Label _label;

    public DropZone()
    {
        Dashed = true;
        Radius = 14;
        AllowDrop = true;

        _label = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Theme.Muted,
            BackColor = Theme.Surface,
            Font = Theme.Font(11f),
            Text = "Перетащите сюда файлы или папки"
        };
        Controls.Add(_label);

        DragEnter += OnDragEnter;
        DragLeave += OnDragLeave;
        DragDrop += OnDragDrop;
    }

    public void SetCaption(string text) => _label.Text = text;

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true)
        {
            e.Effect = DragDropEffects.Copy;
            BorderColor = Theme.Accent;
            _label.ForeColor = Theme.Text;
            Invalidate();
        }
    }

    private void OnDragLeave(object? sender, EventArgs e)
    {
        BorderColor = Theme.Stroke;
        _label.ForeColor = Theme.Muted;
        Invalidate();
    }

    private void OnDragDrop(object? sender, DragEventArgs e)
    {
        BorderColor = Theme.Stroke;
        _label.ForeColor = Theme.Muted;
        Invalidate();

        if (e.Data?.GetData(DataFormats.FileDrop) is string[] items)
            Dropped?.Invoke(items);
    }
}

internal sealed class ModernButton : Button
{
    private bool _hover;
    private bool _pressed;

    public bool Accent { get; set; } = true;
    public bool Danger { get; set; }
    public bool Ghost { get; set; }
    public int Radius { get; set; } = 10;

    public ModernButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        Font = Theme.Font(9.5f, FontStyle.Bold);
        Height = 42;
        Cursor = Cursors.Hand;
        UseVisualStyleBackColor = false;
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hover = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = false;
        _pressed = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        _pressed = true;
        Invalidate();
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        _pressed = false;
        Invalidate();
        base.OnMouseUp(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? Theme.Bg);

        if (Width < 4 || Height < 4)
            return;

        var rect = new RectangleF(1f, 1f, Width - 3f, Height - 3f);
        using var path = Theme.Rounded(rect, Radius);

        if (Ghost)
        {
            using var brush = new SolidBrush(_hover ? Theme.SurfaceAlt : Color.Transparent);
            g.FillPath(brush, path);
            using var pen = new Pen(Danger && _hover ? Theme.Danger : Theme.Stroke);
            g.DrawPath(pen, path);
        }
        else if (Danger)
        {
            var color = !Enabled ? Theme.SurfaceAlt : _hover ? Color.FromArgb(245, 105, 105) : Theme.Danger;
            using var brush = new SolidBrush(color);
            g.FillPath(brush, path);
        }
        else if (Accent)
        {
            var color = !Enabled ? Theme.SurfaceAlt : _pressed ? Theme.Accent : _hover ? Theme.AccentHover : Theme.Accent;
            using var brush = new SolidBrush(color);
            g.FillPath(brush, path);
        }
        else
        {
            using var brush = new SolidBrush(_hover ? Theme.SurfaceAlt : Theme.Surface);
            g.FillPath(brush, path);
            using var pen = new Pen(Theme.Stroke);
            g.DrawPath(pen, path);
        }

        var textColor = !Enabled
            ? Theme.Muted
            : Ghost
                ? Danger ? Theme.Danger : Theme.Text
                : Accent || Danger
                    ? Color.White
                    : Theme.Text;

        TextRenderer.DrawText(g, Text, Font, Rectangle.Round(rect), textColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }
}

internal sealed class PromptForm : Form
{
    public string Value => _input.Text;

    private readonly TextBox _input;

    public PromptForm(string title, string caption, string expected)
    {
        Text = title;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        Font = Theme.Font(9.5f);
        ClientSize = new Size(420, 170);

        var label = new Label
        {
            Text = caption,
            ForeColor = Theme.Muted,
            Location = new Point(20, 18),
            Size = new Size(380, 40)
        };

        _input = new TextBox
        {
            Location = new Point(20, 62),
            Width = 380,
            BackColor = Theme.SurfaceAlt,
            ForeColor = Theme.Text,
            BorderStyle = BorderStyle.FixedSingle,
            Font = Theme.Font(11f)
        };

        var ok = new ModernButton
        {
            Text = "Подтвердить",
            Accent = true,
            Danger = true,
            Location = new Point(20, 108),
            Width = 180
        };
        ok.Click += (_, _) =>
        {
            if (!string.Equals(_input.Text.Trim(), expected, StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(this, $"Введите слово {expected} без ошибок.", "Не совпало",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            DialogResult = DialogResult.OK;
            Close();
        };

        var cancel = new ModernButton
        {
            Text = "Отмена",
            Accent = false,
            Location = new Point(212, 108),
            Width = 188
        };
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };

        Controls.Add(label);
        Controls.Add(_input);
        Controls.Add(ok);
        Controls.Add(cancel);
        AcceptButton = ok;
        CancelButton = cancel;
    }
}
