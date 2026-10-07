/// <summary>
/// Keeps the fields, property accessors and instance constructors of every type the linker keeps in
/// the root assembly. Serializers, mappers and ORMs reach those by reflection the linker cannot see,
/// and a missing setter fails silently. Unreferenced types and unreferenced methods are still
/// removed. In the global namespace so --custom-step can name it unqualified.
/// </summary>
public sealed class KeepDataShape :
    IMarkHandler
{
    public void Initialize(LinkContext context, MarkContext markContext) =>
        markContext.RegisterMarkTypeAction(_ => Preserve(context, _));

    static void Preserve(LinkContext context, TypeDefinition type)
    {
        if (!RootAssembly.Contains(context, type))
        {
            return;
        }

        var annotations = context.Annotations;
        if (type.HasFields)
        {
            annotations.SetPreserve(type, TypePreserve.Fields);
        }

        foreach (var method in type.Methods.Where(IsShape))
        {
            annotations.AddPreservedMethod(type, method);
        }
    }

    static bool IsShape(MethodDefinition method)
    {
        if (method.IsConstructor)
        {
            return !method.IsStatic;
        }

        return method.IsGetter ||
               method.IsSetter;
    }
}
