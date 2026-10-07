namespace Scenarios;

// The visible surface: public, protected and protected internal stay; the rest goes unless
// something reaches it.
public class Surface
{
    public int PublicField;
    protected int ProtectedField;
    internal int InternalField;
    int privateField;

    public string Used() =>
        Helper.Used() + new UsedInternal().Value;

    protected virtual void Protected()
    {
    }

    protected internal void ProtectedInternal()
    {
    }

    private protected void PrivateProtected()
    {
    }

    internal void Internal()
    {
    }

    void Private()
    {
    }
}

static class Helper
{
    public static string Used() => "used";

    public static string Unused() => "unused";
}

class UsedInternal
{
    public UsedInternal()
    {
    }

    public UsedInternal(int unused)
    {
    }

    public string Value => "value";

    public string UnusedProperty { get; set; } = "";
}

class UnusedInternal
{
    public void Method()
    {
    }
}

// Library mode keeps every interface, used or not.
interface IInternalInterface
{
    void Method();
}

public class Nesting
{
    public class PublicNested
    {
    }

    protected class ProtectedNested
    {
    }

    internal class InternalNested
    {
    }

    class PrivateNested
    {
    }
}
