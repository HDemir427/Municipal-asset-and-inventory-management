using System.Windows.Forms;

namespace MAIMS.WinUI.Theming;

/// <summary>
/// Custom ProfessionalColorTable that forces the MenuStrip and StatusStrip
/// to use MAIMS brand colors (municipal blue + white text).
///
/// WinForms' default ToolStripProfessionalRenderer IGNORES the BackColor /
/// ForeColor properties on MenuStrip/StatusStrip/ToolStripMenuItem — it paints
/// with its own color table instead. To get our brand colors to actually
/// appear, we override the color table here and assign a renderer that uses it.
/// </summary>
public class MaimsColorTable : ProfessionalColorTable
{
    public override Color MenuStripGradientBegin => MaimsTheme.Primary;
    public override Color MenuStripGradientEnd => MaimsTheme.Primary;
    public override Color MenuBorder => MaimsTheme.PrimaryDark;

    public override Color MenuItemSelected => MaimsTheme.PrimaryDark;
    public override Color MenuItemSelectedGradientBegin => MaimsTheme.PrimaryDark;
    public override Color MenuItemSelectedGradientEnd => MaimsTheme.PrimaryDark;

    public override Color MenuItemPressedGradientBegin => MaimsTheme.PrimaryDark;
    public override Color MenuItemPressedGradientEnd => MaimsTheme.PrimaryDark;
    public override Color MenuItemBorder => MaimsTheme.PrimaryDark;

    public override Color ToolStripPanelGradientBegin => MaimsTheme.Primary;
    public override Color ToolStripPanelGradientEnd => MaimsTheme.Primary;

    public override Color ToolStripBorder => MaimsTheme.PrimaryDark;
    public override Color ToolStripGradientBegin => MaimsTheme.Primary;
    public override Color ToolStripGradientMiddle => MaimsTheme.Primary;
    public override Color ToolStripGradientEnd => MaimsTheme.Primary;

    public override Color ToolStripContentPanelGradientBegin => MaimsTheme.PrimaryDark;
    public override Color ToolStripContentPanelGradientEnd => MaimsTheme.PrimaryDark;

    public override Color StatusStripGradientBegin => MaimsTheme.PrimaryDark;
    public override Color StatusStripGradientEnd => MaimsTheme.PrimaryDark;

    public override Color SeparatorDark => Color.FromArgb(80, 255, 255, 255);
    public override Color SeparatorLight => Color.FromArgb(120, 255, 255, 255);

    // Dropdown menu items (when you click File / Assets / etc.)
    public override Color ToolStripDropDownBackground => MaimsTheme.Surface;
    public override Color ImageMarginGradientBegin => MaimsTheme.Surface;
    public override Color ImageMarginGradientMiddle => MaimsTheme.Surface;
    public override Color ImageMarginGradientEnd => MaimsTheme.Surface;
    public override Color CheckBackground => MaimsTheme.Accent;
    public override Color CheckSelectedBackground => MaimsTheme.Primary;
    public override Color ButtonSelectedHighlight => MaimsTheme.Primary;
    public override Color ButtonSelectedGradientBegin => MaimsTheme.Primary;
    public override Color ButtonSelectedGradientEnd => MaimsTheme.Primary;
}

/// <summary>
/// Custom renderer that uses MaimsColorTable AND forces white text on the
/// top-level menu items (File/Assets/Reports/Help). The base
/// ToolStripProfessionalRenderer uses SystemColors.MenuText which respects
/// the OS theme — on a light theme that means black text on our dark blue
/// menu strip, which is unreadable. We override the text color explicitly.
///
/// IMPORTANT: Top-level menu items (on the dark blue bar) need WHITE text.
/// But dropdown items (File → Sign out) appear on a WHITE surface and need
/// DARK text. We detect this by checking if the item is currently rendered
/// inside a dropdown (OwnerItem != null).
/// </summary>
public class MaimsToolStripRenderer : ToolStripProfessionalRenderer
{
    public MaimsToolStripRenderer() : base(new MaimsColorTable()) { }

    /// <summary>
    /// Checks whether the mouse is genuinely hovering over the item (not just
    /// that Selected=true, which can persist after a right-click elsewhere).
    /// We verify the cursor is actually within the item's screen bounds.
    /// </summary>
    private static bool IsRealHover(ToolStripItem item)
    {
        if (!item.Selected) return false;
        if (item.Pressed) return false;

        // Right-click should NEVER show hover.
        if (Control.MouseButtons == MouseButtons.Right) return false;

        // After a right-click on another control, ToolStripItem.Selected can
        // stay true even though the cursor has moved away. Verify the cursor
        // is actually within this item's bounds.
        try
        {
            var cursorPos = Cursor.Position;
            var itemScreenRect = item.Bounds;
            // Translate item bounds to screen coordinates.
            var toolStrip = item.Owner;
            if (toolStrip != null)
            {
                var toolStripOrigin = toolStrip.PointToScreen(Point.Empty);
                itemScreenRect.Offset(toolStripOrigin);
            }
            if (!itemScreenRect.Contains(cursorPos))
                return false;
        }
        catch
        {
            // If we can't verify position, fall back to Selected check.
        }

        return true;
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        var item = e.Item;
        var g = e.Graphics;
        var bounds = new Rectangle(Point.Empty, item.Size);

        if (item is ToolStripMenuItem menuItem)
        {
            if (menuItem.OwnerItem == null)
            {
                // Top-level menu item on the dark blue bar.
                // Show hover (PrimaryDark) only for genuine left-click hover or pressed.
                // Right-click should NOT change the background.
                var isHoverOrPressed = IsRealHover(menuItem) || menuItem.Pressed;
                var bg = isHoverOrPressed
                    ? MaimsTheme.PrimaryDark
                    : MaimsTheme.Primary;
                using var brush = new SolidBrush(bg);
                g.FillRectangle(brush, bounds);
                return;
            }
            else
            {
                // Dropdown menu item — always white background.
                // On genuine hover (left-click): light blue accent.
                // On right-click: no hover effect (stay white).
                Color bg;
                if (menuItem.Pressed)
                    bg = Color.FromArgb(0xFF, 0xD6, 0xE4, 0xF0);  // slightly darker blue (pressed)
                else if (IsRealHover(menuItem))
                    bg = Color.FromArgb(0xFF, 0xE6, 0xEE, 0xF7);  // very light blue tint (hover)
                else
                    bg = MaimsTheme.Surface;  // white (default / right-click)
                using var brush = new SolidBrush(bg);
                g.FillRectangle(brush, bounds);
                return;
            }
        }
        base.OnRenderMenuItemBackground(e);
    }

    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
    {
        // Force the image margin (left strip of dropdown) to be white too.
        var g = e.Graphics;
        var rect = e.AffectedBounds;
        using var brush = new SolidBrush(MaimsTheme.Surface);
        g.FillRectangle(brush, rect);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        // Draw a thin border around dropdown menus so they look like a popup.
        if (e.ToolStrip is ToolStripDropDown)
        {
            var g = e.Graphics;
            var rect = new Rectangle(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
            using var pen = new Pen(MaimsTheme.Border);
            g.DrawRectangle(pen, rect);
        }
        else
        {
            base.OnRenderToolStripBorder(e);
        }
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        // Top-level menu items (no OwnerItem) are on the dark blue bar → white text.
        // Dropdown items (OwnerItem != null) are on white surface → dark text.
        var oldColor = e.TextColor;
        if (e.Item is ToolStripMenuItem menuItem)
        {
            if (menuItem.OwnerItem == null)
            {
                // Top-level: white text on dark blue bar.
                e.TextColor = Color.White;
            }
            else
            {
                // Dropdown: dark text on white surface. Honor enabled/disabled.
                e.TextColor = menuItem.Enabled ? MaimsTheme.TextPrimary : MaimsTheme.TextSecondary;
            }
            base.OnRenderItemText(e);
            e.TextColor = oldColor;
        }
        else if (e.Item is ToolStripStatusLabel)
        {
            // Status strip labels: white text on dark blue bar.
            e.TextColor = Color.White;
            base.OnRenderItemText(e);
            e.TextColor = oldColor;
        }
        else
        {
            base.OnRenderItemText(e);
        }
    }
}
