using System.Reflection;
using MethodTimer;

namespace Lib;

public class Timed
{
    [Time]
    public string Work() => "worked";
}

// Found by the weaver by its name. No source calls it.
static class MethodTimeLogger
{
    public static void Log(MethodBase methodBase, long milliseconds, string? message) =>
        Console.WriteLine($"timed {methodBase.Name}");
}

class UnusedInternal
{
}
