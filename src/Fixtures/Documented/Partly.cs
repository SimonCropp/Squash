namespace Documented;

// A type that stays while some of its members go. The members that go share a name with one that
// stays wherever the language allows it, so only the rest of the name tells them apart.

/// <summary>Kept.</summary>
public class Partly
{
    /// <summary>Kept.</summary>
    public int Field;

    /// <summary>Removed.</summary>
    int unused;

    /// <summary>Kept.</summary>
    public Partly()
    {
    }

    /// <summary>Removed.</summary>
    Partly(string a)
    {
    }

    /// <summary>Kept.</summary>
    public void Overloaded(int a) =>
        Used();

    /// <summary>Removed.</summary>
    void Overloaded(string a)
    {
    }

    /// <summary>Kept.</summary>
    public void Overloaded<T>(T a)
    {
    }

    /// <summary>Removed.</summary>
    void Overloaded<T>(T a, int b)
    {
    }

    /// <summary>Removed.</summary>
    void Overloaded<T, U>(T a)
    {
    }

    /// <summary>Kept.</summary>
    void Used()
    {
    }

    /// <summary>Kept.</summary>
    public int Property { get; set; }

    /// <summary>Removed.</summary>
    int Unused { get; set; }

    /// <summary>Kept.</summary>
    public int this[int a] => 0;

    /// <summary>Removed.</summary>
    int this[string a] => 0;

    /// <summary>Kept.</summary>
    public event EventHandler? Event;

    /// <summary>Removed.</summary>
    event EventHandler? UnusedEvent;

    /// <summary>Kept.</summary>
    public static implicit operator int(Partly a) =>
        0;

    /// <summary>Kept.</summary>
    public class Nested
    {
    }

    /// <summary>Removed.</summary>
    class UnusedNested
    {
        /// <summary>Removed.</summary>
        public void Method()
        {
        }
    }
}
