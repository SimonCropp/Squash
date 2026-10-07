/// <summary>
/// What ships: the Squash package itself, and the packages and signed assemblies built with it.
/// </summary>
public class PackagingTests
{
    [Test]
    public async Task PackageShape()
    {
        await using var archive = await ZipFile.OpenReadAsync(PackageUnderTest.Ensure().NupkgPath);
        var entries = archive.Entries
            .Select(_ => _.FullName)
            // Written by NuGet for every package, one of them under a random name.
            .Where(_ => !_.StartsWith("_rels/", StringComparison.Ordinal) &&
                        !_.StartsWith("package/", StringComparison.Ordinal) &&
                        _ != "[Content_Types].xml")
            .OrderBy(_ => _, StringComparer.Ordinal);

        // The task with no dependency beside it, and the linker with everything its deps.json names.
        await Verify(string.Join("\n", entries));
    }

    [Test]
    public async Task ThePackageDeclaresNoDependencies()
    {
        await using var archive = await ZipFile.OpenReadAsync(PackageUnderTest.Ensure().NupkgPath);
        await using var stream = await archive.GetEntry("Squash.nuspec")!.OpenAsync();
        using var reader = new StreamReader(stream);
        var nuspec = await reader.ReadToEndAsync();

        // The linker is carried inside the package, not restored beside it, so a consumer's restore
        // never sees Microsoft.NET.ILLink.Tasks and cannot conflict with the version the SDK picks.
        await Assert.That(nuspec).DoesNotContain("<dependency ");
        await Assert.That(nuspec).Contains("<developmentDependency>true</developmentDependency>");
    }

    [Test]
    public async Task PackShipsTheTrimmedAssembly()
    {
        var work = Consumer.Prepare("Library");
        var output = Path.Combine(work, "out");
        var result = await Consumer.Dotnet(work, "pack", Path.Combine("Lib", "Lib.csproj"), arguments: ["--configuration", "Release", "--output", output]);
        await Assert.That(result.Cli.ExitCode).IsEqualTo(0).Because(result.Cli.Combined);

        var nupkg = Directory.GetFiles(output, "*.nupkg").Single();
        await using var archive = await ZipFile.OpenReadAsync(nupkg);
        foreach (var framework in new[] { "netstandard2.0", "net10.0" })
        {
            var extracted = Path.Combine(output, framework + ".dll");
            await archive.GetEntry($"lib/{framework}/Lib.dll")!.ExtractToFileAsync(extracted);
            await Assert.That(Consumer.HasType(extracted, "Lib.Api")).IsTrue();
            await Assert.That(Consumer.HasType(extracted, "Lib.UnusedInternal")).IsFalse();
        }

        // A development dependency: whoever installs Lib does not get Squash.
        await using var stream = await archive.GetEntry("Lib.nuspec")!.OpenAsync();
        using var reader = new StreamReader(stream);
        await Assert.That(await reader.ReadToEndAsync()).DoesNotContain("Squash");
    }

    [Test]
    public async Task ASignedLibraryIsSignedAgainWithItsEmbeddedSymbols()
    {
        var result = await Consumer.Build("Signed", Path.Combine("Lib", "Lib.csproj"));
        await Assert.That(result.Cli.ExitCode).IsEqualTo(0).Because(result.Cli.Combined);

        var assembly = result.Output("Lib", "netstandard2.0", "Lib.dll");
        var image = await File.ReadAllBytesAsync(assembly);
        var publicKey = AssemblyName.GetAssemblyName(assembly).GetPublicKey()!;

        await Assert.That(Consumer.HasType(assembly, "Lib.UnusedInternal")).IsFalse();
        await Assert.That(StrongName.IsSigned(image)).IsTrue();
        await Assert.That(StrongName.Verify(image, publicKey)).IsTrue();
        await Assert.That(AssemblyDump.Full(assembly)).Contains("EmbeddedPortablePdb");
    }

    [Test]
    public async Task SourceLinkSurvives()
    {
        // The SDK writes source link into the pdb of any project in a repository with a remote it
        // recognises, so the fixture is made into one.
        var work = Consumer.Prepare("Library");
        string[][] commands =
        [
            ["init", "--quiet"],
            ["remote", "add", "origin", "https://github.com/example/example.git"],
            ["add", "--all"],
            ["-c", "user.name=fixture", "-c", "user.email=fixture@example.com", "commit", "--quiet", "--message", "fixture"]
        ];
        foreach (var command in commands)
        {
            var git = await CliRunner.Run("git", command, work);
            await Assert.That(git.ExitCode).IsEqualTo(0).Because(git.Combined);
        }

        var result = await Consumer.Rebuild(work, Path.Combine("Lib", "Lib.csproj"));
        await Assert.That(result.Cli.ExitCode).IsEqualTo(0).Because(result.Cli.Combined);

        const string sourceLink = "SourceLink on ModuleDefinition: 1";
        var compiled = result.Squash("Lib", "net10.0", Path.Combine("in", "Lib.dll"));
        await Assert.That(AssemblyDump.Symbols(compiled)).Contains(sourceLink);
        await Assert.That(AssemblyDump.Symbols(result.Output("Lib", "net10.0", "Lib.dll"))).Contains(sourceLink);
    }

    [Test]
    public async Task FodyWeavesBeforeSquashTrims()
    {
        var result = await Consumer.Build("Woven", Path.Combine("App", "App.csproj"));
        await Assert.That(result.Cli.ExitCode).IsEqualTo(0).Because(result.Cli.Combined);

        // Nothing calls MethodTimeLogger until the weaver has run. Trimmed first, it would be gone
        // before the weaver looked for it.
        var library = result.Output("App", "net10.0", "Lib.dll");
        await Assert.That(Consumer.HasType(library, "Lib.MethodTimeLogger")).IsTrue();
        await Assert.That(Consumer.HasType(library, "Lib.UnusedInternal")).IsFalse();

        var run = await CliRunner.Run("dotnet", [result.Output("App", "net10.0", "App.dll")], result.Work);
        await Assert.That(run.ExitCode).IsEqualTo(0).Because(run.Combined);
        await Assert.That(run.Stdout).Contains("timed Work");
        await Assert.That(run.Stdout).Contains("worked");
    }
}
