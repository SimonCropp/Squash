/// <summary>
/// The InternalsVisibleTo attributes taken off the root assembly, held between the step that hides
/// them and the step that restores them. Static because the linker creates each step itself and
/// gives them nothing in common; one linker process trims one assembly.
/// </summary>
static class HiddenFriends
{
    static AssemblyDefinition? assembly;

    // Ascending by original position, so inserting in order lands each attribute where it was.
    static readonly List<(int Index, CustomAttribute Attribute)> hidden = [];

    public static bool IsInternalsVisibleTo(CustomAttribute attribute)
    {
        var type = attribute.AttributeType;
        return type.Namespace == "System.Runtime.CompilerServices" &&
               type.Name == "InternalsVisibleToAttribute";
    }

    public static void Hide(AssemblyDefinition target)
    {
        assembly = target;
        var attributes = target.CustomAttributes;

        // Backwards, so each removal leaves the positions still to be visited unchanged.
        for (var index = attributes.Count - 1; index >= 0; index--)
        {
            var attribute = attributes[index];
            if (!IsInternalsVisibleTo(attribute))
            {
                continue;
            }

            attributes.RemoveAt(index);
            hidden.Insert(0, (index, attribute));
        }
    }

    public static void Restore()
    {
        if (assembly == null)
        {
            return;
        }

        foreach (var (index, attribute) in hidden)
        {
            assembly.CustomAttributes.Insert(index, attribute);
        }

        hidden.Clear();
    }

    /// <summary>
    /// "Friend, PublicKey=0024..." gives "Friend".
    /// </summary>
    public static List<string> Names() =>
        hidden
            .Select(_ => Name(_.Attribute))
            .Where(_ => _.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(_ => _, StringComparer.Ordinal)
            .ToList();

    static string Name(CustomAttribute attribute)
    {
        if (attribute.ConstructorArguments.Count == 0 ||
            attribute.ConstructorArguments[0].Value is not string value)
        {
            return "";
        }

        return value.Split(',')[0].Trim();
    }
}
