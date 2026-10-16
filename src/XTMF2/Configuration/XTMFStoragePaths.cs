using System;
using System.IO;

namespace XTMF2.Configuration;

/// <summary>
/// Provides the resolved storage directories used by XTMF and RunServer.
/// </summary>
public sealed class XTMFStoragePaths
{
    private readonly string? _runWorkspaceBaseDirectory;

    /// <summary>
    /// Gets the root directory containing XTMF storage.
    /// </summary>
    public string DataRoot { get; }

    /// <summary>
    /// Gets the directory containing user data.
    /// </summary>
    public string UserDirectory { get; }

    /// <summary>
    /// Gets the directory containing persisted remote-run records and artifacts.
    /// </summary>
    public string RemoteRunDirectory => Path.Combine(DataRoot, "RemoteRuns");

    /// <summary>
    /// Gets the parent directory under which each run's workspace directory is created.
    /// Defaults to <c>Runs</c> beneath <see cref="DataRoot"/>.
    /// </summary>
    public string RunWorkspaceBaseDirectory => _runWorkspaceBaseDirectory ?? Path.Combine(DataRoot, "Runs");

    /// <summary>
    /// Gets the directory containing staged module deployments.
    /// </summary>
    public string DeploymentDirectory => Path.Combine(DataRoot, "Deployments");

    /// <summary>
    /// Initializes storage paths from a root directory and an optional run-workspace base directory.
    /// </summary>
    /// <param name="dataRoot">The root directory for XTMF storage.</param>
    /// <param name="runWorkspaceBaseDirectory">The parent directory for per-run workspaces, or <see langword="null"/> to use <c>Runs</c> beneath <paramref name="dataRoot"/>.</param>
    /// <exception cref="ArgumentException"><paramref name="dataRoot"/> is null, empty, or whitespace.</exception>
    public XTMFStoragePaths(string dataRoot, string? runWorkspaceBaseDirectory = null)
        : this(dataRoot, null, runWorkspaceBaseDirectory)
    {
    }

    private XTMFStoragePaths(string dataRoot, string? userDirectory, string? runWorkspaceBaseDirectory)
    {
        if (string.IsNullOrWhiteSpace(dataRoot))
            throw new ArgumentException("A storage data root is required.", nameof(dataRoot));

        DataRoot = Path.GetFullPath(dataRoot);
        UserDirectory = userDirectory is null
            ? Path.Combine(DataRoot, "Users")
            : Path.GetFullPath(userDirectory);
        _runWorkspaceBaseDirectory = string.IsNullOrWhiteSpace(runWorkspaceBaseDirectory)
            ? null
            : Path.GetFullPath(runWorkspaceBaseDirectory);
    }

    /// <summary>
    /// Gets the default storage root for the desktop application.
    /// </summary>
    /// <returns>The <c>XTMF2</c> directory beneath the user's Documents directory.</returns>
    public static string GetDefaultDesktopDataRoot()
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "XTMF2");

    /// <summary>
    /// Gets the default storage root for a standalone RunServer.
    /// </summary>
    /// <returns>The <c>XTMF2</c> directory beneath the user's local application data directory.</returns>
    public static string GetDefaultRunServerDataRoot()
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "XTMF2");

    internal static XTMFStoragePaths FromUserDirectory(string userDirectory)
    {
        if (string.IsNullOrWhiteSpace(userDirectory))
            throw new ArgumentException("A user directory is required.", nameof(userDirectory));

        var fullUserDirectory = Path.GetFullPath(userDirectory);
        var parentDirectory = Directory.GetParent(fullUserDirectory)?.FullName ?? fullUserDirectory;
        return new XTMFStoragePaths(parentDirectory, fullUserDirectory, null);
    }
}