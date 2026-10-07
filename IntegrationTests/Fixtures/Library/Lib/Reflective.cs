namespace Lib;

public class Reflective
{
    // The linker cannot tell which type this names, and warns: IL2057.
    public Type? Find(string typeName) =>
        Type.GetType(typeName);
}
