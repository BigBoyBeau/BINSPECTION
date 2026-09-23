using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace BINSPECTION.UI
{
    // Non-blocking replacement for a SendMsgToUser2(...swMbInformation...)
    // popup - shows bottom-right, auto-dismisses, and never steals focus or
    // stops the user from continuing to work. Only wired into the handful
    // of commands (Save/Restore Position, Generate Report) whose SUCCESS
    // outcome doesn't need an acknowledgment click; every hard-stop error
    // in the add-in still uses SendMsgToUser2's native SolidWorks modal,
    // since those genuinely need to block until acknowledged.
    public partial class ToastNotification : Window
    {
        private const int DisplayMilliseconds = 4000;
        private const int TickMilliseconds = 50;

        private readonly DispatcherTimer _dismissTimer;
        private int _elapsedMilliseconds;

        private ToastNotification(string title, string detail, bool isWarning)
        {
            InitializeComponent();

            TitleText.Text = title;
            DetailText.Text = detail;

            Brush accent = isWarning
                ? (Brush)new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E0A857"))
                : (Brush)new SolidColorBrush((Color)ColorConverter.ConvertFromString("#57C77A"));

            AccentBar.Background = accent;
            ProgressBar.Background = accent;
            CheckRing.Stroke = accent;

            CheckIcon.Visibility = isWarning ? Visibility.Collapsed : Visibility.Visible;
            CheckRing.Visibility = isWarning ? Visibility.Collapsed : Visibility.Visible;
            WarnIcon.Visibility = isWarning ? Visibility.Visible : Visibility.Collapsed;
            WarnIcon.Stroke = accent;

            MouseLeftButtonDown += (s, e) => Dismiss();

            _dismissTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(TickMilliseconds) };
            _dismissTimer.Tick += DismissTimer_Tick;

            Loaded += (s, e) =>
            {
                PositionBottomRight();
                _dismissTimer.Start();
            };
        }

        private void PositionBottomRight()
        {
            Left = SystemParameters.WorkArea.Right - ActualWidth - 24;
            Top = SystemParameters.WorkArea.Bottom - ActualHeight - 24;
        }

        private void DismissTimer_Tick(object sender, EventArgs e)
        {
            _elapsedMilliseconds += TickMilliseconds;

            double remaining = 1.0 - ((double)_elapsedMilliseconds / DisplayMilliseconds);

            ProgressBar.Width = Math.Max(0, remaining) * ActualWidth;

            if (_elapsedMilliseconds >= DisplayMilliseconds)
                Dismiss();
        }

        private void Dismiss()
        {
            _dismissTimer.Stop();
            Close();
        }

        public static void ShowSuccess(string title, string detail)
        {
            Show(title, detail, isWarning: false);
        }

        public static void ShowWarning(string title, string detail)
        {
            Show(title, detail, isWarning: true);
        }

        private static void Show(string title, string detail, bool isWarning)
        {
            try
            {
                new ToastNotification(title, detail, isWarning).Show();
            }
            catch (Exception ex)
            {
                // Best-effort UI only - never let a toast failure take down
                // the command that triggered it.
                System.Diagnostics.Debug.WriteLine($"ToastNotification.Show Error: {ex}");
            }
        }
    }
}
