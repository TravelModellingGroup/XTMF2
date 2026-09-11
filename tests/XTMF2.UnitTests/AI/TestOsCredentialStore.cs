using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using XTMF2.AI;

namespace XTMF2.UnitTests.AI;

[TestClass]
public sealed class TestOsCredentialStore
{
    [TestMethod]
    public async Task RejectsEmptyCredentialKeys()
    {
        var store = new OsCredentialStore();

        await Assert.ThrowsExactlyAsync<ArgumentException>(
            () => store.GetAsync(" "));
    }

    [TestMethod]
    public async Task RejectsCredentialKeysContainingControlCharacters()
    {
        var store = new OsCredentialStore();

        await Assert.ThrowsExactlyAsync<ArgumentException>(
            () => store.SetAsync("provider\nkey", "secret"));
    }

    [TestMethod]
    public async Task RoundTripsThroughAvailableNativeCredentialStore()
    {
        if (OperatingSystem.IsLinux() && !CommandExists("secret-tool") ||
            OperatingSystem.IsMacOS() && !CommandExists("security"))
        {
            Assert.Inconclusive("The native credential-store command is not installed.");
        }

        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            Assert.Inconclusive("No native credential-store backend is available.");
        }

        var key = $"test-{Guid.NewGuid():N}";
        var store = new OsCredentialStore();
        try
        {
            try
            {
                await store.SetAsync(key, "xtmf2-test-secret");
                Assert.AreEqual("xtmf2-test-secret", await store.GetAsync(key));
            }
            catch (AiProviderException exception)
            {
                Assert.Inconclusive($"The native credential store is unavailable: {exception.Message}");
            }
        }
        finally
        {
            try
            {
                await store.DeleteAsync(key);
            }
            catch (AiProviderException)
            {
            }
        }
    }

    private static bool CommandExists(string command)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "which",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            ArgumentList = { command }
        });
        process?.WaitForExit();
        return process?.ExitCode == 0;
    }
}