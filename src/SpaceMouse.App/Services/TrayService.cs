using System.Windows.Forms;
using SpaceMouse.Core.Engine;

namespace SpaceMouse.App.Services;

/// <summary>System tray icon with quick controls. WinForms NotifyIcon is used because it is the most reliable tray API on Win10/11.</summary>
public sealed class TrayService : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly AppController _app;
    private readonly ContextMenuStrip _menu = new();

    public TrayService(AppController app)
    {
        _app = app;
        System.Drawing.Icon? ico = null;
        try
        {
            var uri = new Uri("pack://application:,,,/Assets/app.ico");
            using var s = Application.GetResourceStream(uri)?.Stream;
            if (s != null) ico = new System.Drawing.Icon(s);
        }
        catch { /* fall back */ }
        _icon = new NotifyIcon { Icon = ico ?? System.Drawing.SystemIcons.Application, Text = "Space Mouse Studio", Visible = true, ContextMenuStrip = _menu };
        _icon.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) _app.RequestShowMainWindow(); };
        _menu.Opening += (_, _) => Rebuild();
        _app.StateChanged += UpdateText;
        UpdateText();
    }

    private void UpdateText()
    {
        var t = "Space Mouse Studio - " + (_app.IsActive ? "active" : "standard mode");
        _icon.Text = t.Length > 63 ? t[..63] : t;
    }

    private void Rebuild()
    {
        _menu.Items.Clear();
        _menu.Items.Add("Open Space Mouse Studio", null, (_, _) => _app.RequestShowMainWindow());
        _menu.Items.Add(new ToolStripSeparator());
        var toggle = new ToolStripMenuItem(_app.IsActive ? "Switch to Standard mode (off)" : "Activate custom control") ;
        toggle.Click += (_, _) => _app.SetActive(!_app.IsActive);
        _menu.Items.Add(toggle);
        var profiles = new ToolStripMenuItem("Profile");
        foreach (var p in _app.Profiles)
        {
            var item = new ToolStripMenuItem(p.Name) { Checked = _app.ActiveProfile?.Id == p.Id };
            var captured = p;
            item.Click += (_, _) => _app.ActivateProfile(captured);
            profiles.DropDownItems.Add(item);
        }
        _menu.Items.Add(profiles);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("Virtual controller...", null, (_, _) => SpaceMouse.App.Views.VirtualControllerWindow.ShowSingleton());
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("Quit", null, (_, _) => ((App)Application.Current).Quit());
    }

    public void Dispose() { _icon.Visible = false; _icon.Dispose(); _menu.Dispose(); }
}
