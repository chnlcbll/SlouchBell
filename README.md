# SlouchBell

SlouchBell is a Windows 10/11 tray app that delivers shuffled Taglish posture reminders. The installer works for your Windows account. The standalone executable also runs directly on Windows with .NET Framework 4.8.

1. Run **SlouchBell-Setup.exe** from the release folder.
2. Open SlouchBell from the Start menu or the bell in the system tray.
3. Select **Remind me now** to try a real reminder, or **Try this line** to preview the favorite without using a card from the deck.
4. Use **Reminders** to set your hours, frequency, and screen position. Use **Sounds** to pick and preview a chime. Turn on launch at sign-in in **General** if you want it.

Closing the dashboard leaves reminders running. The tray menu offers pause, snooze, resume, and **Quit SlouchBell**. If the shortcut is available, **Ctrl+Alt+P** focuses an open reminder or opens the dashboard.

To uninstall, open **Windows Settings → Apps → Installed apps → SlouchBell → Uninstall**. Choose whether to remove your local settings too. The installer and app are unsigned.

To rebuild locally, run `powershell -ExecutionPolicy Bypass -File .\build.ps1` from this project folder. The build uses the Windows .NET Framework compiler and requires no dependency downloads.
