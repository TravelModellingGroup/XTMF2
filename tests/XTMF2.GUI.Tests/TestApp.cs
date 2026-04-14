using Avalonia;
using Avalonia.Headless;
using Avalonia.Themes.Fluent;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace XTMF2.GUI.Tests;

/// <summary>
/// Minimal Avalonia application used by headless tests.
/// Does NOT start <see cref="XTMFRuntime"/> — only initialises the Avalonia framework.
/// </summary>
public class TestApp : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
    }
}

/// <summary>
/// Bootstraps the headless Avalonia session once for the entire test assembly.
/// Referenced from headless test classes via <see cref="HeadlessSession"/>.
/// </summary>
[TestClass]
public static class HeadlessAppLifetime
{
    internal static HeadlessUnitTestSession? HeadlessSession { get; private set; }

    [AssemblyInitialize]
    public static void InitializeHeadless(TestContext _)
    {
        HeadlessSession = HeadlessUnitTestSession.StartNew(typeof(TestApp));
    }

    [AssemblyCleanup]
    public static void CleanupHeadless()
    {
        HeadlessSession?.Dispose();
        HeadlessSession = null;
    }
}
