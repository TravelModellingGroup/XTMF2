/*
    Copyright 2026 University of Toronto

    This file is part of XTMF2.

    XTMF2 is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    XTMF2 is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with XTMF2.  If not, see <http://www.gnu.org/licenses/>.
*/
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using XTMF2.GUI.Properties;

namespace XTMF2.GUI;

/// <summary>
/// Plays OS-native audio alerts and sends desktop notifications without requiring
/// any extra NuGet packages.
/// All methods are fire-and-forget; failures are silently swallowed so a missing
/// audio device, notification daemon, or absent sound file never crashes the application.
/// </summary>
internal static class SystemAlert
{
    // Windows: MB_ICONHAND / MB_ICONSTOP — the standard "error" beep.
    [DllImport("user32.dll", SetLastError = false)]
    private static extern bool MessageBeep(uint uType);
    private const uint MB_ICONHAND = 0x00000010;
    // MB_OK (0) is the "Default Beep" — the same gentle thud Windows plays
    // when you press Backspace in an empty field or do something that is simply
    // not allowed rather than catastrophically wrong.
    private const uint MB_OK = 0x00000000;

    /// <summary>Plays the platform "cannot do that" bell asynchronously and does not block the caller.</summary>
    public static void PlayError()
    {
        // Respect the user's opt-in setting; do nothing when sounds are disabled.
        if (!Properties.Settings.Default.PlaySystemSounds)
            return;

        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // MB_OK is the "Default Beep" — the same sound Windows plays when
                // you press Backspace in an empty text field.
                MessageBeep(MB_OK);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                // Tink is the standard macOS UI "cannot do that" sound.
                LaunchDetached("afplay", "/System/Library/Sounds/Tink.aiff");
            }
            else
            {
                // Linux / BSD: the freedesktop "bell" id is the closest equivalent.
                // Fall back to the bell OGA file if canberra is unavailable.
                if (!TryLaunch("canberra-gtk-play", "--id=bell"))
                    TryLaunch("paplay",
                        "/usr/share/sounds/freedesktop/stereo/bell.oga");
            }
        }
        catch
        {
            // Never let a missing audio system crash the app.
        }
    }

    // ── helpers ───────────────────────────────────────────────────────────

    /// <summary>
    /// Shows a desktop system notification that a run finished successfully.
    /// Fire-and-forget; failures are silently swallowed.
    /// </summary>
    public static void ShowRunFinished(string runName)
        => ShowNotification("XTMF2", $"Run '{runName}' finished successfully.");

    /// <summary>
    /// Shows a desktop system notification that a run encountered an error.
    /// Fire-and-forget; failures are silently swallowed.
    /// </summary>
    public static void ShowRunFailed(string runName)
        => ShowNotification("XTMF2", $"Run '{runName}' encountered an error.");

    private static void ShowNotification(string title, string message)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                ShowWindowsToast(title, message);
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                TryLaunch("osascript",
                    $"-e 'display notification \"{Escape(message)}\" with title \"{Escape(title)}\"'");
            else
                ShowLinuxNotification(title, message);
        }
        catch { /* never crash on a missing notification daemon */ }
    }

    /// <summary>
    /// Sends a Linux/BSD desktop notification.
    /// Tries tools in order, stopping at the first that succeeds (exit code 0).
    /// <list type="number">
    ///   <item><c>gdbus call</c> – part of GLib (<c>libglib2.0-bin</c>); handles the
    ///     <c>a{sv}</c> hints dict correctly via GVariant text format.  Present on
    ///     KDE, GNOME, and most other desktops.</item>
    ///   <item><c>notify-send</c> – fallback for systems that have <c>libnotify-bin</c>
    ///     but not <c>gdbus</c>.</item>
    /// </list>
    /// See: https://specifications.freedesktop.org/notification-spec/latest/
    /// </summary>
    private static void ShowLinuxNotification(string title, string message)
    {
        // gdbus call with GVariant text format:
        //   "[]"  → empty as (array of strings, actions)
        //   "{}"  → empty a{sv} (hints dict) — cannot be expressed with dbus-send
        //   7000  → expire timeout in ms
        var gdbusCmdArgs = string.Join(" ",
            "call --session",
            "--dest org.freedesktop.Notifications",
            "--object-path /org/freedesktop/Notifications",
            "--method org.freedesktop.Notifications.Notify",
            $"\"{Escape(title)}\"",
            "0",
            "\"dialog-information\"",
            $"\"{Escape(title)}\"",
            $"\"{Escape(message)}\"",
            "\"[]\"",
            "\"{}\"",
            "7000");

        if (!TryLaunchAndWait("gdbus", gdbusCmdArgs))
            TryLaunchAndWait("notify-send",
                $"--icon=dialog-information \"{Escape(title)}\" \"{Escape(message)}\"");
    }

    /// <summary>
    /// Sends a Windows 10/11 toast notification via PowerShell's WinRT bindings.
    /// Uses <c>-EncodedCommand</c> (Base64 UTF-16LE) so the run name never needs
    /// to be shell-quoted.
    /// </summary>
    private static void ShowWindowsToast(string title, string message)
    {
        // Escape XML special characters so the inline XML literal is valid.
        var xmlTitle   = XmlEscape(title);
        var xmlMessage = XmlEscape(message);

        var script = $"""
$xml = [Windows.Data.Xml.Dom.XmlDocument,Windows.Data.Xml.Dom.XmlDocument,ContentType=WindowsRuntime]::new()
$xml.LoadXml('<toast><visual><binding template="ToastGeneric"><text>{xmlTitle}</text><text>{xmlMessage}</text></binding></visual></toast>')
$toast = [Windows.UI.Notifications.ToastNotification,Windows.UI.Notifications,ContentType=WindowsRuntime]::new($xml)
[Windows.UI.Notifications.ToastNotificationManager,Windows.UI.Notifications,ContentType=WindowsRuntime]::CreateToastNotifier('XTMF2').Show($toast)
""";
        // Encode as UTF-16LE, Base64 — bypasses all shell-quoting issues.
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));

        // Prefer pwsh (PowerShell 7+); fall back to the inbox powershell.exe.
        if (!TryLaunch("pwsh", $"-NonInteractive -WindowStyle Hidden -EncodedCommand {encoded}"))
            TryLaunch("powershell", $"-NonInteractive -WindowStyle Hidden -EncodedCommand {encoded}");
    }

    private static string XmlEscape(string s) => s
        .Replace("&",  "&amp;")
        .Replace("<",  "&lt;")
        .Replace(">",  "&gt;")
        .Replace("'",  "&apos;")
        .Replace("\"", "&quot;");

    /// <summary>Escapes double-quotes and backslashes for embedding in a shell argument.</summary>
    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    // ── process helpers ───────────────────────────────────────────────────

    /// <summary>
    /// Launches <paramref name="exe"/> fire-and-forget.
    /// Returns <c>true</c> if the process started; does not check the exit code.
    /// Use for audio playback where we never need to fall back on failure.
    /// </summary>
    private static bool TryLaunch(string exe, string args)
    {
        try { LaunchDetached(exe, args); return true; }
        catch { return false; }
    }

    /// <summary>
    /// Launches <paramref name="exe"/>, waits up to 3 s for it to finish, and returns
    /// <c>true</c> only when the process exits with code 0.
    /// Use for notification commands where we need to try the next fallback on failure.
    /// </summary>
    private static bool TryLaunchAndWait(string exe, string args)
    {
        try
        {
            var psi = new ProcessStartInfo(exe, args)
            {
                UseShellExecute        = false,
                RedirectStandardOutput = true,  // suppress gdbus return-value output
                RedirectStandardError  = true,  // suppress error text
                CreateNoWindow         = true,
            };
            using var proc = Process.Start(psi);
            if (proc is null) return false;
            proc.WaitForExit(3000);
            return proc.ExitCode == 0;
        }
        catch { return false; }
    }

    private static void LaunchDetached(string exe, string args)
    {
        var psi = new ProcessStartInfo(exe, args)
        {
            UseShellExecute        = false,
            RedirectStandardOutput = true,  // suppress any incidental output
            RedirectStandardError  = true,
            CreateNoWindow         = true,
        };
        using var proc = Process.Start(psi);
        // proc is intentionally not awaited; the sound plays in the background.
    }
}
