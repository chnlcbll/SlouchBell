using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

internal sealed class Reminder : Window
{
    private readonly SlouchBell app;
    private readonly bool preview;
    private readonly Random random;
    private readonly Border card;
    private readonly TextBlock heading, prompt;
    private readonly StackPanel body, footer;
    private readonly WrapPanel actions;
    private readonly Button done, reset;
    private readonly TextBlock timerLabel;
    private readonly DispatcherTimer resetTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly Stopwatch resetClock = new Stopwatch();
    private string location;
    private Forms.Screen monitor;
    private bool closing, completed, positioned;
    private IntPtr handle;

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr window, out Desktop.Rect rect);

    internal Reminder(SlouchBell app, Message message, bool preview, Random random, bool activate = false)
    {
        this.app = app;
        this.preview = preview;
        this.random = random;
        location = app.Preferences.Position;
        monitor = Forms.Screen.FromHandle(Desktop.GetForegroundWindow());
        if (Desktop.GetForegroundWindow() == IntPtr.Zero) monitor = Forms.Screen.FromPoint(Forms.Cursor.Position);
        Title = SlouchBell.TitleText + " · SlouchBell";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowActivated = preview || activate;
        ShowInTaskbar = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.Manual;
        FontFamily = new FontFamily("Segoe UI");
        UseLayoutRounding = true;
        Width = app.Preferences.Size == "Compact" ? 365 : (app.Preferences.Size == "Large" ? 540 : 446);
        Opacity = 0;
        Cozy.Apply(this, app.Preferences.Theme);

        card = Cozy.Card("Surface", 24, 19);
        card.Margin = new Thickness(12);
        if (!SystemParameters.HighContrast)
            card.Effect = new DropShadowEffect { BlurRadius = 17, ShadowDepth = 3, Opacity = .24, Color = Colors.Black };
        var scroller = new ScrollViewer { Content = card, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Content = scroller;
        body = new StackPanel();
        card.Child = body;

        var header = new DockPanel();
        var close = Cozy.Button("×", Dismiss, flat: true);
        close.FontSize = 20;
        close.Padding = new Thickness(8, 0, 8, 0);
        AutomationProperties.SetName(close, "Dismiss reminder");
        DockPanel.SetDock(close, Dock.Right);
        header.Children.Add(close);
        var brand = new StackPanel { Orientation = Orientation.Horizontal, Background = Brushes.Transparent };
        brand.Children.Add(Cozy.Mascot(43));
        var label = Cozy.Label(preview ? "SLOUCHBELL  /  PREVIEW" : "SLOUCHBELL  /  POSTURE CHECK");
        label.Margin = new Thickness(6, 0, 0, 0);
        label.VerticalAlignment = VerticalAlignment.Center;
        brand.Children.Add(label);
        brand.MouseLeftButtonDown += (sender, e) =>
        {
            if (e.ButtonState != MouseButtonState.Pressed) return;
            try { DragMove(); ClampToScreen(); }
            catch (InvalidOperationException) { }
        };
        header.Children.Add(brand);
        body.Children.Add(header);

        heading = Cozy.Text(SlouchBell.TitleText, 26, "Ink", true);
        heading.FontWeight = FontWeights.Bold;
        heading.Margin = new Thickness(0, 10, 0, 6);
        body.Children.Add(heading);
        prompt = Cozy.Text(message.Text, app.Preferences.Size == "Large" ? 21 : 18, "Ink", false);
        prompt.Margin = new Thickness(0, 0, 0, 15);
        body.Children.Add(prompt);
        actions = new WrapPanel();
        done = Cozy.Button("✓  I fixed it", Complete, true);
        actions.Children.Add(done);
        Button snooze = null;
        snooze = Cozy.Button("Snooze ▾", () => snooze.ContextMenu.IsOpen = true);
        snooze.Margin = new Thickness(6, 0, 0, 0);
        var snoozes = Cozy.Options(snooze);
        foreach (int time in new[] { 5, 10, 30 })
        {
            int minutes = time;
            Cozy.Option(snoozes, minutes + " minutes", () =>
            {
                if (preview) Chimes.Play(app.Preferences, "snooze");
                else app.Snooze(minutes, false);
                React("Snoozed for " + minutes + " minutes.", "Enjoy your temporary freedom.");
            });
        }
        actions.Children.Add(snooze);
        body.Children.Add(actions);

        footer = new StackPanel();
        var links = new WrapPanel { Margin = new Thickness(0, 5, 0, 0) };
        reset = Cozy.Button("20-second reset", ToggleReset, flat: true);
        links.Children.Add(reset);
        Button more = null;
        more = Cozy.Button("Pause / move ▾", () => more.ContextMenu.IsOpen = true, flat: true);
        var options = Cozy.Options(more);
        Cozy.Option(options, "Pause for 1 hour", () => Pause(60, "for 1 hour"));
        Cozy.Option(options, "Pause until tomorrow", () => Pause(0, "until tomorrow"));
        Cozy.Option(options, "Pause until I resume", () => Pause(-1, "until you resume"));
        options.Items.Add(new Separator());
        foreach (string corner in new[] { "Top left", "Top right", "Bottom left", "Bottom right" })
        {
            string target = corner;
            Cozy.Option(options, "Move to " + target.ToLowerInvariant(), () => { location = target; Place(false); });
        }
        links.Children.Add(more);
        footer.Children.Add(links);
        timerLabel = Cozy.Text("", 14, "Muted", true);
        timerLabel.Typography.NumeralAlignment = FontNumeralAlignment.Tabular;
        timerLabel.Visibility = Visibility.Collapsed;
        footer.Children.Add(timerLabel);
        body.Children.Add(footer);

        PreviewKeyDown += (sender, e) => { if (e.Key == Key.Escape) { e.Handled = true; Dismiss(); } };
        Activated += (sender, e) => { if (!IsKeyboardFocusWithin && !completed) done.Focus(); };
        SourceInitialized += (sender, e) =>
        {
            handle = new WindowInteropHelper(this).Handle;
            var area = monitor.WorkingArea;
            SetWindowPos(handle, IntPtr.Zero, area.Left + 12, area.Top + 12, 0, 0, 0x15);
            double scale = DpiScale();
            MaxWidth = Math.Max(180, area.Width / scale - 16);
            MaxHeight = Math.Max(150, area.Height / scale - 16);
        };
        ContentRendered += (sender, e) =>
        {
            if (positioned) return;
            positioned = true;
            Place(true);
            Animate(true);
        };
        SizeChanged += (sender, e) => { if (positioned) ClampToScreen(); };
        resetTimer.Tick += (sender, e) => UpdateReset();
        Closed += (sender, e) => resetTimer.Stop();
    }

    private void Complete()
    {
        if (completed || closing) return;
        app.Done(preview);
        React("✓  Posture restored", "Slay responsibly, Bubby.");
    }

    private void Pause(int minutes, string label)
    {
        if (!preview) app.Pause(minutes, false);
        React("Reminders paused", "Paused " + label + ".");
    }

    private void React(string state, string line)
    {
        if (completed || closing) return;
        completed = true;
        resetTimer.Stop();
        heading.Text = state;
        heading.SetResourceReference(TextBlock.ForegroundProperty, "Success");
        prompt.Text = line;
        actions.Visibility = footer.Visibility = Visibility.Collapsed;
        card.SetResourceReference(Border.BackgroundProperty, "SuccessTint");
        var delay = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1450) };
        delay.Tick += (sender, e) => { delay.Stop(); Dismiss(); };
        delay.Start();
    }

    private void ToggleReset()
    {
        if (resetTimer.IsEnabled)
        {
            resetTimer.Stop();
            timerLabel.Visibility = Visibility.Collapsed;
            reset.Content = "20-second reset";
            return;
        }
        resetClock.Restart();
        reset.Content = "Stop reset";
        timerLabel.Visibility = Visibility.Visible;
        resetTimer.Start();
        UpdateReset();
    }

    private void UpdateReset()
    {
        int remaining = Math.Max(0, 20 - (int)resetClock.Elapsed.TotalSeconds);
        timerLabel.Text = remaining > 10 ? "Sit tall and take a breath · " + remaining + "s" :
            "Let your shoulders relax · " + remaining + "s";
        if (remaining == 0) Complete();
    }

    private void Dismiss()
    {
        if (closing || !IsLoaded) return;
        closing = true;
        Animate(false);
    }

    private void Animate(bool entering)
    {
        bool slide = !app.Preferences.ReducedMotion && SystemParameters.ClientAreaAnimation;
        int time = entering ? 280 : 145;
        var duration = TimeSpan.FromMilliseconds(time);
        var fade = new DoubleAnimation(entering ? 1 : 0, duration) { EasingFunction = new QuadraticEase() };
        if (!entering) fade.Completed += (sender, e) => Close();
        BeginAnimation(OpacityProperty, fade);
        if (slide)
        {
            var lift = new TranslateTransform(0, entering ? 8 : 0);
            card.RenderTransform = lift;
            lift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(entering ? 0 : -6, duration));
        }
    }

    private double DpiScale()
    {
        try { return Math.Max(96u, Desktop.GetDpiForWindow(handle)) / 96.0; }
        catch (EntryPointNotFoundException) { return 1; }
    }

    private void Place(bool initial)
    {
        Desktop.Rect rect;
        if (!GetWindowRect(handle, out rect)) return;
        if (!initial) monitor = Forms.Screen.FromHandle(handle);
        var work = monitor.WorkingArea;
        var target = Placement.Pick(work, rect.Right - rect.Left, rect.Bottom - rect.Top, location, Forms.Cursor.Position, random);
        SetWindowPos(handle, IntPtr.Zero, target.X, target.Y, 0, 0, 0x15);
    }

    internal void ClampToScreen()
    {
        Desktop.Rect rect;
        if (!positioned || !GetWindowRect(handle, out rect)) return;
        monitor = Forms.Screen.FromHandle(handle);
        var work = monitor.WorkingArea;
        var target = Placement.Clamp(work, rect.Right - rect.Left, rect.Bottom - rect.Top, rect.Left, rect.Top);
        SetWindowPos(handle, IntPtr.Zero, target.X, target.Y, 0, 0, 0x15);
    }
}

internal static class Placement
{
    private static readonly string[] Corners = { "Top left", "Top right", "Bottom left", "Bottom right" };
    private static Drawing.Point? previous;

    internal static Drawing.Point Pick(Drawing.Rectangle work, int width, int height, string position, Drawing.Point pointer, Random random)
    {
        int left = work.Left + 9, top = work.Top + 9;
        int right = Math.Max(left, work.Right - width - 9), bottom = Math.Max(top, work.Bottom - height - 9);
        if (position == "Random corner")
        {
            var candidates = Corners.Select(corner => Corner(corner, left, top, right, bottom)).ToArray();
            var safe = candidates.Where(point => IsClear(point, width, height, pointer) && point != previous).ToArray();
            var choices = safe.Length > 0 ? safe : candidates;
            var picked = choices[random.Next(choices.Length)];
            previous = picked;
            return picked;
        }
        if (position != "Random anywhere") return Corner(position, left, top, right, bottom);
        Drawing.Point result = new Drawing.Point(left, top);
        for (int i = 0; i < 24; i++)
        {
            result = new Drawing.Point(random.Next(left, right + 1), random.Next(top, bottom + 1));
            if (IsClear(result, width, height, pointer) && result != previous) break;
        }
        previous = result;
        return result;
    }

    private static bool IsClear(Drawing.Point point, int width, int height, Drawing.Point pointer)
    {
        return !Drawing.Rectangle.Inflate(new Drawing.Rectangle(point.X, point.Y, width, height), 24, 24).Contains(pointer);
    }

    private static Drawing.Point Corner(string corner, int left, int top, int right, int bottom)
    {
        return new Drawing.Point(corner.EndsWith("right", StringComparison.Ordinal) ? right : left,
            corner.StartsWith("Bottom", StringComparison.Ordinal) ? bottom : top);
    }

    internal static Drawing.Point Clamp(Drawing.Rectangle work, int width, int height, int x, int y)
    {
        return new Drawing.Point(Math.Max(work.Left, Math.Min(x, work.Right - width)),
            Math.Max(work.Top, Math.Min(y, work.Bottom - height)));
    }
}
