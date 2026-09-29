using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;

namespace RepoTransit;

internal static class WindowSnapBehavior
{
    private const int WmMoving = 0x0216;
    private const int WmActivate = 0x0006;
    private const int WmDisplayChange = 0x007E;
    private const int SnapDistance = 16;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;

    internal static void Attach(Window window)
    {
        window.SourceInitialized += (_, _) =>
        {
            var source = (HwndSource)PresentationSource.FromVisual(window)!;
            HwndSourceHook hook = (IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled) =>
            {
                if (message == WmMoving && window.WindowState == WindowState.Normal)
                {
                    var bounds = Marshal.PtrToStructure<NativeRect>(lParam);
                    var rectangle = bounds.ToRectangle();
                    var snapped = WindowSnap.Snap(rectangle, Screen.FromRectangle(rectangle).WorkingArea, SnapDistance);
                    if (snapped.Location != rectangle.Location)
                    {
                        bounds.Left = snapped.Left;
                        bounds.Top = snapped.Top;
                        bounds.Right = snapped.Right;
                        bounds.Bottom = snapped.Bottom;
                        Marshal.StructureToPtr(bounds, lParam, false);
                    }
                }
                else if (message == WmDisplayChange ||
                         (message == WmActivate && (wParam.ToInt64() & 0xFFFF) != 0))
                {
                    window.Dispatcher.BeginInvoke(() => EnsureVisible(window));
                }

                return IntPtr.Zero;
            };
            source.AddHook(hook);
            window.Closed += (_, _) => source.RemoveHook(hook);
        };
    }

    internal static void EnsureVisible(Window window)
    {
        if (!window.IsVisible || window.WindowState != WindowState.Normal) return;
        var hwnd = new WindowInteropHelper(window).Handle;
        if (!GetWindowRect(hwnd, out var bounds)) return;

        var rectangle = bounds.ToRectangle();
        var workAreas = Screen.AllScreens.Select(screen => screen.WorkingArea);
        if (WindowSnap.TitleBarVisible(rectangle, workAreas)) return;

        var visible = WindowSnap.KeepVisible(rectangle, Screen.FromRectangle(rectangle).WorkingArea);
        SetWindowPos(hwnd, IntPtr.Zero, visible.Left, visible.Top, 0, 0,
            SwpNoSize | SwpNoZOrder | SwpNoActivate);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;

        internal readonly Rectangle ToRectangle() => Rectangle.FromLTRB(Left, Top, Right, Bottom);
    }

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect bounds);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
}
