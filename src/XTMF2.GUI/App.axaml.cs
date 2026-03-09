using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
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
                // Artificial delay for testing (0.5 seconds)
                await Task.Delay(500);
                try
                {
                    // Create the XTMF Runtime
                    Runtime = XTMFRuntime.CreateRuntime();
                }
                catch (XTMFCodeStyleError codeError)
                {
                    // Handle code style errors (e.g. invalid config)
                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        var errorDialog = new MessageDialog(
                            Strings.Format(Strings.RuntimeInitialization_CodeStyleError, codeError.Message),
                            Strings.RuntimeInitialization_ErrorTitle,
                            MessageDialog.MessageType.Error);
                        errorDialog.ShowDialog(mainWindow);
                    });
                    System.Environment.Exit(1);
                }
                catch (System.Exception ex)
                {
                    // Handle general XTMF exceptions
                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        var errorDialog = new MessageDialog(
                            Strings.Format(Strings.RuntimeInitialization_ErrorMessage, ex.Message),
                            Strings.RuntimeInitialization_ErrorTitle,
                            MessageDialog.MessageType.Error);
                        errorDialog.ShowDialog(mainWindow);
                    });
                    System.Environment.Exit(1);
                }
                
                // Get or create the default user
                var users = Runtime.UserController.Users;

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
                    mainWindow.InitializeWithRuntime(Runtime, _runController);
                });
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
    /// <param name="themeName">The theme to apply (Dark, Light, ForestGreen, RubyRed, SapphireBlue)</param>
    public void ChangeTheme(string themeName)
    {
        switch (themeName)
        {
            case "Dark":
                RequestedThemeVariant = ThemeVariant.Dark;
                break;
            case "Light":
            case "ForestGreen":
            case "RubyRed":
            case "SapphireBlue":
                // Colored themes use Light as the base
                RequestedThemeVariant = ThemeVariant.Light;
                break;
            default:
                RequestedThemeVariant = ThemeVariant.Dark;
                break;
        }
        
        // Save theme preference
        SaveThemePreference(themeName);
    }

    private void LoadThemePreference()
    {
        // Load theme from settings
        var savedTheme = Properties.Settings.Default.Theme ?? "Dark";
        ApplyTheme(savedTheme);
    }

    private void ApplyTheme(string themeName)
    {
        switch (themeName)
        {
            case "Dark":
                RequestedThemeVariant = ThemeVariant.Dark;
                break;
            case "Light":
            case "ForestGreen":
            case "RubyRed":
            case "SapphireBlue":
                RequestedThemeVariant = ThemeVariant.Light;
                break;
            default:
                RequestedThemeVariant = ThemeVariant.Dark;
                break;
        }
    }

    /// <summary>
    /// Apply a theme without saving (for preview purposes)
    /// </summary>
    public void ApplyThemePreview(string themeName)
    {
        ApplyTheme(themeName);
    }

    private void SaveThemePreference(string themeName)
    {
        Properties.Settings.Default.Theme = themeName;
        Properties.Settings.Default.Save();
    }
}
