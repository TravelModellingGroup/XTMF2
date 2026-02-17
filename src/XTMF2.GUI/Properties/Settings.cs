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
using System.IO;
using System.Text.Json;

namespace XTMF2.GUI.Properties;

/// <summary>
/// Application settings storage
/// </summary>
public class Settings
{
    private static Settings? _default;
    private static readonly object _lock = new();
    private static readonly string SettingsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "XTMF2", "GUI");

    /// <summary>
    /// Gets the default settings instance (based on desktop user)
    /// </summary>
    public static Settings Default
    {
        get
        {
            if (_default == null)
            {
                lock (_lock)
                {
                    if (_default == null)
                    {
                        _default = Load();
                    }
                }
            }
            return _default;
        }
    }

    private static string SettingsPath
    {
        get
        {
            var userName = Environment.UserName;
            return Path.Combine(SettingsDirectory, $"{userName}.settings.json");
        }
    }

    public Settings()
    {
    }

    public string? Theme { get; set; } = "Dark";
    public string? Language { get; set; } = "en";

    public void Save()
    {
        try
        {
            if (!Directory.Exists(SettingsDirectory))
            {
                Directory.CreateDirectory(SettingsDirectory);
            }

            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(SettingsPath, json);
        }
        catch
        {
            // Ignore save errors for now
        }
    }

    private static Settings Load()
    {
        var settings = new Settings();
        
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                var loaded = JsonSerializer.Deserialize<Settings>(json);
                if (loaded != null)
                {
                    settings.Theme = loaded.Theme;
                    settings.Language = loaded.Language;
                }
            }
        }
        catch
        {
            // Ignore load errors, use defaults
        }

        return settings;
    }
}
