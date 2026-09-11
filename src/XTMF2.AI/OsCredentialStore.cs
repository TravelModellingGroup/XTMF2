using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace XTMF2.AI;

public interface IAiCredentialStore
{
    Task<string?> GetAsync(string key, CancellationToken cancellationToken = default);

    Task SetAsync(string key, string secret, CancellationToken cancellationToken = default);

    Task DeleteAsync(string key, CancellationToken cancellationToken = default);
}

public sealed class OsCredentialStore : IAiCredentialStore
{
    private const string ServiceName = "XTMF2";

    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        ValidateKey(key);
        if (OperatingSystem.IsWindows())
        {
            return WindowsCredentialStore.Get(key);
        }

        if (OperatingSystem.IsLinux())
        {
            return await RunSecretToolAsync(["lookup", "service", ServiceName, "username", key], null, cancellationToken)
                .ConfigureAwait(false);
        }

        if (OperatingSystem.IsMacOS())
        {
            return await RunSecurityAsync(["find-generic-password", "-s", ServiceName, "-a", key, "-w"], null, cancellationToken)
                .ConfigureAwait(false);
        }

        throw UnsupportedPlatform();
    }

    public async Task SetAsync(string key, string secret, CancellationToken cancellationToken = default)
    {
        ValidateKey(key);
        ArgumentNullException.ThrowIfNull(secret);
        if (OperatingSystem.IsWindows())
        {
            WindowsCredentialStore.Set(key, secret);
            return;
        }

        if (OperatingSystem.IsLinux())
        {
            await RunSecretToolAsync(
                ["store", "--label", ServiceName + " credential", "service", ServiceName, "username", key],
                secret,
                cancellationToken).ConfigureAwait(false);
            return;
        }

        if (OperatingSystem.IsMacOS())
        {
            await RunSecurityAsync(
                ["add-generic-password", "-U", "-s", ServiceName, "-a", key, "-w", secret],
                null,
                cancellationToken).ConfigureAwait(false);
            return;
        }

        throw UnsupportedPlatform();
    }

    public async Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        ValidateKey(key);
        if (OperatingSystem.IsWindows())
        {
            WindowsCredentialStore.Delete(key);
            return;
        }

        if (OperatingSystem.IsLinux())
        {
            await RunSecretToolAsync(["clear", "service", ServiceName, "username", key], null, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        if (OperatingSystem.IsMacOS())
        {
            await RunSecurityAsync(["delete-generic-password", "-s", ServiceName, "-a", key], null, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        throw UnsupportedPlatform();
    }

    private static async Task<string?> RunSecretToolAsync(
        string[] arguments,
        string? standardInput,
        CancellationToken cancellationToken)
    {
        return await RunCommandAsync("secret-tool", arguments, standardInput, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string?> RunSecurityAsync(
        string[] arguments,
        string? standardInput,
        CancellationToken cancellationToken)
    {
        return await RunCommandAsync("security", arguments, standardInput, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string?> RunCommandAsync(
        string executable,
        string[] arguments,
        string? standardInput,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            RedirectStandardInput = standardInput is not null,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                throw new AiProviderException($"Could not start credential-store executable '{executable}'.");
            }

            if (standardInput is not null)
            {
                await process.StandardInput.WriteAsync(standardInput.AsMemory(), cancellationToken).ConfigureAwait(false);
                await process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
                process.StandardInput.Close();
            }

            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            var output = await outputTask.ConfigureAwait(false);
            var error = await errorTask.ConfigureAwait(false);
            if (process.ExitCode != 0)
            {
                if (executable == "secret-tool" &&
                    error.Contains("No such secret", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                throw new AiProviderException(
                    $"Credential-store operation '{executable}' failed with exit code {process.ExitCode}: {error.Trim()}");
            }

            return output.TrimEnd('\r', '\n');
        }
        catch (OperationCanceledException)
        {
            TryTerminate(process);
            throw;
        }
        catch (Win32Exception exception)
        {
            throw new AiProviderException(
                $"Credential-store executable '{executable}' could not be started.", exception);
        }
        catch (IOException exception)
        {
            throw new AiProviderException(
                $"Communication with credential-store executable '{executable}' failed.", exception);
        }
    }

    private static void ValidateKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.IndexOfAny(['\r', '\n', '\0']) >= 0)
        {
            throw new ArgumentException("A non-empty credential key without control characters is required.", nameof(key));
        }
    }

    private static AiProviderException UnsupportedPlatform() =>
        new("No OS credential-store backend is available on this platform.");

    private static void TryTerminate(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static class WindowsCredentialStore
    {
        private const uint GenericCredentialType = 1;
        private const uint PersistLocalMachine = 2;

        public static string? Get(string key)
        {
            if (!CredRead(Target(key), GenericCredentialType, 0, out var credentialPointer))
            {
                var error = Marshal.GetLastWin32Error();
                if (error == 1168)
                {
                    return null;
                }

                throw new AiProviderException($"Windows Credential Manager could not read credential '{key}' (error {error}).");
            }

            try
            {
                var credential = Marshal.PtrToStructure<NativeCredential>(credentialPointer);
                if (credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize == 0)
                {
                    return string.Empty;
                }

                return Marshal.PtrToStringUni(credential.CredentialBlob, checked((int)credential.CredentialBlobSize / 2));
            }
            finally
            {
                CredFree(credentialPointer);
            }
        }

        public static void Set(string key, string secret)
        {
            var target = Target(key);
            var targetPointer = Marshal.StringToCoTaskMemUni(target);
            var userPointer = Marshal.StringToCoTaskMemUni(Environment.UserName);
            var blobPointer = Marshal.StringToCoTaskMemUni(secret);
            try
            {
                var credential = new NativeCredential
                {
                    Type = GenericCredentialType,
                    TargetName = targetPointer,
                    UserName = userPointer,
                    CredentialBlob = blobPointer,
                    CredentialBlobSize = checked((uint)Encoding.Unicode.GetByteCount(secret)),
                    Persist = PersistLocalMachine
                };
                if (!CredWrite(ref credential, 0))
                {
                    throw new AiProviderException(
                        $"Windows Credential Manager could not store credential '{key}' (error {Marshal.GetLastWin32Error()}).");
                }
            }
            finally
            {
                Marshal.FreeCoTaskMem(targetPointer);
                Marshal.FreeCoTaskMem(userPointer);
                Marshal.FreeCoTaskMem(blobPointer);
            }
        }

        public static void Delete(string key)
        {
            if (!CredDelete(Target(key), GenericCredentialType, 0))
            {
                var error = Marshal.GetLastWin32Error();
                if (error != 1168)
                {
                    throw new AiProviderException($"Windows Credential Manager could not delete credential '{key}' (error {error}).");
                }
            }
        }

        private static string Target(string key) => $"{ServiceName}/{key}";

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NativeCredential
        {
            public uint Flags;
            public uint Type;
            public IntPtr TargetName;
            public IntPtr Comment;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
            public uint CredentialBlobSize;
            public IntPtr CredentialBlob;
            public uint Persist;
            public uint AttributeCount;
            public IntPtr Attributes;
            public IntPtr TargetAlias;
            public IntPtr UserName;
        }

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredWrite(ref NativeCredential credential, uint flags);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredDelete(string target, uint type, uint flags);

        [DllImport("advapi32.dll")]
        private static extern bool CredFree(IntPtr credential);
    }
}