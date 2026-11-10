using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Drawing = System.Drawing;

internal static class SelfTest
{
    internal static int Run()
    {
        try
        {
            var settings = new Settings();
            var random = new Random(17);
            Require(Deck.BuiltIn.Count == 57, "approved message count");
            Require(Deck.BuiltIn.Single(line => line.Id == "90").Text ==
                "Posture check! Ang goal ay mag mukhang human, hindi cooked shrimp", "approved shrimp edit");
            var round = Enumerable.Range(0, 57).Select(index => Deck.Draw(settings, random).Id).ToArray();
            Require(round.Distinct().Count() == 57, "no repeats within the deck");
            Require(Deck.Draw(settings, random).Id != round.Last(), "no repeat at the reshuffle boundary");
            var monday = new DateTime(2026, 11, 9, 9, 0, 0);
            var scheduled = Schedule.Plan(settings, monday, random);
            Require(scheduled >= monday.AddMinutes(40) && scheduled <= monday.AddMinutes(75), "random delay bounds");
            Require(Schedule.NextAllowed(settings, monday.AddDays(5), false) == monday.AddDays(7), "weekend exclusion");
            settings.SnoozedUntil = monday.AddMinutes(10).Ticks;
            Require(Schedule.Plan(settings, monday, random) == monday.AddMinutes(10), "snooze deadline");
            settings.Start = "21:00"; settings.End = "05:00";
            Require(Schedule.NextAllowed(settings, monday.AddHours(16), false) == monday.AddHours(16), "overnight active hours");
            settings.SnoozedUntil = 0;
            settings.Start = "09:00"; settings.End = "18:00";
            settings.Day = Schedule.DayKey(monday);
            settings.Sent = settings.DailyLimit;
            var nextDay = Schedule.Plan(settings, monday, random);
            Require(nextDay >= monday.AddDays(1).AddMinutes(40) && nextDay <= monday.AddDays(1).AddMinutes(75),
                "daily cap defers to a randomized slot the next day");

            var work = new Drawing.Rectangle(-1920, 0, 1920, 1040);
            var cursor = new Drawing.Point(-900, 500);
            var points = Enumerable.Range(0, 60).Select(index => Placement.Pick(work, 446, 296, "Random anywhere", cursor, random)).ToArray();
            Require(points.Distinct().Count() > 55, "varied random placements");
            foreach (var point in points)
            {
                var popup = new Drawing.Rectangle(point.X, point.Y, 446, 296);
                Require(work.Contains(popup) && !popup.Contains(cursor), "pop-up stays within its working area and avoids the cursor");
            }
            using (var sample = typeof(SelfTest).Assembly.GetManifestResourceStream("Muted.ico"))
            using (var icon = new Drawing.Icon(sample)) Require(icon.Width > 0, "tray icon loads");

            var output = Environment.GetEnvironmentVariable("SLOUCHBELL_TEST_OUTPUT");
            if (!string.IsNullOrEmpty(output))
            {
                Directory.CreateDirectory(output);
                var stored = Path.Combine(output, "test-settings.json");
                settings.Custom.Add("Test only");
                Store.Write(stored, settings);
                settings.Volume = 47;
                Store.Write(stored, settings);
                var read = Store.Read(stored);
                Require(read.Volume == 47 && read.Custom.Single() == "Test only" && read.Played.Count > 0, "settings round-trip");
                Render(output);
            }
            return 0;
        }
        catch (Exception error)
        {
            var output = Environment.GetEnvironmentVariable("SLOUCHBELL_TEST_OUTPUT");
            if (!string.IsNullOrEmpty(output)) File.WriteAllText(Path.Combine(output, "test-error.txt"), error.ToString());
            return 1;
        }
    }

    private static void Render(string output)
    {
        var app = new SlouchBell { Preferences = new Settings { Theme = "Light", ReducedMotion = true },
            ShutdownMode = ShutdownMode.OnExplicitShutdown, Next = DateTime.Now.AddHours(1) };
        var home = new Dashboard(app);
        home.ShowActivated = false;
        home.Opacity = 0;
        home.Show();
        home.Dispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));
        Capture(home, Path.Combine(output, "dashboard.png"));
        home.Width = 360;
        home.Dispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));
        Capture(home, Path.Combine(output, "dashboard-narrow.png"));
        var reminders = FindButton((DependencyObject)home.Content, "Reminders");
        Require(reminders != null, "reminder settings navigation");
        reminders.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        home.Dispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));
        Require(FindButton((DependencyObject)home.Content, "Save reminder settings") != null, "schedule settings open");
        app.Preferences.Theme = "Dark";
        app.Preferences.Sound = "Silent";
        var popup = new Reminder(app, Deck.BuiltIn.Single(item => item.Id == "84"), true, new Random(4));
        popup.Show();
        popup.Dispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));
        Capture(popup, Path.Combine(output, "reminder.png"));
        var reset = FindButton((DependencyObject)popup.Content, "20-second reset");
        Require(reset != null, "guided reset action");
        reset.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Require((string)reset.Content == "Stop reset", "reset starts");
        reset.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Require((string)reset.Content == "20-second reset", "reset can be stopped");
        var done = FindButton((DependencyObject)popup.Content, "✓  I fixed it");
        Require(done != null, "completion action");
        done.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Require(done.Visibility == Visibility.Visible && !done.IsVisible && app.TodayCompleted == 0,
            "preview completes without changing today’s count");
        popup.Close();
        app.Shutdown();
    }

    private static Button FindButton(DependencyObject root, string label)
    {
        var button = root as Button;
        if (button != null && Equals(button.Content, label)) return button;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var match = FindButton(VisualTreeHelper.GetChild(root, i), label);
            if (match != null) return match;
        }
        return null;
    }

    private static void Capture(Window window, string file)
    {
        var root = (FrameworkElement)window.Content;
        root.UpdateLayout();
        Require(root.ActualWidth > 250 && root.ActualHeight > 100, "window measured");
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth), (int)Math.Ceiling(root.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = File.Create(file)) encoder.Save(stream);
    }

    private static void Require(bool condition, string scenario)
    {
        if (!condition) throw new InvalidOperationException("Failed check: " + scenario);
    }
}
