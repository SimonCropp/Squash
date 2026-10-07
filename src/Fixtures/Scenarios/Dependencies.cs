using Dependency;

namespace Scenarios;

public class Constants
{
    // Both branches must survive. Folding the dependency's constant in would bake one build of that
    // dependency's behaviour into this library.
    public string Describe()
    {
        if (Flags.IsSupported)
        {
            return "supported";
        }

        return "unsupported";
    }
}

public class Contracts
{
    public IContract Get() => new Implementation();
}

// Run implements an interface from an assembly the linker does not trim, so it stays although
// nothing here calls it. Extra goes.
class Implementation :
    IContract
{
    public void Run()
    {
    }

    public void Extra()
    {
    }
}
