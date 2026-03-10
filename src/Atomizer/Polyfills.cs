// Enables C# 9+ record types (and init-only properties) when targeting frameworks
// that predate System.Runtime.CompilerServices.IsExternalInit — specifically
// netstandard2.0 and netstandard2.1.
#if NETSTANDARD2_0 || NETSTANDARD2_1
namespace System.Runtime.CompilerServices
{
    // This type is referenced by the compiler for record declarations and
    // init-only property setters.  Defining it here satisfies the compiler
    // without pulling in any extra runtime dependency.
    internal static class IsExternalInit { }
}
#endif
