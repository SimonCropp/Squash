namespace Squash;

/// <summary>
/// Diagnostic codes, their short names, and the message shape every emitted finding shares. The
/// linker's own findings keep their ILxxxx codes and are not listed here.
/// </summary>
public static class Diagnostics
{
    public const string Subcategory = "Squash";

    // Codes are never reused.
    public const string HostNotFound = "Squash001";
    public const string RuntimeMissing = "Squash002";
    public const string LinkerFailed = "Squash003";
    public const string NothingReachable = "Squash004";
    public const string SymbolsNotRewritten = "Squash005";
    public const string CannotResign = "Squash006";
    public const string Failed = "Squash007";
    public const string FriendsIgnored = "Squash008";
    public const string InvalidInput = "Squash009";
    public const string FriendsNotFound = "Squash010";

    public static readonly string[] All =
    [
        HostNotFound,
        RuntimeMissing,
        LinkerFailed,
        NothingReachable,
        SymbolsNotRewritten,
        CannotResign,
        Failed,
        FriendsIgnored,
        InvalidInput,
        FriendsNotFound
    ];

    const string docsBaseUrl = "https://github.com/SimonCropp/Squash/blob/main/docs/DiagnosticCodes.md";

    public static string NameFor(string code) =>
        code switch
        {
            HostNotFound => "dotnet host not found",
            RuntimeMissing => "No .NET runtime for the linker",
            LinkerFailed => "Linker failed",
            NothingReachable => "Nothing reachable from the public surface",
            SymbolsNotRewritten => "Symbols could not be rewritten",
            CannotResign => "Assembly could not be signed again",
            Failed => "Trimming failed",
            FriendsIgnored => "InternalsVisibleTo ignored",
            InvalidInput => "Invalid input",
            FriendsNotFound => "Friend assemblies not found",
            _ => code
        };

    public static string DocsUrl(string code) =>
        $"{docsBaseUrl}#{code.ToLowerInvariant()}";

    public static string Render(string code, string body) =>
        $"{NameFor(code)}. {body} See: {DocsUrl(code)}";
}

public enum Severity
{
    Error,
    Warning,
    Message
}

public sealed record Diagnostic(string Code, Severity Severity, string Body);

/// <summary>
/// A failure with a diagnostic code of its own, so the task reports that code and not the general
/// one.
/// </summary>
public sealed class SquashException(string code, string message) :
    Exception(message)
{
    public string Code { get; } = code;
}
