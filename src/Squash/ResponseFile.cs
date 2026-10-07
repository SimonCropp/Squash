namespace Squash;

/// <summary>
/// The linker's arguments, one per line. On the essential path this text is all Squash knows about
/// the linker: the command line is the one contract that has survived every linker release, where
/// its types and its own MSBuild task have not.
/// </summary>
public static class ResponseFile
{
    // Squash.Steps reads these. CustomData there repeats them, as that assembly cannot be referenced
    // from here.
    public const string RootAssemblyKey = "Squash.RootAssembly";
    public const string FriendsFileKey = "Squash.FriendsFile";
    public const string ReportFileKey = "Squash.ReportFile";

    public static List<string> Build(SquashRequest request)
    {
        var name = Quote(request.AssemblyName);
        var lines = new List<string>
        {
            // The visible surface is the root. A name, not a path: newer linkers accept only names,
            // and resolve them through -reference.
            $"-a {name} library"
        };

        if (request.RootEntryPoint)
        {
            // Library mode does not root a private Main, and the sweep would then clear the entry
            // point of an executable.
            lines.Add($"-a {name} entrypoint");
        }

        // The assembly being trimmed goes first: the linker takes the first reference whose file
        // name matches.
        lines.Add($"-reference {Quote(request.Input)}");
        lines.AddRange(References(request).Select(_ => $"-reference {Quote(_)}"));

        // Every assembly is there to resolve against, except the one being trimmed.
        lines.Add("--action skip");
        lines.Add("--trim-mode skip");
        lines.Add($"--action link {name}");
        lines.Add("--skip-unresolved true");

        // A library may embed XML that removes attributes when an application is trimmed. Building
        // the library is not that moment.
        lines.Add("--ignore-link-attributes true");

        // Otherwise the linker strips the signature data an F# consumer compiles against.
        lines.Add("--keep-compilers-resources true");

        // Otherwise SecurityCritical, AllowPartiallyTrustedCallers and the rest are stripped.
        lines.Add("--strip-security false");

        // Otherwise a constant returned by a dependency is folded into this assembly, baking in the
        // behaviour of whichever build of that dependency was compiled against.
        lines.Add("--disable-opt ipconstprop");

        // Rewrite the symbols, and keep the path the compiler recorded for them.
        lines.Add("-b true");
        lines.Add("--preserve-symbol-paths");

        // MSBuild does not apply TreatWarningsAsErrors to what a task logs, so the linker is told.
        if (request.WarningsAsErrors)
        {
            lines.Add("--warnaserror");

            // WarningsNotAsErrors, which MSBuild cannot apply either once the linker has promoted
            // the warning. After the switch above, which would otherwise promote these again.
            if (request.WarningsNotAsErrors.Count > 0)
            {
                lines.Add($"--warnaserror- {string.Join(";", request.WarningsNotAsErrors)}");
            }
        }
        else
        {
            lines.Add("--warnaserror-");
        }

        // MSBuild applies NoWarn to the warnings a task logs, but one the linker has promoted
        // arrives as an error, which NoWarn does not touch.
        if (request.NoWarn.Count > 0)
        {
            lines.Add($"--nowarn {string.Join(";", request.NoWarn)}");
        }

        lines.AddRange(request.RootDescriptors.Select(_ => $"-x {Quote(_)}"));

        lines.Add($"--custom-data {Quote($"{RootAssemblyKey}={request.AssemblyName}")}");
        lines.Add($"--custom-data {Quote($"{FriendsFileKey}={request.FriendsFile}")}");
        lines.Add($"--custom-data {Quote($"{ReportFileKey}={request.ReportFile}")}");

        if (request.IgnoreInternalsVisibleTo)
        {
            // Around the linker's own root step, which keeps every internal as soon as it sees one
            // InternalsVisibleTo.
            lines.Add(Step(request, "-RootAssemblyInput:HideInternalsVisibleTo"));
            lines.Add(Step(request, "-MarkStep:RestoreInternalsVisibleTo"));
        }

        if (request.KeepDataShape)
        {
            lines.Add(Step(request, "KeepDataShape"));
        }

        lines.Add(Step(request, "-SweepStep:ReportRemoved"));

        // Last of the options, so that a later --enable-opt or --disable-opt wins over the ones above.
        var extra = request.ExtraArgs.Trim();
        if (extra.Length > 0)
        {
            lines.Add(extra);
        }

        lines.Add($"-out {Quote(request.OutputDirectory)}");
        return lines;
    }

    /// <summary>
    /// The first reference with each file name, which is the one the linker would use, less any that
    /// would shadow the assembly being trimmed.
    /// </summary>
    static IEnumerable<string> References(SquashRequest request)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal)
        {
            Path.GetFileName(request.Input)
        };
        return request.References.Where(_ => seen.Add(Path.GetFileName(_)));
    }

    /// <summary>
    /// The linker's own codes out of a NoWarn or WarningsNotAsErrors list. The rest belong to the compiler, analyzers and
    /// NuGet, and a bare number there means a compiler warning, which the linker would read as one
    /// of its own.
    /// </summary>
    public static List<string> LinkerCodes(string codes) =>
        codes
            .Split([';', ',', ' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Where(IsLinkerCode)
            .Select(_ => _.ToUpperInvariant())
            .Distinct()
            .OrderBy(_ => _, StringComparer.Ordinal)
            .ToList();

    static bool IsLinkerCode(string code) =>
        code.Length > 2 &&
        code.StartsWith("IL", StringComparison.OrdinalIgnoreCase) &&
        code.Skip(2).All(_ => _ is >= '0' and <= '9');

    static string Step(SquashRequest request, string step) =>
        $"--custom-step {Quote($"{step},{request.StepsAssembly}")}";

    /// <summary>
    /// The linker's response file parser reads a backslash before a quote as an escape, so a
    /// directory ending in one would swallow the closing quote.
    /// </summary>
    public static string Quote(string value) =>
        $"\"{value.TrimEnd('\\')}\"";
}
