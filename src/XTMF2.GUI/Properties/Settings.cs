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
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    /// <summary>
    /// When true the application will play a system sound for error toasts.
    /// Defaults to false so the feature is opt-in.
    /// </summary>
    public bool PlaySystemSounds { get; set; } = false;
    public string AiProvider { get; set; } = "ollama";
    public string AiModel { get; set; } = "llama3.2";
    public string OllamaEndpoint { get; set; } = "http://localhost:11434";
    public int AiMaxCompactionCycles { get; set; } = 100;
    public List<RunServerEndpoint> RunServers { get; set; } = new() { RunServerEndpoint.CreateLocal() };

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
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
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
                    settings.PlaySystemSounds = loaded.PlaySystemSounds;
                    settings.AiProvider = string.IsNullOrWhiteSpace(loaded.AiProvider) ? "ollama" : loaded.AiProvider;
                    settings.AiModel = string.IsNullOrWhiteSpace(loaded.AiModel) ? "llama3.2" : loaded.AiModel;
                    settings.OllamaEndpoint = string.IsNullOrWhiteSpace(loaded.OllamaEndpoint)
                        ? "http://localhost:11434"
                        : loaded.OllamaEndpoint;
                    settings.AiMaxCompactionCycles = loaded.AiMaxCompactionCycles is < 1 or > 100
                        ? 100
                        : loaded.AiMaxCompactionCycles;
                    settings.RunServers = NormalizeRunServers(loaded.RunServers);
                }
            }
        }
        catch
        {
            // Ignore load errors, use defaults
        }

        return settings;
    }

    private static List<RunServerEndpoint> NormalizeRunServers(IEnumerable<RunServerEndpoint>? endpoints)
    {
        var normalized = new List<RunServerEndpoint>();
        if (endpoints is not null)
        {
            foreach (var endpoint in endpoints)
            {
                if (endpoint is null || string.IsNullOrWhiteSpace(endpoint.Address) || endpoint.Port is < 0 or > 65535)
                    continue;

                endpoint.BasicParameterOverrides ??= new(StringComparer.OrdinalIgnoreCase);

                if (normalized.Any(existing => existing.Id == endpoint.Id))
                    continue;

                normalized.Add(endpoint);
            }
        }

        var local = normalized.FirstOrDefault(endpoint => endpoint.IsLocal);
        if (local is null)
        {
            normalized.Insert(0, RunServerEndpoint.CreateLocal());
        }
        else
        {
            local.Id = "local";
            local.Name = "Local RunServer";
            local.Enabled = true;
            local.Address = "127.0.0.1";
            local.Port = 0;
        }

        return normalized;
    }
}
