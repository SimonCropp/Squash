#if !NET
namespace System.Diagnostics.CodeAnalysis;

// Not in netstandard2.0 or .NET Framework. The linker recognises both by name, which is how a
// library on those frameworks roots a member.
[AttributeUsage(AttributeTargets.Constructor | AttributeTargets.Field | AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
sealed class DynamicDependencyAttribute :
    Attribute
{
    public DynamicDependencyAttribute(string memberSignature, Type type)
    {
        MemberSignature = memberSignature;
        Type = type;
    }

    public DynamicDependencyAttribute(DynamicallyAccessedMemberTypes memberTypes, Type type)
    {
        MemberTypes = memberTypes;
        Type = type;
    }

    public string? MemberSignature { get; }

    public DynamicallyAccessedMemberTypes MemberTypes { get; }

    public Type Type { get; }
}

[Flags]
enum DynamicallyAccessedMemberTypes
{
    None = 0,
    All = -1
}
#endif
