using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Quarry.Generators.IR;
using Quarry.Generators.Models;
using Quarry.Generators.Sql;

namespace Quarry.Analyzers.Rules;

internal sealed class QueryAnalysisContext
{
    private string? _renderedSql;

    public RawCallSite Site { get; }
    public EntityInfo? PrimaryEntity { get; }
    public IReadOnlyList<EntityInfo>? JoinedEntities { get; }
    public ContextInfo? Context { get; }
    public SemanticModel SemanticModel { get; }
    public SyntaxNode InvocationSyntax { get; }
    public AnalyzerConfigOptions Options { get; }

    public QueryAnalysisContext(
        RawCallSite site,
        EntityInfo? primaryEntity,
        IReadOnlyList<EntityInfo>? joinedEntities,
        ContextInfo? context,
        SemanticModel semanticModel,
        SyntaxNode invocationSyntax,
        AnalyzerConfigOptions options)
    {
        Site = site;
        PrimaryEntity = primaryEntity;
        JoinedEntities = joinedEntities;
        Context = context;
        SemanticModel = semanticModel;
        InvocationSyntax = invocationSyntax;
        Options = options;
    }

    /// <summary>
    /// Gets the rendered SQL fragment for the site's expression, or null if there is no expression.
    /// Uses a generic parameter format (@p0, @p1) since dialect-specific formatting is not needed for analysis.
    /// </summary>
    public string? GetRenderedSql()
    {
        if (Site.Expression == null)
            return null;

        return _renderedSql ??= SqlExprRenderer.Render(
            Site.Expression,
            Context?.Dialect ?? SqlDialect.PostgreSQL,
            useGenericParamFormat: true,
            stripOuterParens: true);
    }
}
