using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("InternalsOnly.Friend")]

namespace InternalsOnly;

static class Shared
{
    public static string Value() => "value";
}
