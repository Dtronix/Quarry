using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Quarry.Analyzers.Rules;
using Quarry.Analyzers.Rules.Dialect;
using Quarry.Analyzers.Rules.Patterns;
using Quarry.Analyzers.Rules.Performance;
using Quarry.Analyzers.Rules.Simplification;
using Quarry.Analyzers.Rules.WastedWork;
using Quarry.Generators.IR;
using Quarry.Generators.Models;
using Quarry.Generators.Parsing;

namespace Quarry.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
internal sealed class QuarryQueryAnalyzer : DiagnosticAnalyzer
{
    private static readonly ImmutableArray<IQueryAnalysisRule> Rules = ImmutableArray.Create<IQueryAnalysisRule>(
        // QRA1xx: Simplification
        new CountComparedToZeroRule(),
        new SingleValueInRule(),
        new TautologicalConditionRule(),
        new ContradictoryConditionRule(),
        new RedundantConditionRule(),
        new NullableWithoutNullCheckRule(),
        // QRA2xx: Wasteful Patterns
        new UnusedJoinRule(),
        new WideTableSelectRule(),
        new OrderByWithoutLimitRule(),
        new DuplicateProjectionColumnRule(),
        new CartesianProductRule(),
        // QRA3xx: Performance
        new LeadingWildcardLikeRule(),
        new FunctionOnColumnInWhereRule(),
        new OrOnDifferentColumnsRule(),
        new WhereOnNonIndexedColumnRule(),
        // QRA4xx: Patterns
        new QueryInsideLoopRule(),
        new MultipleQueriesSameTableRule(),
        // QRA5xx: Dialect
        new DialectOptimizationRule(),
        new SuboptimalForDialectRule());

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(
        AnalyzerDiagnosticDescriptors.CountComparedToZero,
        AnalyzerDiagnosticDescriptors.SingleValueIn,
        AnalyzerDiagnosticDescriptors.TautologicalCondition,
        AnalyzerDiagnosticDescriptors.ContradictoryCondition,
        AnalyzerDiagnosticDescriptors.RedundantCondition,
        AnalyzerDiagnosticDescriptors.NullableWithoutNullCheck,
        AnalyzerDiagnosticDescriptors.UnusedJoin,
        AnalyzerDiagnosticDescriptors.WideTableSelect,
        AnalyzerDiagnosticDescriptors.OrderByWithoutLimit,
        AnalyzerDiagnosticDescriptors.DuplicateProjectionColumn,
        AnalyzerDiagnosticDescriptors.CartesianProduct,
        AnalyzerDiagnosticDescriptors.LeadingWildcardLike,
        AnalyzerDiagnosticDescriptors.FunctionOnColumnInWhere,
        AnalyzerDiagnosticDescriptors.OrOnDifferentColumns,
        AnalyzerDiagnosticDescriptors.WhereOnNonIndexedColumn,
        AnalyzerDiagnosticDescriptors.QueryInsideLoop,
        AnalyzerDiagnosticDescriptors.MultipleQueriesSameTable,
        AnalyzerDiagnosticDescriptors.DialectOptimization,
        AnalyzerDiagnosticDescriptors.SuboptimalForDialect);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(
            GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);

        context.RegisterCompilationStartAction(compilationContext =>
        {
            var entityCache = new ConcurrentDictionary<string, EntityInfo>(StringComparer.Ordinal);
            var contextCache = new ConcurrentDictionary<string, ContextInfo>(StringComparer.Ordinal);

            // Pre-discover all contexts and their entities
            foreach (var syntaxTree in compilationContext.Compilation.SyntaxTrees)
            {
#pragma warning disable RS1030 // GetSemanticModel in CompilationStart is acceptable for pre-caching
                var semanticModel = compilationContext.Compilation.GetSemanticModel(syntaxTree);
#pragma warning restore RS1030
                var root = syntaxTree.GetRoot(compilationContext.CancellationToken);

                foreach (var classDecl in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
                {
                    if (!ContextParser.HasQuarryContextAttribute(classDecl))
                        continue;

                    var contextInfo = ContextParser.ParseContext(classDecl, semanticModel, compilationContext.CancellationToken);
                    if (contextInfo == null)
                        continue;

                    contextCache.TryAdd(contextInfo.ClassName, contextInfo);

                    foreach (var entity in contextInfo.Entities)
                    {
                        entityCache.TryAdd(entity.EntityName, entity);
                    }
                }
            }

            compilationContext.RegisterSyntaxNodeAction(
                nodeContext => AnalyzeInvocation(nodeContext, entityCache, contextCache),
                SyntaxKind.InvocationExpression);
        });
    }

    private static void AnalyzeInvocation(
        SyntaxNodeAnalysisContext nodeContext,
        ConcurrentDictionary<string, EntityInfo> entityCache,
        ConcurrentDictionary<string, ContextInfo> contextCache)
    {
        if (!UsageSiteDiscovery.IsQuarryMethodCandidate(nodeContext.Node))
            return;

        var invocation = (InvocationExpressionSyntax)nodeContext.Node;
        var site = UsageSiteDiscovery.DiscoverRawCallSite(invocation, nodeContext.SemanticModel, nodeContext.CancellationToken);
        if (site == null)
            return;

        // Resolve entity and context info from caches
        EntityInfo? primaryEntity = null;
        if (site.EntityTypeName != null)
            entityCache.TryGetValue(site.EntityTypeName, out primaryEntity);

        List<EntityInfo>? joinedEntities = null;
        if (site.JoinedEntityTypeNames != null)
        {
            joinedEntities = new List<EntityInfo>();
            foreach (var joinedName in site.JoinedEntityTypeNames)
            {
                if (entityCache.TryGetValue(joinedName, out var joinedEntity))
                    joinedEntities.Add(joinedEntity);
            }
        }
        else if (site.JoinedEntityTypeName != null)
        {
            if (entityCache.TryGetValue(site.JoinedEntityTypeName, out var joinedEntity))
                joinedEntities = new List<EntityInfo> { joinedEntity };
        }

        ContextInfo? contextInfo = null;
        if (site.ContextClassName != null)
            contextCache.TryGetValue(site.ContextClassName, out contextInfo);

        var options = nodeContext.Options.AnalyzerConfigOptionsProvider.GetOptions(nodeContext.Node.SyntaxTree);

        var analysisContext = new QueryAnalysisContext(
            site,
            primaryEntity,
            joinedEntities,
            contextInfo,
            nodeContext.SemanticModel,
            nodeContext.Node,
            options);

        foreach (var rule in Rules)
        {
            foreach (var diagnostic in rule.Analyze(analysisContext))
            {
                nodeContext.ReportDiagnostic(diagnostic);
            }
        }
    }
}
