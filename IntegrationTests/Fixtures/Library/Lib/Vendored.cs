// Stands in for a source-only package compiled into the library: internal, in a namespace of its
// own, and nothing a friend is meant to use.
namespace Vendored;

static class Helper
{
    public static string Unused() => "unused";
}
