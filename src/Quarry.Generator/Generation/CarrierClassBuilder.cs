using System.Collections.Generic;
using Quarry.Generators.Models;

namespace Quarry.Generators.Generation;

/// <summary>
/// Builds <see cref="CarrierClassInfo"/> from a <see cref="PrebuiltChainInfo"/>.
/// The carrier class is a lightweight <c>file sealed class</c> that replaces
/// QueryBuilder/JoinedQueryBuilder + QueryState with a single allocation per chain.
/// </summary>
internal static class CarrierClassBuilder
{
    /// <summary>
    /// Builds a CarrierClassInfo for a carrier-eligible PrebuiltDispatch chain.
    /// Returns null if the chain cannot be carrier-optimized.
    /// </summary>
    public static CarrierClassInfo? Build(PrebuiltChainInfo chain, int chainIndex)
    {
        if (!chain.IsCarrierEligible)
            return null;

        var fields = new List<CarrierField>();

        // Determine if this is a ToSql-only chain (no execution context needed)
        var isToSqlOnly = chain.Analysis.ExecutionSite.Kind == InterceptorKind.ToSql;

        // Field: IQueryExecutionContext? Ctx (omit for ToSql-only chains)
        if (!isToSqlOnly)
        {
            fields.Add(new CarrierField("Ctx", "IQueryExecutionContext?", FieldRole.ExecutionContext));
        }

        // Fields: typed parameters P0, P1, ...
        foreach (var param in chain.ChainParameters)
        {
            fields.Add(new CarrierField($"P{param.Index}", param.TypeName, FieldRole.Parameter));
        }

        // Field: Mask (if chain has conditional clauses)
        if (chain.Analysis.ConditionalClauses.Count > 0)
        {
            var bitCount = chain.Analysis.ConditionalClauses.Count;
            var maskType = bitCount <= 8 ? "byte" : bitCount <= 16 ? "ushort" : "uint";
            fields.Add(new CarrierField("Mask", maskType, FieldRole.ClauseMask));
        }

        // Fields: Limit/Offset (if chain has runtime pagination values)
        var hasRuntimeLimit = false;
        var hasRuntimeOffset = false;
        foreach (var clause in chain.Analysis.Clauses)
        {
            if (clause.Role == ClauseRole.Limit)
                hasRuntimeLimit = true;
            if (clause.Role == ClauseRole.Offset)
                hasRuntimeOffset = true;
        }

        if (hasRuntimeLimit)
            fields.Add(new CarrierField("Limit", "int", FieldRole.Limit));
        if (hasRuntimeOffset)
            fields.Add(new CarrierField("Offset", "int", FieldRole.Offset));

        // Field: Timeout (if chain contains WithTimeout)
        var hasTimeout = false;
        foreach (var clause in chain.Analysis.Clauses)
        {
            if (clause.Role == ClauseRole.WithTimeout)
            {
                hasTimeout = true;
                break;
            }
        }

        if (hasTimeout)
            fields.Add(new CarrierField("Timeout", "TimeSpan?", FieldRole.Timeout));

        var className = $"Chain_{chainIndex}";

        return new CarrierClassInfo(
            className: className,
            implementedInterfaces: System.Array.Empty<string>(),
            fields: fields,
            deadMethods: System.Array.Empty<CarrierInterfaceStub>());
    }
}
