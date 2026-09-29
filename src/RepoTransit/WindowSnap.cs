using System.Drawing;

namespace RepoTransit;

internal static class WindowSnap
{
    internal static Rectangle Snap(Rectangle window, Rectangle workArea, int distance)
    {
        var left = window.Left;
        var top = window.Top;

        if (Math.Abs(window.Left - workArea.Left) <= distance)
            left = workArea.Left;
        else if (Math.Abs(window.Right - workArea.Right) <= distance)
            left = workArea.Right - window.Width;

        if (Math.Abs(window.Top - workArea.Top) <= distance)
            top = workArea.Top;
        else if (Math.Abs(window.Bottom - workArea.Bottom) <= distance)
            top = workArea.Bottom - window.Height;

        return new Rectangle(left, top, window.Width, window.Height);
    }

    internal static Rectangle KeepVisible(Rectangle window, Rectangle workArea) => new(
        window.Width <= workArea.Width
            ? Math.Clamp(window.Left, workArea.Left, workArea.Right - window.Width)
            : workArea.Left,
        window.Height <= workArea.Height
            ? Math.Clamp(window.Top, workArea.Top, workArea.Bottom - window.Height)
            : workArea.Top,
        window.Width,
        window.Height);

    internal static bool TitleBarVisible(Rectangle window, IEnumerable<Rectangle> workAreas)
    {
        var titleBar = new Rectangle(window.Left, window.Top, window.Width, Math.Min(window.Height, 40));
        return workAreas.Any(area =>
        {
            var visible = Rectangle.Intersect(titleBar, area);
            return visible.Width >= 80 && visible.Height >= 20;
        });
    }
}
