using AnalyseTool.Core.Common.Dispatch;
using AnalyseTool.App.Common.Transport;
using AnalyseTool.Sdk;
using Serilog;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace AnalyseTool.App.Common.Activity
{
    /// <summary>
    /// The host's own sign that something is running — for the person at Revit, who otherwise has
    /// none. The busy bar lives inside the WebView windows, and an agent working over MCP needs no
    /// window at all: the user saw Revit pause, a dock pane they could close, and nothing that said
    /// "a command is running, this is which, this is how far". This is a small window owned by Revit's
    /// main window that appears by itself when a command runs and goes away when the queue is empty.
    ///
    /// Rules, and why:
    /// <list type="bullet">
    /// <item>shown for every source but <c>webview2</c> while a window is open — a call from a window is
    ///       already reported by that window's bar; once every window is closed, its calls show here too;</item>
    /// <item>appears only after a short delay, so a 200 ms read does not flash a window;</item>
    /// <item>stays a moment after the last command ends, so a sequence of quick calls does not flicker;</item>
    /// <item>never steals focus and is never modal — the whole point is to inform, not to interrupt;</item>
    /// <item>Cancel goes through <see cref="CommandQueue.TryCancel"/>, so it works for a call from MCP
    ///       exactly as for one from a button — the command answers its caller as cancelled.</item>
    /// </list>
    /// Painted before a command reaches the Revit thread: the start event arrives while that thread is
    /// still free, so even a command that then freezes Revit leaves a visible "running X" behind.
    /// </summary>
    internal static class ActivityIndicator
    {
        private static readonly TimeSpan ShowDelay = TimeSpan.FromMilliseconds(600);
        private static readonly TimeSpan HideDelay = TimeSpan.FromMilliseconds(1500);
        private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(500);

        private static CommandQueue? _queue;
        private static Dispatcher? _dispatcher;
        private static ActivityWindow? _window;
        private static DispatcherTimer? _timer;
        private static DateTime? _busySince;
        private static DateTime? _idleSince;
        /// <summary>The run the user hid the window for — it stays hidden until a different run starts.</summary>
        private static long _hiddenForRun = -1;

        /// <summary>Call once, on the UI thread (bootstrap runs in a command context, which is one).</summary>
        public static void Initialize(CommandQueue queue)
        {
            if (_queue is not null) return;
            _queue = queue;
            _dispatcher = Dispatcher.CurrentDispatcher;
            queue.RunningChanged += () => OnUiThread(Refresh);
            queue.ProgressReported += (_, _) => OnUiThread(Refresh);
        }

        private static void OnUiThread(Action action)
        {
            Dispatcher? dispatcher = _dispatcher;
            if (dispatcher is null) return;
            if (dispatcher.CheckAccess()) action();
            else dispatcher.BeginInvoke(action);
        }

        /// <summary>The command this window should talk about: the longest-running one that is not
        /// already reported by an open window of its own.</summary>
        private static RunningCommand? Relevant()
        {
            if (_queue is null) return null;
            bool windowsOpen = WebView2Transport.AttachedCount > 0;
            return _queue.Running
                .Where(r => !windowsOpen || !string.Equals(r.Source, "webview2", StringComparison.OrdinalIgnoreCase))
                .OrderBy(r => r.StartedUtc)
                .FirstOrDefault();
        }

        private static void Refresh()
        {
            try
            {
                RunningCommand? current = Relevant();
                DateTime now = DateTime.UtcNow;

                if (current is null)
                {
                    _busySince = null;
                    _idleSince ??= now;
                    if (_window is { IsVisible: true } && now - _idleSince >= HideDelay) HideWindow();
                    else if (_window is { IsVisible: true }) EnsureTimer();
                    else StopTimer();
                    return;
                }

                _idleSince = null;
                _busySince ??= now;
                if (current.Id == _hiddenForRun) return;
                if (now - _busySince < ShowDelay)
                {
                    EnsureTimer(); // come back when the delay has passed
                    return;
                }

                ActivityWindow window = _window ??= new ActivityWindow();
                window.Describe(current, (now - current.StartedUtc).TotalSeconds);
                if (!window.IsVisible) window.Show();
                EnsureTimer();
            }
            catch (Exception ex)
            {
                // A failure to paint the indicator must never fail the command it indicates.
                Log.Warning(ex, "Activity indicator failed to refresh");
            }
        }

        private static void EnsureTimer()
        {
            if (_timer is not null) return;
            _timer = new DispatcherTimer(Tick, DispatcherPriority.Normal, (_, _) => Refresh(), _dispatcher!);
            _timer.Start();
        }

        private static void StopTimer()
        {
            _timer?.Stop();
            _timer = null;
        }

        private static void HideWindow()
        {
            _window?.Hide();
            StopTimer();
        }

        private static void HideForRun(long runId)
        {
            _hiddenForRun = runId;
            HideWindow();
        }

        private static void Cancel(long runId)
        {
            if (_queue?.TryCancel(runId) == true) _window?.ShowCancelling();
        }

        /// <summary>The window itself: two lines of text, a bar, two buttons. Built in code — it is a
        /// status strip, not a page, and a XAML file would be more ceremony than content.</summary>
        private sealed class ActivityWindow : Window
        {
            private readonly TextBlock _title = new() { FontWeight = FontWeights.SemiBold, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis };
            private readonly TextBlock _detail = new() { FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)), TextTrimming = TextTrimming.CharacterEllipsis };
            private readonly ProgressBar _bar = new() { Height = 6, IsIndeterminate = true, Margin = new Thickness(0, 8, 0, 8), BorderThickness = new Thickness(0) };
            private readonly Button _cancel = new() { Content = "Cancel", Padding = new Thickness(12, 3, 12, 3), MinWidth = 72 };
            private readonly Button _hide = new() { Content = "Hide", Padding = new Thickness(12, 3, 12, 3), MinWidth = 60, Margin = new Thickness(8, 0, 0, 0) };
            private long _runId = -1;

            public ActivityWindow()
            {
                Title = "AnalyseTool";
                Width = 380;
                SizeToContent = SizeToContent.Height;
                ResizeMode = ResizeMode.NoResize;
                WindowStyle = WindowStyle.ToolWindow;
                ShowInTaskbar = false;
                ShowActivated = false;
                Topmost = false;
                Background = new SolidColorBrush(Color.FromRgb(0xFA, 0xFA, 0xFA));
                _ = new WindowInteropHelper(this) { Owner = Context.UiApplication.MainWindowHandle };

                StackPanel buttons = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
                buttons.Children.Add(_cancel);
                buttons.Children.Add(_hide);
                StackPanel root = new() { Margin = new Thickness(14, 10, 14, 12) };
                root.Children.Add(_title);
                root.Children.Add(_detail);
                root.Children.Add(_bar);
                root.Children.Add(buttons);
                Content = root;

                _cancel.Click += (_, _) => ActivityIndicator.Cancel(_runId);
                _hide.Click += (_, _) => ActivityIndicator.HideForRun(_runId);
                // Closing means hiding: the window is reused for the session, and a closed WPF window
                // cannot be shown again.
                Closing += (_, e) => { e.Cancel = true; ActivityIndicator.HideForRun(_runId); };

                // Bottom-right of the work area, where a status strip is expected and no model is hidden.
                Rect area = SystemParameters.WorkArea;
                Left = area.Right - Width - 24;
                Top = area.Bottom - 140;
            }

            public void Describe(RunningCommand command, double seconds)
            {
                if (command.Id != _runId)
                {
                    _runId = command.Id;
                    _cancel.IsEnabled = true;
                    _cancel.Content = "Cancel";
                }
                string source = command.Source switch
                {
                    "mcp" => "an AI agent over MCP",
                    "webview2" => "an AnalyseTool window",
                    "ribbon" => "the ribbon",
                    _ => command.Source,
                };
                _title.Text = $"Running {command.Command} — {Math.Round(seconds):0}s";
                ProgressInfo? progress = command.Progress;
                string message = progress?.Message is { Length: > 0 } m ? m : $"started by {source}";
                _detail.Text = message;
                if (progress is { Fraction: > 0 and <= 1 })
                {
                    _bar.IsIndeterminate = false;
                    _bar.Maximum = 100;
                    _bar.Value = progress.Fraction * 100;
                }
                else
                {
                    _bar.IsIndeterminate = true;
                }
            }

            public void ShowCancelling()
            {
                _cancel.IsEnabled = false;
                _cancel.Content = "Cancelling…";
            }
        }
    }
}
