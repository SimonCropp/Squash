public sealed record BuiltPackage(string Feed, string Version, string NupkgPath);

/// <summary>
/// Stages the freshly built Squash package into a private feed, once per test run.
/// </summary>
public static class PackageUnderTest
{
    static readonly Lazy<BuiltPackage> built = new(Stage, LazyThreadSafetyMode.ExecutionAndPublication);

    public static BuiltPackage Ensure() => built.Value;

    static BuiltPackage Stage()
    {
        var nugets = TestEnvironment.NugetsDirectory;
        if (!Directory.Exists(nugets))
        {
            throw new($"'{nugets}' does not exist. Run: dotnet build src --configuration Release");
        }

        var packages = Directory.GetFiles(nugets, "Squash.*.nupkg").ToList();

        if (packages.Count != 1)
        {
            throw new(
                $"Expected exactly one Squash package in '{nugets}', found {packages.Count}. " +
                "Delete the stale ones, or rebuild with: dotnet build src --configuration Release");
        }

        var source = packages[0];
        var version = ExtractVersion(Path.GetFileName(source));

        // A per-run feed, so a rebuild between runs cannot be shadowed by whatever a previous run
        // staged.
        var feed = Path.Combine(TestEnvironment.RunDirectory, "feed");
        Directory.CreateDirectory(feed);
        File.Copy(source, Path.Combine(feed, Path.GetFileName(source)), overwrite: true);

        return new(feed, version, source);
    }

    /// <summary>
    /// "Squash.0.1.0.nupkg" gives "0.1.0". Read from the filename rather than from
    /// Directory.Build.props, so the fixtures always reference the bits that were actually built.
    /// </summary>
    static string ExtractVersion(string fileName)
    {
        const string prefix = "Squash.";
        const string suffix = ".nupkg";
        return fileName.Substring(prefix.Length, fileName.Length - prefix.Length - suffix.Length);
    }
}
