namespace Friend;

public class UsesKept
{
    // An internal that Lib's own public surface reaches, so it survives trimming.
    public string Get() => Lib.Shared.Kept();
}
