namespace ConsGHost.Host;

internal static class HostTheme
{
    internal static readonly Color Canvas = Color.FromArgb(17, 20, 29);
    internal static readonly Color Sidebar = Color.FromArgb(21, 24, 35);
    internal static readonly Color Surface = Color.FromArgb(26, 30, 43);
    internal static readonly Color Input = Color.FromArgb(19, 23, 34);
    internal static readonly Color Line = Color.FromArgb(45, 51, 71);
    internal static readonly Color Text = Color.FromArgb(239, 241, 250);
    internal static readonly Color Muted = Color.FromArgb(163, 173, 195);
    internal static readonly Color Accent = Color.FromArgb(180, 173, 255);
    internal static readonly Color Selected = Color.FromArgb(50, 47, 79);
    internal static readonly Color Good = Color.FromArgb(124, 222, 183);
    internal static readonly Color Warning = Color.FromArgb(247, 202, 128);
    internal static readonly Color Error = Color.FromArgb(255, 151, 162);

    internal static readonly Font Body = new("Microsoft YaHei UI", 9.5f);
    internal static readonly Font Small = new(Body.FontFamily, 8.5f);
    internal static readonly Font Strong = new(Body.FontFamily, 10, FontStyle.Bold);
    internal static readonly Font Heading = new(Body.FontFamily, 20, FontStyle.Bold);
    internal static readonly Font Metric = new("Segoe UI", 19, FontStyle.Bold);
    internal static readonly Font Mono = new("Consolas", 10);

    internal static Label Caption(string text, Font? font = null, Color? color = null) => new()
    {
        Text = text, Font = font ?? Body, ForeColor = color ?? Text, AutoSize = false,
        Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true,
        Margin = Padding.Empty, UseMnemonic = false
    };

    internal static Button Action(string text, bool primary = false) => new HostButton(primary)
    {
        Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
        MinimumSize = new(0, 34), Padding = new(11, 3, 11, 3), Margin = new(0, 0, 8, 0),
        Font = Strong, UseMnemonic = false
    };

    internal static DataGridView Grid(string name) => new()
    {
        AccessibleName = name,
        Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
        AllowUserToResizeRows = false, RowHeadersVisible = false, MultiSelect = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        ColumnHeadersHeight = 36, ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
        EnableHeadersVisualStyles = false, ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None,
        BorderStyle = BorderStyle.None, CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
        GridColor = Line, BackgroundColor = Surface, RowTemplate = { Height = 44 },
        DefaultCellStyle = new()
        {
            BackColor = Surface, ForeColor = Text, SelectionBackColor = Selected,
            SelectionForeColor = Text, Padding = new(11, 0, 8, 0)
        },
        ColumnHeadersDefaultCellStyle = new()
        {
            BackColor = Input, ForeColor = Muted, SelectionBackColor = Input, SelectionForeColor = Muted,
            Padding = new(11, 0, 8, 0), Font = Small
        }
    };

    internal static TableLayoutPanel Stack()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Margin = Padding.Empty };
        layout.ColumnStyles.Add(new(SizeType.Percent, 100));
        return layout;
    }

    internal static TableLayoutPanel Columns(params ColumnStyle[] styles)
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = styles.Length, RowCount = 1, Margin = Padding.Empty
        };
        layout.RowStyles.Add(new(SizeType.Percent, 100));
        foreach (var style in styles) layout.ColumnStyles.Add(style);
        return layout;
    }

    internal static void Row(TableLayoutPanel layout, Control control, SizeType type, float size)
    {
        int row = layout.RowCount++;
        layout.RowStyles.Add(new(type, size));
        layout.Controls.Add(control, 0, row);
    }

    internal static FlowLayoutPanel Actions(params Control[] controls)
    {
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, WrapContents = false };
        flow.Controls.AddRange(controls);
        return flow;
    }

    private sealed class HostButton : Button
    {
        private readonly bool _primary;
        internal bool Selected;

        internal HostButton(bool primary)
        {
            _primary = primary;
            FlatStyle = FlatStyle.Flat;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var bounds = ClientRectangle;
            if (bounds.IsEmpty) return;
            bool hover = bounds.Contains(PointToClient(MousePosition));
            var fill = !Enabled ? Input : _primary ? (hover ? Color.FromArgb(203, 198, 255) : Accent)
                : Selected || hover ? HostTheme.Selected : Surface;
            using var background = new SolidBrush(fill);
            e.Graphics.FillRectangle(background, bounds);
            if (!_primary || !Enabled)
            {
                using var border = new Pen(Selected && Enabled ? Accent : Line);
                e.Graphics.DrawRectangle(border, 0, 0, bounds.Width - 1, bounds.Height - 1);
            }

            var content = Rectangle.FromLTRB(Padding.Left, Padding.Top, bounds.Right - Padding.Right, bounds.Bottom - Padding.Bottom);
            var alignment = TextAlign == ContentAlignment.MiddleLeft ? TextFormatFlags.Left : TextFormatFlags.HorizontalCenter;
            TextRenderer.DrawText(e.Graphics, Text, Font, content,
                !Enabled ? Muted : _primary ? Input : HostTheme.Text,
                alignment | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);

            if (Focused && ShowFocusCues)
            {
                bounds.Inflate(-3, -3);
                ControlPaint.DrawFocusRectangle(e.Graphics, bounds, _primary ? Input : Accent, fill);
            }
        }
    }

    internal static void Select(Button button, bool selected)
    {
        if (button is HostButton hostButton)
        {
            hostButton.Selected = selected;
            hostButton.Invalidate();
        }
    }
}
