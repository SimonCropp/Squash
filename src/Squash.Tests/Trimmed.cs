/// <summary>
/// One run of SquashTask against a copy of a built fixture, laid out the way a project's obj
/// directory would be. No MSBuild is involved: this is the task and the bundled linker, and nothing
/// else.
/// </summary>
public sealed class Trimmed :
    IDisposable
{
    public TempDirectory Temp { get; } = new();
    public StubBuildEngine Engine { get; } = new();
    public string Name { get; }
    public string Framework { get; }

    /// <summary>Where the task was pointed. On success, the trimmed assembly.</summary>
    public string Assembly { get; }

    public string Symbols { get; }

    /// <summary>An untouched copy of what went in.</summary>
    public string Original { get; }

    public IReadOnlyList<string> References { get; }
    public bool Succeeded { get; private set; }

    public string Directory => Temp.Combine("obj", "Squash");
    public string FriendsFile => Path.Combine(Directory, "friends.txt");
    public string Removed => File.ReadAllText(Path.Combine(Directory, "removed.txt"));
    public string ResponseFile => File.ReadAllText(Path.Combine(Directory, "squash.rsp"));

    Trimmed(string fixture, string framework, string extension)
    {
        Name = fixture;
        Framework = framework;
        var source = TestEnvironment.Fixture(fixture, framework);
        Assembly = Temp.Combine("obj", fixture + extension);
        Symbols = Temp.Combine("obj", fixture + ".pdb");
        Original = Temp.Combine("original", fixture + extension);
        References = File.ReadAllLines(Path.Combine(source, "references.txt"));

        System.IO.Directory.CreateDirectory(Temp.Combine("obj"));
        System.IO.Directory.CreateDirectory(Temp.Combine("original"));
        File.Copy(Path.Combine(source, fixture + extension), Assembly);
        File.Copy(Path.Combine(source, fixture + extension), Original);

        var symbols = Path.Combine(source, fixture + ".pdb");
        if (File.Exists(symbols))
        {
            File.Copy(symbols, Symbols);
            File.Copy(symbols, Temp.Combine("original", fixture + ".pdb"));
        }
    }

    public static Trimmed Run(string fixture, string framework, Action<SquashTask>? configure = null, string extension = ".dll")
    {
        var trimmed = new Trimmed(fixture, framework, extension);
        var task = new SquashTask
        {
            BuildEngine = trimmed.Engine,
            AssemblyFile = trimmed.Assembly,
            AssemblyName = fixture,
            IntermediateDirectory = trimmed.Directory,
            References = trimmed.References
                .Select(ITaskItem (_) => new TaskItem(_))
                .ToArray(),
            LinkerDirectory = TestEnvironment.LinkerDirectory,
            DotNetHost = TestEnvironment.DotNetHost
        };
        if (File.Exists(trimmed.Symbols))
        {
            task.SymbolsFile = trimmed.Symbols;
        }

        configure?.Invoke(task);
        trimmed.Succeeded = task.Execute();
        return trimmed;
    }

    public List<string> ErrorCodes =>
        Engine.Errors
            .Select(_ => _.Code)
            .ToList();

    public List<string> WarningCodes =>
        Engine.Warnings
            .Select(_ => _.Code)
            .ToList();

    public void Dispose() =>
        Temp.Dispose();
}

/// <summary>
/// Runs shared between tests. Each takes most of a second, and most tests only read the result.
/// </summary>
public static class Trims
{
    static ConcurrentDictionary<string, Lazy<Trimmed>> cache = new();

    public static string[] Frameworks = ["netstandard2.0", "net48", "net10.0"];

    public static string Roots(string framework) =>
        Path.Combine(TestEnvironment.Fixture("Scenarios", framework), "roots.xml");

    public static Trimmed Scenarios(string framework) =>
        Get($"Scenarios {framework}", () => Trimmed.Run("Scenarios", framework));

    public static Trimmed Honor(string framework) =>
        Get($"Honor {framework}", () => Trimmed.Run("Scenarios", framework, _ => _.InternalsVisibleTo = "Honor"));

    public static Trimmed DataShape(string framework) =>
        Get($"DataShape {framework}", () => Trimmed.Run("Scenarios", framework, _ => _.Preserve = "DataShape"));

    public static Trimmed InternalNamespaces(string framework) =>
        Get($"InternalNamespaces {framework}", () => Trimmed.Run("Scenarios", framework, _ => _.InternalNamespacesToKeep = "Scenarios"));

    public static Trimmed Descriptor(string framework) =>
        Get(
            $"Descriptor {framework}",
            () => Trimmed.Run("Scenarios", framework, _ => _.RootDescriptors = [new TaskItem(Roots(framework))]));

    public static string KeyFile =>
        Path.Combine(TestEnvironment.RepoRoot, "src", "Fixtures", "Signed", "test.snk");

    public static Trimmed Signed(string framework) =>
        Get($"Signed {framework}", () => Trimmed.Run("Signed", framework, _ => _.KeyFile = KeyFile));

    /// <summary>
    /// By name, so that a data-driven test shows which run it is about.
    /// </summary>
    public static Trimmed Get(string variant, string framework) =>
        variant switch
        {
            "Stock" => Scenarios(framework),
            "Honor" => Honor(framework),
            "DataShape" => DataShape(framework),
            "InternalNamespaces" => InternalNamespaces(framework),
            "Descriptor" => Descriptor(framework),
            "Signed" => Signed(framework),
            _ => throw new($"Unknown variant '{variant}'.")
        };

    /// <summary>
    /// Every run the checks that hold for any trimmed assembly are applied to.
    /// </summary>
    public static IEnumerable<(string Variant, string Framework)> All()
    {
        foreach (var framework in Frameworks)
        {
            yield return ("Stock", framework);
            yield return ("Honor", framework);
            yield return ("DataShape", framework);
            yield return ("InternalNamespaces", framework);
        }

        yield return ("Signed", "netstandard2.0");
        yield return ("Signed", "net48");
    }

    static Trimmed Get(string key, Func<Trimmed> run) =>
        cache.GetOrAdd(key, _ => new(run)).Value;

    [After(HookType.Assembly)]
    public static void Dispose()
    {
        foreach (var trimmed in cache.Values.Where(_ => _.IsValueCreated))
        {
            trimmed.Value.Dispose();
        }
    }
}
