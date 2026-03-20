using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;

namespace Quarry.Generators.Utilities;

/// <summary>
/// Caches <see cref="ITypeSymbol.ToDisplayString"/> results to avoid repeated string allocations
/// for the same symbol across many invocations. Uses ConditionalWeakTable so entries are
/// automatically cleaned up when symbols are GC'd after compilation.
/// </summary>
internal static class SymbolDisplayCache
{
    private static readonly ConditionalWeakTable<ITypeSymbol, string> FullyQualifiedCache = new();
    private static readonly ConditionalWeakTable<ITypeSymbol, string> MinimallyQualifiedCache = new();

    internal static string ToFullyQualifiedDisplayString(this ITypeSymbol symbol)
    {
        return FullyQualifiedCache.GetValue(symbol, s => s.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
    }

    internal static string ToMinimallyQualifiedDisplayString(this ITypeSymbol symbol)
    {
        return MinimallyQualifiedCache.GetValue(symbol, s => s.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));
    }
}
