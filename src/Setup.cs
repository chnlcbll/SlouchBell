using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]
[assembly: AssemblyProduct("SlouchBell")]
[assembly: AssemblyTitle("SlouchBell Setup")]

internal static class Setup
{
    private static string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\SlouchBell";
    private static string StartupKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private static string Local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    private static string Folder = Path.Combine(Local, "SlouchBell");
    private static string ProgramFile = Path.Combine(Folder, "SlouchBell.exe");
    private static string RemoveFile = Path.Combine(Folder, "Uninstall.exe");
    private static string StartLink = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "SlouchBell.lnk");

    [STAThread]
    private static int Main(string[] args)
    {
#if SETUP_TEST
        if (args.Length == 2 && args[0] == "--verify-install") return VerifyInstall(args[1]);
#endif
        if (args.Length == 2 && args[0] == "--verify-payload") return VerifyPayload(args[1]);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        bool removing = args.Contains("--uninstall") || args.Contains("--uninstall-worker");
        if (args.Contains("--uninstall"))
        {
            if (!string.Equals(Application.ExecutablePath, RemoveFile, StringComparison.OrdinalIgnoreCase)) return 1;
            try
            {
                string worker = Path.Combine(Path.GetTempPath(), "SlouchBell-uninstall-" + Guid.NewGuid().ToString("N") + ".exe");
                File.Copy(RemoveFile, worker);
                Process.Start(worker, "--uninstall-worker " + Process.GetCurrentProcess().Id);
                return 0;
            }
            catch (Exception error) { ShowError(error); return 1; }
        }
        if (removing)
        {
            int parent;
            if (args.Length != 2 || !int.TryParse(args[1], out parent) ||
                !string.Equals(Path.GetDirectoryName(Application.ExecutablePath), Path.GetTempPath().TrimEnd('\\'), StringComparison.OrdinalIgnoreCase) ||
                !Path.GetFileName(Application.ExecutablePath).StartsWith("SlouchBell-uninstall-", StringComparison.Ordinal)) return 1;
            try { using (var process = Process.GetProcessById(parent)) process.WaitForExit(5000); }
            catch (ArgumentException) { }
        }

        using (var instance = new Mutex(false, @"Local\SlouchBell.Setup.v1"))
        {
            try
            {
                if (!instance.WaitOne(0))
                {
                    MessageBox.Show("SlouchBell Setup is already open.", "SlouchBell");
                    return 1;
                }
            }
            catch (AbandonedMutexException) { }
            try { ShowDialog(removing); }
            finally { instance.ReleaseMutex(); }
        }
        if (removing) RemoveWorker();
        return 0;
    }

    private static void ShowDialog(bool removing)
    {
        using (var dialog = new Form { Text = removing ? "Uninstall SlouchBell" : "SlouchBell Setup",
            ClientSize = new Size(446, 252), AutoScaleMode = AutoScaleMode.Dpi,
            StartPosition = FormStartPosition.CenterScreen, FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false, MinimizeBox = false, BackColor = ColorTranslator.FromHtml("#FFF7F2"),
            Font = new Font("Segoe UI", 10) })
        {
            dialog.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            dialog.Controls.Add(new Label { Text = removing ? "Uninstall SlouchBell?" : "Meet SlouchBell",
                Left = 24, Top = 21, Width = 392, Height = 43, Font = new Font("Segoe UI", 22, FontStyle.Bold) });
            dialog.Controls.Add(new Label { Text = removing ? "This removes the app, its shortcut, and any startup entry." :
                "Funny Taglish posture checks, right on your desktop. Install for your Windows account.",
                Left = 26, Top = 76, Width = 390, Height = 59 });
            var choice = new CheckBox { Text = removing ? "Also remove my local settings" : "Open SlouchBell when setup finishes",
                Checked = !removing, Left = 26, Top = 145, Width = 386, Height = 30 };
            dialog.Controls.Add(choice);
            var confirm = new Button { Text = removing ? "Uninstall" : "Install", Width = 104, Height = 38,
                Left = 313, Top = 188, FlatStyle = FlatStyle.Flat,
                BackColor = ColorTranslator.FromHtml("#9F3857"), ForeColor = Color.White };
            var cancel = new Button { Text = "Cancel", Width = 95, Height = 38, Left = 206, Top = 188, DialogResult = DialogResult.Cancel };
            dialog.Controls.Add(confirm);
            dialog.Controls.Add(cancel);
            dialog.CancelButton = cancel;
            dialog.AcceptButton = removing ? cancel : confirm;
            if (removing) dialog.Shown += (sender, e) => cancel.Focus();
            confirm.Click += (sender, e) =>
            {
                try
                {
                    if (removing) Uninstall(choice.Checked);
                    else Install();
                }
                catch (Exception error) { ShowError(error); return; }
                dialog.Close();
                if (!removing && choice.Checked)
                {
                    try { Process.Start(ProgramFile); }
                    catch (System.ComponentModel.Win32Exception) { MessageBox.Show("Open SlouchBell from the Start menu.", "Installed"); }
                }
            };
            dialog.ShowDialog();
        }
    }

    private static void Install()
    {
        EnsureRegularDirectory(Folder);
        StopApp();
        Directory.CreateDirectory(Folder);
        var temporary = ProgramFile + ".tmp";
        using (var payload = Assembly.GetExecutingAssembly().GetManifestResourceStream("SlouchBell.exe"))
        using (var destination = File.Create(temporary)) payload.CopyTo(destination);
        if (File.Exists(ProgramFile)) File.Replace(temporary, ProgramFile, null);
        else File.Move(temporary, ProgramFile);
        File.Copy(Application.ExecutablePath, RemoveFile, true);
        CreateShortcut();

        using (var key = Registry.CurrentUser.CreateSubKey(UninstallKey))
        {
            key.SetValue("DisplayName", "SlouchBell");
            key.SetValue("DisplayVersion", "1.0.0");
            key.SetValue("Publisher", "SlouchBell");
            key.SetValue("DisplayIcon", "\"" + ProgramFile + "\",0");
            key.SetValue("InstallLocation", Folder);
            key.SetValue("UninstallString", "\"" + RemoveFile + "\" --uninstall");
            key.SetValue("EstimatedSize", (int)((new FileInfo(ProgramFile).Length + new FileInfo(RemoveFile).Length) / 1024));
            key.SetValue("NoModify", 1);
            key.SetValue("NoRepair", 1);
        }
    }

    private static void Uninstall(bool clearData)
    {
        EnsureRegularDirectory(Folder);
        string data = Path.Combine(Local, "SlouchBell Data");
        if (clearData) EnsureRegularDirectory(data);
        StopApp();
        if (clearData && Directory.Exists(data))
        {
            File.Delete(Path.Combine(data, "settings.json"));
            File.Delete(Path.Combine(data, "settings.json.tmp"));
            DeleteIfEmpty(data);
        }
        using (var key = Registry.CurrentUser.OpenSubKey(StartupKey, true))
        {
            if (key != null && string.Equals(key.GetValue("SlouchBell") as string, "\"" + ProgramFile + "\" --startup", StringComparison.OrdinalIgnoreCase))
                key.DeleteValue("SlouchBell", false);
        }
        File.Delete(ProgramFile);
        File.Delete(ProgramFile + ".tmp");
        File.Delete(StartLink);
        File.Delete(RemoveFile);
        DeleteIfEmpty(Folder);
        Registry.CurrentUser.DeleteSubKey(UninstallKey, false);
    }

    private static void EnsureRegularDirectory(string path)
    {
        if (Directory.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("The SlouchBell folder is redirected. Restore it to a regular folder and try again.");
    }

    private static void DeleteIfEmpty(string path)
    {
        if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any()) Directory.Delete(path);
    }

    private static void StopApp()
    {
        foreach (var process in Process.GetProcessesByName("SlouchBell"))
        {
            using (process)
            {
                string file;
                try { file = process.MainModule.FileName; }
                catch (System.ComponentModel.Win32Exception) { continue; }
                catch (InvalidOperationException) { continue; }
                if (!string.Equals(file, ProgramFile, StringComparison.OrdinalIgnoreCase)) continue;
                EventWaitHandle stop;
                if (EventWaitHandle.TryOpenExisting(@"Local\SlouchBell.Stop.v1", out stop))
                    using (stop) stop.Set();
                if (!process.WaitForExit(4000))
                    throw new IOException("Choose Quit SlouchBell from the tray, then try again.");
            }
        }
    }

    private static void CreateShortcut()
    {
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell", true));
        dynamic link = shell.CreateShortcut(StartLink);
        try
        {
            link.TargetPath = ProgramFile;
            link.WorkingDirectory = Folder;
            link.Description = "Open SlouchBell";
            link.Save();
        }
        finally { Marshal.FinalReleaseComObject(link); Marshal.FinalReleaseComObject(shell); }
    }

    private static void RemoveWorker()
    {
        // The running image is locked. A second process retries deletion after this worker exits.
        var path = Application.ExecutablePath;
        if (path.IndexOfAny(new[] { '"', '%', '!' }) >= 0) return;
        var shell = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        var ping = Path.Combine(Environment.SystemDirectory, "ping.exe");
        var args = "/d /v:off /c for /l %i in (1,1,5) do @if exist \"" + path +
            "\" (\"" + ping + "\" -n 2 127.0.0.1 >nul & del /q \"" + path + "\" 2>nul)";
        try
        {
            Process.Start(new ProcessStartInfo(shell, args) {
                UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Environment.SystemDirectory
            });
        }
        catch (System.ComponentModel.Win32Exception) { }
    }

    private static int VerifyPayload(string original)
    {
        using (var payload = Assembly.GetExecutingAssembly().GetManifestResourceStream("SlouchBell.exe"))
        using (var file = File.OpenRead(original))
        using (var sha = System.Security.Cryptography.SHA256.Create())
            return payload != null && sha.ComputeHash(payload).SequenceEqual(sha.ComputeHash(file)) ? 0 : 1;
    }

#if SETUP_TEST
    private static int VerifyInstall(string output)
    {
        var run = "SlouchBell-Test-" + Guid.NewGuid().ToString("N");
        Local = Path.Combine(output, run);
        Folder = Path.Combine(Local, "SlouchBell");
        ProgramFile = Path.Combine(Folder, "SlouchBell.exe");
        RemoveFile = Path.Combine(Folder, "Uninstall.exe");
        StartLink = Path.Combine(Local, "SlouchBell.lnk");
        var testKey = @"Software\" + run;
        UninstallKey = testKey + @"\Installed";
        StartupKey = testKey + @"\Startup";
        var data = Path.Combine(Local, "SlouchBell Data");
        var settings = Path.Combine(data, "settings.json");
        try
        {
            Install();
            Check(File.Exists(ProgramFile) && File.Exists(RemoveFile) && File.Exists(StartLink), "app, uninstaller and shortcut");
            using (var entry = Registry.CurrentUser.OpenSubKey(UninstallKey))
                Check(entry != null && (string)entry.GetValue("UninstallString") == "\"" + RemoveFile + "\" --uninstall", "uninstall registration");
            using (var entry = Registry.CurrentUser.CreateSubKey(StartupKey))
                entry.SetValue("SlouchBell", "\"" + ProgramFile + "\" --startup");
            Directory.CreateDirectory(data);
            File.WriteAllText(settings, "{}");
            Uninstall(false);
            Check(File.Exists(settings), "keep settings by choice");
            Check(!File.Exists(StartLink) && !Directory.Exists(Folder), "installed files and shortcut removed");
            using (var entry = Registry.CurrentUser.OpenSubKey(UninstallKey))
                Check(entry == null, "uninstall registration removed");
            using (var entry = Registry.CurrentUser.OpenSubKey(StartupKey))
                Check(entry.GetValue("SlouchBell") == null, "startup entry removed");
            Install();
            Uninstall(true);
            Check(!Directory.Exists(data), "settings removed by choice");
            Install();
            Uninstall(true);
            Check(!Directory.Exists(Folder), "uninstall works with no saved settings");
            return 0;
        }
        catch (Exception error)
        {
            File.WriteAllText(Path.Combine(output, "installer-error.txt"), error.ToString());
            return 1;
        }
        finally
        {
            if (Directory.Exists(Folder)) Uninstall(true);
            Registry.CurrentUser.DeleteSubKey(StartupKey, false);
            Registry.CurrentUser.DeleteSubKey(testKey, false);
            DeleteIfEmpty(Local);
        }
    }

    private static void Check(bool passed, string check)
    {
        if (!passed) throw new InvalidOperationException("Failed installer check: " + check);
    }
#endif

    private static void ShowError(Exception error)
    {
        MessageBox.Show(error.Message, "SlouchBell Setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}
