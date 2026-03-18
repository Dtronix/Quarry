using System.Collections.Generic;
using System.Collections.Immutable;
using Quarry.Internal;

namespace Quarry;

internal static class DiagnosticsHelper
{
    internal static IReadOnlyList<DiagnosticParameter> ConvertParameters(ImmutableArray<QueryParameter> parameters)
    {
        if (parameters.IsDefaultOrEmpty) return [];
        var result = new DiagnosticParameter[parameters.Length];
        for (int i = 0; i < parameters.Length; i++)
            result[i] = new DiagnosticParameter($"@p{parameters[i].Index}", parameters[i].Value);
        return result;
    }

    internal static IReadOnlyList<DiagnosticParameter> ConvertParameters(List<ModificationParameter> parameters)
    {
        if (parameters.Count == 0) return [];
        var result = new DiagnosticParameter[parameters.Count];
        for (int i = 0; i < parameters.Count; i++)
            result[i] = new DiagnosticParameter($"@p{parameters[i].Index}", parameters[i].Value);
        return result;
    }
}
