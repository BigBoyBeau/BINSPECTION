using System;
using System.Windows;

namespace BINSPECTION.UI
{
    // Shared sizing behavior for the add-in's WPF dialogs. Each window is
    // authored with a fixed design-time Height/Width sized for a normal
    // desktop monitor, but shop-floor machines vary (small laptops, older
    // 1366x768 panels) and a grid full of rows/changes can make a dialog
    // feel too tall to work in. FitToScreen shrinks (never grows) a window
    // to the work area it actually opens on and keeps it resizable/
    // maximizable so the user can always get to every row and button.
    internal static class WindowSizing
    {
        internal static void FitToScreen(Window window, double margin = 40)
        {
            if (window.ResizeMode == ResizeMode.NoResize || window.ResizeMode == ResizeMode.CanMinimize)
            {
                window.ResizeMode = ResizeMode.CanResize;
            }

            window.Loaded += (s, e) =>
            {
                Rect workArea = SystemParameters.WorkArea;
                double maxWidth = Math.Max(window.MinWidth, workArea.Width - margin);
                double maxHeight = Math.Max(window.MinHeight, workArea.Height - margin);

                if (window.Width > maxWidth)
                {
                    window.Width = maxWidth;
                }
                if (window.Height > maxHeight)
                {
                    window.Height = maxHeight;
                }

                window.MaxWidth = workArea.Width;
                window.MaxHeight = workArea.Height;

                if (window.WindowStartupLocation == WindowStartupLocation.CenterScreen)
                {
                    window.Left = workArea.Left + Math.Max(0, (workArea.Width - window.Width) / 2);
                    window.Top = workArea.Top + Math.Max(0, (workArea.Height - window.Height) / 2);
                }
            };
        }
    }
}
