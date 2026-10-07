public class ResponseFileTests
{
    static SquashRequest Request() =>
        new()
        {
            AssemblyName = "Lib",
            Input = "/obj/Squash/in/Lib.dll",
            References =
            [
                "/refs/netstandard.dll",
                "/packages/a/Dep.dll",
                // The linker would use the first Dep.dll, so the second is noise.
                "/packages/b/Dep.dll",
                // Would shadow the assembly being trimmed.
                "/stale/Lib.dll"
            ],
            OutputDirectory = "/obj/Squash/out",
            StepsAssembly = "/linker/Squash.Steps.dll",
            FriendsFile = "friends.txt",
            ReportFile = "removed.txt",
            IgnoreInternalsVisibleTo = true
        };

    static string Lines(SquashRequest request) =>
        string.Join("\n", ResponseFile.Build(request));

    [Test]
    public Task Default() =>
        Verify(Lines(Request()));

    [Test]
    public Task Honor()
    {
        var request = new SquashRequest
        {
            AssemblyName = "Lib",
            Input = "/obj/Squash/in/Lib.dll",
            OutputDirectory = "/obj/Squash/out",
            StepsAssembly = "/linker/Squash.Steps.dll",
            FriendsFile = "friends.txt",
            ReportFile = "removed.txt",
            IgnoreInternalsVisibleTo = false
        };

        // No step touches InternalsVisibleTo: the linker's own behaviour, on its own.
        return Verify(Lines(request));
    }

    [Test]
    public Task EverySetting()
    {
        var request = new SquashRequest
        {
            AssemblyName = "App",
            Input = "/obj/Squash/in/App.exe",
            References = ["/refs/System.Runtime.dll"],
            RootDescriptors = ["/src/roots.xml", "/src/more roots.xml"],
            OutputDirectory = "/obj/Squash/out",
            StepsAssembly = "/linker/Squash.Steps.dll",
            FriendsFile = "friends.txt",
            ReportFile = "removed.txt",
            IgnoreInternalsVisibleTo = true,
            KeepDataShape = true,
            WarningsAsErrors = true,
            RootEntryPoint = true,
            ExtraArgs = "  --enable-opt ipconstprop --verbose "
        };
        return Verify(Lines(request));
    }

    [Test]
    public async Task ExtraArgsComeAfterTheDefaultsTheyOverride()
    {
        var request = Request();
        var lines = ResponseFile.Build(
            new()
            {
                AssemblyName = request.AssemblyName,
                Input = request.Input,
                OutputDirectory = request.OutputDirectory,
                StepsAssembly = request.StepsAssembly,
                ExtraArgs = "--enable-opt ipconstprop"
            });

        // The linker applies optimization switches in order, so the later one wins.
        await Assert.That(lines.IndexOf("--enable-opt ipconstprop")).IsGreaterThan(lines.IndexOf("--disable-opt ipconstprop"));
        await Assert.That(lines[^1]).StartsWith("-out ");
    }

    [Test]
    public async Task ATrailingBackslashDoesNotEscapeTheClosingQuote()
    {
        await Assert.That(ResponseFile.Quote(@"C:\obj\out\")).IsEqualTo("\"C:\\obj\\out\"");
        await Assert.That(ResponseFile.Quote(@"C:\with space\file.dll")).IsEqualTo("\"C:\\with space\\file.dll\"");
    }

    [Test]
    public async Task TheKeysMatchWhatTheStepsRead()
    {
        // Squash.Steps cannot be referenced from the task, so both spell the keys out. Read from its
        // metadata: loading it would need the linker it is compiled against.
        using var stream = File.OpenRead(Path.Combine(TestEnvironment.LinkerDirectory, "Squash.Steps.dll"));
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();
        var keys = reader.TypeDefinitions
            .Select(reader.GetTypeDefinition)
            .Single(_ => reader.GetString(_.Name) == "CustomData")
            .GetFields()
            .Select(reader.GetFieldDefinition)
            .Where(_ => !_.GetDefaultValue().IsNil)
            .ToDictionary(
                _ => reader.GetString(_.Name),
                _ =>
                {
                    var blob = reader.GetBlobReader(reader.GetConstant(_.GetDefaultValue()).Value);
                    return blob.ReadUTF16(blob.Length);
                });

        await Assert.That(keys["RootAssembly"]).IsEqualTo(ResponseFile.RootAssemblyKey);
        await Assert.That(keys["FriendsFile"]).IsEqualTo(ResponseFile.FriendsFileKey);
        await Assert.That(keys["ReportFile"]).IsEqualTo(ResponseFile.ReportFileKey);
    }
}
