using System;
using System.Collections.Generic;
using System.Linq;
using Quarry.Generators.Generation;
using Quarry.Generators.Models;
using Quarry.Generators.Sql;

namespace Quarry.Generators.CodeGen;

/// <summary>
/// Analyzes a pre-built chain to determine carrier optimization eligibility
/// and compute the carrier strategy (base class, fields, parameters).
/// Consolidates eligibility checks previously spread across QuarryGenerator,
/// CarrierClassBuilder, and InterceptorCodeGenerator.Carrier.cs.
/// </summary>
internal static class CarrierAnalyzer
{
    /// <summary>
    /// Analyzes a chain and returns its carrier optimization strategy.
    /// </summary>
    public static CarrierStrategy Analyze(PrebuiltChainInfo chain)
    {
        // Gate 1: Chain parameters must be resolvable
        if (chain.ChainParameters == null)
            return CarrierStrategy.Ineligible("chain parameters not resolved");

        // Gate 2: No unmatched method names in the chain
        if (chain.Analysis.UnmatchedMethodNames != null)
            return CarrierStrategy.Ineligible("chain has unmatched methods");

        // Gate 3: Terminal-specific eligibility
        var terminalResult = CheckTerminalEligibility(chain);
        if (terminalResult != null)
            return terminalResult;

        // Gate 4: MySQL InsertExecuteScalar requires separate SELECT LAST_INSERT_ID()
        if (chain.Analysis.ExecutionSite.Kind == InterceptorKind.InsertExecuteScalar
            && chain.Dialect == SqlDialect.MySQL)
            return CarrierStrategy.Ineligible("MySQL InsertExecuteScalar requires separate query");

        // All gates passed — build the strategy
        return BuildEligibleStrategy(chain);
    }

    /// <summary>
    /// Checks terminal-specific eligibility based on the execution site kind and query type.
    /// Returns an ineligible strategy if the terminal would not be emitted, null if OK.
    /// </summary>
    private static CarrierStrategy? CheckTerminalEligibility(PrebuiltChainInfo chain)
    {
        var site = chain.Analysis.ExecutionSite;

        switch (site.Kind)
        {
            case InterceptorKind.ExecuteFetchAll:
            case InterceptorKind.ExecuteFetchFirst:
            case InterceptorKind.ExecuteFetchFirstOrDefault:
            case InterceptorKind.ExecuteFetchSingle:
            case InterceptorKind.ToAsyncEnumerable:
            {
                // Reader terminal: needs resolved result type and reader delegate
                var rawResult = InterceptorCodeGenerator.ResolveExecutionResultTypePublic(
                    site.ResultTypeName, chain.ResultTypeName, chain.ProjectionInfo);
                if (string.IsNullOrEmpty(rawResult))
                    return CarrierStrategy.Ineligible("reader terminal: unresolved result type");
                if (chain.ReaderDelegateCode == null)
                    return CarrierStrategy.Ineligible("reader terminal: no reader delegate");
                if (chain.ProjectionInfo != null && chain.ProjectionInfo.Columns.Any(c =>
                    c.SqlExpression != null && !string.IsNullOrEmpty(c.ColumnName)))
                    return CarrierStrategy.Ineligible("reader terminal: ambiguous projection columns");
                return null;
            }

            case InterceptorKind.ExecuteScalar:
            {
                var rawResult = InterceptorCodeGenerator.ResolveExecutionResultTypePublic(
                    site.ResultTypeName, chain.ResultTypeName, chain.ProjectionInfo);
                if (string.IsNullOrEmpty(rawResult))
                    return CarrierStrategy.Ineligible("scalar terminal: unresolved result type");
                return null;
            }

            case InterceptorKind.ExecuteNonQuery:
            {
                if (chain.SqlMap.Values.Any(v => string.IsNullOrWhiteSpace(v.Sql)
                    || (chain.QueryKind == QueryKind.Update && v.Sql.Contains("SET  "))))
                    return CarrierStrategy.Ineligible("nonquery terminal: malformed SQL");
                return null;
            }

            case InterceptorKind.InsertExecuteNonQuery:
            case InterceptorKind.InsertExecuteScalar:
            case InterceptorKind.InsertToDiagnostics:
            {
                if (chain.SqlMap.Values.Any(v => string.IsNullOrWhiteSpace(v.Sql)))
                    return CarrierStrategy.Ineligible("insert terminal: empty SQL");
                return null;
            }

            case InterceptorKind.ToDiagnostics:
                // ToDiagnostics doesn't read rows — always eligible if we got this far
                return null;

            default:
                return null;
        }
    }

    /// <summary>
    /// Builds the full carrier strategy for an eligible chain: base class, fields, parameters.
    /// </summary>
    private static CarrierStrategy BuildEligibleStrategy(PrebuiltChainInfo chain)
    {
        var fields = new List<CarrierField>();
        var parameters = new List<CarrierParameter>();
        var staticFields = new List<CarrierStaticField>();

        // Build parameter fields and metadata
        foreach (var param in chain.ChainParameters)
        {
            // Entity-sourced parameters (SetPoco) don't get carrier fields
            if (param.EntityPropertyExpression != null)
            {
                parameters.Add(new CarrierParameter(
                    globalIndex: param.Index,
                    fieldName: $"P{param.Index}",
                    fieldType: param.TypeName,
                    extractionCode: null,
                    bindingCode: null,
                    typeMappingClass: param.TypeMapping,
                    isEntitySourced: true));
                continue;
            }

            if (param.IsCollection && param.ElementTypeName != null)
            {
                var elementType = NormalizeFieldType(param.ElementTypeName);
                var fieldType = $"System.Collections.Generic.IReadOnlyList<{elementType}>";
                fields.Add(new CarrierField($"P{param.Index}", fieldType));

                parameters.Add(new CarrierParameter(
                    globalIndex: param.Index,
                    fieldName: $"P{param.Index}",
                    fieldType: fieldType,
                    extractionCode: param.ValueExpression,
                    bindingCode: null,
                    isCollection: true));
            }
            else
            {
                var fieldType = NormalizeFieldType(param.TypeName);
                fields.Add(new CarrierField($"P{param.Index}", fieldType));

                parameters.Add(new CarrierParameter(
                    globalIndex: param.Index,
                    fieldName: $"P{param.Index}",
                    fieldType: fieldType,
                    extractionCode: param.ValueExpression,
                    bindingCode: null,
                    typeMappingClass: param.TypeMapping,
                    isSensitive: param.IsSensitive));
            }

            // Static FieldInfo cache for captured params
            if (param.NeedsFieldInfoCache)
                staticFields.Add(new CarrierStaticField($"F{param.Index}", "FieldInfo?", null));
        }

        // Mask field for conditional clauses
        if (chain.Analysis.ConditionalClauses.Count > 0)
        {
            var bitCount = chain.Analysis.ConditionalClauses.Count;
            var maskType = bitCount <= 8 ? "byte" : bitCount <= 16 ? "ushort" : "uint";
            fields.Add(new CarrierField("Mask", maskType));
        }

        // Limit/Offset fields
        foreach (var clause in chain.Analysis.Clauses)
        {
            if (clause.Role == ClauseRole.Limit)
                fields.Add(new CarrierField("Limit", "int"));
            if (clause.Role == ClauseRole.Offset)
                fields.Add(new CarrierField("Offset", "int"));
        }

        // Timeout field
        if (chain.Analysis.Clauses.Any(c => c.Role == ClauseRole.WithTimeout))
            fields.Add(new CarrierField("Timeout", "TimeSpan?"));

        // Entity field for insert/setPoco chains
        if (chain.QueryKind == QueryKind.Insert
            || (chain.QueryKind == QueryKind.Update
                && chain.Analysis.Clauses.Any(c => c.Site.Kind == InterceptorKind.UpdateSetPoco)))
        {
            var entityType = InterceptorCodeGenerator.GetShortTypeName(chain.EntityTypeName);
            fields.Add(new CarrierField("Entity", entityType + "?"));
        }

        var baseClassName = InterceptorCodeGenerator.ResolveCarrierBaseClass(chain);

        return new CarrierStrategy(
            isEligible: true,
            ineligibleReason: null,
            baseClassName: baseClassName,
            fields: fields,
            staticFields: staticFields,
            parameters: parameters);
    }

    /// <summary>
    /// Normalizes a parameter type for carrier field emission.
    /// </summary>
    private static string NormalizeFieldType(string typeName)
    {
        // Normalize Nullable<T> → T?
        if (typeName.StartsWith("System.Nullable<") || typeName.StartsWith("Nullable<"))
        {
            var inner = typeName.Substring(typeName.IndexOf('<') + 1).TrimEnd('>');
            return inner + "?";
        }

        if (typeName.EndsWith("?"))
            return typeName;

        if (ValueTypes.Contains(typeName))
            return typeName;

        if (typeName.Contains('<') || typeName.Contains('[') || typeName.Contains('.'))
            return typeName;

        // Reference types — append ? for nullable context
        return typeName + "?";
    }

    private static readonly HashSet<string> ValueTypes = new(StringComparer.Ordinal)
    {
        "int", "long", "short", "byte", "sbyte", "uint", "ulong", "ushort",
        "float", "double", "decimal", "bool", "char",
        "DateTime", "DateTimeOffset", "TimeSpan", "Guid", "DateOnly", "TimeOnly",
        "Int32", "Int64", "Int16", "Byte", "SByte", "UInt32", "UInt64", "UInt16",
        "Single", "Double", "Decimal", "Boolean", "Char"
    };
}
