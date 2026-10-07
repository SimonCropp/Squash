namespace Friend;

public class UsesRemoved
{
    // An internal that only this assembly uses, so trimming removes it unless friends are honored.
    public string Get() => Lib.Shared.OnlyForFriends();
}
