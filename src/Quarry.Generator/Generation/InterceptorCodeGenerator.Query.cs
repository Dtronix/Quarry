using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Quarry.Generators.Models;
using Quarry.Generators.Sql;
using Quarry;
using Quarry.Generators.Projection;
using Quarry.Generators.Translation;

namespace Quarry.Generators.Generation;

internal static partial class InterceptorCodeGenerator
{
    /// <summary>
    /// Generates an interceptor method for a usage site.
    /// </summary>
    private static void GenerateInterceptorMethod(
        StringBuilder sb,
        UsageSiteInfo site,
        List<CachedExtractorField> staticFields,
        Dictionary<string, List<CachedExtractorField>> staticFieldsByMethod,
        Dictionary<string, PrebuiltChainInfo> chainLookup,
        Dictionary<string, int> clauseBitMap,
        Dictionary<string, PrebuiltChainInfo> chainClauseLookup,
        HashSet<string> firstClauseIds,
        Dictionary<string, (CarrierClassInfo Carrier, PrebuiltChainInfo Chain)>? carrierLookup = null,
        Dictionary<string, (CarrierClassInfo Carrier, PrebuiltChainInfo Chain)>? carrierClauseLookup = null,
        HashSet<string>? carrierFirstClauseIds = null)
    {
        // Check if this site belongs to a carrier-optimized chain
        var isCarrierSite = false;
        CarrierClassInfo? carrierInfo = null;
        PrebuiltChainInfo? carrierChain = null;

        if (carrierLookup != null && carrierLookup.TryGetValue(site.UniqueId, out var carrierExec))
        {
            isCarrierSite = true;
            carrierInfo = carrierExec.Carrier;
            carrierChain = carrierExec.Chain;
        }
        else if (carrierClauseLookup != null && carrierClauseLookup.TryGetValue(site.UniqueId, out var carrierClause))
        {
            isCarrierSite = true;
            carrierInfo = carrierClause.Carrier;
            carrierChain = carrierClause.Chain;
        }

        // Check for skippable Select sites BEFORE emitting the attribute
        if (site.Kind == InterceptorKind.Select && ShouldSkipSelectInterceptor(site))
        {
            return; // Don't emit anything for this site
        }

        // Limit/Offset/Distinct/WithTimeout are tracked for chain analysis only on the non-carrier path.
        // On the carrier path, they need interceptors to set carrier fields or act as noops.
        if (site.Kind is InterceptorKind.Limit or InterceptorKind.Offset or InterceptorKind.Distinct or InterceptorKind.WithTimeout)
        {
            if (!isCarrierSite)
                return;
        }

        // ChainRoot (entity set factory method, e.g., db.Users()).
        // On carrier path: creates the carrier directly from the context (zero QueryBuilder allocation).
        // On non-carrier path: skip (original method runs normally).
        if (site.Kind is InterceptorKind.ChainRoot)
        {
            if (!isCarrierSite)
                return;
        }

        // DeleteTransition/UpdateTransition/InsertTransition (.Delete()/.Update()/.Insert() on IEntityAccessor).
        // On carrier path: noop cast or entity store (carrier implements both interfaces).
        // On non-carrier path: skip (original method runs normally).
        if (site.Kind is InterceptorKind.DeleteTransition or InterceptorKind.UpdateTransition or InterceptorKind.InsertTransition)
        {
            if (!isCarrierSite)
                return;
        }

        // For execution interceptors: only emit if we have a pre-built chain for this site
        // AND we can resolve the result type. Otherwise skip — the built-in execution methods
        // work correctly via the Select interceptor.
        if (site.Kind is InterceptorKind.ExecuteFetchAll or InterceptorKind.ExecuteFetchFirst
            or InterceptorKind.ExecuteFetchFirstOrDefault or InterceptorKind.ExecuteFetchSingle
            or InterceptorKind.ToAsyncEnumerable)
        {
            if (!chainLookup.TryGetValue(site.UniqueId, out var chain))
                return;
            // Skip chains with unmatched fluent methods (Limit, Offset, AddWhereClause, etc.)
            // whose effects are invisible to the pre-built SQL.
            if (chain.Analysis.UnmatchedMethodNames != null)
                return;
            // Resolve the best available result type. The site/chain result types come from the
            // semantic model and may be empty or broken for source-generated entity types.
            // The enriched ProjectionInfo.ResultTypeName is rebuilt from entity metadata and is
            // authoritative for tuples and single-column projections.
            var rawResult = ResolveExecutionResultType(site.ResultTypeName, chain.ResultTypeName, chain.ProjectionInfo);
            if (string.IsNullOrEmpty(rawResult))
                return;
            // Must have a reader delegate
            if (chain.ReaderDelegateCode == null)
                return;
            // Skip chains with projections that have SqlExpression set alongside ColumnName.
            // CompileTimeSqlBuilder uses SqlExpression (e.g., "LOWER(col)") but the runtime path
            // uses ColumnName and quotes it. These produce different SQL.
            // Only allow execution interceptors when the SQL will be byte-identical.
            if (chain.ProjectionInfo != null)
            {
                var hasAmbiguousColumns = chain.ProjectionInfo.Columns.Any(c =>
                    c.SqlExpression != null && !string.IsNullOrEmpty(c.ColumnName));
                if (hasAmbiguousColumns)
                    return;
            }
        }
        else if (site.Kind is InterceptorKind.ExecuteScalar)
        {
            if (!chainLookup.TryGetValue(site.UniqueId, out var scalarChain))
                return;
            if (scalarChain.Analysis.UnmatchedMethodNames != null)
                return;
            // ExecuteScalar doesn't need a reader delegate — just pre-built SQL
            var rawScalarResult = ResolveExecutionResultType(site.ResultTypeName, scalarChain.ResultTypeName, scalarChain.ProjectionInfo);
            if (string.IsNullOrEmpty(rawScalarResult))
                return;
        }
        else if (site.Kind is InterceptorKind.ExecuteNonQuery)
        {
            if (!chainLookup.TryGetValue(site.UniqueId, out var nqChain))
                return;
            // Validate that all SQL variants are non-empty and well-formed
            if (nqChain.SqlMap.Values.Any(v => string.IsNullOrWhiteSpace(v.Sql)
                || (nqChain.QueryKind == QueryKind.Update && v.Sql.Contains("SET  "))))
                return;
        }
        else if (site.Kind is InterceptorKind.ToDiagnostics)
        {
            if (chainLookup.TryGetValue(site.UniqueId, out var diagChain)
                && diagChain.Analysis.UnmatchedMethodNames != null)
                return;
            // Runtime-delegating interceptors can only be generated for unprojected builders.
            // Projected types (TResult) may reference types unavailable in the generated namespace.
            if (!chainLookup.ContainsKey(site.UniqueId) && site.ResultTypeName != null)
                return;
        }

        // Skip clause interceptors where the clause could not be translated to SQL.
        // The original runtime method will run instead. A QRY019 diagnostic is reported separately.
        // Carrier-optimized sites always emit interceptors — their metadata is on the chain, not the site.
        if (!isCarrierSite && ShouldSkipNonTranslatableClause(site))
        {
            return;
        }

        var methodName = $"{site.MethodName}_{site.UniqueId}";

        // Determine chain analysis status for remarks
        PrebuiltChainInfo? chainForRemarks = null;
        chainLookup.TryGetValue(site.UniqueId, out chainForRemarks);
        if (chainForRemarks == null)
            chainClauseLookup.TryGetValue(site.UniqueId, out chainForRemarks);

        // XML documentation
        sb.AppendLine($"    /// <summary>");
        sb.AppendLine($"    /// Intercepts {site.MethodName}() call at {GetRelativePath(site.FilePath)}:{site.Line}:{site.Column}");
        sb.AppendLine($"    /// </summary>");
        if (chainForRemarks != null)
        {
            var carrierLabel = carrierInfo != null ? ", Carrier-Optimized" : "";
            sb.AppendLine($"    /// <remarks>Chain: Fully Analyzed ({chainForRemarks.Analysis.Tier}{carrierLabel})</remarks>");
        }
        else
            sb.AppendLine($"    /// <remarks>Chain: Not Analyzed (standalone interceptor)</remarks>");

        // InterceptsLocation attribute - use new format with version and data if available
        if (!string.IsNullOrEmpty(site.InterceptableLocationData))
        {
            sb.AppendLine($"    [InterceptsLocation({site.InterceptableLocationVersion}, \"{site.InterceptableLocationData}\")]");
        }
        else
        {
            // Fallback: skip this site if we don't have valid location data
            sb.AppendLine($"    // WARNING: Could not generate InterceptsLocation for this call site");
            return;
        }

        // Check if this is on a joined builder type
        var isJoinedBuilder = site.JoinedEntityTypeNames != null && site.JoinedEntityTypeNames.Count >= 2;

        // Look up clause bitmask bit index for conditional clause interceptors
        clauseBitMap.TryGetValue(site.UniqueId, out var rawBit);
        int? clauseBit = clauseBitMap.ContainsKey(site.UniqueId) ? rawBit : null;

        // Resolve prebuilt chain membership for clause sites
        chainClauseLookup.TryGetValue(site.UniqueId, out var prebuiltClauseChain);
        var isFirstClauseInChain = prebuiltClauseChain != null && firstClauseIds.Contains(site.UniqueId);

        // For carrier chains, override isFirstInChain with the carrier-specific entry point.
        // In conditional chains, this is the first UNCONDITIONAL clause (not necessarily Clauses[0]).
        if (isCarrierSite && carrierFirstClauseIds != null)
        {
            isFirstClauseInChain = carrierFirstClauseIds.Contains(site.UniqueId);
        }

        // Generate method based on kind
        switch (site.Kind)
        {
            case InterceptorKind.Select:
                if (isJoinedBuilder)
                    GenerateJoinedSelectInterceptor(sb, site, methodName, prebuiltClauseChain, isFirstClauseInChain, carrierInfo);
                else
                    CodeGen.ClauseBodyEmitter.EmitSelect(sb, site, methodName, prebuiltClauseChain, isFirstClauseInChain, carrierInfo);
                break;

            case InterceptorKind.Where:
                {
                    staticFieldsByMethod.TryGetValue(methodName, out var methodFields);
                    if (isJoinedBuilder)
                        GenerateJoinedWhereInterceptor(sb, site, methodName, methodFields, clauseBit, prebuiltClauseChain, isFirstClauseInChain, carrierInfo);
                    else
                        CodeGen.ClauseBodyEmitter.EmitWhere(sb, site, methodName, methodFields, clauseBit, prebuiltClauseChain, isFirstClauseInChain, carrierInfo);
                }
                break;

            case InterceptorKind.OrderBy:
            case InterceptorKind.ThenBy:
                if (isJoinedBuilder)
                    GenerateJoinedOrderByInterceptor(sb, site, methodName, clauseBit, prebuiltClauseChain, isFirstClauseInChain, carrierInfo);
                else
                    CodeGen.ClauseBodyEmitter.EmitOrderBy(sb, site, methodName, clauseBit, prebuiltClauseChain, isFirstClauseInChain, carrierInfo);
                break;

            case InterceptorKind.GroupBy:
                CodeGen.ClauseBodyEmitter.EmitGroupBy(sb, site, methodName, clauseBit, prebuiltClauseChain, isFirstClauseInChain, carrierInfo);
                break;

            case InterceptorKind.Having:
                CodeGen.ClauseBodyEmitter.EmitHaving(sb, site, methodName, clauseBit, prebuiltClauseChain, isFirstClauseInChain, carrierInfo);
                break;

            case InterceptorKind.Set:
                CodeGen.ClauseBodyEmitter.EmitSet(sb, site, methodName, clauseBit, prebuiltClauseChain, isFirstClauseInChain, carrier: carrierInfo);
                break;

            case InterceptorKind.Join:
            case InterceptorKind.LeftJoin:
            case InterceptorKind.RightJoin:
                GenerateJoinInterceptor(sb, site, methodName, prebuiltClauseChain, isFirstClauseInChain, carrierInfo);
                break;

            case InterceptorKind.ExecuteFetchAll:
            case InterceptorKind.ExecuteFetchFirst:
            case InterceptorKind.ExecuteFetchFirstOrDefault:
            case InterceptorKind.ExecuteFetchSingle:
            case InterceptorKind.ExecuteScalar:
            case InterceptorKind.ToAsyncEnumerable:
                if (chainLookup.TryGetValue(site.UniqueId, out var selectChain))
                {
                    if (selectChain.IsJoinChain)
                        GeneratePrebuiltJoinExecutionInterceptor(sb, site, methodName, selectChain, carrierInfo);
                    else
                        GeneratePrebuiltSelectExecutionInterceptor(sb, site, methodName, selectChain, carrierInfo);
                }
                break;

            case InterceptorKind.ExecuteNonQuery:
                if (chainLookup.TryGetValue(site.UniqueId, out var nonQueryChain))
                    GeneratePrebuiltNonQueryExecutionInterceptor(sb, site, methodName, nonQueryChain, carrierInfo);
                break;

            case InterceptorKind.ToDiagnostics:
                if (chainLookup.TryGetValue(site.UniqueId, out var toDiagChain))
                    GeneratePrebuiltToDiagnosticsInterceptor(sb, site, methodName, toDiagChain, carrierInfo);
                else
                    GenerateRuntimeToDiagnosticsInterceptor(sb, site, methodName);
                break;

            case InterceptorKind.DeleteWhere:
                {
                    staticFieldsByMethod.TryGetValue(methodName, out var deleteMethodFields);
                    CodeGen.ClauseBodyEmitter.EmitModificationWhere(sb, site, methodName, deleteMethodFields, isDelete: true, clauseBit, prebuiltClauseChain, isFirstClauseInChain, carrier: carrierInfo);
                }
                break;

            case InterceptorKind.UpdateSet:
                CodeGen.ClauseBodyEmitter.EmitUpdateSet(sb, site, methodName, clauseBit, prebuiltClauseChain, isFirstClauseInChain, carrier: carrierInfo);
                break;

            case InterceptorKind.UpdateSetAction:
                CodeGen.ClauseBodyEmitter.EmitUpdateSetAction(sb, site, methodName, clauseBit, prebuiltClauseChain, isFirstClauseInChain, carrier: carrierInfo);
                break;

            case InterceptorKind.UpdateSetPoco:
                CodeGen.ClauseBodyEmitter.EmitUpdateSetPoco(sb, site, methodName, clauseBit, prebuiltClauseChain, isFirstClauseInChain, carrier: carrierInfo);
                break;

            case InterceptorKind.UpdateWhere:
                {
                    staticFieldsByMethod.TryGetValue(methodName, out var updateMethodFields);
                    CodeGen.ClauseBodyEmitter.EmitModificationWhere(sb, site, methodName, updateMethodFields, isDelete: false, clauseBit, prebuiltClauseChain, isFirstClauseInChain, carrier: carrierInfo);
                }
                break;

            case InterceptorKind.InsertExecuteNonQuery:
                {
                    chainLookup.TryGetValue(site.UniqueId, out var insertChain);
                    GenerateInsertExecuteNonQueryInterceptor(sb, site, methodName, insertChain, carrierInfo);
                }
                break;

            case InterceptorKind.InsertExecuteScalar:
                {
                    chainLookup.TryGetValue(site.UniqueId, out var insertScalarChain);
                    GenerateInsertExecuteScalarInterceptor(sb, site, methodName, insertScalarChain, carrierInfo);
                }
                break;

            case InterceptorKind.InsertToDiagnostics:
                {
                    chainLookup.TryGetValue(site.UniqueId, out var insertDiagChain);
                    GenerateInsertToDiagnosticsInterceptor(sb, site, methodName, insertDiagChain, carrierInfo);
                }
                break;

            case InterceptorKind.RawSqlAsync:
                CodeGen.RawSqlBodyEmitter.EmitRawSqlAsync(sb, site, methodName);
                break;

            case InterceptorKind.RawSqlScalarAsync:
                CodeGen.RawSqlBodyEmitter.EmitRawSqlScalarAsync(sb, site, methodName);
                break;

            case InterceptorKind.Limit:
            case InterceptorKind.Offset:
                if (carrierInfo != null && carrierChain != null)
                    CodeGen.TransitionBodyEmitter.EmitPagination(sb, site, methodName, carrierInfo, carrierChain);
                break;

            case InterceptorKind.Distinct:
                if (carrierInfo != null && carrierChain != null)
                    CodeGen.TransitionBodyEmitter.EmitDistinct(sb, site, methodName, carrierInfo, carrierChain);
                break;

            case InterceptorKind.WithTimeout:
                if (carrierInfo != null && carrierChain != null)
                    CodeGen.TransitionBodyEmitter.EmitWithTimeout(sb, site, methodName, carrierInfo, carrierChain);
                break;

            case InterceptorKind.ChainRoot:
                if (carrierInfo != null && carrierChain != null)
                    CodeGen.TransitionBodyEmitter.EmitChainRoot(sb, site, methodName, carrierInfo);
                break;

            case InterceptorKind.DeleteTransition:
            case InterceptorKind.UpdateTransition:
                if (carrierInfo != null)
                    CodeGen.TransitionBodyEmitter.EmitDeleteUpdateTransition(sb, site, methodName);
                break;

            case InterceptorKind.InsertTransition:
                if (carrierInfo != null)
                    CodeGen.TransitionBodyEmitter.EmitInsertTransition(sb, site, methodName, carrierInfo);
                break;

            case InterceptorKind.AllTransition:
                CodeGen.TransitionBodyEmitter.EmitAllTransition(sb, site, methodName, carrierInfo);
                break;

            default:
                GeneratePlaceholderInterceptor(sb, site, methodName);
                break;
        }

        sb.AppendLine();
    }

    // Select, Where, OrderBy, GroupBy, Having, Set methods moved to CodeGen.ClauseBodyEmitter
}
