using System;
using System.Collections.Generic;
using System.Threading;
using Quarry.Generators.Models;
using Quarry.Generators.Sql;
using Quarry.Generators.Translation;

namespace Quarry.Generators.IR;

/// <summary>
/// Translates a bound call site into a fully-translated call site
/// with resolved SQL expression, parameters, and type metadata.
/// Wraps SqlExprBinder/SqlExprRenderer pipeline; handles parameter extraction,
/// KeyTypeName/ValueTypeName resolution, and clause-specific enrichment.
/// </summary>
internal static class CallSiteTranslator
{
    /// <summary>
    /// Translates a bound call site into a fully-translated call site.
    /// For clause-bearing sites, runs the SqlExpr bind → extract parameters → render pipeline.
    /// For non-clause sites, produces a TranslatedCallSite with null Clause.
    /// </summary>
    public static TranslatedCallSite Translate(
        BoundCallSite bound,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var raw = bound.Raw;

        // Non-clause sites: Limit, Offset, Distinct, WithTimeout, ChainRoot,
        // execution terminals, insert/delete transitions
        if (raw.Expression == null || !IsClauseBearingKind(raw.Kind))
        {
            return new TranslatedCallSite(bound);
        }

        // Attempt clause translation via SqlExpr pipeline
        try
        {
            return TranslateClause(bound, ct);
        }
        catch
        {
            // Translation failed — produce a TranslatedCallSite with null Clause.
            // QRY019 diagnostic will be reported in the collected stage.
            return new TranslatedCallSite(bound);
        }
    }

    private static TranslatedCallSite TranslateClause(BoundCallSite bound, CancellationToken ct)
    {
        var raw = bound.Raw;
        var expression = raw.Expression!;
        var clauseKind = raw.ClauseKind ?? ClauseKind.Where;

        // Check for unsupported SqlRawExpr nodes before attempting translation
        if (ContainsUnsupportedRawExpr(expression))
        {
            return new TranslatedCallSite(bound);
        }

        // Build column lookup for key/value type resolution
        var columnLookup = new Dictionary<string, ColumnInfo>(StringComparer.Ordinal);
        foreach (var col in bound.Entity.Columns)
            columnLookup[col.PropertyName] = col;

        // Step 1: Bind column references
        var inBooleanContext = clauseKind == ClauseKind.Where || clauseKind == ClauseKind.Having;

        // Reconstruct EntityInfo for the binder (it needs EntityInfo, not EntityRef)
        // For now, use the entity columns from EntityRef to create a minimal EntityInfo
        var entityInfo = ReconstructEntityInfo(bound);
        if (entityInfo == null)
        {
            return new TranslatedCallSite(bound);
        }

        // Determine lambda parameter name from the expression tree.
        // If the expression has no column references (e.g., u => true), use a placeholder
        // since the binder won't need to resolve any columns.
        var lambdaParamName = ExtractLambdaParameterName(expression);
        if (lambdaParamName == null)
        {
            // Expressions without column references (literals, constants) don't need
            // column resolution. Use a placeholder name for the binder.
            lambdaParamName = "_";
        }

        var boundExpr = SqlExprBinder.Bind(
            expression,
            entityInfo,
            bound.Dialect,
            lambdaParamName,
            inBooleanContext: inBooleanContext);

        // Step 2: Extract parameters
        int paramIndex = 0;
        var parameters = new List<ParameterInfo>();
        boundExpr = SqlExprClauseTranslator.ExtractParametersPublic(boundExpr, parameters, ref paramIndex);

        // Step 3: Render to SQL
        var sql = SqlExprRenderer.Render(boundExpr, bound.Dialect, useGenericParamFormat: true);

        if (string.IsNullOrEmpty(sql))
        {
            return new TranslatedCallSite(bound);
        }

        // Resolve key type for OrderBy/ThenBy/GroupBy
        string? keyTypeName = null;
        if (clauseKind == ClauseKind.OrderBy || clauseKind == ClauseKind.GroupBy)
        {
            keyTypeName = ResolveKeyType(expression, columnLookup);
        }

        // Resolve value type for Set
        string? valueTypeName = null;
        if (clauseKind == ClauseKind.Set)
        {
            valueTypeName = ResolveKeyType(expression, columnLookup);
        }

        // Build the translated clause
        var joinKind = raw.Kind switch
        {
            InterceptorKind.Join => (JoinClauseKind?)JoinClauseKind.Inner,
            InterceptorKind.LeftJoin => JoinClauseKind.Left,
            InterceptorKind.RightJoin => JoinClauseKind.Right,
            _ => null
        };

        string? joinedTableName = null;
        string? joinedSchemaName = null;
        if (joinKind != null && bound.JoinedEntity != null)
        {
            joinedTableName = bound.JoinedEntity.TableName;
            joinedSchemaName = bound.JoinedEntity.SchemaName;
        }

        var clause = new TranslatedClause(
            kind: clauseKind,
            resolvedExpression: boundExpr,
            parameters: parameters,
            isSuccess: true,
            isDescending: raw.IsDescending,
            joinKind: joinKind,
            joinedTableName: joinedTableName,
            joinedSchemaName: joinedSchemaName);

        return new TranslatedCallSite(bound, clause, keyTypeName, valueTypeName);
    }

    /// <summary>
    /// Extracts the first lambda parameter name from the SqlExpr tree.
    /// Walks ColumnRefExpr nodes to find the parameter name.
    /// </summary>
    private static string? ExtractLambdaParameterName(SqlExpr expr)
    {
        switch (expr)
        {
            case ColumnRefExpr colRef:
                return colRef.ParameterName;
            case BinaryOpExpr bin:
                return ExtractLambdaParameterName(bin.Left) ?? ExtractLambdaParameterName(bin.Right);
            case UnaryOpExpr unary:
                return ExtractLambdaParameterName(unary.Operand);
            case FunctionCallExpr func:
                foreach (var arg in func.Arguments)
                {
                    var name = ExtractLambdaParameterName(arg);
                    if (name != null) return name;
                }
                return null;
            case InExpr inExpr:
                return ExtractLambdaParameterName(inExpr.Operand);
            case IsNullCheckExpr isNull:
                return ExtractLambdaParameterName(isNull.Operand);
            case LikeExpr like:
                return ExtractLambdaParameterName(like.Operand);
            default:
                return null;
        }
    }

    /// <summary>
    /// Reconstructs an EntityInfo from the BoundCallSite's EntityRef.
    /// The SqlExprBinder requires EntityInfo, so we bridge from EntityRef.
    /// </summary>
    private static EntityInfo? ReconstructEntityInfo(BoundCallSite bound)
    {
        var entity = bound.Entity;
        if (entity.Columns.Count == 0 && entity.TableName == "")
            return null; // Empty entity ref — unresolved

        return new EntityInfo(
            entityName: entity.EntityName,
            schemaClassName: "",
            schemaNamespace: entity.SchemaNamespace ?? "",
            tableName: entity.TableName,
            namingStyle: Quarry.Shared.Migration.NamingStyleKind.SnakeCase,
            columns: entity.Columns,
            navigations: entity.Navigations,
            indexes: Array.Empty<IndexInfo>(),
            location: Microsoft.CodeAnalysis.Location.None);
    }

    /// <summary>
    /// Resolves the CLR type of a key/value expression from column metadata.
    /// </summary>
    private static string? ResolveKeyType(SqlExpr expr, Dictionary<string, ColumnInfo> columnLookup)
    {
        if (expr is ColumnRefExpr colRef)
        {
            if (columnLookup.TryGetValue(colRef.PropertyName, out var column))
                return column.FullClrType;
        }
        return null;
    }

    /// <summary>
    /// Checks if an InterceptorKind represents a clause-bearing method.
    /// </summary>
    private static bool IsClauseBearingKind(InterceptorKind kind)
    {
        return kind switch
        {
            InterceptorKind.Where => true,
            InterceptorKind.DeleteWhere => true,
            InterceptorKind.UpdateWhere => true,
            InterceptorKind.OrderBy => true,
            InterceptorKind.ThenBy => true,
            InterceptorKind.GroupBy => true,
            InterceptorKind.Having => true,
            InterceptorKind.Set => true,
            InterceptorKind.UpdateSet => true,
            InterceptorKind.UpdateSetAction => true,
            InterceptorKind.Join => true,
            InterceptorKind.LeftJoin => true,
            InterceptorKind.RightJoin => true,
            _ => false
        };
    }

    /// <summary>
    /// Checks whether the SqlExpr tree contains unsupported SqlRawExpr nodes.
    /// </summary>
    private static bool ContainsUnsupportedRawExpr(SqlExpr expr)
    {
        switch (expr)
        {
            case SqlRawExpr raw:
                return raw.SqlText != "*";
            case BinaryOpExpr bin:
                return ContainsUnsupportedRawExpr(bin.Left) || ContainsUnsupportedRawExpr(bin.Right);
            case UnaryOpExpr unary:
                return ContainsUnsupportedRawExpr(unary.Operand);
            case FunctionCallExpr func:
                foreach (var arg in func.Arguments)
                    if (ContainsUnsupportedRawExpr(arg))
                        return true;
                return false;
            case InExpr inExpr:
                if (ContainsUnsupportedRawExpr(inExpr.Operand))
                    return true;
                foreach (var val in inExpr.Values)
                    if (ContainsUnsupportedRawExpr(val))
                        return true;
                return false;
            case IsNullCheckExpr isNull:
                return ContainsUnsupportedRawExpr(isNull.Operand);
            case LikeExpr like:
                return ContainsUnsupportedRawExpr(like.Operand) || ContainsUnsupportedRawExpr(like.Pattern);
            case ExprListExpr list:
                foreach (var e in list.Expressions)
                    if (ContainsUnsupportedRawExpr(e))
                        return true;
                return false;
            default:
                return false;
        }
    }
}
