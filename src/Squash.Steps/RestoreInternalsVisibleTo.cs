/// <summary>
/// Puts back what HideInternalsVisibleTo took off, after the root step has run and before marking.
/// Marking then keeps them like any other assembly-level attribute. In the global namespace so
/// --custom-step can name it unqualified.
/// </summary>
public sealed class RestoreInternalsVisibleTo :
    BaseStep
{
    protected override void Process() =>
        HiddenFriends.Restore();
}
