using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Scenarios.Friend")]
[assembly: InternalsVisibleTo("Scenarios.OtherFriend")]

namespace Scenarios;

// Reached only through InternalsVisibleTo. Removed unless SquashInternalsVisibleTo=Honor.
class FriendOnly
{
    public void Method()
    {
    }
}

public class Dynamic
{
    // One member, by name.
    [DynamicDependency("Kept", typeof(DynamicTarget))]
    public void Run()
    {
    }

    // Every member of a type.
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(DynamicWhole))]
    public void RunAll()
    {
    }
}

class DynamicTarget
{
    void Kept()
    {
    }

    void NotKept()
    {
    }
}

class DynamicWhole
{
    string field = "";

    DynamicWhole()
    {
    }

    string Property { get; set; } = "";

    void Method()
    {
    }
}

// Rooted by roots.xml when the test passes it as a root descriptor.
class DescriptorRooted
{
    void Rooted()
    {
    }

    void NotRooted()
    {
    }
}
