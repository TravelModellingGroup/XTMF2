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
using System.Globalization;
using System.Resources;

namespace XTMF2.GUI.Resources;

/// <summary>
/// Provides access to localized strings
/// </summary>
public static class Strings
{
    private static readonly ResourceManager _resourceManager = 
        new ResourceManager("XTMF2.GUI.Resources.Strings", typeof(Strings).Assembly);

    /// <summary>
    /// Get a localized string by key
    /// </summary>
    public static string Get(string key)
    {
        return _resourceManager.GetString(key, CultureInfo.CurrentUICulture) ?? key;
    }

    /// <summary>
    /// Get a formatted localized string by key with arguments
    /// </summary>
    public static string Format(string key, params object[] args)
    {
        var format = Get(key);
        return string.Format(format, args);
    }

    // Main Window
    public static string MainWindow_Title => Get(nameof(MainWindow_Title));
    public static string MainWindow_StartTab => Get(nameof(MainWindow_StartTab));
    public static string MainWindow_Settings => Get(nameof(MainWindow_Settings));
    public static string MainWindow_LoadingTitle => Get(nameof(MainWindow_LoadingTitle));
    public static string MainWindow_LoadingMessage => Get(nameof(MainWindow_LoadingMessage));

    // Projects View
    public static string Projects_Title => Get(nameof(Projects_Title));
    public static string Projects_UserFormat => Get(nameof(Projects_UserFormat));
    public static string Projects_NewProject => Get(nameof(Projects_NewProject));
    public static string Projects_Import => Get(nameof(Projects_Import));
    public static string Projects_ImportTitle => Get(nameof(Projects_ImportTitle));
    public static string Projects_ImportNamePrompt => Get(nameof(Projects_ImportNamePrompt));
    public static string Projects_ImportError => Get(nameof(Projects_ImportError));
    public static string Projects_Refresh => Get(nameof(Projects_Refresh));
    public static string Projects_Open => Get(nameof(Projects_Open));
    public static string Projects_Delete => Get(nameof(Projects_Delete));
    public static string Projects_OwnerFormat => Get(nameof(Projects_OwnerFormat));
    public static string Projects_CreateTitle => Get(nameof(Projects_CreateTitle));
    public static string Projects_CreatePrompt => Get(nameof(Projects_CreatePrompt));
    public static string Projects_CreateError => Get(nameof(Projects_CreateError));
    public static string Projects_CloseTab => Get(nameof(Projects_CloseTab));
    public static string Projects_DeleteConfirmTitle => Get(nameof(Projects_DeleteConfirmTitle));
    public static string Projects_DeleteConfirmMessage => Get(nameof(Projects_DeleteConfirmMessage));
    public static string Projects_DeleteFailedTitle => Get(nameof(Projects_DeleteFailedTitle));
    public static string Projects_Rename => Get(nameof(Projects_Rename));
    public static string Projects_RenameTitle => Get(nameof(Projects_RenameTitle));
    public static string Projects_RenameFailedTitle => Get(nameof(Projects_RenameFailedTitle));
    public static string Projects_Export => Get(nameof(Projects_Export));
    public static string Projects_ExportTitle => Get(nameof(Projects_ExportTitle));
    public static string Projects_ExportError => Get(nameof(Projects_ExportError));

    // Model Systems View
    public static string ModelSystems_TitleFormat => Get(nameof(ModelSystems_TitleFormat));
    public static string ModelSystems_UserFormat => Get(nameof(ModelSystems_UserFormat));
    public static string ModelSystems_NewModelSystem => Get(nameof(ModelSystems_NewModelSystem));
    public static string ModelSystems_Import => Get(nameof(ModelSystems_Import));
    public static string ModelSystems_ImportTitle => Get(nameof(ModelSystems_ImportTitle));
    public static string ModelSystems_ImportNamePrompt => Get(nameof(ModelSystems_ImportNamePrompt));
    public static string ModelSystems_ImportError => Get(nameof(ModelSystems_ImportError));
    public static string ModelSystems_Export => Get(nameof(ModelSystems_Export));
    public static string ModelSystems_ExportTitle => Get(nameof(ModelSystems_ExportTitle));
    public static string ModelSystems_ExportError => Get(nameof(ModelSystems_ExportError));
    public static string ModelSystems_Open => Get(nameof(ModelSystems_Open));
    public static string ModelSystems_OpenFailedTitle => Get(nameof(ModelSystems_OpenFailedTitle));
    public static string ModelSystems_Rename => Get(nameof(ModelSystems_Rename));
    public static string ModelSystems_Delete => Get(nameof(ModelSystems_Delete));
    public static string ModelSystems_UnknownProject => Get(nameof(ModelSystems_UnknownProject));
    public static string ModelSystems_CreateTitle => Get(nameof(ModelSystems_CreateTitle));
    public static string ModelSystems_CreatePrompt => Get(nameof(ModelSystems_CreatePrompt));
    public static string ModelSystems_CreateFailedTitle => Get(nameof(ModelSystems_CreateFailedTitle));
    public static string ModelSystems_RenameTitle => Get(nameof(ModelSystems_RenameTitle));
    public static string ModelSystems_RenameFailedTitle => Get(nameof(ModelSystems_RenameFailedTitle));
    public static string ModelSystems_DeleteConfirmTitle => Get(nameof(ModelSystems_DeleteConfirmTitle));
    public static string ModelSystems_DeleteConfirmMessage => Get(nameof(ModelSystems_DeleteConfirmMessage));
    public static string ModelSystems_DeleteFailedTitle => Get(nameof(ModelSystems_DeleteFailedTitle));
    public static string ModelSystems_UnknownError => Get(nameof(ModelSystems_UnknownError));

    // Settings Window
    public static string Settings_Title => Get(nameof(Settings_Title));
    public static string Settings_Subtitle => Get(nameof(Settings_Subtitle));
    public static string Settings_General => Get(nameof(Settings_General));
    public static string Settings_DefaultUser => Get(nameof(Settings_DefaultUser));
    public static string Settings_DefaultUserWatermark => Get(nameof(Settings_DefaultUserWatermark));
    public static string Settings_Theme => Get(nameof(Settings_Theme));
    public static string Settings_ThemeDark => Get(nameof(Settings_ThemeDark));
    public static string Settings_ThemeLight => Get(nameof(Settings_ThemeLight));
    public static string Settings_ThemeForestGreen => Get(nameof(Settings_ThemeForestGreen));
    public static string Settings_ThemeRubyRed => Get(nameof(Settings_ThemeRubyRed));
    public static string Settings_ThemeSapphireBlue => Get(nameof(Settings_ThemeSapphireBlue));
    public static string Settings_Runtime => Get(nameof(Settings_Runtime));
    public static string Settings_VerboseLogging => Get(nameof(Settings_VerboseLogging));
    public static string Settings_AutoSave => Get(nameof(Settings_AutoSave));
    public static string Settings_PlaySystemSounds => Get(nameof(Settings_PlaySystemSounds));
    public static string Settings_Cancel => Get(nameof(Settings_Cancel));
    public static string Settings_Save => Get(nameof(Settings_Save));
    public static string Settings_Language => Get(nameof(Settings_Language));
    public static string Settings_LanguageEnglish => Get(nameof(Settings_LanguageEnglish));
    public static string Settings_LanguageFrench => Get(nameof(Settings_LanguageFrench));
    public static string Settings_LanguageSpanish => Get(nameof(Settings_LanguageSpanish));

    // Main Window Menu
    public static string MainWindow_MenuHelp => Get(nameof(MainWindow_MenuHelp));
    public static string MainWindow_MenuAbout => Get(nameof(MainWindow_MenuAbout));

    // About Dialog
    public static string About_Title => Get(nameof(About_Title));
    public static string About_Description => Get(nameof(About_Description));
    public static string About_Copyright => Get(nameof(About_Copyright));
    public static string About_License => Get(nameof(About_License));
    public static string About_Version => Get(nameof(About_Version));
    public static string About_Close => Get(nameof(About_Close));

    // Runtime Initialization
    public static string RuntimeInitialization_ErrorTitle => Get(nameof(RuntimeInitialization_ErrorTitle));
    public static string RuntimeInitialization_CodeStyleError => Get(nameof(RuntimeInitialization_CodeStyleError));
    public static string RuntimeInitialization_ErrorMessage => Get(nameof(RuntimeInitialization_ErrorMessage));

    // Model System Editor
    public static string ModelSystemEditor_SaveTooltip => Get(nameof(ModelSystemEditor_SaveTooltip));
    public static string ModelSystemEditor_ExportTooltip => Get(nameof(ModelSystemEditor_ExportTooltip));
    public static string ModelSystemEditor_RevertTooltip => Get(nameof(ModelSystemEditor_RevertTooltip));
    public static string ModelSystemEditor_ToastSaving => Get(nameof(ModelSystemEditor_ToastSaving));
    public static string ModelSystemEditor_ToastSaved => Get(nameof(ModelSystemEditor_ToastSaved));
    public static string ModelSystemEditor_ToastSaveFailed => Get(nameof(ModelSystemEditor_ToastSaveFailed));

    // Dialogs
    public static string Dialog_OK => Get(nameof(Dialog_OK));
    public static string Dialog_Cancel => Get(nameof(Dialog_Cancel));
    public static string Dialog_Yes => Get(nameof(Dialog_Yes));
    public static string Dialog_No => Get(nameof(Dialog_No));
    public static string Dialog_EnterText => Get(nameof(Dialog_EnterText));
}
