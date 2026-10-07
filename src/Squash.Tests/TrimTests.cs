/// <summary>
/// What the task and the bundled linker do to real assemblies. The snapshots here are the contract
/// with the linker: when a new linker keeps or removes something different, this is where it shows.
/// </summary>
public class TrimTests
{
    static IEnumerable<string> Lines(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>
    /// What one setting keeps that the default removes.
    /// </summary>
    static string Keeps(Trimmed setting, Trimmed stock) =>
        string.Join("\n", Lines(stock.Removed).Except(Lines(setting.Removed)));

    [Test]
    [Arguments("netstandard2.0")]
    [Arguments("net48")]
    [Arguments("net10.0")]
    public async Task Survivors(string framework)
    {
        var trimmed = Trims.Scenarios(framework);

        await Assert.That(trimmed.Succeeded).IsTrue();
        await Verify(AssemblyDump.Full(trimmed.Assembly));
    }

    [Test]
    [Arguments("netstandard2.0")]
    [Arguments("net48")]
    [Arguments("net10.0")]
    public Task Removed(string framework) =>
        Verify(Trims.Scenarios(framework).Removed);

    [Test]
    public Task HonoringFriendsKeeps() =>
        Verify(Keeps(Trims.Honor("netstandard2.0"), Trims.Scenarios("netstandard2.0")));

    [Test]
    public Task DataShapeKeeps() =>
        Verify(Keeps(Trims.DataShape("netstandard2.0"), Trims.Scenarios("netstandard2.0")));

    [Test]
    public Task ARootDescriptorKeeps() =>
        Verify(Keeps(Trims.Descriptor("netstandard2.0"), Trims.Scenarios("netstandard2.0")))
            .Snapshot("type Scenarios.DescriptorRooted");

    [Test]
    public async Task IgnoredFriendsAreStillDeclaredAndReported()
    {
        var trimmed = Trims.Scenarios("netstandard2.0");
        var attributes = AssemblyDump.AssemblyAttributes(trimmed.Assembly);

        // The attributes are back in the output: what is left of the internals is still shared.
        await Assert.That(attributes).Contains("InternalsVisibleTo(Scenarios.Friend)");
        await Assert.That(attributes).Contains("InternalsVisibleTo(Scenarios.OtherFriend)");

        await Assert.That(await File.ReadAllTextAsync(trimmed.FriendsFile)).IsEqualTo("Scenarios.Friend\nScenarios.OtherFriend\n");
        var message = trimmed.Engine.Messages.Single(_ => _.Code == Diagnostics.FriendsIgnored);
        await Assert.That(message.Message!).Contains("Scenarios.Friend, Scenarios.OtherFriend");
    }

    [Test]
    public async Task HonoredFriendsAreNotReported()
    {
        var trimmed = Trims.Honor("netstandard2.0");

        await Assert.That(File.Exists(trimmed.FriendsFile)).IsFalse();
        await Assert.That(trimmed.Engine.Messages.Any(_ => _.Code == Diagnostics.FriendsIgnored)).IsFalse();
        await Assert.That(AssemblyDump.AssemblyAttributes(trimmed.Assembly)).Contains("InternalsVisibleTo(Scenarios.Friend)");
    }

    [Test]
    [Arguments("netstandard2.0")]
    [Arguments("net48")]
    [Arguments("net10.0")]
    public async Task ADependencysConstantIsNotFoldedIn(string framework)
    {
        const string method = "Scenarios.Constants::Describe() : string";
        var trimmed = Trims.Scenarios(framework);

        await Assert.That(AssemblyDump.BodySizes(trimmed.Assembly)[method]).IsEqualTo(AssemblyDump.BodySizes(trimmed.Original)[method]);
    }

    [Test]
    public async Task ExtraArgsOverrideTheDefaults()
    {
        // The other side of the test above: asked to, the linker does fold the constant in, and the
        // branch that could never run under this build of the dependency is gone.
        const string method = "Scenarios.Constants::Describe() : string";
        using var trimmed = Trimmed.Run("Scenarios", "netstandard2.0", _ => _.ExtraArgs = "--enable-opt ipconstprop");

        await Assert.That(trimmed.Succeeded).IsTrue();
        await Assert.That(AssemblyDump.BodySizes(trimmed.Assembly)[method]).IsLessThan(AssemblyDump.BodySizes(trimmed.Original)[method]);
    }

    [Test]
    public async Task LinkerWarningsKeepTheirCodeAndLocation()
    {
        var trimmed = Trims.Scenarios("netstandard2.0");
        var warning = trimmed.Engine.Warnings.Single();

        await Assert.That(warning.Code).IsEqualTo("IL2057");
        await Assert.That(Path.GetFileName(warning.File)).IsEqualTo("Generated.cs");
        await Assert.That(warning.LineNumber).IsGreaterThan(0);
        await Assert.That(trimmed.Engine.Errors).IsEmpty();
    }

    [Test]
    public async Task WarningsAsErrorsFailAndPutNothingBack()
    {
        using var trimmed = Trimmed.Run("Scenarios", "netstandard2.0", _ => _.TreatWarningsAsErrors = "true");

        await Assert.That(trimmed.Succeeded).IsFalse();
        await Assert.That(trimmed.ErrorCodes).Contains("IL2057");

        // The linker did write an assembly before it reported the error. It must not come back: the
        // next build has to compile again.
        await Assert.That(File.Exists(trimmed.Assembly)).IsFalse();
    }

    [Test]
    public async Task NoWarnReachesAWarningTheLinkerWouldPromote()
    {
        using var trimmed = Trimmed.Run(
            "Scenarios",
            "netstandard2.0",
            _ =>
            {
                _.TreatWarningsAsErrors = "true";
                _.NoWarn = "CS1591,IL2057";
            });

        await Assert.That(trimmed.Succeeded).IsTrue();
        await Assert.That(trimmed.Engine.Errors).IsEmpty();
        await Assert.That(trimmed.Engine.Warnings).IsEmpty();
        await Assert.That(trimmed.ResponseFile).Contains("--nowarn IL2057");
    }

    [Test]
    public async Task WarningsNotAsErrorsStayWarnings()
    {
        using var trimmed = Trimmed.Run(
            "Scenarios",
            "netstandard2.0",
            _ =>
            {
                _.TreatWarningsAsErrors = "true";
                _.WarningsNotAsErrors = "CS0618,IL2057";
            });

        await Assert.That(trimmed.Succeeded).IsTrue();
        await Assert.That(trimmed.Engine.Errors).IsEmpty();
        await Assert.That(trimmed.Engine.Warnings.Single().Code).IsEqualTo("IL2057");
    }

    [Test]
    [Arguments("netstandard2.0")]
    [Arguments("net48")]
    public async Task ASignedAssemblyIsSignedAgain(string framework)
    {
        var trimmed = Trims.Signed(framework);
        var image = await File.ReadAllBytesAsync(trimmed.Assembly);
        var publicKey = AssemblyName.GetAssemblyName(trimmed.Assembly).GetPublicKey()!;

        await Assert.That(trimmed.Succeeded).IsTrue();
        await Assert.That(image.Length).IsNotEqualTo(0);
        await Assert.That(StrongName.IsSigned(image)).IsTrue();
        await Assert.That(StrongName.Verify(image, publicKey)).IsTrue();
        await Assert.That(publicKey.SequenceEqual(AssemblyName.GetAssemblyName(trimmed.Original).GetPublicKey()!)).IsTrue();
    }

    [Test]
    public Task WhatTheSymbolsCarriedBefore() =>
        Verify(AssemblyDump.Symbols(Trims.Signed("netstandard2.0").Original))
            .Snapshot(
                """
                CompilationMetadataReferences on ModuleDefinition: 1
                CompilationOptions on ModuleDefinition: 1
                EmbeddedSource on Document: 4
                TypeDefinitionDocuments on TypeDefinition: 1
                """);

    [Test]
    public Task WhatTheSymbolsCarryAfter() =>
        // The compiler's record of how the assembly was built comes through. So does the embedded
        // source of the one file that holds code. The other three files hold only assembly
        // attributes and usings, and the linker's rewrite drops the source embedded for them: a
        // known limit, pinned here so that a linker which fixes it shows as a change. The type
        // that needed a document of its own was removed.
        Verify(AssemblyDump.Symbols(Trims.Signed("netstandard2.0").Assembly))
            .Snapshot(
                """
                CompilationMetadataReferences on ModuleDefinition: 1
                CompilationOptions on ModuleDefinition: 1
                EmbeddedSource on Document: 1
                """);

    [Test]
    public async Task ASignedAssemblyWithoutItsKeyFails()
    {
        using var trimmed = Trimmed.Run("Signed", "netstandard2.0");

        await Assert.That(trimmed.Succeeded).IsFalse();
        await Assert.That(trimmed.ErrorCodes.Single()).IsEqualTo(Diagnostics.CannotResign);
        await Assert.That(File.Exists(trimmed.Assembly)).IsFalse();
    }

    [Test]
    public async Task AKeyContainerIsNamedInTheError()
    {
        using var trimmed = Trimmed.Run("Signed", "netstandard2.0", _ => _.KeyContainer = "VS_KEY_1234");

        await Assert.That(trimmed.Succeeded).IsFalse();
        await Assert.That(trimmed.Engine.Errors.Single().Message!).Contains("VS_KEY_1234");
    }

    [Test]
    public async Task APublicSignedAssemblyNeedsNoKey()
    {
        using var trimmed = Trimmed.Run("PublicSigned", "netstandard2.0");

        await Assert.That(trimmed.Succeeded).IsTrue();
        await Assert.That(StrongName.IsSigned(await File.ReadAllBytesAsync(trimmed.Assembly))).IsFalse();
        await Assert.That(AssemblyName.GetAssemblyName(trimmed.Assembly).GetPublicKey()!.Length).IsGreaterThan(0);
    }

    [Test]
    public async Task AnExecutableKeepsItsEntryPoint()
    {
        using var trimmed = Trimmed.Run("EntryPoint", "net10.0", _ => _.RootEntryPoint = "true");

        await Assert.That(trimmed.Succeeded).IsTrue();
        await Assert.That(Checks.HasEntryPoint(trimmed.Assembly)).IsTrue();
        await Verify(trimmed.Removed)
            .Snapshot(
                """
                method System.String Greeter::Unused()
                method System.Void Program::.ctor()
                type Unused

                """);
    }

    [Test]
    public async Task WithoutItsEntryPointRootedAnExecutableKeepsNothing()
    {
        // Why the entry point is rooted as well: a top-level program has no public type at all.
        using var trimmed = Trimmed.Run("EntryPoint", "net10.0");

        await Assert.That(trimmed.Succeeded).IsFalse();
        await Assert.That(trimmed.ErrorCodes.Single()).IsEqualTo(Diagnostics.NothingReachable);
    }

    [Test]
    public async Task NothingReachableIsAnError()
    {
        // No public types, and its one friend ignored: the linker keeps nothing and writes nothing.
        using var trimmed = Trimmed.Run("InternalsOnly", "netstandard2.0");

        await Assert.That(trimmed.Succeeded).IsFalse();
        await Assert.That(trimmed.ErrorCodes.Single()).IsEqualTo(Diagnostics.NothingReachable);
        await Assert.That(trimmed.Engine.Errors.Single().Message!).Contains("SquashInternalsVisibleTo");
    }

    [Test]
    public async Task AnInternalsOnlyAssemblyWorksWhenFriendsAreHonored()
    {
        using var trimmed = Trimmed.Run("InternalsOnly", "netstandard2.0", _ => _.InternalsVisibleTo = "Honor");

        await Assert.That(trimmed.Succeeded).IsTrue();
        await Assert.That(AssemblyDump.Full(trimmed.Assembly)).Contains("internal InternalsOnly.Shared");
    }

    [Test]
    public async Task AMissingHostIsReportedBeforeAnythingMoves()
    {
        using var trimmed = Trimmed.Run("Scenarios", "netstandard2.0", _ => _.DotNetHost = Path.Combine(Path.GetTempPath(), "no-such-dotnet"));

        await Assert.That(trimmed.Succeeded).IsFalse();
        await Assert.That(trimmed.ErrorCodes.Single()).IsEqualTo(Diagnostics.HostNotFound);
        await Assert.That(File.Exists(trimmed.Assembly)).IsTrue();
    }

    [Test]
    public async Task AMissingLinkerIsReportedBeforeAnythingMoves()
    {
        using var trimmed = Trimmed.Run("Scenarios", "netstandard2.0", _ => _.LinkerDirectory = Path.GetTempPath());

        await Assert.That(trimmed.Succeeded).IsFalse();
        await Assert.That(trimmed.ErrorCodes.Single()).IsEqualTo(Diagnostics.InvalidInput);
        await Assert.That(File.Exists(trimmed.Assembly)).IsTrue();
    }

    /// <summary>
    /// A copy of the staged linker that a test can damage.
    /// </summary>
    static string CopyLinker(TempDirectory temp)
    {
        var directory = temp.Combine("linker");
        Directory.CreateDirectory(directory);
        foreach (var file in Directory.GetFiles(TestEnvironment.LinkerDirectory))
        {
            File.Copy(file, Path.Combine(directory, Path.GetFileName(file)));
        }

        return directory;
    }

    [Test]
    public async Task ARuntimeTheHostDoesNotHaveIsReported()
    {
        // What a build under an older SDK looks like: the linker asks for a runtime that is not
        // installed.
        using var temp = new TempDirectory();
        var linker = CopyLinker(temp);
        var config = Path.Combine(linker, "illink.runtimeconfig.json");
        var text = await File.ReadAllTextAsync(config);
        var major = AssemblyName.GetAssemblyName(Path.Combine(linker, "illink.dll")).Version!.Major;
        await File.WriteAllTextAsync(config, text.Replace($"\"{major}.0.0\"", "\"99.0.0\"").Replace($"net{major}.0", "net99.0"));

        using var trimmed = Trimmed.Run("Scenarios", "netstandard2.0", _ => _.LinkerDirectory = linker);

        await Assert.That(trimmed.Succeeded).IsFalse();
        await Assert.That(trimmed.ErrorCodes.Single()).IsEqualTo(Diagnostics.RuntimeMissing);
        await Assert.That(File.Exists(trimmed.Assembly)).IsFalse();
    }

    [Test]
    public async Task ALinkerThatFailsWithoutSayingWhyIsReported()
    {
        // Not an assembly at all, so the host fails before the linker can report anything.
        using var temp = new TempDirectory();
        var linker = CopyLinker(temp);
        await File.WriteAllTextAsync(Path.Combine(linker, "illink.dll"), "not an assembly");

        using var trimmed = Trimmed.Run("Scenarios", "netstandard2.0", _ => _.LinkerDirectory = linker);

        await Assert.That(trimmed.Succeeded).IsFalse();
        await Assert.That(trimmed.ErrorCodes.Single()).IsEqualTo(Diagnostics.LinkerFailed);
        await Assert.That(trimmed.Engine.Errors.Single().Message!).Contains("squash.rsp");
        await Assert.That(File.Exists(trimmed.Assembly)).IsFalse();
    }

    [Test]
    public async Task ALinkerFailureLeavesNoAssemblyBehind()
    {
        using var trimmed = Trimmed.Run(
            "Scenarios",
            "netstandard2.0",
            _ => _.RootDescriptors = [new TaskItem(Path.Combine(Path.GetTempPath(), "no-such-descriptor.xml"))]);

        await Assert.That(trimmed.Succeeded).IsFalse();
        await Assert.That(trimmed.Engine.Errors).IsNotEmpty();

        // Gone from where the build expects it, and kept where it can be looked at.
        await Assert.That(File.Exists(trimmed.Assembly)).IsFalse();
        await Assert.That(File.Exists(Path.Combine(trimmed.Directory, "in", "Scenarios.dll"))).IsTrue();
    }

    [Test]
    public async Task TheArgumentsAreKeptForRerunningByHand()
    {
        var trimmed = Trims.Scenarios("netstandard2.0");

        await Assert.That(trimmed.ResponseFile).StartsWith("-a \"Scenarios\" library");
        await Assert.That(trimmed.ResponseFile).Contains("--disable-opt ipconstprop");
    }

    [Test]
    public async Task TheOriginalIsKeptBesideTheResult()
    {
        var trimmed = Trims.Scenarios("netstandard2.0");
        var kept = await File.ReadAllBytesAsync(Path.Combine(trimmed.Directory, "in", "Scenarios.dll"));
        var original = await File.ReadAllBytesAsync(trimmed.Original);

        await Assert.That(kept.SequenceEqual(original)).IsTrue();
        await Assert.That(new FileInfo(trimmed.Assembly).Length).IsLessThan(kept.Length);
    }
}
