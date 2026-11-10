using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

internal sealed class Dashboard : Window
{
    private readonly SlouchBell app;
    private readonly DockPanel layout = new DockPanel();
    private readonly StackPanel rail = new StackPanel();
    private readonly ScrollViewer scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly Dictionary<string, Button> tabs = new Dictionary<string, Button>();
    private StackPanel content;
    private string page;
    private TextBlock status, next, total, feedback;
    private Button pause, remind;
    private TextBlock fieldError;
    private Border hero;
    private Grid heroGrid;
    private Viewbox illustration;
    private bool lastPaused, hasHeroStatus, narrow;
    private Settings Preferences { get { return app.Preferences; } }

    internal Dashboard(SlouchBell app)
    {
        this.app = app;
        Title = "SlouchBell";
        Width = 930; Height = 720; MinWidth = 360; MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = new FontFamily("Segoe UI");
        UseLayoutRounding = true;
        ApplyTheme();
        SetResourceReference(BackgroundProperty, "Page");
        layout.SetResourceReference(Panel.BackgroundProperty, "Page");
        Content = layout;

        var sidebar = new Border { Child = rail };
        sidebar.SetResourceReference(Border.BackgroundProperty, "Surface");
        sidebar.SetResourceReference(Border.BorderBrushProperty, "Line");
        sidebar.BorderThickness = new Thickness(0, 0, 1, 0);
        DockPanel.SetDock(sidebar, Dock.Left);
        layout.Children.Add(sidebar);

        var brand = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 2, 0, 24) };
        brand.Children.Add(Cozy.Mascot(38));
        var wordmark = Cozy.Text("SlouchBell", 20, "Ink", true);
        wordmark.Margin = new Thickness(7, 0, 0, 0);
        wordmark.VerticalAlignment = VerticalAlignment.Center;
        brand.Children.Add(wordmark);
        rail.Children.Add(brand);

        var nav = new WrapPanel();
        rail.Children.Add(nav);
        foreach (string name in new[] { "Home", "Reminders", "Messages", "Sounds", "Appearance", "General", "About" })
        {
            string key = name;
            var button = Cozy.Button(name, () => Navigate(key, true), flat: true);
            button.HorizontalContentAlignment = HorizontalAlignment.Left;
            tabs.Add(name, button);
            nav.Children.Add(button);
        }
        layout.Children.Add(scroll);
        SizeChanged += (sender, e) =>
        {
            narrow = ActualWidth < 680;
            DockPanel.SetDock(sidebar, narrow ? Dock.Top : Dock.Left);
            sidebar.BorderThickness = narrow ? new Thickness(0, 0, 0, 1) : new Thickness(0, 0, 1, 0);
            rail.Width = narrow ? double.NaN : 176;
            rail.Margin = narrow ? new Thickness(10, 3, 10, 4) : new Thickness(13, 22, 13, 12);
            brand.Visibility = narrow ? Visibility.Collapsed : Visibility.Visible;
            foreach (var button in tabs.Values) button.Width = narrow ? double.NaN : 150;
            if (illustration != null) illustration.Visibility = ActualWidth < 590 ? Visibility.Collapsed : Visibility.Visible;
            if (content != null) content.Margin = narrow ? new Thickness(16, 18, 16, 24) : new Thickness(34, 30, 34, 30);
        };
        Closing += (sender, e) =>
        {
            if (app.Exiting || Dispatcher.HasShutdownStarted) return;
            e.Cancel = true;
            Hide();
        };
        Navigate("Home", false);
    }

    internal void ApplyTheme() { Cozy.Apply(this, Preferences.Theme); }

    private void Navigate(string name, bool focus)
    {
        page = name;
        Title = name + " · SlouchBell";
        foreach (var entry in tabs)
        {
            bool selected = entry.Key == name;
            Cozy.PaintButton(entry.Value, false, !selected);
            if (selected) entry.Value.SetResourceReference(Control.BackgroundProperty, "Tint");
            AutomationProperties.SetItemStatus(entry.Value, selected ? "Current page" : "");
        }

        status = next = total = null;
        fieldError = null;
        heroGrid = null;
        illustration = null;
        hasHeroStatus = false;
        content = new StackPanel { MaxWidth = 810, Margin = narrow ?
            new Thickness(16, 18, 16, 24) : new Thickness(34, 30, 34, 30) };
        scroll.Content = content;
        scroll.ScrollToTop();

        var kicker = Cozy.Label(name == "Home" ? "THE POSTURE CLUB" : "SLOUCHBELL  /  SETTINGS");
        content.Children.Add(kicker);
        var heading = Cozy.Text(name == "Home" ? "Hey, Bubby!" : name, 37, "Ink", true);
        heading.Focusable = true;
        heading.IsHitTestVisible = false;
        content.Children.Add(heading);
        var intro = Cozy.Text(Description(name), 15, "Muted", false);
        intro.Margin = new Thickness(0, 5, 0, 21);
        content.Children.Add(intro);
        feedback = Cozy.Text("", 14, "Ink", true);
        AutomationProperties.SetLiveSetting(feedback, AutomationLiveSetting.Polite);

        switch (name)
        {
            case "Home": BuildHome(); break;
            case "Reminders": BuildReminders(); break;
            case "Messages": BuildMessages(); break;
            case "Sounds": BuildSounds(); break;
            case "Appearance": BuildAppearance(); break;
            case "General": BuildGeneral(); break;
            case "About": BuildAbout(); break;
        }
        content.Children.Add(feedback);
        if (focus) Keyboard.Focus(heading);
    }

    private static string Description(string name)
    {
        switch (name)
        {
            case "Home": return "A little nudge to sit tall while you do your thing.";
            case "Reminders": return "Choose when SlouchBell can pop in and where it lands.";
            case "Messages": return "Each enabled line plays once before the deck reshuffles.";
            case "Sounds": return "Pick a small sound for your posture check.";
            case "Appearance": return "Make the dashboard and pop-up feel like yours.";
            case "General": return "Manage how SlouchBell runs in the background.";
            default: return "A posture reminder for Windows. Version 1.0.0.";
        }
    }

    private void BuildHome()
    {
        hero = Cozy.Card("Tint", 23, 23);
        hero.Margin = new Thickness(0, 0, 0, 16);
        heroGrid = new Grid();
        heroGrid.ColumnDefinitions.Add(new ColumnDefinition());
        heroGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        heroGrid.Children.Add(words);
        hero.Child = heroGrid;
        status = Cozy.Label("REMINDERS ON");
        words.Children.Add(status);
        words.Children.Add(Cozy.Text("Time to unslouch?", 28, "Ink", true));
        next = Cozy.Text("", 15, "Muted", false);
        next.Margin = new Thickness(0, 6, 0, 13);
        words.Children.Add(next);
        var actions = new WrapPanel();
        remind = Cozy.Button("Remind me now", app.RemindNow, true);
        actions.Children.Add(remind);
        pause = Cozy.Button("Pause ▾", () =>
        {
            if (app.Paused || app.Snoozed) app.Resume();
            else pause.ContextMenu.IsOpen = true;
        });
        pause.Margin = new Thickness(5, 0, 0, 0);
        var menu = Cozy.Options(pause);
        Cozy.Option(menu, "Snooze for 5 minutes", () => app.Snooze(5, true));
        Cozy.Option(menu, "Snooze for 10 minutes", () => app.Snooze(10, true));
        Cozy.Option(menu, "Snooze for 30 minutes", () => app.Snooze(30, true));
        menu.Items.Add(new Separator());
        Cozy.Option(menu, "Pause for 1 hour", () => app.Pause(60, true));
        Cozy.Option(menu, "Pause until tomorrow", () => app.Pause(0, true));
        Cozy.Option(menu, "Pause until I resume", () => app.Pause(-1, true));
        Cozy.Option(menu, "Custom pause…", CustomPause);
        actions.Children.Add(pause);
        words.Children.Add(actions);
        content.Children.Add(hero);

        var stats = new WrapPanel();
        var count = Cozy.Card("Surface", 16, 16);
        count.MinWidth = 154;
        count.Margin = new Thickness(0, 0, 12, 12);
        var countBody = new StackPanel();
        countBody.Children.Add(Cozy.Label("FIXED TODAY"));
        total = Cozy.Text("0", 26, "Ink", true);
        total.Typography.NumeralAlignment = FontNumeralAlignment.Tabular;
        countBody.Children.Add(total);
        count.Child = countBody;
        stats.Children.Add(count);
        var schedule = Cozy.Card("Surface", 16, 16);
        schedule.Margin = new Thickness(0, 0, 0, 12);
        var scheduleBody = new StackPanel();
        scheduleBody.Children.Add(Cozy.Label("ACTIVE HOURS"));
        scheduleBody.Children.Add(Cozy.Text(Preferences.Start + "–" + Preferences.End, 22, "Ink", true));
        schedule.Child = scheduleBody;
        stats.Children.Add(schedule);
        content.Children.Add(stats);

        var card = Group(Preferences.Welcomed ? "FROM THE DECK" : "FIRST POSTURE CHECK");
        var sample = Deck.BuiltIn.Single(message => message.Id == "84");
        card.Children.Add(Cozy.Text("“" + sample.Text + "”", 18, "Ink", true));
        var row = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        row.Children.Add(Cozy.Button("Try this line", () =>
        {
            Preferences.Welcomed = true;
            app.Save();
            app.Deliver(true, sample);
        }, flat: true));
        row.Children.Add(Cozy.Button("Set active hours", () => Navigate("Reminders", true), flat: true));
        card.Children.Add(row);
        RefreshStatus();
    }

    internal void RefreshStatus()
    {
        if (page != "Home" || status == null) return;
        status.Text = app.Status.ToUpperInvariant();
        next.Text = app.NextLabel;
        total.Text = app.TodayCompleted.ToString(CultureInfo.CurrentCulture);
        bool suspended = app.Paused || app.Snoozed;
        pause.Content = suspended ? "Resume now" : "Pause ▾";
        remind.Content = suspended || !app.HasMessages ? "Preview a reminder" : "Remind me now";
        Cozy.PaintButton(pause, suspended, false);
        Cozy.PaintButton(remind, !suspended, false);
        if (hasHeroStatus && lastPaused == suspended) return;
        lastPaused = suspended;
        hasHeroStatus = true;
        if (illustration != null) heroGrid.Children.Remove(illustration);
        illustration = Cozy.Mascot(145, suspended);
        illustration.Margin = new Thickness(16, 0, 0, 0);
        illustration.Visibility = ActualWidth > 0 && ActualWidth < 590 ? Visibility.Collapsed : Visibility.Visible;
        Grid.SetColumn(illustration, 1);
        heroGrid.Children.Add(illustration);
    }

    private void CustomPause()
    {
        var dialog = new Window
        {
            Owner = this, Title = "Pause reminders", Width = 330, SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize,
            FontFamily = FontFamily
        };
        Cozy.Apply(dialog, Preferences.Theme);
        dialog.SetResourceReference(BackgroundProperty, "Page");
        var body = new StackPanel { Margin = new Thickness(18) };
        dialog.Content = body;
        var value = Input(body, "Pause for (minutes)", "60");
        var message = Cozy.Text("", 13, "Ink", false);
        body.Children.Add(message);
        var row = new WrapPanel();
        var submit = Cozy.Button("Pause", () =>
        {
            int minutes;
            if (!int.TryParse(value.Text, out minutes) || minutes < 1 || minutes > 10080)
            {
                message.Text = "Enter a duration from 1 to 10,080 minutes.";
                value.Focus();
                return;
            }
            app.Pause(minutes, true);
            dialog.Close();
        }, true);
        submit.IsDefault = true;
        row.Children.Add(submit);
        var cancel = Cozy.Button("Cancel", () => dialog.Close(), flat: true);
        cancel.IsCancel = true;
        row.Children.Add(cancel);
        body.Children.Add(row);
        dialog.Loaded += (sender, e) => { value.Focus(); value.SelectAll(); };
        dialog.ShowDialog();
    }

    private void BuildReminders()
    {
        var timing = Group("TIMING");
        var min = Input(timing, "Minimum minutes", Preferences.Minimum.ToString());
        var max = Input(timing, "Maximum minutes", Preferences.Maximum.ToString());
        var cap = Input(timing, "Daily reminder limit", Preferences.DailyLimit.ToString());
        var hours = Group("ACTIVE HOURS");
        var start = Input(hours, "Start (24-hour HH:mm)", Preferences.Start);
        var end = Input(hours, "End (24-hour HH:mm)", Preferences.End);
        hours.Children.Add(Cozy.Label("ACTIVE DAYS"));
        var days = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
        var dayChecks = new List<CheckBox>();
        for (int i = 0; i < 7; i++)
        {
            int day = (i + 1) % 7;
            var check = Toggle(days, CultureInfo.CurrentCulture.DateTimeFormat.GetAbbreviatedDayName((DayOfWeek)day),
                (Preferences.Days & (1 << day)) != 0, null);
            dayChecks.Add(check);
        }
        hours.Children.Add(days);
        var placement = Group("DELIVERY");
        var position = Choose(placement, "Pop-up position", Preferences.Position,
            "Random anywhere", "Random corner", "Top left", "Top right", "Bottom left", "Bottom right");
        var quiet = Toggle(placement, "Defer during fullscreen apps and presentations", Preferences.Quiet, null);
        var save = Cozy.Button("Save reminder settings", () =>
        {
            int minimum, maximum, limit;
            TimeSpan from, until;
            if (!int.TryParse(min.Text, out minimum) || minimum < 1 || minimum > 1440)
            { Fail("Enter a minimum of 1–1,440 minutes.", min); return; }
            if (!int.TryParse(max.Text, out maximum) || maximum < minimum || maximum > 1440)
            { Fail("Enter a maximum between the minimum and 1,440 minutes.", max); return; }
            if (!int.TryParse(cap.Text, out limit) || limit < 1 || limit > 100)
            { Fail("Enter a daily limit of 1–100 reminders.", cap); return; }
            if (!Schedule.TryHours(start.Text, out from)) { Fail("Enter a start time like 09:00.", start); return; }
            if (!Schedule.TryHours(end.Text, out until)) { Fail("Enter an end time like 18:00.", end); return; }
            int enabledDays = 0;
            for (int i = 0; i < dayChecks.Count; i++)
                if (dayChecks[i].IsChecked == true) enabledDays |= 1 << ((i + 1) % 7);
            if (enabledDays == 0) { Fail("Select at least one active day.", dayChecks[0]); return; }
            Preferences.Minimum = minimum; Preferences.Maximum = maximum; Preferences.DailyLimit = limit;
            Preferences.Start = start.Text; Preferences.End = end.Text; Preferences.Days = enabledDays;
            Preferences.Position = (string)position.SelectedItem; Preferences.Quiet = quiet.IsChecked == true;
            Persist(true);
        }, true);
        save.HorizontalAlignment = HorizontalAlignment.Left;
        content.Children.Add(save);
    }

    private void BuildMessages()
    {
        var add = Group("ADD YOUR OWN");
        var message = Input(add, "Custom reminder", "");
        message.MaxLength = 220;
        var list = Group("YOUR MESSAGE DECK");
        Action fill = () =>
        {
            list.Children.Clear();
            list.Children.Add(Cozy.Label("YOUR MESSAGE DECK"));
            foreach (var line in Deck.All(Preferences))
            {
                var row = new DockPanel();
                if (line.Id.StartsWith("custom:", StringComparison.Ordinal))
                {
                    var remove = Cozy.Button("Remove", () =>
                    {
                        Preferences.Custom.Remove(line.Text);
                        Preferences.Disabled.Remove(line.Id);
                        Preferences.Played.Remove(line.Id);
                        Persist(true);
                        Navigate("Messages", true);
                    }, flat: true);
                    AutomationProperties.SetName(remove, "Remove custom reminder: " + line.Text);
                    DockPanel.SetDock(remove, Dock.Right);
                    row.Children.Add(remove);
                }
                var caption = Cozy.Text(line.Text + (line.Id == "84" ? "  ★" : ""), 14, "Ink", false);
                var check = Toggle(row, line.Text, !Preferences.Disabled.Contains(line.Id), enabled =>
                {
                    if (enabled) Preferences.Disabled.Remove(line.Id);
                    else Preferences.Disabled.Add(line.Id);
                    Persist(true);
                });
                check.Content = caption;
                list.Children.Add(row);
            }
        };
        var button = Cozy.Button("Add to deck", () =>
        {
            var value = message.Text.Trim();
            if (value.Length == 0) { Fail("Enter a reminder message first.", message); return; }
            if (Deck.All(Preferences).Any(item => string.Equals(item.Text, value, StringComparison.OrdinalIgnoreCase)))
            { Fail("That line is already in the deck.", message); return; }
            Preferences.Custom.Insert(0, value);
            message.Clear();
            Persist(true);
            fill();
        }, true);
        button.HorizontalAlignment = HorizontalAlignment.Left;
        add.Children.Add(button);
        fill();
    }

    private void BuildSounds()
    {
        var card = Group("REMINDER CHIME");
        var sound = Choose(card, "Sound", Preferences.Sound, "Soft bell", "Wood tap", "Glass chime", "Digital pop", "Silent");
        sound.SelectionChanged += (sender, e) => { Preferences.Sound = (string)sound.SelectedItem; Persist(false); };
        var volumeLabel = Cozy.Label("Volume");
        card.Children.Add(volumeLabel);
        var volume = new Slider { Minimum = 0, Maximum = 100, Value = Preferences.Volume, LargeChange = 10, SmallChange = 1,
            MaxWidth = 330, HorizontalAlignment = HorizontalAlignment.Stretch, MinHeight = 29, Margin = new Thickness(0, 0, 0, 4) };
        AutomationProperties.SetName(volume, "Volume");
        card.Children.Add(volume);
        var value = Cozy.Text(Preferences.Volume + "%", 14, "Muted", false);
        card.Children.Add(value);
        volume.ValueChanged += (sender, e) => { Preferences.Volume = (int)Math.Round(volume.Value); value.Text = Preferences.Volume + "%"; Persist(false); };
        var play = Cozy.Button("Play sound preview", () => Chimes.Play(Preferences, "arrive"), true);
        play.Margin = new Thickness(0, 10, 0, 0);
        play.HorizontalAlignment = HorizontalAlignment.Left;
        card.Children.Add(play);
        var reactions = Group("REACTIONS");
        Toggle(reactions, "Play a sound when I fix it", Preferences.CompletionSound, on => { Preferences.CompletionSound = on; Persist(false); });
        Toggle(reactions, "Play a sound when I snooze", Preferences.SnoozeSound, on => { Preferences.SnoozeSound = on; Persist(false); });
    }

    private void BuildAppearance()
    {
        var card = Group("COZY POP");
        var theme = Choose(card, "Color theme", Preferences.Theme, "System", "Light", "Dark");
        theme.SelectionChanged += (sender, e) =>
        { Preferences.Theme = (string)theme.SelectedItem; ApplyTheme(); Persist(false); };
        var size = Choose(card, "Pop-up size", Preferences.Size, "Compact", "Standard", "Large");
        size.SelectionChanged += (sender, e) => { Preferences.Size = (string)size.SelectedItem; Persist(false); };
        Toggle(card, "Reduce motion", Preferences.ReducedMotion, on => { Preferences.ReducedMotion = on; Persist(false); });
        var preview = Cozy.Button("Preview a reminder", () => app.Deliver(true, null), true);
        preview.HorizontalAlignment = HorizontalAlignment.Left;
        card.Children.Add(preview);
    }

    private void BuildGeneral()
    {
        var card = Group("WINDOWS STARTUP");
        bool isEnabled = SlouchBell.StartupEnabled();
        var startup = Toggle(card, "Start SlouchBell when I sign in", isEnabled, null);
        startup.Click += (sender, e) =>
        {
            try { SlouchBell.SetStartup(startup.IsChecked == true); SetFeedback("Startup setting saved."); }
            catch (UnauthorizedAccessException) { startup.IsChecked = SlouchBell.StartupEnabled(); SetFeedback("Windows blocked the startup change."); }
            catch (System.Security.SecurityException) { startup.IsChecked = SlouchBell.StartupEnabled(); SetFeedback("Windows blocked the startup change."); }
        };
        Toggle(card, "Open in the tray at sign-in", Preferences.StartMinimized, on => { Preferences.StartMinimized = on; Persist(false); });
        var tray = Group("SYSTEM TRAY");
        tray.Children.Add(Cozy.Text("Closing the dashboard keeps reminders running. Use Quit SlouchBell in the tray to stop the app.", 15, "Ink", false));
        if (app.HasHotkey)
            tray.Children.Add(Cozy.Text("Ctrl + Alt + P opens SlouchBell or focuses a reminder.", 14, "Muted", false));
        var reset = Group("RESET SETTINGS");
        reset.Children.Add(Cozy.Text("Restore defaults and clear custom messages, disabled lines, and today’s count.", 14, "Muted", false));
        var button = Cozy.Button("Reset settings…", () =>
        {
            if (MessageBox.Show(this, "Reset your local SlouchBell settings and custom messages?", "Reset settings",
                MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            try { SlouchBell.SetStartup(false); }
            catch (UnauthorizedAccessException) { SetFeedback("Windows blocked the startup change. Turn it off in Windows Startup apps and try again."); return; }
            catch (System.Security.SecurityException) { SetFeedback("Windows blocked the startup change. Turn it off in Windows Startup apps and try again."); return; }
            app.Preferences = new Settings();
            ApplyTheme();
            Navigate("General", true);
            Persist(true);
        });
        button.HorizontalAlignment = HorizontalAlignment.Left;
        reset.Children.Add(button);
    }

    private void BuildAbout()
    {
        var privacy = Group("LOCAL BY DESIGN");
        privacy.Children.Add(Cozy.Text("SlouchBell runs offline. It uses a timer and Windows activity state to deliver reminders. Your preferences and message history stay on this computer.", 15, "Ink", false));
        var uninstall = Group("UNINSTALL");
        uninstall.Children.Add(Cozy.Text("Open Windows Installed apps, find SlouchBell, and choose Uninstall. You can keep or remove your settings.", 15, "Ink", false));
        var open = Cozy.Button("Open Installed apps", () =>
        {
            try { System.Diagnostics.Process.Start("ms-settings:appsfeatures"); }
            catch (System.ComponentModel.Win32Exception) { SetFeedback("Open Settings → Apps → Installed apps from the Start menu."); }
        }, true);
        open.HorizontalAlignment = HorizontalAlignment.Left;
        uninstall.Children.Add(open);
    }

    private StackPanel Group(string heading)
    {
        var border = Cozy.Card("Surface", 18, 16);
        border.Margin = new Thickness(0, 0, 0, 13);
        var body = new StackPanel();
        body.Children.Add(Cozy.Label(heading));
        border.Child = body;
        content.Children.Add(border);
        return body;
    }

    private static TextBox Input(Panel parent, string caption, string text)
    {
        var box = new TextBox { Text = text, FontSize = 15, MinHeight = 34, Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(0, 0, 0, 10), MaxWidth = 420, HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 155 };
        box.SetResourceReference(Control.ForegroundProperty, "Ink");
        box.SetResourceReference(Control.BackgroundProperty, "Surface");
        box.SetResourceReference(Control.BorderBrushProperty, "ControlLine");
        var label = Cozy.Label(caption);
        AutomationProperties.SetLabeledBy(box, label);
        parent.Children.Add(label);
        parent.Children.Add(box);
        return box;
    }

    private static ComboBox Choose(Panel parent, string caption, string selected, params string[] options)
    {
        var label = Cozy.Label(caption);
        var box = new ComboBox { ItemsSource = options, SelectedIndex = Math.Max(0, Array.IndexOf(options, selected)),
            FontSize = 15, MinHeight = 34, MinWidth = 170, Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(0, 0, 0, 10), HorizontalAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetLabeledBy(box, label);
        parent.Children.Add(label);
        parent.Children.Add(box);
        return box;
    }

    private static CheckBox Toggle(Panel parent, string caption, bool value, Action<bool> change)
    {
        var box = new CheckBox { Content = Cozy.Text(caption, 14, "Ink", false), IsChecked = value, MinHeight = 34, FontSize = 14,
            VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        box.SetResourceReference(Control.ForegroundProperty, "Ink");
        AutomationProperties.SetName(box, caption);
        if (change != null) box.Click += (sender, e) => change(box.IsChecked == true);
        parent.Children.Add(box);
        return box;
    }

    private void Persist(bool schedule)
    {
        ClearError();
        SetFeedback(app.Save() ? "Saved on this computer." : "Settings couldn’t be saved. Check your Windows permissions and try again.");
        app.Changed(schedule);
    }

    private void SetFeedback(string message)
    {
        if (feedback.Text == message) return;
        feedback.Text = message;
        var peer = FrameworkElementAutomationPeer.FromElement(feedback);
        if (peer != null) peer.RaiseAutomationEvent(System.Windows.Automation.Peers.AutomationEvents.LiveRegionChanged);
    }

    private void ClearError()
    {
        if (fieldError == null) return;
        ((Panel)fieldError.Parent).Children.Remove(fieldError);
        fieldError = null;
    }

    private void Fail(string message, Control field)
    {
        ClearError();
        var group = (Panel)field.Parent;
        fieldError = Cozy.Text(message, 14, "Ink", true);
        fieldError.Margin = new Thickness(0, 0, 0, 7);
        group.Children.Insert(group.Children.IndexOf(field) + 1, fieldError);
        SetFeedback(message);
        field.Focus();
        fieldError.BringIntoView();
    }
}
