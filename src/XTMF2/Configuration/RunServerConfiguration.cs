using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;

namespace XTMF2.Configuration;

/// <summary>
/// Defines the settings used to configure a standalone RunServer.
/// </summary>
public sealed class RunServerConfiguration
{
    /// <summary>
    /// Gets the current RunServer configuration schema version.
    /// </summary>
    public const int CurrentVersion = 1;

    /// <summary>
    /// Gets or sets the schema version of this configuration.
    /// </summary>
    public int Version { get; set; } = CurrentVersion;

    /// <summary>
    /// Gets or sets the directory containing RunServer-managed persistent data.
    /// </summary>
    public string ServerStorageDirectory { get; set; } = XTMFStoragePaths.GetDefaultRunServerDataRoot();

    /// <summary>
    /// Gets or sets the IP address on which the server listens.
    /// </summary>
    public string ListenAddress { get; set; } = "127.0.0.1";

    /// <summary>
    /// Gets or sets the TCP port on which the server listens; zero requests an available port.
    /// </summary>
    public int Port { get; set; }

    /// <summary>
    /// Gets or sets the directory containing the RunServer's TLS certificate and authentication token.
    /// </summary>
    public string? SecurityDirectory { get; set; }

    /// <summary>
    /// Gets or sets additional module assemblies to load when the server starts.
    /// </summary>
    public List<string> AdditionalAssemblies { get; set; } = [];

    /// <summary>
    /// Gets or sets whether clients may deploy modules to this server.
    /// </summary>
    public bool AllowDeployment { get; set; }

    /// <summary>
    /// Gets or sets the parent directory under which each run's workspace is created.
    /// When unset, workspaces are created beneath <c>Runs</c> in <see cref="ServerStorageDirectory"/>.
    /// </summary>
    public string? RunWorkspaceBaseDirectory { get; set; }

    /// <summary>
    /// Creates resolved storage paths from this configuration.
    /// </summary>
    /// <returns>The storage paths configured for this RunServer.</returns>
    public XTMFStoragePaths CreateStoragePaths()
        => new(ServerStorageDirectory, RunWorkspaceBaseDirectory);

    /// <summary>
    /// Loads and validates a RunServer configuration file, resolving relative paths against its directory.
    /// </summary>
    /// <param name="path">The path to the JSON configuration file.</param>
    /// <returns>The validated configuration with its paths resolved to absolute paths.</returns>
    /// <exception cref="ArgumentException"><paramref name="path"/> is null, empty, or whitespace.</exception>
    /// <exception cref="InvalidDataException">The file is empty, uses an unsupported version, or contains invalid configuration.</exception>
    /// <exception cref="IOException">The configuration file cannot be read.</exception>
    /// <exception cref="JsonException">The file contains invalid JSON.</exception>
    public static RunServerConfiguration Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullConfigPath = Path.GetFullPath(path);
        var configDirectory = Path.GetDirectoryName(fullConfigPath)!;
        var json = File.ReadAllText(fullConfigPath);
        var configuration = JsonSerializer.Deserialize<RunServerConfiguration>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("The RunServer configuration file is empty.");

        if (configuration.Version != CurrentVersion)
            throw new InvalidDataException($"Unsupported RunServer configuration version {configuration.Version}.");

        configuration.ServerStorageDirectory = ResolvePath(configuration.ServerStorageDirectory, configDirectory,
            XTMFStoragePaths.GetDefaultRunServerDataRoot());
        configuration.RunWorkspaceBaseDirectory = string.IsNullOrWhiteSpace(configuration.RunWorkspaceBaseDirectory)
            ? null
            : ResolvePath(configuration.RunWorkspaceBaseDirectory, configDirectory);
        configuration.SecurityDirectory = string.IsNullOrWhiteSpace(configuration.SecurityDirectory)
            ? null
            : ResolvePath(configuration.SecurityDirectory, configDirectory);
        configuration.AdditionalAssemblies = (configuration.AdditionalAssemblies ?? [])
            .Where(assembly => !string.IsNullOrWhiteSpace(assembly))
            .Select(assembly => ResolvePath(assembly, configDirectory))
            .ToList();

        configuration.Validate();
        return configuration;
    }

    /// <summary>
    /// Validates and saves this configuration as JSON.
    /// </summary>
    /// <param name="path">The path where the configuration file will be written.</param>
    /// <exception cref="ArgumentException"><paramref name="path"/> is null, empty, or whitespace.</exception>
    /// <exception cref="InvalidDataException">The configuration contains invalid values.</exception>
    /// <exception cref="IOException">The configuration file cannot be written.</exception>
    public void Save(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Validate();
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, JsonSerializer.Serialize(this, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        }));
    }

    /// <summary>
    /// Validates the schema version, storage settings, listener, security requirements, and module paths.
    /// </summary>
    /// <exception cref="InvalidDataException">A configuration value is invalid or violates a security requirement.</exception>
    public void Validate()
    {
        if (Version != CurrentVersion)
            throw new InvalidDataException($"Unsupported RunServer configuration version {Version}.");
        if (string.IsNullOrWhiteSpace(ServerStorageDirectory))
            throw new InvalidDataException("A RunServer serverStorageDirectory is required.");
        if (!IPAddress.TryParse(ListenAddress, out var listenAddress))
            throw new InvalidDataException($"The RunServer listenAddress '{ListenAddress}' is not a valid IP address.");
        if (Port is < 0 or > IPEndPoint.MaxPort)
            throw new InvalidDataException($"The RunServer port '{Port}' is outside the valid range.");
        if (!IPAddress.IsLoopback(listenAddress) && string.IsNullOrWhiteSpace(SecurityDirectory))
            throw new InvalidDataException("A securityDirectory is required when the RunServer listens on a non-loopback address.");
        if (AllowDeployment && string.IsNullOrWhiteSpace(SecurityDirectory))
            throw new InvalidDataException("RunServer deployment requires a securityDirectory.");
        if (AdditionalAssemblies.Any(string.IsNullOrWhiteSpace))
            throw new InvalidDataException("RunServer additionalAssemblies cannot contain empty paths.");
    }

    private static string ResolvePath(string path, string baseDirectory, string? fallback = null)
    {
        var value = string.IsNullOrWhiteSpace(path) ? fallback : path;
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidDataException("A configured path cannot be empty.");
        return Path.GetFullPath(value, baseDirectory);
    }
}