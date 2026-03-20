using System;
using System.Collections.Generic;
using Quarry.Generators.Models;
using Quarry.Generators.Sql;
using Quarry.Generators.Translation;
using Microsoft.CodeAnalysis;

namespace Quarry.Generators.IR;

/// <summary>
/// Bidirectional adapter between UsageSiteInfo and the new layered call site types.
/// Enables incremental migration: downstream code continues using UsageSiteInfo while
/// the discovery/binding/translation pipeline is migrated to the new types.
/// </summary>
internal static class UsageSiteInfoAdapter
{
    /// <summary>
    /// Converts a UsageSiteInfo to a RawCallSite.
    /// </summary>
    public static RawCallSite ToRawCallSite(UsageSiteInfo site)
    {
        // Convert PendingClauseInfo expression to SqlExpr if present
        SqlExpr? expression = null;
        ClauseKind? clauseKind = null;
        bool isDescending = false;

        if (site.PendingClauseInfo != null)
        {
            expression = SyntacticExpressionAdapter.Convert(site.PendingClauseInfo.Expression);
            clauseKind = site.PendingClauseInfo.Kind;
            isDescending = site.PendingClauseInfo.IsDescending;
        }
        else if (site.ClauseInfo != null)
        {
            // Clause already translated — wrap the SQL as a SqlRawExpr
            expression = new SqlRawExpr(site.ClauseInfo.SqlFragment);
            clauseKind = site.ClauseInfo.Kind;
            if (site.ClauseInfo is OrderByClauseInfo orderBy)
                isDescending = orderBy.IsDescending;
        }

        return new RawCallSite(
            methodName: site.MethodName,
            filePath: site.FilePath,
            line: site.Line,
            column: site.Column,
            uniqueId: site.UniqueId,
            kind: site.Kind,
            builderKind: site.BuilderKind,
            entityTypeName: site.EntityTypeName,
            resultTypeName: site.ResultTypeName,
            isAnalyzable: site.IsAnalyzable,
            nonAnalyzableReason: site.NonAnalyzableReason,
            interceptableLocationData: site.InterceptableLocationData,
            interceptableLocationVersion: site.InterceptableLocationVersion,
            location: DiagnosticLocation.FromSyntaxNode(site.InvocationSyntax),
            expression: expression,
            clauseKind: clauseKind,
            isDescending: isDescending,
            projectionInfo: site.ProjectionInfo,
            joinedEntityTypeName: site.JoinedEntityTypeName,
            initializedPropertyNames: site.InitializedPropertyNames,
            constantIntValue: site.ConstantIntValue,
            isNavigationJoin: site.IsNavigationJoin,
            contextClassName: site.ContextClassName,
            contextNamespace: site.ContextNamespace);
    }

    /// <summary>
    /// Creates a UsageSiteInfo from a TranslatedCallSite.
    /// This is the reverse adapter for downstream code that still expects UsageSiteInfo.
    /// </summary>
    public static UsageSiteInfo ToUsageSiteInfo(TranslatedCallSite translated, SyntaxNode invocationSyntax)
    {
        var raw = translated.Bound.Raw;

        // Convert TranslatedClause back to ClauseInfo
        ClauseInfo? clauseInfo = null;
        if (translated.Clause != null && translated.Clause.IsSuccess)
        {
            var sql = SqlExprRenderer.Render(translated.Clause.ResolvedExpression, translated.Bound.Dialect);

            if (translated.Clause.Kind == ClauseKind.OrderBy || translated.Clause.Kind == ClauseKind.GroupBy)
            {
                clauseInfo = new OrderByClauseInfo(
                    sql,
                    translated.Clause.IsDescending,
                    translated.Clause.Parameters,
                    translated.KeyTypeName);
            }
            else if (translated.Clause.Kind == ClauseKind.Set)
            {
                var paramIdx = translated.Clause.Parameters.Count > 0
                    ? translated.Clause.Parameters[translated.Clause.Parameters.Count - 1].Index
                    : 0;
                clauseInfo = new SetClauseInfo(
                    sql, paramIdx, translated.Clause.Parameters,
                    translated.Clause.CustomTypeMappingClass,
                    translated.ValueTypeName);
            }
            else if (translated.Clause.Kind == ClauseKind.Join && translated.Clause.JoinKind.HasValue)
            {
                clauseInfo = new JoinClauseInfo(
                    translated.Clause.JoinKind.Value,
                    translated.Bound.JoinedEntity?.EntityName ?? "",
                    translated.Clause.JoinedTableName ?? "",
                    sql,
                    translated.Clause.Parameters,
                    translated.Clause.JoinedSchemaName,
                    translated.Clause.TableAlias);
            }
            else
            {
                clauseInfo = ClauseInfo.Success(translated.Clause.Kind, sql, translated.Clause.Parameters);
            }
        }
        else if (translated.Clause != null && !translated.Clause.IsSuccess)
        {
            clauseInfo = ClauseInfo.Failure(translated.Clause.Kind, translated.Clause.ErrorMessage ?? "Translation failed");
        }

        return new UsageSiteInfo(
            methodName: raw.MethodName,
            filePath: raw.FilePath,
            line: raw.Line,
            column: raw.Column,
            builderTypeName: "", // Not needed in the new pipeline
            entityTypeName: raw.EntityTypeName,
            isAnalyzable: raw.IsAnalyzable,
            kind: raw.Kind,
            invocationSyntax: invocationSyntax,
            uniqueId: raw.UniqueId,
            resultTypeName: raw.ResultTypeName,
            nonAnalyzableReason: raw.NonAnalyzableReason,
            contextClassName: translated.Bound.ContextClassName,
            contextNamespace: translated.Bound.ContextNamespace,
            projectionInfo: raw.ProjectionInfo,
            clauseInfo: clauseInfo,
            interceptableLocationData: raw.InterceptableLocationData,
            interceptableLocationVersion: raw.InterceptableLocationVersion,
            insertInfo: translated.Bound.InsertInfo,
            joinedEntityTypeName: raw.JoinedEntityTypeName,
            joinedEntityTypeNames: translated.Bound.JoinedEntityTypeNames,
            dialect: translated.Bound.Dialect,
            initializedPropertyNames: raw.InitializedPropertyNames,
            updateInfo: translated.Bound.UpdateInfo,
            keyTypeName: translated.KeyTypeName,
            rawSqlTypeInfo: translated.Bound.RawSqlTypeInfo,
            isNavigationJoin: raw.IsNavigationJoin,
            constantIntValue: raw.ConstantIntValue,
            builderKind: raw.BuilderKind,
            valueTypeName: translated.ValueTypeName);
    }
}
