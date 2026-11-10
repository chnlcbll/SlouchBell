using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

internal sealed class SlouchBell : Application
{
    internal const string TitleText = "FIX POSTURE PLEASE";
    internal const string StartupName = "SlouchBell";
    internal const string StartupKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    internal Settings Preferences;
    internal DateTime Next;
    internal Dashboard Home;
    private Reminder popup;
    private Forms.NotifyIcon tray;
    private Drawing.Icon icon, mutedIcon;
    private DispatcherTimer timer;
    private HwndSource input;
    private readonly Random random = new Random();
    private bool locked, sleeping;
    private DateTime blockedUntil;
    private readonly string settingsFile = Path.Combine(Store.DirectoryPath, "settings.json");

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Contains("--self-test")) return SelfTest.Run();
        using (var activation = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\SlouchBell.Show.v1"))
        using (var stop = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\SlouchBell.Stop.v1"))
        using (var instance = new Mutex(false, @"Local\SlouchBell.App.v1"))
        {
            try
            {
                if (!instance.WaitOne(0)) { activation.Set(); return 0; }
            }
            catch (AbandonedMutexException) { }

            try
            {
                var app = new SlouchBell { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                app.Startup += (sender, e) => app.Start(args.Contains("--startup"));
                var listener = ThreadPool.RegisterWaitForSingleObject(activation, (state, expired) =>
                    app.Dispatcher.BeginInvoke(new Action(app.Open)), null, -1, false);
                var shutdown = ThreadPool.RegisterWaitForSingleObject(stop, (state, expired) =>
                    app.Dispatcher.BeginInvoke(new Action(app.Shutdown)), null, -1, false);
                try { return app.Run(); }
                finally { listener.Unregister(null); shutdown.Unregister(null); }
            }
            finally { instance.ReleaseMutex(); }
        }
    }

    private void Start(bool atLogin)
    {
        try { Preferences = File.Exists(settingsFile) ? Store.Read(settingsFile) : new Settings(); }
        catch (IOException) { Preferences = new Settings(); }
        catch (System.Runtime.Serialization.SerializationException) { Preferences = new Settings(); }

        icon = Drawing.Icon.ExtractAssociatedIcon(Forms.Application.ExecutablePath);
        mutedIcon = new Drawing.Icon(typeof(SlouchBell).Assembly.GetManifestResourceStream("Muted.ico"));
        tray = new Forms.NotifyIcon { Icon = icon, Text = "SlouchBell", Visible = true };
        tray.MouseClick += (sender, e) => { if (e.Button == Forms.MouseButtons.Left) Open(); };

        input = new HwndSource(new HwndSourceParameters("SlouchBell hotkeys") { WindowStyle = 0 });
        input.AddHook(Hotkey);
        HasHotkey = Desktop.RegisterHotKey(input.Handle, 1, 0x4003, 0x50); // Ctrl+Alt+P
        SystemEvents.PowerModeChanged += PowerChanged;
        SystemEvents.SessionSwitch += SessionChanged;
        SystemEvents.TimeChanged += TimeChanged;
        SystemEvents.UserPreferenceChanged += WindowsThemeChanged;
        SystemEvents.DisplaySettingsChanged += DisplayChanged;

        timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        timer.Tick += (sender, e) => Tick();
        Replan();
        timer.Start();
        if (!atLogin || !Preferences.StartMinimized) Open();
    }

    internal bool HasHotkey { get; private set; }
    internal bool Paused { get { return Preferences.PausedUntil > DateTime.Now.Ticks; } }
    internal bool Snoozed { get { return Preferences.SnoozedUntil > DateTime.Now.Ticks; } }
    internal int TodayCompleted { get { return Preferences.Day == Schedule.DayKey(DateTime.Now) ? Preferences.Completed : 0; } }
    internal bool HasMessages { get { return Deck.All(Preferences).Any(message => !Preferences.Disabled.Contains(message.Id)); } }
    internal string Status
    {
        get
        {
            if (Paused) return "Paused";
            if (Snoozed) return "Snoozed";
            if (!HasMessages) return "No messages enabled";
            if (Schedule.NextAllowed(Preferences, DateTime.Now, false) > DateTime.Now) return "Outside active hours";
            if (blockedUntil > DateTime.Now) return "Waiting for a quiet moment";
            if (Preferences.Day == Schedule.DayKey(DateTime.Now) && Preferences.Sent >= Preferences.DailyLimit)
                return "Daily limit reached";
            return "Reminders on";
        }
    }

    internal string NextLabel
    {
        get
        {
            if (Paused) return Preferences.PausedUntil == Schedule.Forever ? "Until you resume" :
                "Resumes " + FormatWhen(new DateTime(Preferences.PausedUntil));
            if (Snoozed) return "Back " + FormatWhen(Next);
            if (!HasMessages || Next == DateTime.MaxValue) return "Enable a message to start";
            if (Next.Date > DateTime.Today || Status == "Outside active hours")
                return "Scheduled for " + FormatWhen(Next);
            if (Status == "Waiting for a quiet moment") return "Reminders will resume when Windows is available.";
            return "One at a random time, every " + Preferences.Minimum + "–" + Preferences.Maximum + " min";
        }
    }

    private static string FormatWhen(DateTime time)
    {
        return time.Date == DateTime.Today ? "at " + time.ToString("t") : time.ToString("ddd, MMM d 'at' t");
    }

    internal bool Save()
    {
        try
        {
            Directory.CreateDirectory(Store.DirectoryPath);
            Store.Write(settingsFile, Preferences);
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    internal void Changed(bool schedule)
    {
        if (schedule) Replan();
        else Refresh();
    }

    internal void Replan()
    {
        var now = DateTime.Now;
        if (!Paused && Preferences.PausedUntil != 0)
        {
            Preferences.PausedUntil = 0;
            Save();
        }
        Next = Paused || !HasMessages ? DateTime.MaxValue : Schedule.Plan(Preferences, now, random);
        Refresh();
    }

    private void Tick()
    {
        if (sleeping || locked) return;
        var now = DateTime.Now;
        if (Preferences.PausedUntil != 0 && !Paused) { Replan(); return; }
        if (Paused || now < Next || now < blockedUntil || popup != null) { UpdateHome(); return; }
        if (Schedule.NextAllowed(Preferences, now, false) > now) { Replan(); return; }
        if (Preferences.Day == Schedule.DayKey(now) && Preferences.Sent >= Preferences.DailyLimit)
        { Replan(); return; }
        if (Preferences.Quiet && Desktop.ShouldDefer())
        {
            blockedUntil = now.AddSeconds(30);
            UpdateHome();
            return;
        }
        Deliver(false, null);
    }

    internal void Open()
    {
        if (Home == null)
        {
            Home = new Dashboard(this);
            MainWindow = Home;
        }
        Home.Show();
        if (Home.WindowState == WindowState.Minimized) Home.WindowState = WindowState.Normal;
        Home.Activate();
        UpdateHome();
    }

    internal void Deliver(bool preview, Message sample, bool activate = false)
    {
        if (popup != null) { if (preview) popup.Activate(); return; }
        var message = sample;
        if (message == null && preview)
        {
            var enabled = Deck.All(Preferences).Where(item => !Preferences.Disabled.Contains(item.Id)).ToList();
            if (enabled.Count > 0) message = enabled[random.Next(enabled.Count)];
        }
        if (message == null && !preview) message = Deck.Draw(Preferences, random);
        if (message == null)
        {
            if (!preview) { Replan(); return; }
            message = new Message("84", "Quick! Magpanggap kang may magandang posture.");
        }

        if (!preview)
        {
            SetDay();
            Preferences.Sent++;
            Preferences.SnoozedUntil = 0;
            Save();
            Replan();
        }
        popup = new Reminder(this, message, preview, random, activate);
        popup.Closed += (sender, e) =>
        {
            popup = null;
            if (Next <= DateTime.Now && Preferences.SnoozedUntil == 0) Replan();
            else Refresh();
        };
        popup.Show();
        Chimes.Play(Preferences, "arrive");
        Refresh();
    }

    private void SetDay()
    {
        var day = Schedule.DayKey(DateTime.Now);
        if (day == Preferences.Day) return;
        Preferences.Day = day;
        Preferences.Sent = Preferences.Completed = 0;
    }

    internal void Done(bool preview)
    {
        if (!preview) { SetDay(); Preferences.Completed++; Save(); UpdateHome(); }
        Chimes.Play(Preferences, "done");
    }

    internal void Snooze(int minutes, bool closePopup)
    {
        if (Paused) return;
        Preferences.SnoozedUntil = DateTime.Now.AddMinutes(minutes).Ticks;
        if (closePopup && popup != null) popup.Close();
        Save();
        Replan();
        Chimes.Play(Preferences, "snooze");
    }

    internal void Pause(int minutes, bool closePopup)
    {
        Preferences.PausedUntil = minutes == -1 ? Schedule.Forever :
            (minutes == 0 ? DateTime.Today.AddDays(1).Ticks : DateTime.Now.AddMinutes(minutes).Ticks);
        Preferences.SnoozedUntil = 0;
        if (closePopup && popup != null) popup.Close();
        Save();
        Replan();
    }

    internal void Resume()
    {
        Preferences.PausedUntil = Preferences.SnoozedUntil = 0;
        blockedUntil = DateTime.MinValue;
        Save();
        Replan();
    }

    private void Refresh()
    {
        if (tray == null) return;
        tray.Icon = Paused || Snoozed ? mutedIcon : icon;
        tray.Text = "SlouchBell · " + Status;
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open SlouchBell", null, (sender, e) => Open());
        menu.Items.Add(popup == null ? "Preview a reminder" : "Focus reminder", null, (sender, e) =>
        {
            if (popup == null) Deliver(true, null);
            else popup.Activate();
        });
        if (Paused || Snoozed) menu.Items.Add("Resume reminders", null, (sender, e) => Resume());
        if (!Paused)
        {
            var snooze = new Forms.ToolStripMenuItem("Snooze");
            foreach (int minutes in new[] { 5, 10, 30 })
            {
                int length = minutes;
                snooze.DropDownItems.Add(length + " minutes", null, (sender, e) => Snooze(length, true));
            }
            menu.Items.Add(snooze);
            var pause = new Forms.ToolStripMenuItem("Pause reminders");
            pause.DropDownItems.Add("For 1 hour", null, (sender, e) => Pause(60, true));
            pause.DropDownItems.Add("Until tomorrow", null, (sender, e) => Pause(0, true));
            pause.DropDownItems.Add("Until I resume", null, (sender, e) => Pause(-1, true));
            menu.Items.Add(pause);
        }
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Quit SlouchBell", null, (sender, e) => Shutdown());
        var previous = tray.ContextMenuStrip;
        tray.ContextMenuStrip = menu;
        if (previous != null) Dispatcher.BeginInvoke(new Action(previous.Dispose));
        UpdateHome();
    }

    private void UpdateHome()
    {
        if (Home != null && Home.IsVisible) Home.RefreshStatus();
    }

    internal void RemindNow()
    {
        if (Paused || Snoozed || !HasMessages) { Deliver(true, null); return; }
        if (popup != null) { popup.Activate(); return; }
        Deliver(false, null, true);
    }

    private IntPtr Hotkey(IntPtr hwnd, int message, IntPtr w, IntPtr l, ref bool handled)
    {
        if (message == 0x312 && w.ToInt32() == 1)
        {
            if (popup != null) popup.Activate();
            else Open();
            handled = true;
        }
        return IntPtr.Zero;
    }

    private void SessionChanged(object sender, SessionSwitchEventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (e.Reason == SessionSwitchReason.SessionLock) { locked = true; if (popup != null) popup.Close(); }
            if (e.Reason == SessionSwitchReason.SessionUnlock) { locked = false; Replan(); }
        }));
    }

    private void PowerChanged(object sender, PowerModeChangedEventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (e.Mode == PowerModes.Suspend) { sleeping = true; if (popup != null) popup.Close(); }
            if (e.Mode == PowerModes.Resume) { sleeping = false; Replan(); }
        }));
    }

    private void TimeChanged(object sender, EventArgs e) { Dispatcher.BeginInvoke(new Action(Replan)); }
    private void DisplayChanged(object sender, EventArgs e)
    { Dispatcher.BeginInvoke(new Action(() => { if (popup != null) popup.ClampToScreen(); })); }
    private void WindowsThemeChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (Home != null) Home.ApplyTheme();
            if (popup != null) Cozy.Apply(popup, Preferences.Theme);
        }));
    }

    internal static bool StartupEnabled()
    {
        using (var key = Registry.CurrentUser.OpenSubKey(StartupKey))
            return key != null && string.Equals(key.GetValue(StartupName) as string, StartupCommand, StringComparison.OrdinalIgnoreCase);
    }

    private static string StartupCommand { get { return "\"" + Forms.Application.ExecutablePath + "\" --startup"; } }

    internal static void SetStartup(bool enabled)
    {
        using (var key = Registry.CurrentUser.CreateSubKey(StartupKey))
        {
            if (enabled) key.SetValue(StartupName, StartupCommand);
            else if (string.Equals(key.GetValue(StartupName) as string, StartupCommand, StringComparison.OrdinalIgnoreCase))
                key.DeleteValue(StartupName, false);
        }
    }

    internal bool Exiting { get; private set; }
    protected override void OnExit(ExitEventArgs e)
    {
        Exiting = true;
        SystemEvents.PowerModeChanged -= PowerChanged;
        SystemEvents.SessionSwitch -= SessionChanged;
        SystemEvents.TimeChanged -= TimeChanged;
        SystemEvents.UserPreferenceChanged -= WindowsThemeChanged;
        SystemEvents.DisplaySettingsChanged -= DisplayChanged;
        if (timer != null) timer.Stop();
        if (input != null) { Desktop.UnregisterHotKey(input.Handle, 1); input.Dispose(); }
        if (tray != null) { tray.Visible = false; tray.ContextMenuStrip.Dispose(); tray.Dispose(); }
        if (icon != null) icon.Dispose();
        if (mutedIcon != null) mutedIcon.Dispose();
        base.OnExit(e);
    }
}
