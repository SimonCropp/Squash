/// <summary>
/// Things that have to stay in step with each other and that nothing else would notice drifting.
/// </summary>
public class StructureTests
{
    static XDocument Targets() =>
        XDocument.Load(TestEnvironment.TargetsFile);

    static XElement TaskElement() =>
        Targets()
            .Descendants()
            .Single(_ => _.Name.LocalName == nameof(SquashTask));

    [Test]
    public async Task EveryCodeIsDocumented()
    {
        var docs = await File.ReadAllTextAsync(Path.Combine(TestEnvironment.RepoRoot, "docs", "DiagnosticCodes.md"));
        foreach (var code in Diagnostics.All)
        {
            await Assert.That(docs).Contains($"## {code}");
        }
    }

    [Test]
    public async Task EveryCodeHasAName()
    {
        var constants = typeof(Diagnostics)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(_ => _.IsLiteral && _.Name != nameof(Diagnostics.Subcategory))
            .Select(_ => (string)_.GetRawConstantValue()!)
            .ToList();

        await Assert.That(constants.OrderBy(_ => _).SequenceEqual(Diagnostics.All.OrderBy(_ => _))).IsTrue();
        await Assert.That(constants.Distinct().Count()).IsEqualTo(constants.Count);
        foreach (var code in constants)
        {
            await Assert.That(Diagnostics.NameFor(code)).IsNotEqualTo(code);
        }
    }

    [Test]
    public async Task TargetsPassOnlyRealParameters()
    {
        var attributes = TaskElement()
            .Attributes()
            .Select(_ => _.Name.LocalName)
            .ToHashSet();
        var properties = typeof(SquashTask)
            .GetProperties()
            .Where(_ => _.DeclaringType == typeof(SquashTask))
            .Select(_ => _.Name);
        await Assert.That(attributes.SetEquals(properties)).IsTrue();
    }

    [Test]
    public async Task EverySettingTheTaskReadsIsACompileInput()
    {
        // SquashAssembly runs only when the compiler does. A setting passed to the task but missing
        // from the recorded inputs could change without the assembly being trimmed again.
        var targets = Targets();
        var lines = targets
            .Descendants()
            .Single(_ => _.Name.LocalName == "WriteLinesToFile")
            .Attribute("Lines")!
            .Value;

        // Not settings: the directory is derived from the project, and the host only says where
        // dotnet is on this machine.
        string[] derived = ["$(_Squash_Directory)", "$(_Squash_DotNetHost)"];
        var properties = TaskElement()
            .Attributes()
            .Select(_ => _.Value)
            .Where(_ => _.StartsWith("$(", StringComparison.Ordinal) && !derived.Contains(_))
            .ToList();

        await Assert.That(properties).IsNotEmpty();
        foreach (var property in properties)
        {
            await Assert.That(lines).Contains(property);
        }

        // An item cannot be written beside other text, so the descriptors go through a property.
        await Assert.That(lines).Contains("$(_Squash_RootDescriptors)");
    }

    [Test]
    public async Task TheLinkerTheStepsAndTheRuntimeConfigAgree()
    {
        var directory = TestEnvironment.LinkerDirectory;
        var linker = AssemblyName.GetAssemblyName(Path.Combine(directory, "illink.dll")).Version!.Major;

        var config = await File.ReadAllTextAsync(Path.Combine(directory, "illink.runtimeconfig.json"));
        await Assert.That(config).Contains($"\"tfm\": \"net{linker}.0\"");

        // The steps load into the linker's process, so they cannot target a newer framework than it.
        await Assert.That(TargetFramework(Path.Combine(directory, "Squash.Steps.dll"))).IsEqualTo($".NETCoreApp,Version=v{linker}.0");
    }

    [Test]
    public async Task TheRuntimeConfigAsksForTheMajorNotAPatch()
    {
        var directory = TestEnvironment.LinkerDirectory;
        var linker = AssemblyName.GetAssemblyName(Path.Combine(directory, "illink.dll")).Version!.Major;
        var config = await File.ReadAllTextAsync(Path.Combine(directory, "illink.runtimeconfig.json"));

        // A prerelease linker, which only the canary builds, keeps the file it shipped with.
        if (!config.Contains('-'))
        {
            await Assert.That(config).Contains($"\"version\": \"{linker}.0.0\"");
        }

        await Assert.That(config).Contains("\"rollForward\": \"Major\"");
    }

    [Test]
    public async Task EverythingTheLinkerDependsOnIsStaged()
    {
        // The dotnet host refuses to start an application when a file its deps.json names is absent.
        var directory = TestEnvironment.LinkerDirectory;
        var deps = await File.ReadAllTextAsync(Path.Combine(directory, "illink.deps.json"));
        var assemblies = System.Text.RegularExpressions.Regex
            .Matches(deps, "\"(?:[^\"/]+/)*([^\"/]+\\.dll)\"\\s*:")
            .Select(_ => _.Groups[1].Value)
            .Distinct()
            .ToList();

        await Assert.That(assemblies).Contains("illink.dll");
        await Assert.That(assemblies).Contains("Mono.Cecil.dll");
        foreach (var assembly in assemblies)
        {
            await Assert.That(File.Exists(Path.Combine(directory, assembly))).IsTrue().Because(assembly);
        }

        await Assert.That(File.Exists(Path.Combine(directory, "LICENSE.TXT"))).IsTrue();
        await Assert.That(File.Exists(Path.Combine(directory, "THIRD-PARTY-NOTICES.TXT"))).IsTrue();
    }

    [Test]
    public async Task ThePinnedLinkerMatchesItsDeclaredMajor()
    {
        // Dependabot moves the package version; a major also moves the framework the steps target,
        // which only a person can decide. This fails until both are changed together.
        var src = Path.Combine(TestEnvironment.RepoRoot, "src");
        var packages = XDocument.Load(Path.Combine(src, "Directory.Packages.props"));
        var version = packages
            .Descendants("PackageVersion")
            .First(_ => (string?)_.Attribute("Include") == "Microsoft.NET.ILLink.Tasks")
            .Attribute("Version")!
            .Value;
        var major = XDocument.Load(Path.Combine(src, "Directory.Build.props"))
            .Descendants("ILLinkMajor")
            .Single()
            .Value;

        await Assert.That(version.Split('.')[0]).IsEqualTo(major);
    }

    [Test]
    public async Task TheTaskAssemblyHasNoDependenciesToShip()
    {
        // The package carries exactly what is in this folder. A second assembly here would be a
        // dependency loaded into every MSBuild process that runs the task.
        var directory = Path.Combine(TestEnvironment.RepoRoot, "src", "Squash", "bin", TestEnvironment.Configuration, "netstandard2.0");
        var assemblies = Directory
            .GetFiles(directory, "*.dll")
            .Select(Path.GetFileName)
            .ToList();

        await Assert.That(assemblies.SequenceEqual(["Squash.dll"])).IsTrue();
    }

    static string TargetFramework(string path)
    {
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();
        foreach (var handle in reader.GetAssemblyDefinition().GetCustomAttributes())
        {
            var attribute = reader.GetCustomAttribute(handle);
            if (attribute.Constructor.Kind != HandleKind.MemberReference)
            {
                continue;
            }

            var parent = reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor).Parent;
            if (parent.Kind != HandleKind.TypeReference ||
                reader.GetString(reader.GetTypeReference((TypeReferenceHandle)parent).Name) != "TargetFrameworkAttribute")
            {
                continue;
            }

            var blob = reader.GetBlobReader(attribute.Value);
            blob.ReadUInt16();
            return blob.ReadSerializedString()!;
        }

        return "";
    }
}
