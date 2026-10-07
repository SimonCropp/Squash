namespace Dependency;

public static class Flags
{
    // A constant the linker could fold into a caller. It must not: the application may run a
    // different build of this assembly than the one the library was compiled against.
    public static bool IsSupported => false;
}

public interface IContract
{
    void Run();
}
