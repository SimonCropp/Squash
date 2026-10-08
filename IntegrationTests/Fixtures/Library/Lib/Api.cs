using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Friend")]

namespace Lib;

/// <summary>The api.</summary>
public class Api
{
    /// <summary>Greets.</summary>
    public string Greet() => Shared.Kept();
}

/// <summary>Shared.</summary>
static class Shared
{
    /// <summary>Kept.</summary>
    public static string Kept() => "kept";

    // Nothing in this assembly calls it. Only a friend could.
    /// <summary>Only for friends.</summary>
    public static string OnlyForFriends() => "friends";
}

/// <summary>Unused.</summary>
class UnusedInternal
{
}
