using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Tippy.Core.Models;

namespace Tippy.App;

public partial class StatusOverlayWindow : Window
{
    private const int GwlExstyle = -20;
    private const int WsExTransparent = 0x20;
    private const int WsExNoactivate = 0x08000000;
    private const int WsExToolwindow = 0x80;
    private readonly DispatcherTimer _hideTimer = new();

    public StatusOverlayWindow()
    {
        InitializeComponent();
        _hideTimer.Tick += (_, _) => { _hideTimer.Stop(); Hide(); };
        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            var style = GetWindowLong(handle, GwlExstyle);
            SetWindowLong(handle, GwlExstyle, style | WsExTransparent | WsExNoactivate | WsExToolwindow);
        };
    }

    public void ShowStatus(string title, string context, OverlaySettings settings)
    {
        if (!settings.Enabled) return;
        TitleText.Text = title;
        ContextText.Text = context;
        Left = Math.Clamp(settings.Left, SystemParameters.VirtualScreenLeft,
            SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - Width);
        Top = Math.Clamp(settings.Top, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 110);
        _hideTimer.Stop();
        _hideTimer.Interval = TimeSpan.FromSeconds(settings.VisibleSeconds);
        Show();
        _hideTimer.Start();
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr handle, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong(IntPtr handle, int index, int value);
}
