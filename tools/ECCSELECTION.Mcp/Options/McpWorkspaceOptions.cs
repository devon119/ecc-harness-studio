namespace ECCSELECTION.Mcp.Options;

/// <summary>
/// Configuration options for locating and validating the ECCSELECTION workspace root.
/// </summary>
public sealed class McpWorkspaceOptions
{
    private const string SolutionFileName = "ECCSELECTION.slnx";

    /// <summary>
    /// Initializes a new instance of <see cref="McpWorkspaceOptions"/>.
    /// </summary>
    public McpWorkspaceOptions()
    {
        RootDirectory = ResolveDefaultRoot();
    }

    /// <summary>
    /// Initializes a new instance of <see cref="McpWorkspaceOptions"/> with an explicit root directory.
    /// </summary>
    /// <param name="rootDirectory">The workspace root directory path.</param>
    public McpWorkspaceOptions(string rootDirectory)
    {
        RootDirectory = Path.GetFullPath(rootDirectory);
    }

    /// <summary>
    /// Gets or sets the absolute path to the ECCSELECTION repository root.
    /// </summary>
    public string RootDirectory { get; set; }

    /// <summary>
    /// Validates that <see cref="RootDirectory"/> exists and contains <c>ECCSELECTION.slnx</c>.
    /// </summary>
    /// <returns>The normalized full path of the workspace root.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the workspace root is invalid.</exception>
    public string EnsureValidWorkspaceRoot()
    {
        if (string.IsNullOrWhiteSpace(RootDirectory))
        {
            throw new InvalidOperationException("ECCSELECTION workspace root directory is not configured.");
        }

        string fullRoot = Path.GetFullPath(RootDirectory);
        string slnxPath = Path.Combine(fullRoot, SolutionFileName);

        if (!Directory.Exists(fullRoot) || !File.Exists(slnxPath))
        {
            throw new InvalidOperationException(
                $"Invalid ECCSELECTION workspace root '{fullRoot}'. Expected '{SolutionFileName}' to exist.");
        }

        return fullRoot;
    }

    private static string ResolveDefaultRoot()
    {
        string? envRoot = Environment.GetEnvironmentVariable("ECCSELECTION_ROOT");
        if (!string.IsNullOrWhiteSpace(envRoot))
        {
            return Path.GetFullPath(envRoot);
        }

        string? fromCurrent = FindSolutionRoot(Directory.GetCurrentDirectory());
        if (fromCurrent is not null)
        {
            return fromCurrent;
        }

        string? fromBase = FindSolutionRoot(AppContext.BaseDirectory);
        if (fromBase is not null)
        {
            return fromBase;
        }

        return Path.GetFullPath(Directory.GetCurrentDirectory());
    }

    private static string? FindSolutionRoot(string startDirectory)
    {
        DirectoryInfo? dir = new(startDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, SolutionFileName)))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return null;
    }
}
