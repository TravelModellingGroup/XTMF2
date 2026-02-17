using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using System.Threading.Tasks;
using XTMF2;

namespace XTMF2.GUI;

public partial class App : Application
{
    /// <summary>
    /// The XTMF Runtime instance
    /// </summary>
    public XTMFRuntime? Runtime { get; private set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Create the main window first
            var mainWindow = new MainWindow();
            desktop.MainWindow = mainWindow;
            
            // Load saved theme preference
            LoadThemePreference();
            
            // Load the XTMF Runtime asynchronously
            _ = Task.Run(async () =>
            {
                // Artificial delay for testing (0.5 seconds)
                await Task.Delay(2000);
                
                // Create the XTMF Runtime
                Runtime = XTMFRuntime.CreateRuntime();
                
                // Get or create the default user
                var users = Runtime.UserController.Users;
                if (users.Count == 0)
                {
                    Runtime.UserController.CreateOrGet("DefaultUser", false, out _, out _);
                }
                
                // Initialize the main window with the runtime on the UI thread
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    mainWindow.InitializeWithRuntime(Runtime);
                });
            });
            
            // Shutdown XTMF when the application exits
            desktop.ShutdownRequested += (s, e) =>
            {
                Runtime?.Shutdown();
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
