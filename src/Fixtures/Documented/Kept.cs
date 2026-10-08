using System.Collections;

namespace Documented;

// Public, so all of it is the root and none of it may lose its documentation, however its name is
// spelled.

/// <summary>Kept.</summary>
/// <typeparam name="T">Kept.</typeparam>
public unsafe class Kept<T> :
    IEnumerable<T>,
    IDisposable
{
    /// <summary>Kept.</summary>
    public const int Constant = 1;

    /// <summary>Kept.</summary>
    public int Field;

    /// <summary>Kept.</summary>
    public Kept()
    {
    }

    /// <summary>Kept.</summary>
    /// <param name="a">Kept.</param>
    /// <param name="b">Kept.</param>
    public Kept(int a, T b)
    {
    }

    /// <summary>
    /// Kept. Over more than one line, with <see cref="Field"/> and <c>code</c>, an entity &amp; and
    /// <![CDATA[a section that holds </member> without ending anything]]>.
    /// </summary>
    /// <remarks>
    /// <para>Kept.</para>
    /// <code>
    /// var kept = new Kept&lt;int&gt;();
    /// </code>
    /// </remarks>
    public void Plain()
    {
    }

    /// <summary>Kept.</summary>
    public void Signature(ref int a, out T b, int[,] c, int* d, Dictionary<string, List<T>> e, delegate*<int, void> f) =>
        b = default!;

    /// <summary>Kept.</summary>
    public U GenericMethod<U>(U a, Kept<U>.Nested<T> b) =>
        a;

    /// <summary>Kept.</summary>
    public static implicit operator int(Kept<T> a) =>
        0;

    /// <summary>Kept.</summary>
    public static explicit operator string(Kept<T> a) =>
        "";

    /// <summary>Kept.</summary>
    public int Property { get; set; }

    /// <summary>Kept.</summary>
    public int this[string a, T b] => 0;

    /// <summary>Kept.</summary>
    public event EventHandler? Event;

    /// <summary>Kept.</summary>
    void IDisposable.Dispose()
    {
    }

    /// <summary>Kept.</summary>
    IEnumerator<T> IEnumerable<T>.GetEnumerator() =>
        null!;

    /// <summary>Kept.</summary>
    IEnumerator IEnumerable.GetEnumerator() =>
        null!;

    /// <summary>Kept.</summary>
    public class Nested<U>
    {
        /// <summary>Kept.</summary>
        public void Method(T a, U b)
        {
        }
    }
}

/// <summary>Kept.</summary>
public enum KeptEnum
{
    /// <summary>Kept.</summary>
    First,

    /// <summary>Kept.</summary>
    Second
}

/// <summary>Kept.</summary>
public static class KeptExtensions
{
    /// <summary>Kept.</summary>
    public static void Classic(this string a, int b)
    {
    }

    /// <summary>Kept.</summary>
    extension(string a)
    {
        /// <summary>Kept.</summary>
        public void Block(int b)
        {
        }

        /// <summary>Kept.</summary>
        public int BlockProperty => 0;
    }
}
