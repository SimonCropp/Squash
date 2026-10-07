/// <summary>
/// The package as a project consumes it: restored from a feed, wired in by its targets, and run by
/// a real build.
/// </summary>
public class LibraryTests
{
    static readonly string project = Path.Combine("Lib", "Lib.csproj");
    static readonly string[] frameworks = ["netstandard2.0", "net10.0"];

    static Dictionary<string, string> With(string name, string value = "true") =>
        new()
        {
            [name] = value
        };

    [Test]
    public async Task TrimsEveryTargetFramework()
    {
        var result = await Consumer.Build("Library", project);
        await Assert.That(result.Cli.ExitCode).IsEqualTo(0).Because(result.Cli.Combined);

        foreach (var framework in frameworks)
        {
            var assembly = result.Output("Lib", framework, "Lib.dll");
            await Assert.That(Consumer.HasType(assembly, "Lib.Api")).IsTrue();
            await Assert.That(Consumer.HasType(assembly, "Lib.UnusedInternal")).IsFalse();
            await Assert.That(await File.ReadAllTextAsync(result.Squash("Lib", framework, "removed.txt"))).Contains("type Lib.UnusedInternal");

            // What the compiler produced is kept for comparison.
            await Assert.That(Consumer.HasType(result.Squash("Lib", framework, Path.Combine("in", "Lib.dll")), "Lib.UnusedInternal")).IsTrue();
        }
    }

    [Test]
    public async Task RepeatBuildsNeitherCompileNorTrim()
    {
        var first = await Consumer.Build("Library", project);
        await Assert.That(first.Cli.ExitCode).IsEqualTo(0).Because(first.Cli.Combined);

        var stamp = first.Squash("Lib", "net10.0", "squash.stamp");
        var assembly = first.Output("Lib", "net10.0", "Lib.dll");
        var stamped = File.GetLastWriteTimeUtc(stamp);
        var written = File.GetLastWriteTimeUtc(assembly);

        // More than one repeat: a file the build forgets to claim is deleted by the second build and
        // recreated by the third, so the mistake only shows on alternate builds.
        for (var build = 2; build <= 4; build++)
        {
            var next = await Consumer.Rebuild(first.Work, project);
            await Assert.That(next.Cli.ExitCode).IsEqualTo(0).Because(next.Cli.Combined);
            await Assert.That(File.GetLastWriteTimeUtc(stamp)).IsEqualTo(stamped).Because($"build {build}");
            await Assert.That(File.GetLastWriteTimeUtc(assembly)).IsEqualTo(written).Because($"build {build}");
            await Assert.That(File.Exists(first.Squash("Lib", "net10.0", "friends.txt"))).IsTrue().Because($"build {build}");
            await Assert.That(File.Exists(first.Squash("Lib", "net10.0", "removed.txt"))).IsTrue().Because($"build {build}");
        }
    }

    [Test]
    public async Task AnEditTrimsAgain()
    {
        var first = await Consumer.Build("Library", project);
        await Assert.That(first.Cli.ExitCode).IsEqualTo(0).Because(first.Cli.Combined);

        await File.AppendAllTextAsync(Path.Combine(first.Work, "Lib", "Api.cs"), "\nclass AddedLater\n{\n}\n");
        var second = await Consumer.Rebuild(first.Work, project);

        await Assert.That(second.Cli.ExitCode).IsEqualTo(0).Because(second.Cli.Combined);
        await Assert.That(await File.ReadAllTextAsync(second.Squash("Lib", "net10.0", "removed.txt"))).Contains("type Lib.AddedLater");
        await Assert.That(Consumer.HasType(second.Output("Lib", "net10.0", "Lib.dll"), "Lib.AddedLater")).IsFalse();
    }

    [Test]
    public async Task ASettingChangeTrimsAgain()
    {
        var first = await Consumer.Build("Library", project);
        await Assert.That(first.Cli.ExitCode).IsEqualTo(0).Because(first.Cli.Combined);
        await Assert.That(File.Exists(first.Squash("Lib", "net10.0", "friends.txt"))).IsTrue();

        // No file changed. Only the recorded settings tell the build it has work to do.
        var second = await Consumer.Rebuild(first.Work, project, With("SquashInternalsVisibleTo", "Honor"));

        await Assert.That(second.Cli.ExitCode).IsEqualTo(0).Because(second.Cli.Combined);
        await Assert.That(Consumer.HasType(second.Output("Lib", "net10.0", "Lib.dll"), "Lib.UnusedInternal")).IsTrue();
        await Assert.That(File.Exists(second.Squash("Lib", "net10.0", "friends.txt"))).IsFalse();
    }

    [Test]
    public async Task ARootDescriptorKeepsATypeAndEditingItTrimsAgain()
    {
        var first = await Consumer.Build("Library", project, With("Roots"));
        await Assert.That(first.Cli.ExitCode).IsEqualTo(0).Because(first.Cli.Combined);
        await Assert.That(Consumer.HasType(first.Output("Lib", "net10.0", "Lib.dll"), "Lib.UnusedInternal")).IsTrue();

        await File.WriteAllTextAsync(Path.Combine(first.Work, "Lib", "roots.xml"), "<linker>\n</linker>\n");
        var second = await Consumer.Rebuild(first.Work, project, With("Roots"));

        await Assert.That(second.Cli.ExitCode).IsEqualTo(0).Because(second.Cli.Combined);
        await Assert.That(Consumer.HasType(second.Output("Lib", "net10.0", "Lib.dll"), "Lib.UnusedInternal")).IsFalse();
    }

    [Test]
    public async Task DisablingAfterATrimLeavesAnUntrimmedAssembly()
    {
        var first = await Consumer.Build("Library", project);
        await Assert.That(first.Cli.ExitCode).IsEqualTo(0).Because(first.Cli.Combined);

        var second = await Consumer.Rebuild(first.Work, project, With("SquashEnabled", "false"));

        await Assert.That(second.Cli.ExitCode).IsEqualTo(0).Because(second.Cli.Combined);
        await Assert.That(Consumer.HasType(second.Output("Lib", "net10.0", "Lib.dll"), "Lib.UnusedInternal")).IsTrue();

        // Nothing left over that describes an assembly which is no longer trimmed.
        var left = Directory
            .GetFiles(Path.GetDirectoryName(second.Squash("Lib", "net10.0", "inputs.txt"))!, "*", SearchOption.AllDirectories)
            .Select(Path.GetFileName)
            .ToList();
        await Assert.That(left.SequenceEqual(["inputs.txt"])).IsTrue().Because(string.Join(", ", left));
    }

    [Test]
    public async Task DebugIsNotTrimmed()
    {
        var result = await Consumer.Build("Library", project, configuration: "Debug");

        await Assert.That(result.Cli.ExitCode).IsEqualTo(0).Because(result.Cli.Combined);
        await Assert.That(Consumer.HasType(result.Output("Lib", "net10.0", "Lib.dll", "Debug"), "Lib.UnusedInternal")).IsTrue();
        await Assert.That(File.Exists(result.Squash("Lib", "net10.0", "squash.stamp", "Debug"))).IsFalse();
    }

    [Test]
    public async Task DebugIsTrimmedWhenAsked()
    {
        var result = await Consumer.Build("Library", project, With("SquashEnabled"), "Debug");

        await Assert.That(result.Cli.ExitCode).IsEqualTo(0).Because(result.Cli.Combined);
        await Assert.That(Consumer.HasType(result.Output("Lib", "net10.0", "Lib.dll", "Debug"), "Lib.UnusedInternal")).IsFalse();
    }

    [Test]
    public async Task AnApplicationUsingTheLibraryRuns()
    {
        var result = await Consumer.Build("Library", Path.Combine("App", "App.csproj"));
        await Assert.That(result.Cli.ExitCode).IsEqualTo(0).Because(result.Cli.Combined);

        // The copy beside the application is the trimmed one.
        await Assert.That(Consumer.HasType(result.Output("App", "net10.0", "Lib.dll"), "Lib.UnusedInternal")).IsFalse();

        var run = await CliRunner.Run("dotnet", [result.Output("App", "net10.0", "App.dll")], result.Work);
        await Assert.That(run.ExitCode).IsEqualTo(0).Because(run.Combined);
        await Assert.That(run.Stdout.Trim()).IsEqualTo("kept");
    }

    [Test]
    public async Task ALinkerFailureFailsTheBuildAndTheNextBuildRecovers()
    {
        var failed = await Consumer.Build("Library", project, With("BreakDescriptor"));

        await Assert.That(failed.Cli.ExitCode).IsNotEqualTo(0);
        await Assert.That(failed.Cli.Combined).Contains("missing.xml");

        // Not where the build would pick it up and ship it untrimmed.
        await Assert.That(File.Exists(failed.Intermediate("Lib", "net10.0", "Lib.dll"))).IsFalse();

        var recovered = await Consumer.Rebuild(failed.Work, project);
        await Assert.That(recovered.Cli.ExitCode).IsEqualTo(0).Because(recovered.Cli.Combined);
        await Assert.That(Consumer.HasType(recovered.Output("Lib", "net10.0", "Lib.dll"), "Lib.UnusedInternal")).IsFalse();
    }

    [Test]
    public async Task LinkerWarningsAreBuildWarnings()
    {
        var result = await Consumer.Build("Library", project, With("Reflective"));

        await Assert.That(result.Cli.ExitCode).IsEqualTo(0).Because(result.Cli.Combined);
        await Assert.That(result.Cli.Combined).Contains("warning IL2057");
        await Assert.That(result.Cli.Combined).Contains("Reflective.cs");
    }

    [Test]
    public async Task NoWarnSilencesALinkerWarning()
    {
        var properties = With("Reflective");
        properties["NoWarn"] = "IL2057";
        var result = await Consumer.Build("Library", project, properties);

        await Assert.That(result.Cli.ExitCode).IsEqualTo(0).Because(result.Cli.Combined);
        await Assert.That(result.Cli.Combined).DoesNotContain("IL2057");
    }

    [Test]
    public async Task WarningsAsErrorsFailTheBuildEveryTime()
    {
        var properties = With("Reflective");
        properties["TreatWarningsAsErrors"] = "true";
        var first = await Consumer.Build("Library", project, properties);

        await Assert.That(first.Cli.ExitCode).IsNotEqualTo(0);
        await Assert.That(first.Cli.Combined).Contains("error IL2057");

        // The linker had already written a trimmed assembly when it reported the error. Had that
        // been put back, this build would find everything up to date and pass.
        var second = await Consumer.Rebuild(first.Work, project, properties);
        await Assert.That(second.Cli.ExitCode).IsNotEqualTo(0);
        await Assert.That(second.Cli.Combined).Contains("error IL2057");
    }

    [Test]
    public async Task NoWarnSilencesAWarningTheLinkerWouldPromote()
    {
        var properties = With("Reflective");
        properties["TreatWarningsAsErrors"] = "true";

        // A list, as a project has: the linker is given only its own code.
        properties["NoWarn"] = "CS1591%3BIL2057";
        var result = await Consumer.Build("Library", project, properties);

        await Assert.That(result.Cli.ExitCode).IsEqualTo(0).Because(result.Cli.Combined);
        await Assert.That(result.Cli.Combined).DoesNotContain("IL2057");
        foreach (var framework in frameworks)
        {
            var arguments = await File.ReadAllLinesAsync(result.Squash("Lib", framework, "squash.rsp"));
            await Assert.That(arguments).Contains("--nowarn IL2057");
        }
    }

    [Test]
    public async Task WarningsNotAsErrorsKeepsALinkerWarningAWarning()
    {
        var properties = With("Reflective");
        properties["TreatWarningsAsErrors"] = "true";
        properties["WarningsNotAsErrors"] = "CS0618%3BIL2057";
        var result = await Consumer.Build("Library", project, properties);

        await Assert.That(result.Cli.ExitCode).IsEqualTo(0).Because(result.Cli.Combined);
        await Assert.That(result.Cli.Combined).Contains("warning IL2057");
        await Assert.That(result.Cli.Combined).DoesNotContain("error IL2057");
        foreach (var framework in frameworks)
        {
            var arguments = await File.ReadAllLinesAsync(result.Squash("Lib", framework, "squash.rsp"));
            await Assert.That(arguments).Contains("--warnaserror- IL2057");
        }
    }

    [Test]
    public async Task APathWithSpacesAndOtherAlphabets()
    {
        var work = Consumer.Prepare("Library", "with space ünïcödé 日本語");
        var result = await Consumer.Rebuild(work, project);

        await Assert.That(result.Cli.ExitCode).IsEqualTo(0).Because(result.Cli.Combined);
        await Assert.That(Consumer.HasType(result.Output("Lib", "net10.0", "Lib.dll"), "Lib.UnusedInternal")).IsFalse();
    }

    [Test]
    public async Task ADesignTimeBuildWritesNothing()
    {
        var work = Consumer.Prepare("Library");
        var restore = await Consumer.Dotnet(work, "restore", project);
        await Assert.That(restore.Cli.ExitCode).IsEqualTo(0).Because(restore.Cli.Combined);

        // What an IDE runs to learn about a project without building it: the compile target, with
        // the compiler told only to report its command line.
        var design = await Consumer.Dotnet(
            work,
            "msbuild",
            project,
            new Dictionary<string, string>
            {
                ["Configuration"] = "Release",
                ["TargetFramework"] = "net10.0",
                ["DesignTimeBuild"] = "true",
                ["BuildingProject"] = "false",
                ["SkipCompilerExecution"] = "true",
                ["ProvideCommandLineArgs"] = "true"
            },
            ["-t:Compile"]);

        await Assert.That(design.Cli.ExitCode).IsEqualTo(0).Because(design.Cli.Combined);
        await Assert.That(Directory.Exists(Path.GetDirectoryName(design.Squash("Lib", "net10.0", "inputs.txt"))!)).IsFalse();
    }
}
