using System.Collections;

namespace Documented;

// Internal, and nothing uses it, so the type goes and every member with it. Each member is one way
// a signature can be spelled in a documentation name; an entry that outlives its member means that
// spelling was not recognised.

/// <summary>Removed.</summary>
unsafe class Removed<T> :
    IDisposable,
    IEnumerable<T>,
    IComparable<Removed<T>>,
    INotify,
    IHasValue<T>,
    IPair<int, T>
{
    /// <summary>Removed.</summary>
    public const int Constant = 1;

    /// <summary>Removed.</summary>
    public static int Static;

    /// <summary>Removed.</summary>
    public int Instance;

    /// <summary>Removed.</summary>
    public volatile int Volatile;

    /// <summary>Removed.</summary>
    public T? OfTypeParameter;

    /// <summary>Removed.</summary>
    static Removed() =>
        Static = 1;

    /// <summary>Removed.</summary>
    public Removed()
    {
    }

    /// <summary>Removed.</summary>
    public Removed(int a, T b)
    {
    }

    /// <summary>Removed.</summary>
    ~Removed()
    {
    }

    /// <summary>Removed.</summary>
    public void Plain()
    {
    }

    /// <summary>Removed.</summary>
    public void Primitives(bool a, byte b, sbyte c, char d, short e, ushort f, int g, uint h, long i, ulong j, float k, double l, decimal m, string n, object o, nint p, nuint q)
    {
    }

    /// <summary>Removed.</summary>
    public void Arrays(int[] a, int[,] b, int[][] c, int[,,] d, string[][,] e)
    {
    }

    /// <summary>Removed.</summary>
    public void References(ref int a, out int b, in int c, ref readonly int d, ref List<T> e) =>
        b = 0;

    /// <summary>Removed.</summary>
    public void Pointers(int* a, void* b, int** c, int*[] d)
    {
    }

    /// <summary>Removed.</summary>
    public void Generics(List<string> a, Dictionary<string, List<int>> b, int? c, (int First, string Second) d, T e, T[] f, List<T> g)
    {
    }

    /// <summary>Removed.</summary>
    public void Dynamic(dynamic a, List<dynamic> b)
    {
    }

    /// <summary>Removed.</summary>
    public U GenericMethod<U>(U a) =>
        a;

    /// <summary>Removed.</summary>
    public void GenericMethod<U, V>(U a, V[] b, KeyValuePair<T, List<V>> c)
    {
    }

    /// <summary>Removed.</summary>
    public void Nested(Outer<int>.Inner<string> a, Outer<T>.Inner<T>.Innermost b, Outer<int>.Plain c, Removed<T>.NestedGeneric<int> d)
    {
    }

    /// <summary>Removed.</summary>
    public void Params(params int[] a)
    {
    }

    /// <summary>Removed.</summary>
    public void Optional(int a = 1, string? b = null)
    {
    }

    /// <summary>Removed.</summary>
    public void FunctionPointers(delegate*<int, void> a, delegate* unmanaged[Cdecl]<int> b)
    {
    }

    /// <summary>Removed.</summary>
    public void FunctionPointerBetween(int a, delegate*<int, void> b, string c)
    {
    }

    /// <summary>Removed.</summary>
    public ref int ReturnsReference(ref int a) =>
        ref a;

    /// <summary>Removed.</summary>
    public static Removed<T> operator +(Removed<T> a, Removed<T> b) =>
        a;

    /// <summary>Removed.</summary>
    public static implicit operator int(Removed<T> a) =>
        0;

    /// <summary>Removed.</summary>
    public static implicit operator Removed<T>(int a) =>
        new();

    /// <summary>Removed.</summary>
    public static explicit operator string(Removed<T> a) =>
        "";

    /// <summary>Removed.</summary>
    public static explicit operator byte(Removed<T> a) =>
        0;

    /// <summary>Removed.</summary>
    public static explicit operator checked byte(Removed<T> a) =>
        0;

    /// <summary>Removed.</summary>
    public int Property { get; set; }

    /// <summary>Removed.</summary>
    public static int StaticProperty { get; set; }

    /// <summary>Removed.</summary>
    public int Init { get; init; }

    /// <summary>Removed.</summary>
    public int this[int a] => 0;

    /// <summary>Removed.</summary>
    public int this[string a, T b] => 0;

    /// <summary>Removed.</summary>
    public event EventHandler? Event;

    /// <summary>Removed.</summary>
    event EventHandler INotify.Changed
    {
        add
        {
        }
        remove
        {
        }
    }

    /// <summary>Removed.</summary>
    void IDisposable.Dispose()
    {
    }

    /// <summary>Removed.</summary>
    IEnumerator<T> IEnumerable<T>.GetEnumerator() =>
        null!;

    /// <summary>Removed.</summary>
    IEnumerator IEnumerable.GetEnumerator() =>
        null!;

    /// <summary>Removed.</summary>
    int IComparable<Removed<T>>.CompareTo(Removed<T>? other) =>
        0;

    /// <summary>Removed.</summary>
    T IHasValue<T>.Value => default!;

    /// <summary>Removed.</summary>
    T IHasValue<T>.this[int a] => default!;

    /// <summary>Removed.</summary>
    void IPair<int, T>.Method(Dictionary<int, T> a)
    {
    }

    /// <summary>Removed.</summary>
    public class NestedClass
    {
        /// <summary>Removed.</summary>
        public void Method(T a)
        {
        }
    }

    /// <summary>Removed.</summary>
    public struct NestedStruct
    {
        /// <summary>Removed.</summary>
        public int Field;
    }

    /// <summary>Removed.</summary>
    public enum NestedEnum
    {
        /// <summary>Removed.</summary>
        First,

        /// <summary>Removed.</summary>
        Second
    }

    /// <summary>Removed.</summary>
    public delegate void NestedDelegate(int a);

    /// <summary>Removed.</summary>
    public class NestedGeneric<U>
    {
        /// <summary>Removed.</summary>
        public void Method(T a, U b)
        {
        }

        /// <summary>Removed.</summary>
        public class Deeper<V>
        {
            /// <summary>Removed.</summary>
            public void Method<W>(T a, U b, V c, W d)
            {
            }
        }
    }
}

/// <summary>Removed.</summary>
class Outer<T>
{
    /// <summary>Removed.</summary>
    public class Plain
    {
    }

    /// <summary>Removed.</summary>
    public class Inner<U>
    {
        /// <summary>Removed.</summary>
        public class Innermost
        {
        }
    }
}

// Library mode keeps every interface, used or not, with its members. What implements one is still
// removed where nothing else keeps it.

/// <summary>Kept.</summary>
interface INotify
{
    /// <summary>Kept.</summary>
    event EventHandler Changed;
}

/// <summary>Kept.</summary>
interface IHasValue<T>
{
    /// <summary>Kept.</summary>
    T Value { get; }

    /// <summary>Kept.</summary>
    T this[int a] { get; }
}

/// <summary>Kept.</summary>
interface IPair<TFirst, TSecond>
    where TFirst : notnull
{
    /// <summary>Kept.</summary>
    void Method(Dictionary<TFirst, TSecond> a);
}

/// <summary>Removed.</summary>
static class RemovedExtensions
{
    /// <summary>Removed.</summary>
    public static void Classic(this string a, int b)
    {
    }

    /// <summary>Removed.</summary>
    extension(string a)
    {
        /// <summary>Removed.</summary>
        public void Block(int b)
        {
        }

        /// <summary>Removed.</summary>
        public int BlockProperty => 0;

        /// <summary>Removed.</summary>
        public static void BlockStatic()
        {
        }
    }

    /// <summary>Removed.</summary>
    extension<U>(List<U> a)
    {
        /// <summary>Removed.</summary>
        public void GenericBlock<V>(U b, V c)
        {
        }
    }
}
