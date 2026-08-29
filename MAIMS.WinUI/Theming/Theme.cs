namespace MAIMS.WinUI.Theming;

/// <summary>
/// Centralised palette + font choices. Municipal blue (#1F4E79) as accent;
/// greys for chrome; no red/green-only status indicators (accessibility §10).
/// </summary>
public static class MaimsTheme
{
    public static readonly Color Primary = ColorTranslator.FromHtml("#1F4E79");
    public static readonly Color PrimaryDark = ColorTranslator.FromHtml("#0F2F50");
    public static readonly Color Accent = ColorTranslator.FromHtml("#2E86C1");
    public static readonly Color Background = ColorTranslator.FromHtml("#F5F6F8");
    public static readonly Color Surface = Color.White;
    public static readonly Color TextPrimary = ColorTranslator.FromHtml("#1A1A1A");
    public static readonly Color TextSecondary = ColorTranslator.FromHtml("#5A5A5A");
    public static readonly Color Border = ColorTranslator.FromHtml("#D0D4DA");
    public static readonly Color Warning = ColorTranslator.FromHtml("#F4A33A");
    public static readonly Color Critical = ColorTranslator.FromHtml("#C0392B");
    public static readonly Color OK = ColorTranslator.FromHtml("#2E7D5B");

    public static readonly Font Heading = new("Segoe UI", 11F, FontStyle.Bold);
    public static readonly Font Body = new("Segoe UI", 9F);
    public static readonly Font Small = new("Segoe UI", 8.25F);

    public static void ApplyToControl(Control c)
    {
        c.Font = Body;
        c.BackColor = Surface;
        c.ForeColor = TextPrimary;
    }

    public static void ApplyToForm(Form f)
    {
        f.Font = Body;
        f.BackColor = Background;
        f.ForeColor = TextPrimary;
        f.StartPosition = FormStartPosition.CenterScreen;
    }

    public static void StyleButton(Button b, bool primary = false)
    {
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderSize = 0;
        b.Padding = new Padding(12, 6, 12, 6);
        b.Font = Body;
        if (primary)
        {
            b.BackColor = Primary;
            b.ForeColor = Color.White;
        }
        else
        {
            b.BackColor = Surface;
            b.ForeColor = TextPrimary;
            b.FlatAppearance.BorderColor = Border;
            b.FlatAppearance.BorderSize = 1;
        }
    }

    /// <summary>
    /// Creates a button whose text is always fully visible, regardless of DPI
    /// or font rendering quirks. Uses TextRenderer.MeasureText to compute the
    /// exact pixel size of the text, then adds generous padding. AutoSize is
    /// NOT used because it can produce sizes that are too tight on high-DPI
    /// displays, causing text clipping.
    ///
    /// HEIGHT IS FIXED at 36px so that all buttons — regardless of their text
    /// length — have the same height. This ensures that when buttons are laid
    /// out side-by-side via LayoutButtons (which sets the same Y for each),
    /// their top AND bottom edges align perfectly. Without a fixed height,
    /// TextRenderer.MeasureText can return slightly different heights for
    /// different strings (e.g. "Cancel" vs "Save" vs "OK"), making some
    /// buttons appear taller/shorter than their neighbours — visually
    /// misaligned even though they share the same Y coordinate.
    /// </summary>
    public static Button CreateButton(string text, bool primary = false)
    {
        var b = new Button
        {
            Text = text,
            FlatStyle = FlatStyle.Flat,
            Font = Body,
            TextAlign = ContentAlignment.MiddleCenter,
            UseCompatibleTextRendering = false,
            // Default WinForms Button margin is (3,3,3,3). When buttons are
            // placed inside a FlowLayoutPanel, the Top margin shifts the button
            // down — so if one button has Margin.Top=0 (explicitly set) and
            // another keeps the default Margin.Top=3, they end up on different
            // Y baselines even though they have the same Height. Setting
            // Margin=(0,0,0,0) here ensures all CreateButton-produced buttons
            // start from the same Y baseline. Forms that need spacing between
            // buttons in a FlowLayoutPanel should set the *right* margin
            // explicitly on each button (e.g. btn.Margin = new Padding(0,0,8,0))
            // or use the FlowLayoutPanel's own Padding.
            Margin = new Padding(0)
        };

        // Measure exact text size using TextRenderer (GDI, matches default rendering).
        var textSize = TextRenderer.MeasureText(text, Body, new Size(int.MaxValue, int.MaxValue),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

        // Generous horizontal padding: 28px each side.
        const int padX = 28;
        var width = Math.Max(textSize.Width + padX * 2, 90);

        // FIXED height — 36px for all buttons regardless of text length.
        // This guarantees visual alignment when buttons are placed side-by-side.
        // The Body font (Segoe UI 9pt) measures ~15px tall for typical text,
        // so 36px gives ~10px vertical padding top+bottom — comfortable click target.
        const int fixedHeight = 36;
        b.Size = new Size(width, fixedHeight);

        if (primary)
        {
            b.BackColor = Primary;
            b.ForeColor = Color.White;
            b.FlatAppearance.BorderSize = 0;
        }
        else
        {
            b.BackColor = Surface;
            b.ForeColor = TextPrimary;
            b.FlatAppearance.BorderColor = Border;
            b.FlatAppearance.BorderSize = 1;
        }
        return b;
    }

    /// <summary>
    /// Creates a button with a FIXED width — use this for buttons placed
    /// side-by-side at known X coordinates (e.g., toolbar rows) so the next
    /// button's Location doesn't overlap. The text is auto-fit to the button
    /// with generous padding; if text would overflow, the button grows.
    /// </summary>
    public static Button CreateButton(string text, int fixedWidth, bool primary = false)
    {
        var b = CreateButton(text, primary);
        // Ensure at least fixedWidth; allow growth if text needs more.
        if (b.Width < fixedWidth) b.Width = fixedWidth;
        return b;
    }

    /// <summary>
    /// Lays out a sequence of buttons horizontally with a fixed gap between
    /// each, so the next button's X is always previous.X + previous.Width + gap.
    /// This prevents the overlap problem where a button's width grew past the
    /// next button's X coordinate (because text was longer than expected).
    ///
    /// Usage:
    ///   var (search, newBtn, close) = MaimsTheme.CreateButtons(("Search", true), ("New", false), ("Close", false));
    ///   MaimsTheme.LayoutButtons(8, 9, 8, search, newBtn, close);
    /// </summary>
    public static void LayoutButtons(int startX, int y, int gap, params Button[] buttons)
    {
        int x = startX;
        foreach (var b in buttons)
        {
            b.Location = new Point(x, y);
            x += b.Width + gap;
        }
    }

    /// <summary>
    /// Creates a row of buttons and lays them out horizontally with a fixed gap.
    /// Returns the buttons in order. Each button's text is auto-sized (via
    /// CreateButton) and the next button's X is computed from the previous
    /// button's actual Width, so they NEVER overlap.
    /// </summary>
    public static Button[] CreateButtonRow(int startX, int y, int gap, params (string Text, bool Primary)[] specs)
    {
        var buttons = new Button[specs.Length];
        for (int i = 0; i < specs.Length; i++)
        {
            buttons[i] = CreateButton(specs[i].Text, specs[i].Primary);
        }
        LayoutButtons(startX, y, gap, buttons);
        return buttons;
    }
}
