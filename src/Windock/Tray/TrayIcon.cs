using System.Drawing;
using System.Drawing.Drawing2D;
using Forms = System.Windows.Forms;

namespace Windock.Tray;

/// System tray icon: left click opens Settings; right click shows Settings / Windows mode / Quit (DESIGN.md §8).
internal sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly Forms.ToolStripMenuItem _pauseItem;
    private readonly Icon _glyph;

    public TrayIcon(Action openSettings, Action toggleMode, Action exit)
    {
        _glyph = (Environment.ProcessPath is { } exe ? Icon.ExtractAssociatedIcon(exe) : null) ?? CreateGlyph();
        _pauseItem = new Forms.ToolStripMenuItem("Use Windows mode", null, (_, _) => toggleMode());
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(new Forms.ToolStripMenuItem("Settings…", null, (_, _) => openSettings()));
        menu.Items.Add(_pauseItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(new Forms.ToolStripMenuItem("Quit", null, (_, _) => exit()));

        _icon = new Forms.NotifyIcon { Icon = _glyph, Text = "Windock", ContextMenuStrip = menu, Visible = true };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left) openSettings();
        };
    }

    public void SetMode(bool windowsMode) => _pauseItem.Text = windowsMode ? "Use Windock mode" : "Use Windows mode";

    public void ShowBalloon(string title, string text) =>
        _icon.ShowBalloonTip(4000, title, text, Forms.ToolTipIcon.Info);

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _glyph.Dispose();
    }

    /// Monochrome "dock" glyph: a rounded bar with three dots, drawn at runtime (no binary asset needed).
    private static Icon CreateGlyph()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var white = new SolidBrush(Color.White);
            using var bar = new GraphicsPath();
            bar.AddArc(2, 18, 10, 10, 90, 180);
            bar.AddArc(20, 18, 10, 10, 270, 180);
            bar.CloseFigure();
            g.FillPath(white, bar);
            foreach (var x in new[] { 6, 13, 20 }) g.FillRectangle(white, x, 8, 6, 6);
        }
        var handle = bmp.GetHicon();
        using var temp = Icon.FromHandle(handle);
        var icon = (Icon)temp.Clone();
        Interop.Native.DestroyIcon(handle);
        return icon;
    }
}
