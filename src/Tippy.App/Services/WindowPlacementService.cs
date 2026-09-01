using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Tippy.Core.Models;

namespace Tippy.App.Services;

public sealed class WindowPlacementService
{
    private const uint MonitorDefaultToNearest = 2;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoZOrder = 0x0004;

    public void Restore(Window window, WindowPlacementSettings placement)
    {
        if (!placement.HasPlacement) return;
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;

        var requested = new NativeRect
        {
            Left = placement.Left,
            Top = placement.Top,
            Right = placement.Left + placement.Width,
            Bottom = placement.Top + placement.Height
        };
        var work = GetNearestWorkArea(requested);
        var dpi = GetWindowDpi(handle);
        var minimumWidth = DipToPixels(window.MinWidth, dpi);
        var minimumHeight = DipToPixels(window.MinHeight, dpi);
        var fitted = FitEntirelyWithin(work, requested, minimumWidth, minimumHeight, DipToPixels(12, dpi));

        window.WindowStartupLocation = WindowStartupLocation.Manual;
        SetWindowPos(handle, IntPtr.Zero, fitted.Left, fitted.Top, fitted.Width, fitted.Height,
            SwpNoActivate | SwpNoZOrder);
    }

    public void Capture(Window window, WindowPlacementSettings placement)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero || window.WindowState == WindowState.Minimized) return;
        var nativePlacement = new NativeWindowPlacement { Length = Marshal.SizeOf<NativeWindowPlacement>() };
        if (!GetWindowPlacement(handle, ref nativePlacement)) return;
        var bounds = nativePlacement.NormalPosition;

        placement.HasPlacement = true;
        placement.IsMaximized = window.WindowState == WindowState.Maximized || nativePlacement.ShowCommand == 3;
        placement.Left = bounds.Left;
        placement.Top = bounds.Top;
        placement.Width = Math.Max(1, bounds.Width);
        placement.Height = Math.Max(1, bounds.Height);
    }

    public void ResizeWithinCurrentMonitor(Window window, double desiredWidth, double desiredHeight, double margin)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero || !GetWindowRect(handle, out var current)) return;
        var work = GetMonitorWorkArea(MonitorFromWindow(handle, MonitorDefaultToNearest));
        var dpi = GetWindowDpi(handle);
        var marginPixels = DipToPixels(margin, dpi);
        var minimumWidth = DipToPixels(window.MinWidth, dpi);
        var minimumHeight = DipToPixels(window.MinHeight, dpi);
        var targetWidth = Math.Max(minimumWidth, DipToPixels(desiredWidth, dpi));
        var targetHeight = Math.Max(minimumHeight, DipToPixels(desiredHeight, dpi));

        // Keep the current top-left corner whenever the requested size fits there. If it
        // does not, shrink to the remaining work area before moving the window at all.
        var left = Math.Clamp(current.Left, work.Left + marginPixels, work.Right - marginPixels);
        var top = Math.Clamp(current.Top, work.Top + marginPixels, work.Bottom - marginPixels);
        var availableWidth = Math.Max(1, work.Right - marginPixels - left);
        var availableHeight = Math.Max(1, work.Bottom - marginPixels - top);
        var width = Math.Min(targetWidth, availableWidth);
        var height = Math.Min(targetHeight, availableHeight);

        if (width < minimumWidth)
        {
            width = Math.Min(minimumWidth, Math.Max(1, work.Width - marginPixels * 2));
            left = Math.Max(work.Left + marginPixels, work.Right - marginPixels - width);
        }
        if (height < minimumHeight)
        {
            height = Math.Min(minimumHeight, Math.Max(1, work.Height - marginPixels * 2));
            top = Math.Max(work.Top + marginPixels, work.Bottom - marginPixels - height);
        }

        SetWindowPos(handle, IntPtr.Zero, left, top, width, height, SwpNoActivate | SwpNoZOrder);
    }

    private static NativeRect FitEntirelyWithin(
        NativeRect work,
        NativeRect requested,
        int minimumWidth,
        int minimumHeight,
        int margin)
    {
        var maximumWidth = Math.Max(1, work.Width - margin * 2);
        var maximumHeight = Math.Max(1, work.Height - margin * 2);
        var width = Math.Clamp(requested.Width, Math.Min(minimumWidth, maximumWidth), maximumWidth);
        var height = Math.Clamp(requested.Height, Math.Min(minimumHeight, maximumHeight), maximumHeight);
        var left = Math.Clamp(requested.Left, work.Left + margin, work.Right - margin - width);
        var top = Math.Clamp(requested.Top, work.Top + margin, work.Bottom - margin - height);
        return new NativeRect { Left = left, Top = top, Right = left + width, Bottom = top + height };
    }

    private static NativeRect GetNearestWorkArea(NativeRect bounds)
    {
        var copy = bounds;
        return GetMonitorWorkArea(MonitorFromRect(ref copy, MonitorDefaultToNearest));
    }

    private static NativeRect GetMonitorWorkArea(IntPtr monitor)
    {
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info))
            return new NativeRect
            {
                Left = (int)SystemParameters.VirtualScreenLeft,
                Top = (int)SystemParameters.VirtualScreenTop,
                Right = (int)(SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth),
                Bottom = (int)(SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight)
            };
        return info.WorkArea;
    }

    private static int GetWindowDpi(IntPtr handle)
    {
        try { return Math.Max(96, (int)GetDpiForWindow(handle)); }
        catch (EntryPointNotFoundException) { return 96; }
    }

    private static int DipToPixels(double value, int dpi) =>
        Math.Max(1, (int)Math.Round(value * dpi / 96d));

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
        public readonly int Width => Right - Left;
        public readonly int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect WorkArea;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeWindowPlacement
    {
        public int Length;
        public int Flags;
        public int ShowCommand;
        public NativePoint MinimumPosition;
        public NativePoint MaximumPosition;
        public NativeRect NormalPosition;
    }

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr window, out NativeRect rectangle);

    [DllImport("user32.dll")]
    private static extern bool GetWindowPlacement(IntPtr window, ref NativeWindowPlacement placement);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromRect(ref NativeRect rectangle, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr window);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        IntPtr window,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);
}
