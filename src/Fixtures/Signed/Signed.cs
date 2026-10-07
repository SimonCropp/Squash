namespace Signed;

public class Api
{
    public string Used() =>
        Helper.Used();
}

static class Helper
{
    public static string Used() => "used";

    public static string Unused() => "unused";
}

class Unused
{
}
