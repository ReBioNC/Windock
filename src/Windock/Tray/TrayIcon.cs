using System.Drawing;
using System.Drawing.Drawing2D;
using Forms = System.Windows.Forms;

namespace Windock.Tray;

/// System tray icon: left click opens Settings; right click shows Pengaturan / Jeda / Keluar (DESIGN.md §8).
internal sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly Forms.ToolStripMenuItem _pauseItem;
    private readonly Icon _glyph;

    public TrayIcon(Action openSettings, Action togglePause, Action exit)
    {
        _glyph = CreateGlyph();
        _pauseItem = new Forms.ToolStripMenuItem("Jeda dock", null, (_, _) => togglePause());
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(new Forms.ToolStripMenuItem("Pengaturan…", null, (_, _) => openSettings()));
        menu.Items.Add(_pauseItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(new Forms.ToolStripMenuItem("Keluar", null, (_, _) => exit()));

        _icon = new Forms.NotifyIcon { Icon = _glyph, Text = "Windock", ContextMenuStrip = menu, Visible = true };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left) openSettings();
        };
    }

    public void SetPaused(bool paused) => _pauseItem.Text = paused ? "Lanjutkan dock" : "Jeda dock";

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
