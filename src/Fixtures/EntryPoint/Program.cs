Console.WriteLine(Greeter.Greet());

static class Greeter
{
    public static string Greet() => "hello";

    public static string Unused() => "unused";
}

class Unused
{
}
