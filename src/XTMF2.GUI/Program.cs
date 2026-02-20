using Avalonia;
using System;
using System.Reflection;


[assembly: AssemblyInformationalVersion("0.0.1")]
[assembly: AssemblyVersion("0.0.1.0")]
[assembly: AssemblyFileVersion("0.0.1.0")]
[assembly: AssemblyCopyright("Copyright 2026 Travel Modelling Group, University of Toronto")]
[assembly: AssemblyCompany("Travel Modelling Group, University of Toronto")]
[assembly: AssemblyProduct("XTMF2.GUI")]
namespace XTMF2.GUI;

class Program
{
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
