using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using XTMF2;
using XTMF2.GUI.Resources;
using XTMF2.GUI.Views;

namespace XTMF2.GUI;

public partial class App : Application
{
    /// <summary>
    /// The XTMF Runtime instance
    /// </summary>
    public XTMFRuntime? Runtime { get; private set; }

    /// <summary>
    /// The single RunController for this GUI session.
    /// Created after the runtime is ready; disposed on application shutdown.
    /// </summary>
    private RunController? _runController;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Load settings BEFORE creating UI to ensure correct initial state
            // Load saved theme preference
            LoadThemePreference();
            
            // Initialize localization from settings
            LocalizationManager.Initialize();
            
            // Now create the main window with settings already loaded
            var mainWindow = new MainWindow();
            desktop.MainWindow = mainWindow;
            
            // Load the XTMF Runtime asynchronously
            _ = Task.Run(async () =>
            {
                List<XTMFCodeStyleError> codeStyleErrors = [];
                try
                {
                    // Create the XTMF Runtime
                    Runtime = XTMFRuntime.CreateRuntime();
                }
                catch (XTMFCodeStyleError codeError)
                {
                    System.Console.Error.WriteLine($"[XTMFCodeStyleError] {codeError.Message}");
                    System.Console.Error.Flush();
                    codeStyleErrors.Add(codeError);
                }
                catch (System.AggregateException ex)
                {
                    // Types are checked in parallel and each type may itself report
                    // several code style errors, so this can be nested; Flatten()
                    // collapses that into a single list of XTMFCodeStyleError.
                    foreach(var error in ex.Flatten().InnerExceptions)
                    {
                        if (error is XTMFCodeStyleError codeError)
                        {
                            codeStyleErrors.Add(codeError);
                            System.Console.Error.WriteLine($"[XTMFCodeStyleError] {codeError.Message}");
                        }
                        System.Console.Error.Flush();
                    }
                }
                if (codeStyleErrors.Count > 0)
                {
                    // Handle code style errors (e.g. invalid config)
                    Task? errorDialogTask = null;

                        
                    Avalonia.Threading.Dispatcher.UIThread.Invoke(new Action(() =>
                    {
                        mainWindow.InitializeComponent();
                    }));
                    Avalonia.Threading.Dispatcher.UIThread.Invoke(new Action(() =>
                    {
                        var errorDialog = new CodeStyleErrorDialog(
                            Strings.RuntimeInitialization_ErrorTitle,
                            Strings.RuntimeInitialization_CodeStyleErrorIntro,
                            codeStyleErrors.Select(e => e.Message).ToList());
                        errorDialogTask = errorDialog.ShowDialog(mainWindow);
                    }), DispatcherPriority.Background);
                    errorDialogTask?.GetAwaiter().GetResult();
                    System.Environment.Exit(1);
                }
                else if(Runtime is not null)
                {
                    // Get or create the default user
                    var users = Runtime.UserController.Users;
                    User? currentUser = users.FirstOrDefault(user => user.UserName == "local");
                    if (currentUser is null && users.Count > 0)
                    {
                        currentUser = users[0];
                    }
                    else if (currentUser is null)
                    {
                        Runtime.UserController.CreateOrGet("local", true, out currentUser, out _);
                    }

                    // Initialise the RunController on the background thread (I/O: creates a named pipe
                    // and spawns the client process).  If it fails we pass null and the GUI continues
                    // without run support.
                    string? runControllerError = null;
                    RunController.InitializeRunController(Runtime, out _runController, ref runControllerError);
                    if (_runController is null)
                        System.Diagnostics.Debug.WriteLine($"[RunController] Failed to initialise: {runControllerError}");

                    // Initialize the main window with the runtime on the UI thread
                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        mainWindow.InitializeWithRuntime(Runtime, _runController, currentUser!);
                    });
                }
            });
            
            // Shutdown XTMF when the application exits
            desktop.ShutdownRequested += (s, e) =>
            {
                _runController?.Dispose();
                _runController = null;
                Runtime?.Shutdown();
            };

            // Exit fires unconditionally when the process is actually ending,
            // catching cases where ShutdownRequested was cancelled or bypassed.
            desktop.Exit += (s, e) =>
            {
                _runController?.Dispose();
                _runController = null;
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Change the application theme
    /// </summary>
    /// <param name="themeName">The theme to apply (Dark or Light)</param>
    public void ChangeTheme(string themeName)
    {
        ApplyTheme(themeName);
        
        // Save theme preference
        SaveThemePreference(NormalizeThemeName(themeName));
    }

    private void LoadThemePreference()
    {
        // Load theme from settings
        var savedTheme = NormalizeThemeName(Properties.Settings.Default.Theme);
        ApplyTheme(savedTheme);
    }

    private void ApplyTheme(string themeName)
    {
        RequestedThemeVariant = NormalizeThemeName(themeName) == "Light"
            ? ThemeVariant.Light
            : ThemeVariant.Dark;
    }

    public static string NormalizeThemeName(string? themeName)
        => themeName == "Light" ? "Light" : "Dark";

    /// <summary>
    /// Apply a theme without saving (for preview purposes)
    /// </summary>
    public void ApplyThemePreview(string themeName)
    {
        ApplyTheme(themeName);
    }

    private void SaveThemePreference(string themeName)
    {
        Properties.Settings.Default.Theme = NormalizeThemeName(themeName);
        Properties.Settings.Default.Save();
    }
}
