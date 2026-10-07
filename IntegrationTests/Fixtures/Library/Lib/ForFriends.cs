namespace Lib;

// Each of these is used by the friend alone, and each leaves a different trace in the friend's
// metadata, or none at all.

static class Constants
{
    // Copied into the friend when it compiles: no reference is left.
    public const string Name = "constant";

    public static string Value() => "value";

    public static string Unused() => "unused";
}

// Its property is named where the attribute is applied, in a blob, and not referred to.
[AttributeUsage(AttributeTargets.Class)]
class MarkerAttribute :
    Attribute
{
    public string? Text { get; set; }
}

// The friend overrides Fill and never calls it.
abstract class Template
{
    internal abstract string Fill();

    public string Run() => Fill();
}

enum Mode
{
    Off,
    On
}

class Holder<T>
{
    public T? Value = default;

    public T? Get() => Value;

    public TOther Convert<TOther>(TOther other) => other;

    public void Unused()
    {
    }

    public class Inner
    {
        public string Name() => "inner";
    }
}

// An extension block. The friend's code refers to the implementation of a member, and the compiler
// binds the call against a declaration that is compiled beside it.
static class Extensions
{
    extension(string text)
    {
        public string Doubled() => text + text;

        public static string Fixed() => "fixed";

        public string Unused() => text;
    }
}

// A visible type, with members that are not.
public class Visible
{
    internal const string Hidden = "hidden";

    internal int count = 1;

    internal string Internal() => "internal";

    internal string Unused() => "unused";
}
