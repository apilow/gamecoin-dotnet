#if !NET5_0_OR_GREATER
// Lets `init` accessors (and records) compile for netstandard2.1. Consumers' compilers only look at the type's name.
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit
    {
    }
}
#endif
