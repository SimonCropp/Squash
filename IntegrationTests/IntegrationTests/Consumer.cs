public sealed record BuildResult(CliResult Cli, string Work)
{
    public string Output(string project, string framework, string file, string configuration = "Release") =>
        Path.Combine(Work, project, "bin", configuration, framework, file);

    public string Intermediate(string project, string framework, string file, string configuration = "Release") =>
        Path.Combine(Work, project, "obj", configuration, framework, file);

    /// <summary>
    /// A file Squash leaves in the project's intermediate directory.
    /// </summary>
    public string Squash(string project, string framework, string file, string configuration = "Release") =>
        Intermediate(project, framework, Path.Combine("Squash", file), configuration);
}

/// <summary>
/// Copies a fixture to a temp directory, isolates it from this repository's props and targets, and
/// builds it against the Squash package just built.
/// </summary>
public static class Consumer
{
    // One package folder per run: nuget.org packages download once, and the freshly built Squash
    // cannot be shadowed by an older copy of the same version in the user's global cache.
    static readonly Lazy<string> packages = new(() =>
    {
        var path = Path.Combine(TestEnvironment.RunDirectory, "packages");
        Directory.CreateDirectory(path);
        return path;
    });

    public static string PackagesDirectory => packages.Value;

    /// <summary>
    /// Copies the fixture into a fresh temp directory, isolated from this repository's props, targets
    /// and package versions, with a feed holding the package under test.
    /// </summary>
    public static string Prepare(string fixture, [CallerMemberName] string caller = "")
    {
        var work = TestEnvironment.MakeWorkDirectory(caller);
        TestEnvironment.CopyDirectory(Path.Combine(TestEnvironment.FixturesDirectory, fixture), work);
        TestEnvironment.WriteNugetConfig(work, PackageUnderTest.Ensure().Feed);
        File.WriteAllText(Path.Combine(work, "Directory.Build.props"), "<Project />");
        File.WriteAllText(Path.Combine(work, "Directory.Build.targets"), "<Project />");
        File.WriteAllText(Path.Combine(work, "Directory.Packages.props"), "<Project />");
        File.Copy(Path.Combine(TestEnvironment.RepoRoot, "IntegrationTests", "global.json"), Path.Combine(work, "global.json"));
        File.Copy(TestEnvironment.KeyFile, Path.Combine(work, "test.snk"));
        return work;
    }

    public static Task<BuildResult> Build(
        string fixture,
        string project,
        IReadOnlyDictionary<string, string>? properties = null,
        string configuration = "Release",
        [CallerMemberName] string caller = "")
    {
        var work = Prepare(fixture, caller);
        return Rebuild(work, project, properties, configuration);
    }

    /// <summary>
    /// Builds again in an existing work directory, as a developer's next build would.
    /// </summary>
    public static Task<BuildResult> Rebuild(
        string work,
        string project,
        IReadOnlyDictionary<string, string>? properties = null,
        string configuration = "Release",
        IReadOnlyList<string>? arguments = null) =>
        Dotnet(work, "build", project, properties, ["--configuration", configuration, .. arguments ?? []]);

    public static async Task<BuildResult> Dotnet(
        string work,
        string command,
        string project,
        IReadOnlyDictionary<string, string>? properties = null,
        IReadOnlyList<string>? arguments = null)
    {
        // A build server or a reused node would outlive the test and hold the work directory open.
        // dotnet msbuild passes its arguments straight to MSBuild, which spells that differently.
        var noServers = "--disable-build-servers";
        if (command == "msbuild")
        {
            noServers = "-nodeReuse:false";
        }

        List<string> all =
        [
            command,
            Path.Combine(work, project),
            "-nologo",
            noServers,
            .. arguments ?? [],
            .. Properties(properties)
        ];
        var cli = await CliRunner.Run("dotnet", all, work, PackagesDirectory);
        return new(cli, work);
    }

    public static IEnumerable<string> Properties(IReadOnlyDictionary<string, string>? properties)
    {
        yield return $"-p:Squash_Version={PackageUnderTest.Ensure().Version}";
        if (properties == null)
        {
            yield break;
        }

        foreach (var property in properties)
        {
            yield return $"-p:{property.Key}={property.Value}";
        }
    }

    /// <summary>
    /// Whether a type survived into an assembly, by the name the dump gives it.
    /// </summary>
    public static bool HasType(string assembly, string type) =>
        AssemblyDump.Full(assembly)
            .Split('\n')
            .Any(_ => _.EndsWith($" {type}", StringComparison.Ordinal) || _.Contains($" {type} ["));
}
