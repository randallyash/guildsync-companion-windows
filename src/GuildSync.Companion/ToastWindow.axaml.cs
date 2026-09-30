using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace GuildSync.Companion;

public partial class ToastWindow : Window
{
    public ToastWindow() : this("")
    {
    }

    public ToastWindow(string message)
    {
        InitializeComponent();
        Body.Text = message;
        Opened += (_, _) => Place();
        DispatcherTimer.RunOnce(Close, TimeSpan.FromSeconds(4));
    }

    private void Place()
    {
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is null)
            return;
        var area = screen.WorkingArea;
        var scale = screen.Scaling;
        Position = new PixelPoint(
            area.X + area.Width - (int)(Width * scale) - 24,
            area.Y + area.Height - (int)(Height * scale) - 24);
    }
}
