public static class TestEnvironment
{
    static Lazy<string> repoRoot = new(FindRepoRoot);

    public static string RepoRoot => repoRoot.Value;

    /// <summary>
    /// The configuration these tests were built in, read from where they are running:
    /// bin/{Configuration}/{framework}/. Everything else the solution built sits beside them under
    /// the same name.
    /// </summary>
    public static string Configuration { get; } =
        new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)).Parent!.Name;

    /// <summary>
    /// The linker as Squash.csproj staged it, which is byte for byte what the package carries.
    /// </summary>
    public static string LinkerDirectory =>
        Path.Combine(RepoRoot, "src", "Squash", "bin", Configuration, "illink");

    public static string TargetsFile =>
        Path.Combine(RepoRoot, "src", "Squash", "build", "Squash.targets");

    public static string DotNetHost { get; } = FindDotNetHost();

    public static string Fixture(string name, string framework)
    {
        var directory = Path.Combine(RepoRoot, "src", "Fixtures", name, "bin", Configuration, framework);
        if (!Directory.Exists(directory))
        {
            throw new($"'{directory}' does not exist. Run: dotnet build src --configuration {Configuration}");
        }

        return directory;
    }

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
    /// The host that started these tests: three levels above the runtime it loaded,
    /// {root}/shared/Microsoft.NETCore.App/{version}/.
    /// </summary>
    static string FindDotNetHost()
    {
        var runtime = new DirectoryInfo(RuntimeEnvironment.GetRuntimeDirectory());
        var root = runtime.Parent!.Parent!.Parent!.FullName;
        var name = "dotnet";
        if (OperatingSystem.IsWindows())
        {
            name = "dotnet.exe";
        }

        return Path.Combine(root, name);
    }
}
