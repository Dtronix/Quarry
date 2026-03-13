using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Quarry.Analyzers.Tests;

internal sealed class EmptyAnalyzerConfigOptions : AnalyzerConfigOptions
{
    public override bool TryGetValue(string key, [NotNullWhen(true)] out string? value)
    {
        value = null;
        return false;
    }
}
