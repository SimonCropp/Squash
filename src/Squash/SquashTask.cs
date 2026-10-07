/// <summary>
/// Runs the bundled IL linker over one assembly and puts the trimmed result back in its place. In
/// the global namespace so Squash.targets can name it unqualified.
/// </summary>
public class SquashTask :
    ToolTask
{
    // What the dotnet host returns when no installed runtime can run the application. Only the low
    // byte survives as an exit code outside Windows.
    const int frameworkMissing = unchecked((int)0x80008096);
    const int frameworkMissingLowByte = 0x96;

    public string AssemblyFile { get; set; } = "";
    public string AssemblyName { get; set; } = "";
    public string SymbolsFile { get; set; } = "";
    public string IntermediateDirectory { get; set; } = "";
    public ITaskItem[] References { get; set; } = [];
    public ITaskItem[] RootDescriptors { get; set; } = [];
    public string LinkerDirectory { get; set; } = "";
    public string DotNetHost { get; set; } = "";
    public string InternalsVisibleTo { get; set; } = "";
    public string Preserve { get; set; } = "";
    public string TreatWarningsAsErrors { get; set; } = "";
    public string NoWarn { get; set; } = "";
    public string WarningsNotAsErrors { get; set; } = "";
    public string RootEntryPoint { get; set; } = "";
    public string ExtraArgs { get; set; } = "";
    public string KeyFile { get; set; } = "";
    public string KeyContainer { get; set; } = "";
    public ITaskItem[] FriendAssemblies { get; set; } = [];
    public string FriendRootsFile { get; set; } = "";
    public string UpdateFriendRoots { get; set; } = "";

    // The last lines the linker wrote, for the case where it fails without a diagnostic of its own.
    readonly Queue<string> output = new();
    string responseFile = "";

    protected override string ToolName => Path.GetFileName(DotNetHost);

    protected override string GenerateFullPathToTool() => DotNetHost;

    protected override MessageImportance StandardErrorLoggingImportance => MessageImportance.High;

    protected override string GetWorkingDirectory() => IntermediateDirectory;

    // exec, so the host runs the assembly without first trying to read it as an SDK command. The
    // arguments are in a file Squash writes and keeps, so a failed link can be rerun by hand.
    protected override string GenerateCommandLineCommands() =>
        $"exec {ResponseFile.Quote(Path.Combine(LinkerDirectory, "illink.dll"))} {ResponseFile.Quote("@" + responseFile)}";

    public override bool Execute()
    {
        try
        {
            Run();
        }
        catch (SquashException exception)
        {
            Report(new(exception.Code, Severity.Error, exception.Message));
        }
        catch (Exception exception)
        {
            Report(new(Diagnostics.Failed, Severity.Error, exception.ToString()));
        }

        return !Log.HasLoggedErrors;
    }

    void Run()
    {
        Validate();

        var extension = Path.GetExtension(AssemblyFile);
        var input = Path.Combine(IntermediateDirectory, "in");
        var outputDirectory = Path.Combine(IntermediateDirectory, "out");
        var friendsFile = Path.Combine(IntermediateDirectory, "friends.txt");
        var inputAssembly = Path.Combine(input, AssemblyName + extension);
        var inputSymbols = Path.Combine(input, AssemblyName + ".pdb");
        var outputAssembly = Path.Combine(outputDirectory, AssemblyName + extension);
        var outputSymbols = Path.Combine(outputDirectory, AssemblyName + ".pdb");

        Directory.CreateDirectory(IntermediateDirectory);
        Files.RecreateDirectory(input);
        Files.RecreateDirectory(outputDirectory);
        File.Delete(friendsFile);

        // Moved, not copied. If anything below fails the compiler's output is gone from where the
        // build expects it, so the next build compiles again and cannot ship an untrimmed assembly.
        // Renamed to the assembly name, which is what the linker resolves a root by.
        Files.Move(AssemblyFile, inputAssembly);
        var hasSymbols = SymbolsFile.Length > 0 &&
                         File.Exists(SymbolsFile);
        if (hasSymbols)
        {
            Files.Move(SymbolsFile, inputSymbols);
        }

        var ignoreFriends = !Is(InternalsVisibleTo, "Honor");
        var references = References
            .Select(_ => _.GetMetadata("FullPath"))
            .ToList();
        var steps = Path.Combine(LinkerDirectory, "Squash.Steps.dll");

        // Before the trim, which reads what this writes.
        if (ignoreFriends &&
            Is(UpdateFriendRoots, "true") &&
            !WriteFriendRoots(inputAssembly, references, outputDirectory, steps))
        {
            return;
        }

        var descriptors = RootDescriptors
            .Select(_ => _.GetMetadata("FullPath"))
            .ToList();

        // What the friends use, as it was last written. Where friends are honored every internal is
        // kept without it.
        var friendRoots = ignoreFriends &&
                          File.Exists(FriendRootsFile);
        if (friendRoots)
        {
            descriptors.Add(Path.GetFullPath(FriendRootsFile));
        }

        var request = new SquashRequest
        {
            AssemblyName = AssemblyName,
            Input = inputAssembly,
            References = references,
            RootDescriptors = descriptors,
            OutputDirectory = outputDirectory,
            StepsAssembly = steps,
            // Bare names, resolved by the steps against the working directory: the linker splits
            // custom data on every '=', so a path containing one would be rejected.
            FriendsFile = Path.GetFileName(friendsFile),
            ReportFile = "removed.txt",
            IgnoreInternalsVisibleTo = ignoreFriends,
            KeepDataShape = Is(Preserve, "DataShape"),
            WarningsAsErrors = Is(TreatWarningsAsErrors, "true"),
            NoWarn = ResponseFile.LinkerCodes(NoWarn),
            WarningsNotAsErrors = ResponseFile.LinkerCodes(WarningsNotAsErrors),
            RootEntryPoint = Is(RootEntryPoint, "true"),
            ExtraArgs = ExtraArgs
        };

        // No byte order mark: the linker would read it as part of the first argument.
        responseFile = Path.Combine(IntermediateDirectory, "squash.rsp");
        File.WriteAllLines(responseFile, ResponseFile.Build(request), new UTF8Encoding(false));

        // The linker writes its output before it reports warnings promoted to errors, so the exit
        // code and the log decide, not whether a file appeared.
        if (!base.Execute() ||
            Log.HasLoggedErrors)
        {
            return;
        }

        if (!File.Exists(outputAssembly))
        {
            throw new SquashException(
                Diagnostics.NothingReachable,
                $"The linker wrote no '{AssemblyName}{extension}': nothing in it is reachable from its public types, so nothing was kept. If it is used through InternalsVisibleTo, set SquashInternalsVisibleTo to Honor; otherwise set SquashEnabled to false for this project.");
        }

        if (hasSymbols &&
            !File.Exists(outputSymbols))
        {
            throw new SquashException(
                Diagnostics.SymbolsNotRewritten,
                $"The linker could not read '{Path.GetFileName(SymbolsFile)}', so the trimmed assembly has no matching symbols. Portable and embedded pdbs are supported everywhere; Windows pdbs only on Windows.");
        }

        Resign(inputAssembly, outputAssembly);

        var before = new FileInfo(inputAssembly).Length;
        var after = new FileInfo(outputAssembly).Length;

        Files.Move(outputAssembly, AssemblyFile);
        if (hasSymbols)
        {
            Files.Move(outputSymbols, SymbolsFile);
        }

        Log.LogMessage(
            MessageImportance.Normal,
            $"Squash: {Path.GetFileName(AssemblyFile)} {before.ToString("N0", CultureInfo.InvariantCulture)} -> {after.ToString("N0", CultureInfo.InvariantCulture)} bytes.");

        if (File.Exists(friendsFile))
        {
            var friends = string.Join(", ", File.ReadAllLines(friendsFile));
            var body = $"Internal types and members of '{AssemblyName}' that only {friends} would use have been removed. Set SquashInternalsVisibleTo to Honor to keep every internal.";
            if (friendRoots)
            {
                body = $"Internal types and members of '{AssemblyName}' are kept for {friends} only where '{FriendRootsFile}' names them. Build with SquashFriendRoots set to Update to write that file again, or set SquashInternalsVisibleTo to Honor to keep every internal.";
            }

            Report(new(Diagnostics.FriendsIgnored, Severity.Message, body));
        }
    }

    /// <summary>
    /// Runs the linker once without linking anything, for the step that reads the friend assemblies,
    /// and puts the descriptor that step writes where the project keeps it.
    /// </summary>
    bool WriteFriendRoots(string inputAssembly, List<string> references, string outputDirectory, string steps)
    {
        if (FriendRootsFile.Length == 0)
        {
            throw new SquashException(Diagnostics.InvalidInput, "No file was given to write the friend roots to.");
        }

        var collected = Path.Combine(IntermediateDirectory, "friend-roots.xml");
        var summary = Path.ChangeExtension(collected, ".txt");
        var list = Path.Combine(IntermediateDirectory, "friend-assemblies.txt");
        var noRoots = Path.Combine(IntermediateDirectory, "no-roots.xml");
        var encoding = new UTF8Encoding(false);
        File.Delete(collected);
        File.Delete(summary);
        File.WriteAllLines(
            list,
            FriendAssemblies
                .Select(_ => _.GetMetadata("FullPath"))
                .OrderBy(_ => _, StringComparer.Ordinal),
            encoding);
        File.WriteAllText(noRoots, "<linker>\n</linker>\n", encoding);

        var request = new SquashRequest
        {
            AssemblyName = AssemblyName,
            Input = inputAssembly,
            References = references,
            OutputDirectory = outputDirectory,
            StepsAssembly = steps,
            // Bare names: the linker splits custom data on every '='.
            FriendAssembliesFile = Path.GetFileName(list),
            FriendRootsFile = Path.GetFileName(collected)
        };

        responseFile = Path.Combine(IntermediateDirectory, "friend-roots.rsp");
        File.WriteAllLines(responseFile, ResponseFile.FriendRoots(request, noRoots), encoding);

        if (!base.Execute() ||
            Log.HasLoggedErrors)
        {
            return false;
        }

        output.Clear();
        if (!File.Exists(collected))
        {
            throw new SquashException(
                Diagnostics.LinkerFailed,
                $"The linker wrote no friend roots. Its arguments are in '{responseFile}'.");
        }

        Files.WriteIfDifferent(FriendRootsFile, File.ReadAllText(collected));
        ReportFriendRoots(File.ReadAllLines(summary));
        return true;
    }

    void ReportFriendRoots(string[] summary)
    {
        var read = Tagged(summary, "read");
        var missing = Tagged(summary, "missing");
        if (read.Count == 0 &&
            missing.Count == 0)
        {
            Log.LogMessage(MessageImportance.High, $"Squash: '{AssemblyName}' names no friends in InternalsVisibleTo, so '{FriendRootsFile}' is empty.");
            return;
        }

        if (read.Count == 0)
        {
            Report(
                new(
                    Diagnostics.FriendsNotFound,
                    Severity.Warning,
                    $"'{AssemblyName}' names {string.Join(", ", missing)} in InternalsVisibleTo, and none of them is among the {FriendAssemblies.Length} assemblies searched, so '{FriendRootsFile}' keeps nothing for them. Build them first, with SquashInternalsVisibleTo set to Honor so that they compile, then build this project again with SquashFriendRoots set to Update."));
            return;
        }

        var message = $"Squash: wrote '{FriendRootsFile}' from {read.Count} friend assemblies.";
        if (missing.Count > 0)
        {
            message += $" Not found: {string.Join(", ", missing)}.";
        }

        Log.LogMessage(MessageImportance.High, message);
    }

    static List<string> Tagged(string[] lines, string tag) =>
        lines
            .Where(_ => _.StartsWith(tag + "\t", StringComparison.Ordinal))
            .Select(_ => _.Substring(tag.Length + 1))
            .ToList();

    void Validate()
    {
        if (!File.Exists(AssemblyFile))
        {
            throw new SquashException(Diagnostics.InvalidInput, $"The assembly '{AssemblyFile}' does not exist.");
        }

        // Both would break the linker's own parsing of its arguments.
        if (AssemblyName.Length == 0 ||
            AssemblyName.IndexOfAny(['=', '"']) >= 0)
        {
            throw new SquashException(Diagnostics.InvalidInput, $"'{AssemblyName}' cannot be used as the assembly name.");
        }

        if (IntermediateDirectory.Length == 0)
        {
            throw new SquashException(Diagnostics.InvalidInput, "No intermediate directory was given.");
        }

        if (!File.Exists(Path.Combine(LinkerDirectory, "illink.dll")))
        {
            throw new SquashException(Diagnostics.InvalidInput, $"The linker was not found in '{LinkerDirectory}'.");
        }

        if (!File.Exists(DotNetHost))
        {
            throw new SquashException(
                Diagnostics.HostNotFound,
                $"The linker runs on .NET, and no dotnet host was found at '{DotNetHost}'.");
        }
    }

    void Resign(string inputAssembly, string outputAssembly)
    {
        if (!StrongName.IsSigned(File.ReadAllBytes(inputAssembly)))
        {
            return;
        }

        if (KeyFile.Length == 0 ||
            !File.Exists(KeyFile))
        {
            if (KeyContainer.Length > 0)
            {
                throw new SquashException(
                    Diagnostics.CannotResign,
                    $"'{AssemblyName}' is signed with the key container '{KeyContainer}', which also covers a .pfx key. Only a .snk key file can sign it again after trimming.");
            }

            throw new SquashException(
                Diagnostics.CannotResign,
                $"'{AssemblyName}' is strong-named, and the key file to sign it again after trimming was not found: '{KeyFile}'.");
        }

        var publicKey = System.Reflection.AssemblyName.GetAssemblyName(outputAssembly).GetPublicKey() ?? [];
        var image = File.ReadAllBytes(outputAssembly);
        StrongName.Sign(image, publicKey, File.ReadAllBytes(KeyFile));
        if (!StrongName.Verify(image, publicKey))
        {
            throw new SquashException(Diagnostics.CannotResign, $"The new signature of '{AssemblyName}' does not verify.");
        }

        File.WriteAllBytes(outputAssembly, image);
    }

    // ToolTask announces every command line at high importance. That is one long line per target
    // framework on every compile, for a path that is also in the binary log.
    protected override void LogToolCommand(string message) =>
        Log.LogCommandLine(MessageImportance.Low, message);

    protected override void LogEventsFromTextOutput(string singleLine, MessageImportance messageImportance)
    {
        output.Enqueue(singleLine);
        if (output.Count > 20)
        {
            output.Dequeue();
        }

        base.LogEventsFromTextOutput(singleLine, messageImportance);
    }

    protected override bool HandleTaskExecutionErrors()
    {
        // The linker has already said what is wrong, with a code of its own.
        if (Log.HasLoggedErrors)
        {
            return false;
        }

        var text = string.Join(Environment.NewLine, output);
        if (ExitCode == frameworkMissing ||
            ExitCode == frameworkMissingLowByte ||
            text.Contains("You must install or update .NET"))
        {
            Report(
                new(
                    Diagnostics.RuntimeMissing,
                    Severity.Error,
                    $"The bundled linker needs a .NET runtime that '{DotNetHost}' does not have. Build with a newer .NET SDK, or install the runtime. {text}"));
            return false;
        }

        Report(new(Diagnostics.LinkerFailed, Severity.Error, $"The linker exited with code {ExitCode}. Its arguments are in '{responseFile}'. {text}"));
        return false;
    }

    void Report(Diagnostic diagnostic)
    {
        var message = Diagnostics.Render(diagnostic.Code, diagnostic.Body);
        switch (diagnostic.Severity)
        {
            case Severity.Error:
                Log.LogError(Diagnostics.Subcategory, diagnostic.Code, null, null, 0, 0, 0, 0, message);
                return;
            case Severity.Warning:
                Log.LogWarning(Diagnostics.Subcategory, diagnostic.Code, null, null, 0, 0, 0, 0, message);
                return;
            default:
                Log.LogMessage(Diagnostics.Subcategory, diagnostic.Code, null, null, 0, 0, 0, 0, MessageImportance.High, message);
                return;
        }
    }

    static bool Is(string value, string expected) =>
        string.Equals(value.Trim(), expected, StringComparison.OrdinalIgnoreCase);
}
