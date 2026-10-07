using System.Security;

[assembly: AllowPartiallyTrustedCallers]

namespace Scenarios;

// Compiler-generated closures and state machines must come through intact.
public class Generated
{
    public Func<int, int> Lambda(int add) =>
        _ => _ + add;

    public async Task<int> Async()
    {
        await Task.Yield();
        return 1;
    }

    public IEnumerable<int> Iterator()
    {
        yield return 1;
        yield return 2;
    }
}

// The linker strips security attributes by default. Squash turns that off.
public class Secured
{
    [SecuritySafeCritical]
    public void Critical()
    {
    }
}

public class Reflective
{
    // The linker cannot tell which type this names, and says so with a warning.
    public Type? Find(string typeName) =>
        Type.GetType(typeName);
}
