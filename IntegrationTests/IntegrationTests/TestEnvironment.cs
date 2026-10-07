public static class TestEnvironment
{
    static readonly Lazy<string> repoRoot = new(FindRepoRoot);

    public static string RepoRoot => repoRoot.Value;
    public static string NugetsDirectory => Path.Combine(RepoRoot, "nugets");
    public static string FixturesDirectory => Path.Combine(RepoRoot, "IntegrationTests", "Fixtures");

    /// <summary>
    /// The throwaway key the signed fixtures of both test suites share.
    /// </summary>
    public static string KeyFile => Path.Combine(RepoRoot, "src", "Fixtures", "Signed", "test.snk");

    /// <summary>
    /// Walks up from the test binaries looking for license.txt, so nothing here depends on how deep
    /// the output directory happens to be.
    /// </summary>
    static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null &&
               !File.Exists(Path.Combine(directory.FullName, "license.txt")))
        {
            directory = directory.Parent;
        }

        if (directory == null)
        {
            throw new("Could not locate the repo root: no license.txt above the test binaries.");
        }

        return directory.FullName;
    }

    /// <summary>
    /// Everything one run writes outside the repository: fixture copies, the package feed, and the
    /// packages restored from it. One root, so that it can be removed as one.
    /// </summary>
    public static string RunDirectory { get; } =
        Path.Combine(Path.GetTempPath(), "squash-it", Guid.NewGuid().ToString("N"));

    public static string MakeWorkDirectory(string name)
    {
        var path = Path.Combine(RunDirectory, "work", $"{name}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>
    /// A run restores tens of megabytes of packages. Set SQUASH_KEEP_TEST_OUTPUT to look at what a
    /// failing test left behind.
    /// </summary>
    [After(HookType.Assembly)]
    public static void RemoveRunDirectory()
    {
        if (Environment.GetEnvironmentVariable("SQUASH_KEEP_TEST_OUTPUT") != null ||
            !Directory.Exists(RunDirectory))
        {
            return;
        }

        try
        {
            Directory.Delete(RunDirectory, true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (parts.Contains("bin") ||
                parts.Contains("obj"))
            {
                continue;
            }

            var destination = Path.Combine(target, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: true);
        }
    }

    public static void WriteNugetConfig(string directory, string feed)
    {
        var content = $"""
                       <?xml version="1.0" encoding="utf-8"?>
                       <configuration>
                         <packageSources>
                           <clear />
                           <add key="local" value="{feed}" />
                           <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
                         </packageSources>
                       </configuration>
                       """;
        File.WriteAllText(Path.Combine(directory, "nuget.config"), content);
    }
}
