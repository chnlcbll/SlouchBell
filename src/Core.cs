using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Media;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]
[assembly: AssemblyProduct("SlouchBell")]
[assembly: AssemblyTitle("SlouchBell")]

[DataContract]
public sealed class Settings
{
    [DataMember] public int Minimum = 40;
    [DataMember] public int Maximum = 75;
    [DataMember] public int DailyLimit = 10;
    [DataMember] public int Days = 62;
    [DataMember] public string Start = "09:00";
    [DataMember] public string End = "18:00";
    [DataMember] public string Theme = "System";
    [DataMember] public string Position = "Random anywhere";
    [DataMember] public string Size = "Standard";
    [DataMember] public string Sound = "Soft bell";
    [DataMember] public int Volume = 30;
    [DataMember] public bool CompletionSound = true;
    [DataMember] public bool SnoozeSound;
    [DataMember] public bool Quiet = true;
    [DataMember] public bool ReducedMotion;
    [DataMember] public bool StartMinimized;
    [DataMember] public long PausedUntil;
    [DataMember] public long SnoozedUntil;
    [DataMember] public List<string> Custom = new List<string>();
    [DataMember] public List<string> Disabled = new List<string>();
    [DataMember] public List<string> Played = new List<string>();
    [DataMember] public string LastMessage = "";
    [DataMember] public string Day = "";
    [DataMember] public int Sent;
    [DataMember] public int Completed;
    [DataMember] public bool Welcomed;

    [OnDeserializing]
    private void Initialize(StreamingContext context)
    {
        var defaults = new Settings();
        foreach (var field in typeof(Settings).GetFields()) field.SetValue(this, field.GetValue(defaults));
    }
}

internal static class Store
{
    internal static readonly string DirectoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SlouchBell Data");
    private static readonly DataContractJsonSerializer Format = new DataContractJsonSerializer(typeof(Settings));

    internal static Settings Read(string path)
    {
        using (var stream = File.OpenRead(path))
        {
            var value = (Settings)Format.ReadObject(stream) ?? new Settings();
            value.Minimum = Math.Max(1, Math.Min(1440, value.Minimum));
            value.Maximum = Math.Max(value.Minimum, Math.Min(1440, value.Maximum));
            value.DailyLimit = Math.Max(1, Math.Min(100, value.DailyLimit));
            value.Volume = Math.Max(0, Math.Min(100, value.Volume));
            value.Days &= 127;
            if (value.Days == 0) value.Days = 62;
            TimeSpan ignored;
            if (!Schedule.TryHours(value.Start, out ignored)) value.Start = "09:00";
            if (!Schedule.TryHours(value.End, out ignored)) value.End = "18:00";
            value.PausedUntil = Math.Max(0, Math.Min(Schedule.Forever, value.PausedUntil));
            if (value.SnoozedUntil < 0 || value.SnoozedUntil > DateTime.Now.AddDays(2).Ticks) value.SnoozedUntil = 0;
            value.Custom = value.Custom ?? new List<string>();
            value.Disabled = value.Disabled ?? new List<string>();
            value.Played = value.Played ?? new List<string>();
            return value;
        }
    }

    internal static void Write(string path, Settings settings)
    {
        var temporary = path + ".tmp";
        using (var stream = File.Create(temporary)) Format.WriteObject(stream, settings);
        if (File.Exists(path)) File.Replace(temporary, path, null);
        else File.Move(temporary, path);
    }
}

internal static class Schedule
{
    internal static readonly long Forever = DateTime.MaxValue.Ticks;

    internal static bool TryHours(string value, out TimeSpan result)
    {
        return TimeSpan.TryParseExact(value, @"hh\:mm", CultureInfo.InvariantCulture, out result);
    }

    internal static DateTime NextAllowed(Settings settings, DateTime when, bool tomorrow)
    {
        DateTime end;
        return NextAllowed(settings, tomorrow ? when.Date.AddDays(1) : when, out end);
    }

    private static DateTime NextAllowed(Settings settings, DateTime when, out DateTime closes)
    {
        TimeSpan start, end;
        TryHours(settings.Start, out start);
        TryHours(settings.End, out end);
        for (int day = -1; day <= 7; day++)
        {
            var date = when.Date.AddDays(day);
            if ((settings.Days & (1 << (int)date.DayOfWeek)) == 0) continue;
            var from = date.Add(start);
            var until = date.Add(end).AddDays(end <= start ? 1 : 0);
            if (when >= until) continue;
            closes = until;
            return when < from ? from : when;
        }
        closes = DateTime.MaxValue;
        return DateTime.MaxValue;
    }

    internal static DateTime Plan(Settings settings, DateTime now, Random random)
    {
        bool capped = settings.Day == DayKey(now) && settings.Sent >= settings.DailyLimit;

        if (settings.SnoozedUntil > 0 && !capped)
        {
            var snooze = new DateTime(settings.SnoozedUntil);
            return NextAllowed(settings, snooze > now ? snooze : now, false);
        }

        var earliest = capped ? now.Date.AddDays(1) : now;
        var delay = random.Next(settings.Minimum * 60, settings.Maximum * 60 + 1);
        var candidate = earliest.AddSeconds(delay);
        DateTime closes;
        var allowed = NextAllowed(settings, candidate, out closes);
        if (allowed == candidate || allowed == DateTime.MaxValue) return allowed;
        // Vary the arrival inside the next active window as well, including very short windows.
        var room = Math.Max(0, (int)(closes - allowed).TotalSeconds - 1);
        return allowed.AddSeconds(random.Next(Math.Min(settings.Minimum * 60, room), Math.Min(settings.Maximum * 60, room) + 1));
    }

    internal static string DayKey(DateTime now)
    {
        return now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }
}

internal sealed class Message
{
    internal readonly string Id;
    internal readonly string Text;
    internal Message(string id, string text) { Id = id; Text = text; }
}

internal static class Deck
{
    internal static readonly List<Message> BuiltIn = ReadBuiltIn();

    private static List<Message> ReadBuiltIn()
    {
        var result = new List<Message>();
        using (var reader = new StreamReader(Assembly.GetExecutingAssembly().GetManifestResourceStream("Messages.txt")))
        {
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                var split = line.IndexOf('|');
                if (split > 0) result.Add(new Message(line.Substring(0, split), line.Substring(split + 1)));
            }
        }
        return result;
    }

    internal static List<Message> All(Settings settings)
    {
        return BuiltIn.Concat(settings.Custom.Select(text => new Message("custom:" + text, text))).ToList();
    }

    internal static Message Draw(Settings settings, Random random)
    {
        var active = All(settings).Where(message => !settings.Disabled.Contains(message.Id)).ToList();
        var pool = active.Where(message => !settings.Played.Contains(message.Id)).ToList();
        if (pool.Count == 0)
        {
            settings.Played.Clear();
            pool = active.Count > 1 ? active.Where(message => message.Id != settings.LastMessage).ToList() : active;
        }
        if (pool.Count == 0) return null;
        var next = pool[random.Next(pool.Count)];
        settings.Played.Add(next.Id);
        settings.LastMessage = next.Id;
        return next;
    }
}

internal static class Desktop
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect { internal int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern IntPtr GetShellWindow();
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Rect rect);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out Rect rect, int length);
    [DllImport("shell32.dll")] private static extern int SHQueryUserNotificationState(out int state);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(IntPtr window, int id);
    [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(IntPtr window);

    internal static bool ShouldDefer()
    {
        int state;
        if (SHQueryUserNotificationState(out state) == 0 && (state == 1 || state == 2 || state == 3 || state == 4 || state == 6))
            return true;

        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero || foreground == GetShellWindow()) return false;
        Rect rect;
        if (DwmGetWindowAttribute(foreground, 9, out rect, Marshal.SizeOf(typeof(Rect))) != 0 &&
            !GetWindowRect(foreground, out rect)) return false;
        var screen = System.Windows.Forms.Screen.FromHandle(foreground).Bounds;
        return rect.Left <= screen.Left + 1 && rect.Top <= screen.Top + 1 &&
            rect.Right >= screen.Right - 1 && rect.Bottom >= screen.Bottom - 1;
    }
}

internal static class Chimes
{
    private static SoundPlayer player;
    private static MemoryStream wave;

    internal static void Play(Settings settings, string kind)
    {
        if (settings.Sound == "Silent" || settings.Volume == 0) return;
        if (kind == "done" && !settings.CompletionSound || kind == "snooze" && !settings.SnoozeSound) return;

        int rate = 22050;
        double[] notes;
        double duration, gap, overtone;
        switch (settings.Sound)
        {
            case "Wood tap": notes = new[] { 340.0, 490 }; duration = .13; gap = .10; overtone = 2.7; break;
            case "Glass chime": notes = new[] { 880.0, 1174.7 }; duration = .38; gap = .12; overtone = 2.02; break;
            case "Digital pop": notes = new[] { 520.0, 780 }; duration = .16; gap = .10; overtone = 2; break;
            default: notes = new[] { 587.3, 783.99 }; duration = .42; gap = .15; overtone = 2.41; break;
        }
        if (kind == "done") { notes[0] *= .84; notes[1] *= 1.12; duration *= .75; }
        if (kind == "snooze") { Array.Reverse(notes); duration *= .7; }

        int samples = (int)((duration + gap) * rate);
        var next = new MemoryStream();
        var writer = new BinaryWriter(next);
        writer.Write(0x46464952); writer.Write(36 + samples * 2); writer.Write(0x45564157);
        writer.Write(0x20746D66); writer.Write(16); writer.Write((short)1); writer.Write((short)1);
        writer.Write(rate); writer.Write(rate * 2); writer.Write((short)2); writer.Write((short)16);
        writer.Write(0x61746164); writer.Write(samples * 2);
        double volume = settings.Volume / 100.0 * .36;
        for (int i = 0; i < samples; i++)
        {
            double value = 0;
            for (int n = 0; n < notes.Length; n++)
            {
                double t = i / (double)rate - n * gap;
                if (t < 0 || t >= duration) continue;
                double envelope = Math.Min(1, t / .009) * Math.Pow(1 - t / duration, 3);
                double phase = 2 * Math.PI * notes[n] * t;
                value += envelope * (Math.Sin(phase) + .23 * Math.Sin(phase * overtone));
            }
            writer.Write((short)(value * volume * short.MaxValue));
        }
        writer.Flush();
        next.Position = 0;
        if (player != null) { player.Stop(); player.Dispose(); wave.Dispose(); }
        wave = next;
        player = new SoundPlayer(wave);
        try { player.Play(); }
        catch (InvalidOperationException) { }
    }
}
