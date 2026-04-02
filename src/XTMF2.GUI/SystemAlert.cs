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
using XTMF2.GUI.Properties;

namespace XTMF2.GUI;

/// <summary>
/// Plays OS-native audio alerts without requiring any extra NuGet packages.
/// All methods are fire-and-forget; failures are silently swallowed so a missing
/// audio device or absent sound file never crashes the application.
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

    private static bool TryLaunch(string exe, string args)
    {
        try { LaunchDetached(exe, args); return true; }
        catch { return false; }
    }

    private static void LaunchDetached(string exe, string args)
    {
        var psi = new ProcessStartInfo(exe, args)
        {
            UseShellExecute        = false,
            RedirectStandardOutput = false,
            RedirectStandardError  = false,
            CreateNoWindow         = true,
        };
        using var proc = Process.Start(psi);
        // proc is intentionally not awaited; the sound plays in the background.
    }
}
