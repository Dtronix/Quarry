using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Quarry.Generators.Models;

namespace Quarry.Analyzers.Rules;

internal sealed class QueryAnalysisContext
{
    public UsageSiteInfo Site { get; }
    public EntityInfo? PrimaryEntity { get; }
    public IReadOnlyList<EntityInfo>? JoinedEntities { get; }
    public ContextInfo? Context { get; }
    public SemanticModel SemanticModel { get; }
    public SyntaxNode InvocationSyntax { get; }
    public AnalyzerConfigOptions Options { get; }

    public QueryAnalysisContext(
        UsageSiteInfo site,
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
}
