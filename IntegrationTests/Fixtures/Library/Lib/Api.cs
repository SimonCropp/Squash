using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Friend")]

namespace Lib;

public class Api
{
    public string Greet() => Shared.Kept();
}

static class Shared
{
    public static string Kept() => "kept";

    // Nothing in this assembly calls it. Only a friend could.
    public static string OnlyForFriends() => "friends";
}

class UnusedInternal
{
}
