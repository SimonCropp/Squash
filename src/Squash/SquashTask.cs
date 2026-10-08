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
    public string InternalNamespacesToKeep { get; set; } = "";
    public string DocumentationFile { get; set; } = "";
    public string TrimDocumentation { get; set; } = "";

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
        var input = Path.Combine(IntermediateDirectory, ResponseFile.InputDirectory);
        var outputDirectory = Path.Combine(IntermediateDirectory, ResponseFile.OutputDirectory);
        var friendsFile = Path.Combine(IntermediateDirectory, "friends.txt");
        var keptNamespacesFile = Path.Combine(IntermediateDirectory, "kept-namespaces.xml");
        var removedDocumentationFile = Path.Combine(IntermediateDirectory, "removed-documentation.txt");
        var inputAssembly = Path.Combine(input, AssemblyName + extension);
        var inputSymbols = Path.Combine(input, AssemblyName + ".pdb");
        var outputAssembly = Path.Combine(outputDirectory, AssemblyName + extension);
        var outputSymbols = Path.Combine(outputDirectory, AssemblyName + ".pdb");
        responseFile = Path.Combine(IntermediateDirectory, "squash.rsp");

        Directory.CreateDirectory(IntermediateDirectory);
        Files.RecreateDirectory(input);
        Files.RecreateDirectory(outputDirectory);
        File.Delete(friendsFile);
        File.Delete(keptNamespacesFile);
        File.Delete(removedDocumentationFile);

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
        var descriptors = RootDescriptors
            .Select(_ => _.GetMetadata("FullPath"))
            .ToList();

        // Where friends are honored every internal is kept without this.
        var keptNamespaces = new List<string>();
        if (ignoreFriends)
        {
            keptNamespaces = WriteKeptNamespaces(inputAssembly, keptNamespacesFile);
        }

        if (keptNamespaces.Count > 0)
        {
            descriptors.Add(keptNamespacesFile);
        }

        // The documentation stays where it is while the linker runs: it is only read, and only
        // once the trim has succeeded.
        var trimDocumentation = Is(TrimDocumentation, "true") &&
                                DocumentationFile.Length > 0 &&
                                File.Exists(DocumentationFile);
        var removedDocumentation = "";
        if (trimDocumentation)
        {
            removedDocumentation = Path.GetFileName(removedDocumentationFile);
        }

        var request = new SquashRequest
        {
            AssemblyName = AssemblyName,
            Input = inputAssembly,
            References = References
                .Select(_ => _.GetMetadata("FullPath"))
                .ToList(),
            RootDescriptors = descriptors,
            OutputDirectory = outputDirectory,
            StepsAssembly = Path.Combine(LinkerDirectory, "Squash.Steps.dll"),
            // Bare names, resolved by the steps against the working directory: the linker splits
            // custom data on every '=', so a path containing one would be rejected.
            FriendsFile = Path.GetFileName(friendsFile),
            ReportFile = "removed.txt",
            RemovedDocumentationFile = removedDocumentation,
            IgnoreInternalsVisibleTo = ignoreFriends,
            KeepDataShape = Is(Preserve, "DataShape"),
            WarningsAsErrors = Is(TreatWarningsAsErrors, "true"),
            NoWarn = ResponseFile.LinkerCodes(NoWarn),
            WarningsNotAsErrors = ResponseFile.LinkerCodes(WarningsNotAsErrors),
            RootEntryPoint = Is(RootEntryPoint, "true"),
            ExtraArgs = ExtraArgs
        };

        // No byte order mark: the linker would read it as part of the first argument.
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
                $"The linker wrote no '{AssemblyName}{extension}': nothing in it is reachable from its public types, so nothing was kept. If it is used through InternalsVisibleTo, set Squash_InternalsVisibleTo to Honor; otherwise set Squash_Enabled to false for this project.");
        }

        if (hasSymbols &&
            !File.Exists(outputSymbols))
        {
            throw new SquashException(
                Diagnostics.SymbolsNotRewritten,
                $"The linker could not read '{Path.GetFileName(SymbolsFile)}', so the trimmed assembly has no matching symbols. Portable and embedded pdbs are supported everywhere; Windows pdbs only on Windows.");
        }

        Resign(inputAssembly, outputAssembly);

        // Worked out before anything is moved back, so that nothing is replaced unless all of it
        // can be.
        TrimmedDocumentation? documentation = null;
        if (trimDocumentation)
        {
            documentation = ReadTrimmedDocumentation(removedDocumentationFile);
        }

        var before = new FileInfo(inputAssembly).Length;
        var after = new FileInfo(outputAssembly).Length;

        Files.Move(outputAssembly, AssemblyFile);
        if (hasSymbols)
        {
            Files.Move(outputSymbols, SymbolsFile);
        }

        Log.LogMessage(
            MessageImportance.Normal,
            $"Squash: {Path.GetFileName(AssemblyFile)} {Number(before)} -> {Number(after)} bytes.");

        if (documentation != null)
        {
            ReplaceDocumentation(documentation, input, outputDirectory);
        }

        if (File.Exists(friendsFile))
        {
            var friends = string.Join(", ", File.ReadAllLines(friendsFile));
            var kept = "";
            if (keptNamespaces.Count > 0)
            {
                kept = $", except in {string.Join(", ", keptNamespaces)}, which Squash_InternalNamespacesToKeep keeps whole";
            }

            Report(
                new(
                    Diagnostics.FriendsIgnored,
                    Severity.Message,
                    $"Internal types and members of '{AssemblyName}' that only {friends} would use have been removed{kept}. Set Squash_InternalsVisibleTo to Honor to keep every internal."));
        }
    }

    /// <summary>
    /// The namespaces whose internals friends may use, as a root descriptor that keeps each of them
    /// whole. The setting gives prefixes and a descriptor takes exact names, so the names come from
    /// the assembly the compiler has just produced.
    /// </summary>
    List<string> WriteKeptNamespaces(string inputAssembly, string file)
    {
        var prefixes = Namespaces.Prefixes(InternalNamespacesToKeep);
        if (prefixes.Count == 0)
        {
            return [];
        }

        var declared = Namespaces.Read(File.ReadAllBytes(inputAssembly));
        foreach (var prefix in prefixes)
        {
            if (declared.Any(_ => Namespaces.Matches(_, prefix)))
            {
                continue;
            }

            Report(
                new(
                    Diagnostics.NamespaceToKeepNotFound,
                    Severity.Warning,
                    $"Squash_InternalNamespacesToKeep names '{prefix}', and no namespace of '{AssemblyName}' starts with that, so it keeps nothing. The assembly has types in: {string.Join(", ", declared)}."));
        }

        var matched = Namespaces.Matching(declared, prefixes);
        if (matched.Count > 0)
        {
            File.WriteAllText(file, Namespaces.Descriptor(AssemblyName, matched), new UTF8Encoding(false));
        }

        return matched;
    }

    /// <summary>
    /// The documentation file without the entries for what the linker removed, or null where there
    /// is nothing to take out. The linker step lists the names; a name it did not list stays.
    /// </summary>
    TrimmedDocumentation? ReadTrimmedDocumentation(string removedFile)
    {
        var removed = new HashSet<string>(File.ReadAllLines(removedFile), StringComparer.Ordinal);
        try
        {
            var trimmed = Documentation.Trim(File.ReadAllBytes(DocumentationFile), removed);
            if (trimmed.Removed == 0)
            {
                return null;
            }

            return trimmed;
        }
        catch (InvalidDataException exception)
        {
            Report(
                new(
                    Diagnostics.DocumentationNotTrimmed,
                    Severity.Warning,
                    $"'{DocumentationFile}' is not the XML the compiler writes ({exception.Message}), so it has been left as it was and still documents members that have been removed. Set Squash_TrimDocumentation to false to leave the file alone."));
            return null;
        }
    }

    /// <summary>
    /// What the compiler wrote goes beside the other originals, and the trimmed file takes its
    /// place.
    /// </summary>
    void ReplaceDocumentation(TrimmedDocumentation documentation, string input, string outputDirectory)
    {
        var name = Path.GetFileName(DocumentationFile);
        var before = new FileInfo(DocumentationFile).Length;
        var trimmed = Path.Combine(outputDirectory, name);
        File.WriteAllBytes(trimmed, documentation.Content);
        Files.Move(DocumentationFile, Path.Combine(input, name));
        Files.Move(trimmed, DocumentationFile);

        Log.LogMessage(
            MessageImportance.Normal,
            $"Squash: {name} {Number(before)} -> {Number(documentation.Content.Length)} bytes, {Number(documentation.Removed)} entries removed.");
    }

    static string Number(long count) =>
        count.ToString("N0", CultureInfo.InvariantCulture);

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
