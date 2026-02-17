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
using System.ComponentModel;
using System.Globalization;

namespace XTMF2.GUI.Resources;

/// <summary>
/// Singleton service that provides reactive localization support
/// </summary>
public class LocalizationService : INotifyPropertyChanged
{
    private static LocalizationService? _instance;
    
    public static LocalizationService Instance => _instance ??= new LocalizationService();

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Gets a localized string by key
    /// </summary>
    public string this[string key] => Strings.Get(key);

    /// <summary>
    /// Notifies all bindings that localized strings have changed
    /// </summary>
    internal void NotifyLanguageChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
    }
}

/// <summary>
/// Manages application localization and culture changes
/// </summary>
public static class LocalizationManager
{
    /// <summary>
    /// Event raised when the application language changes
    /// </summary>
    public static event EventHandler? LanguageChanged;

    /// <summary>
    /// Gets the current language code (e.g., "en", "fr", "es")
    /// </summary>
    public static string CurrentLanguage => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;

    /// <summary>
    /// Changes the application language
    /// </summary>
    /// <param name="languageCode">Language code (e.g., "en", "fr", "es")</param>
    public static void ChangeLanguage(string languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
        {
            return;
        }

        try
        {
            var culture = new CultureInfo(languageCode);
            CultureInfo.CurrentUICulture = culture;
            CultureInfo.CurrentCulture = culture;

            // Notify the localization service to update all bindings
            LocalizationService.Instance.NotifyLanguageChanged();

            // Raise the event to notify UI elements
            LanguageChanged?.Invoke(null, EventArgs.Empty);
        }
        catch (CultureNotFoundException)
        {
            // If the culture is not found, fall back to English
            var defaultCulture = new CultureInfo("en");
            CultureInfo.CurrentUICulture = defaultCulture;
            CultureInfo.CurrentCulture = defaultCulture;
            
            LocalizationService.Instance.NotifyLanguageChanged();
        }
    }

    /// <summary>
    /// Initializes the language from settings
    /// </summary>
    public static void Initialize()
    {
        var language = Properties.Settings.Default.Language ?? "en";
        ChangeLanguage(language);
    }
}
