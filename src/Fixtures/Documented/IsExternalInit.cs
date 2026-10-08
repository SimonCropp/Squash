#if !NET
namespace System.Runtime.CompilerServices;

// Not in netstandard2.0 or .NET Framework, and an init accessor cannot be compiled without it.
static class IsExternalInit;
#endif
