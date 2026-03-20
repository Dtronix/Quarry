using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Quarry.Generators.Models;

namespace Quarry.Generators.Parsing;

/// <summary>
/// Performs intra-method dataflow analysis on query chains to determine
/// the optimization tier for pre-built SQL generation.
/// </summary>
/// <remarks>
/// Given an execution call site (e.g., ExecuteFetchAllAsync()), walks backward
/// and forward through the containing method to reconstruct the full query chain
/// and determine whether all clause combinations can be enumerated at compile time.
/// </remarks>
internal static class ChainAnalyzer
{
    /// <summary>
    /// Maximum number of conditional bits before downgrading from tier 1 to tier 2.
    /// 4 bits = up to 16 dispatch variants.
    /// </summary>
    private const int MaxTier1Bits = 4;

    /// <summary>
    /// Maximum nesting depth of if-blocks before abandoning analysis.
    /// </summary>
    private const int MaxIfNestingDepth = 2;

    /// <summary>
    /// Entry point: analyzes the chain ending at an execution call site.
    /// Returns null if the execution site's invocation syntax cannot be resolved.
    /// </summary>
    public static ChainAnalysisResult? AnalyzeChain(
        UsageSiteInfo executionSite,
        IReadOnlyList<UsageSiteInfo> allSitesInMethod,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (executionSite.InvocationSyntax is not InvocationExpressionSyntax executionInvocation)
            return null;

        cancellationToken.ThrowIfCancellationRequested();

        // Step 1: Determine if this is a direct fluent chain or a variable-based chain
        var receiverVariable = ResolveReceiverVariable(executionInvocation, semanticModel);

        if (receiverVariable == null)
        {
            // Direct fluent chain — all clauses are unconditional
            return AnalyzeDirectFluentChain(executionSite, executionInvocation, allSitesInMethod);
        }

        // Check for forked chains (QRY033) — applies to all tiers
        var forkedVarName = DetectForkedChain(
            receiverVariable, executionInvocation, allSitesInMethod, semanticModel);
        if (forkedVarName != null)
        {
            return new ChainAnalysisResult(
                tier: OptimizationTier.RuntimeBuild,
                clauses: Array.Empty<ChainedClauseSite>(),
                executionSite: executionSite,
                conditionalClauses: Array.Empty<ConditionalClause>(),
                possibleMasks: Array.Empty<ulong>(),
                notAnalyzableReason: $"Forked chain: variable '{forkedVarName}' consumed by multiple execution paths",
                forkedVariableName: forkedVarName);
        }

        // Variable-based chain — perform full dataflow analysis
        return AnalyzeVariableChain(
            executionSite, executionInvocation, receiverVariable,
            allSitesInMethod, semanticModel, cancellationToken);
    }

    /// <summary>
    /// Resolves the local variable that the execution call is invoked on.
    /// Returns null if the receiver is a direct fluent chain (no variable).
    /// </summary>
    private static ILocalSymbol? ResolveReceiverVariable(
        InvocationExpressionSyntax executionInvocation,
        SemanticModel semanticModel)
    {
        if (executionInvocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return null;

        var receiver = memberAccess.Expression;

        // Walk past any fluent chain on the receiver to find the root
        while (receiver is InvocationExpressionSyntax chainedInvocation)
        {
            if (chainedInvocation.Expression is MemberAccessExpressionSyntax chainedMemberAccess)
            {
                receiver = chainedMemberAccess.Expression;
            }
            else
            {
                break;
            }
        }

        // Check if the root is a local variable or parameter.
        // QuarryContext variables are chain roots, not builder variables —
        // treat chains rooted on context as direct fluent.
        if (receiver is IdentifierNameSyntax identifier)
        {
            var symbol = semanticModel.GetSymbolInfo(identifier).Symbol;
            ITypeSymbol? varType = symbol switch
            {
                ILocalSymbol local => local.Type,
                IParameterSymbol param => param.Type,
                _ => null
            };

            if (varType != null && !IsQuarryContextType(varType))
            {
                // IParameterSymbol: method parameters holding builder variables.
                // Variable-based chain analysis requires declaration sites in the method body,
                // which parameters lack. Return null to treat as direct fluent chain.
                return symbol as ILocalSymbol;
            }
        }

        return null;
    }

    /// <summary>
    /// Analyzes a direct fluent chain (e.g., db.Students.Where(...).Select(...).ExecuteFetchAllAsync()).
    /// All clauses are unconditional → tier 1 with a single mask value of 0.
    /// </summary>
    private static ChainAnalysisResult AnalyzeDirectFluentChain(
        UsageSiteInfo executionSite,
        InvocationExpressionSyntax executionInvocation,
        IReadOnlyList<UsageSiteInfo> allSitesInMethod)
    {
        var clauses = new List<ChainedClauseSite>();

        // Walk the fluent chain from execution backward, collecting clause sites
        var chainInvocations = CollectFluentChainInvocations(executionInvocation);

        // Match each invocation in the chain against allSitesInMethod
        var unmatchedMethodNames = new List<string>();
        foreach (var invocation in chainInvocations)
        {
            var matchedSite = FindMatchingSite(invocation, allSitesInMethod);
            if (matchedSite != null)
            {
                var role = MapInterceptorKindToClauseRole(matchedSite.Kind);
                if (role != null)
                {
                    clauses.Add(new ChainedClauseSite(matchedSite, isConditional: false, bitIndex: null, role: role.Value));
                }
            }
            else
            {
                // Track method names of unmatched invocations (e.g., Limit, Offset, AddWhereClause)
                var methodName = invocation.Expression is MemberAccessExpressionSyntax ma
                    ? ma.Name.Identifier.Text
                    : invocation.Expression.ToString();
                unmatchedMethodNames.Add(methodName);
            }
        }

        return new ChainAnalysisResult(
            tier: OptimizationTier.PrebuiltDispatch,
            clauses: clauses,
            executionSite: executionSite,
            conditionalClauses: Array.Empty<ConditionalClause>(),
            possibleMasks: new[] { 0UL },
            unmatchedMethodNames: unmatchedMethodNames.Count > 0 ? unmatchedMethodNames : null);
    }

    /// <summary>
    /// Analyzes a variable-based query chain with potential conditional branches.
    /// </summary>
    private static ChainAnalysisResult? AnalyzeVariableChain(
        UsageSiteInfo executionSite,
        InvocationExpressionSyntax executionInvocation,
        ILocalSymbol variable,
        IReadOnlyList<UsageSiteInfo> allSitesInMethod,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        // Find the containing method body
        var methodBody = FindContainingMethodBody(executionInvocation);
        if (methodBody == null)
        {
            return MakeRuntimeBuildResult(executionSite, "Could not find containing method body");
        }

        cancellationToken.ThrowIfCancellationRequested();

        // Pre-cache typed descendant node lists from a single tree walk
        var cache = new MethodBodyCache(methodBody);

        // Step 2 & 3: Find all assignments and build the flow graph
        var flowGraph = BuildFlowGraph(variable, cache, allSitesInMethod, semanticModel);

        // Step 5: Check for disqualifiers
        var disqualifyReason = CheckDisqualifiers(variable, cache, semanticModel);
        if (disqualifyReason != null)
        {
            return MakeRuntimeBuildResult(executionSite, disqualifyReason);
        }

        cancellationToken.ThrowIfCancellationRequested();

        // Also collect any clauses from the fluent chain on the execution invocation itself
        // e.g., query.Where(...).ExecuteFetchAllAsync() — the Where is on the execution chain
        var executionChainClauses = CollectExecutionChainClauses(executionInvocation, allSitesInMethod);

        // Step 4: Identify branch points
        var branchPoints = FindBranchPoints(flowGraph);

        // Check nesting depth
        if (HasExcessiveNesting(branchPoints))
        {
            return MakeRuntimeBuildResult(executionSite, "Conditional nesting depth exceeds maximum");
        }

        cancellationToken.ThrowIfCancellationRequested();

        // Step 6: Assign bit indices
        var conditionalClauses = new List<ConditionalClause>();
        var allChainedClauses = new List<ChainedClauseSite>();
        var bitIndex = 0;

        foreach (var node in flowGraph.Nodes)
        {
            // Process all matched sites in the flow node (a single assignment
            // like `var del = _db.Delete<T>().Where(...).Where(...)` may contain
            // multiple clause invocations in its fluent chain).
            var sitesToProcess = node.AllMatchedSites.Count > 0
                ? node.AllMatchedSites
                : (node.MatchedSite != null ? new List<UsageSiteInfo> { node.MatchedSite } : null);

            if (sitesToProcess == null)
                continue;

            foreach (var site in sitesToProcess)
            {
                var role = MapInterceptorKindToClauseRole(site.Kind);
                if (role == null)
                    continue;

                if (node.IsConditional)
                {
                    var branchKind = GetBranchKind(node, branchPoints);
                    conditionalClauses.Add(new ConditionalClause(bitIndex, site, branchKind));
                    allChainedClauses.Add(new ChainedClauseSite(site, isConditional: true, bitIndex: bitIndex, role: role.Value));
                    bitIndex++;
                }
                else
                {
                    allChainedClauses.Add(new ChainedClauseSite(site, isConditional: false, bitIndex: null, role: role.Value));
                }
            }
        }

        // Add clauses from the execution chain (these are always unconditional
        // since they're part of the terminal fluent chain)
        foreach (var clauseSite in executionChainClauses)
        {
            allChainedClauses.Add(clauseSite);
        }

        // Step 6 continued: Determine tier based on bit count
        var totalBits = bitIndex;
        OptimizationTier tier;

        if (totalBits == 0)
        {
            tier = OptimizationTier.PrebuiltDispatch;
        }
        else if (totalBits <= MaxTier1Bits)
        {
            tier = OptimizationTier.PrebuiltDispatch;
        }
        else
        {
            tier = OptimizationTier.PrequotedFragments;
        }

        // Step 7: Enumerate mask combinations
        var possibleMasks = tier == OptimizationTier.PrebuiltDispatch
            ? EnumerateMaskCombinations(branchPoints, conditionalClauses)
            : Array.Empty<ulong>();

        return new ChainAnalysisResult(
            tier: tier,
            clauses: allChainedClauses,
            executionSite: executionSite,
            conditionalClauses: conditionalClauses,
            possibleMasks: possibleMasks);
    }

    /// <summary>
    /// Collects invocations from a fluent chain, ordered from root to terminal.
    /// Excludes the terminal (execution) invocation itself.
    /// </summary>
    private static List<InvocationExpressionSyntax> CollectFluentChainInvocations(
        InvocationExpressionSyntax terminalInvocation)
    {
        var invocations = new List<InvocationExpressionSyntax>();
        var current = terminalInvocation;

        while (current.Expression is MemberAccessExpressionSyntax memberAccess &&
               memberAccess.Expression is InvocationExpressionSyntax parentInvocation)
        {
            invocations.Add(parentInvocation);
            current = parentInvocation;
        }

        invocations.Reverse(); // Root to terminal order
        return invocations;
    }

    /// <summary>
    /// Collects clause sites from the fluent chain on the execution invocation.
    /// For example, in query.Where(...).ExecuteFetchAllAsync(), the Where is collected.
    /// </summary>
    private static List<ChainedClauseSite> CollectExecutionChainClauses(
        InvocationExpressionSyntax executionInvocation,
        IReadOnlyList<UsageSiteInfo> allSitesInMethod)
    {
        var clauses = new List<ChainedClauseSite>();

        // Walk the fluent chain on the execution invocation (but not past the variable)
        var current = executionInvocation;
        while (current.Expression is MemberAccessExpressionSyntax memberAccess &&
               memberAccess.Expression is InvocationExpressionSyntax parentInvocation)
        {
            var matchedSite = FindMatchingSite(parentInvocation, allSitesInMethod);
            if (matchedSite != null)
            {
                var role = MapInterceptorKindToClauseRole(matchedSite.Kind);
                if (role != null)
                {
                    clauses.Add(new ChainedClauseSite(matchedSite, isConditional: false, bitIndex: null, role: role.Value));
                }
            }
            current = parentInvocation;
        }

        clauses.Reverse(); // Root to terminal order
        return clauses;
    }

    /// <summary>
    /// Finds the containing method body (BlockSyntax) for an invocation.
    /// </summary>
    private static BlockSyntax? FindContainingMethodBody(SyntaxNode node)
    {
        var current = node.Parent;
        while (current != null)
        {
            switch (current)
            {
                case MethodDeclarationSyntax method:
                    return method.Body;
                case LocalFunctionStatementSyntax localFunc:
                    return localFunc.Body;
                case AccessorDeclarationSyntax accessor:
                    return accessor.Body;
            }
            current = current.Parent;
        }
        return null;
    }

    /// <summary>
    /// Builds the variable flow graph by finding all assignments to the tracked variable.
    /// </summary>
    private static VariableFlowGraph BuildFlowGraph(
        ILocalSymbol variable,
        MethodBodyCache cache,
        IReadOnlyList<UsageSiteInfo> allSitesInMethod,
        SemanticModel semanticModel)
    {
        var nodes = new List<FlowNode>();

        // Walk all statements in the method body in order
        foreach (var statement in cache.Statements)
        {
            switch (statement)
            {
                case LocalDeclarationStatementSyntax localDecl:
                {
                    // Check if this declares our tracked variable
                    foreach (var declarator in localDecl.Declaration.Variables)
                    {
                        var declaredSymbol = semanticModel.GetDeclaredSymbol(declarator);
                        if (SymbolEqualityComparer.Default.Equals(declaredSymbol, variable) &&
                            declarator.Initializer?.Value != null)
                        {
                            var rhs = declarator.Initializer.Value;
                            var node = CreateFlowNode(statement, rhs, allSitesInMethod, semanticModel);
                            nodes.Add(node);
                        }
                    }
                    break;
                }

                case ExpressionStatementSyntax exprStmt
                    when exprStmt.Expression is AssignmentExpressionSyntax assignment:
                {
                    // Check if the LHS is our tracked variable
                    var lhsSymbol = semanticModel.GetSymbolInfo(assignment.Left).Symbol;
                    if (SymbolEqualityComparer.Default.Equals(lhsSymbol, variable))
                    {
                        var rhs = assignment.Right;
                        var node = CreateFlowNode(statement, rhs, allSitesInMethod, semanticModel);
                        nodes.Add(node);
                    }
                    break;
                }
            }
        }

        return new VariableFlowGraph(nodes);
    }

    /// <summary>
    /// Creates a flow node from an assignment statement, matching the RHS against known usage sites.
    /// </summary>
    private static FlowNode CreateFlowNode(
        StatementSyntax statement,
        ExpressionSyntax rhs,
        IReadOnlyList<UsageSiteInfo> allSitesInMethod,
        SemanticModel semanticModel)
    {
        // Find all invocations in the RHS (could be a fluent chain)
        var rhsInvocations = rhs.DescendantNodesAndSelf()
            .OfType<InvocationExpressionSyntax>()
            .ToList();

        // Match each invocation against allSitesInMethod
        var matchedSites = new List<UsageSiteInfo>();
        foreach (var invocation in rhsInvocations)
        {
            var site = FindMatchingSite(invocation, allSitesInMethod);
            if (site != null)
            {
                matchedSites.Add(site);
            }
        }

        // Determine nesting context
        var (containingIf, isInElseBranch) = FindContainingIf(statement);

        // Use the last (outermost in fluent chain) matched site as the primary
        var primarySite = matchedSites.Count > 0 ? matchedSites[matchedSites.Count - 1] : null;

        return new FlowNode(
            statement: statement,
            matchedSite: primarySite,
            allMatchedSites: matchedSites,
            containingIf: containingIf,
            isInElseBranch: isInElseBranch,
            isConditional: containingIf != null);
    }

    /// <summary>
    /// Finds the immediately containing IfStatementSyntax for a statement,
    /// and determines if the statement is in the else branch.
    /// </summary>
    private static (IfStatementSyntax? ContainingIf, bool IsInElseBranch) FindContainingIf(
        StatementSyntax statement)
    {
        var current = statement.Parent;
        while (current != null)
        {
            // Stop at method boundaries
            if (current is MethodDeclarationSyntax or LocalFunctionStatementSyntax or
                AccessorDeclarationSyntax)
            {
                return (null, false);
            }

            if (current is IfStatementSyntax ifStatement)
            {
                // Determine if the statement is in the if-branch or else-branch
                var isInElse = ifStatement.Else != null &&
                               ifStatement.Else.Statement.Contains(statement);
                return (ifStatement, isInElse);
            }

            // Check if we're inside an else clause's body
            if (current is ElseClauseSyntax elseClause &&
                elseClause.Parent is IfStatementSyntax parentIf)
            {
                return (parentIf, true);
            }

            current = current.Parent;
        }

        return (null, false);
    }

    /// <summary>
    /// Identifies branch points from the flow graph by grouping nodes by their containing if-statement.
    /// </summary>
    private static List<BranchPoint> FindBranchPoints(VariableFlowGraph flowGraph)
    {
        var branchPoints = new List<BranchPoint>();
        var ifGroups = new Dictionary<IfStatementSyntax, List<FlowNode>>();

        foreach (var node in flowGraph.Nodes)
        {
            if (node.ContainingIf != null)
            {
                if (!ifGroups.TryGetValue(node.ContainingIf, out var group))
                {
                    group = new List<FlowNode>();
                    ifGroups[node.ContainingIf] = group;
                }
                group.Add(node);
            }
        }

        foreach (var kvp in ifGroups)
        {
            var ifStatement = kvp.Key;
            var nodes = kvp.Value;
            var hasIfBranch = nodes.Any(n => !n.IsInElseBranch);
            var hasElseBranch = nodes.Any(n => n.IsInElseBranch);

            var branchKind = hasIfBranch && hasElseBranch
                ? BranchKind.MutuallyExclusive
                : BranchKind.Independent;

            branchPoints.Add(new BranchPoint(ifStatement, nodes, branchKind));
        }

        return branchPoints;
    }

    /// <summary>
    /// Checks for disqualifying patterns that prevent analysis.
    /// Returns the reason string if disqualified, null otherwise.
    /// </summary>
    private static string? CheckDisqualifiers(
        ILocalSymbol variable,
        MethodBodyCache cache,
        SemanticModel semanticModel)
    {
        foreach (var statement in cache.Statements)
        {
            // Check assignments
            ExpressionSyntax? lhs = null;

            if (statement is ExpressionStatementSyntax exprStmt &&
                exprStmt.Expression is AssignmentExpressionSyntax assignment)
            {
                var lhsSymbol = semanticModel.GetSymbolInfo(assignment.Left).Symbol;
                if (SymbolEqualityComparer.Default.Equals(lhsSymbol, variable))
                {
                    lhs = assignment.Left;

                    // Check: assigned inside a loop?
                    if (IsInsideLoop(statement))
                        return "Variable assigned inside a loop body";

                    // Check: assigned inside try/catch/finally?
                    if (IsInsideTryCatchFinally(statement))
                        return "Variable assigned inside a try/catch/finally block";

                    // Check: assigned from opaque method return value?
                    var rhs = assignment.Right;
                    if (IsOpaqueAssignment(rhs, semanticModel))
                        return "Variable assigned from non-Quarry method return value";
                }
            }

            if (statement is LocalDeclarationStatementSyntax localDecl)
            {
                foreach (var declarator in localDecl.Declaration.Variables)
                {
                    var declaredSymbol = semanticModel.GetDeclaredSymbol(declarator);
                    if (SymbolEqualityComparer.Default.Equals(declaredSymbol, variable) &&
                        declarator.Initializer?.Value != null)
                    {
                        // Check: declared inside a loop?
                        if (IsInsideLoop(statement))
                            return "Variable declared inside a loop body";

                        // Check: declared inside try/catch/finally?
                        if (IsInsideTryCatchFinally(statement))
                            return "Variable declared inside a try/catch/finally block";

                        // Check: assigned from opaque method return value?
                        if (IsOpaqueAssignment(declarator.Initializer.Value, semanticModel))
                            return "Variable assigned from non-Quarry method return value";
                    }
                }
            }
        }

        // Check: variable passed as argument to a method call
        foreach (var invocation in cache.Invocations)
        {
            foreach (var arg in invocation.ArgumentList.Arguments)
            {
                var argSymbol = semanticModel.GetSymbolInfo(arg.Expression).Symbol;
                if (SymbolEqualityComparer.Default.Equals(argSymbol, variable))
                    return "Variable passed as argument to a method call";
            }
        }

        // Check: variable captured in lambda or local function
        foreach (var lambda in cache.Lambdas)
        {
            var dataFlow = semanticModel.AnalyzeDataFlow(lambda);
            if (dataFlow != null && dataFlow.Succeeded)
            {
                foreach (var captured in dataFlow.Captured)
                {
                    if (SymbolEqualityComparer.Default.Equals(captured, variable))
                        return "Variable captured in a lambda expression";
                }
            }
        }

        foreach (var localFunc in cache.LocalFunctions)
        {
            if (localFunc.Body != null)
            {
                var dataFlow = semanticModel.AnalyzeDataFlow(localFunc.Body);
                if (dataFlow != null && dataFlow.Succeeded)
                {
                    foreach (var captured in dataFlow.Captured)
                    {
                        if (SymbolEqualityComparer.Default.Equals(captured, variable))
                            return "Variable captured in a local function";
                    }
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Checks if a statement is inside any loop construct.
    /// </summary>
    private static bool IsInsideLoop(StatementSyntax statement)
    {
        var current = statement.Parent;
        while (current != null)
        {
            if (current is MethodDeclarationSyntax or LocalFunctionStatementSyntax)
                return false;

            if (current is ForStatementSyntax or ForEachStatementSyntax or
                WhileStatementSyntax or DoStatementSyntax)
                return true;

            current = current.Parent;
        }
        return false;
    }

    /// <summary>
    /// Checks if a statement is inside a try/catch/finally block.
    /// </summary>
    private static bool IsInsideTryCatchFinally(StatementSyntax statement)
    {
        var current = statement.Parent;
        while (current != null)
        {
            if (current is MethodDeclarationSyntax or LocalFunctionStatementSyntax)
                return false;

            if (current is TryStatementSyntax or CatchClauseSyntax or FinallyClauseSyntax)
                return true;

            current = current.Parent;
        }
        return false;
    }

    /// <summary>
    /// Checks if an RHS expression is an opaque assignment (not a Quarry builder method call).
    /// </summary>
    private static bool IsOpaqueAssignment(ExpressionSyntax rhs, SemanticModel semanticModel)
    {
        // Walk through the expression to find the innermost call
        var rootInvocation = rhs as InvocationExpressionSyntax;

        // If the RHS is not an invocation, check if it's a property access (db.Students)
        if (rootInvocation == null)
        {
            if (rhs is MemberAccessExpressionSyntax memberAccess)
            {
                var symbol = semanticModel.GetSymbolInfo(memberAccess).Symbol;
                if (symbol is IPropertySymbol prop)
                {
                    // Check if the property type is a Quarry builder
                    return !IsQuarryBuilderType(prop.Type);
                }
            }

            // Simple identifier (another variable) or literal — opaque
            if (rhs is IdentifierNameSyntax)
                return false; // Variable reference, not opaque (tracked separately)

            return true; // Other expressions are opaque
        }

        // Walk the fluent chain to find the root
        var current = rootInvocation;
        while (current != null)
        {
            var methodSymbol = semanticModel.GetSymbolInfo(current).Symbol as IMethodSymbol;
            if (methodSymbol != null)
            {
                // Check if this is a Quarry builder method
                if (!IsQuarryBuilderMethod(methodSymbol))
                    return true;
            }

            // Walk to parent invocation in fluent chain
            if (current.Expression is MemberAccessExpressionSyntax chainedMemberAccess &&
                chainedMemberAccess.Expression is InvocationExpressionSyntax parentInvocation)
            {
                current = parentInvocation;
            }
            else
            {
                break;
            }
        }

        return false;
    }

    /// <summary>
    /// Checks if a method belongs to a Quarry builder type or is a factory method
    /// on QuarryContext that returns a builder (e.g., Delete&lt;T&gt;(), Update&lt;T&gt;()).
    /// </summary>
    private static bool IsQuarryBuilderMethod(IMethodSymbol method)
    {
        var containingType = method.ContainingType;
        if (containingType == null)
            return false;

        if (IsQuarryBuilderType(containingType))
            return true;

        // Factory methods on QuarryContext (Delete<T>, Update<T>, Insert, etc.)
        // that return builder types are also valid Quarry chain roots.
        if (IsQuarryContextType(containingType))
            return true;

        return false;
    }

    /// <summary>
    /// Checks if a type is a Quarry builder type.
    /// </summary>
    private static bool IsQuarryBuilderType(ITypeSymbol type)
    {
        var typeName = type.Name;
        return typeName is "QueryBuilder" or "JoinedQueryBuilder" or "JoinedQueryBuilder3"
            or "JoinedQueryBuilder4" or "UpdateBuilder" or "ExecutableUpdateBuilder"
            or "DeleteBuilder" or "ExecutableDeleteBuilder" or "InsertBuilder"
            or "IQueryBuilder" or "IJoinedQueryBuilder" or "IJoinedQueryBuilder3"
            or "IJoinedQueryBuilder4" or "IUpdateBuilder" or "IExecutableUpdateBuilder"
            or "IDeleteBuilder" or "IExecutableDeleteBuilder" or "IInsertBuilder"
            or "EntityAccessor" or "IEntityAccessor";
    }

    /// <summary>
    /// Checks if a type derives from QuarryContext.
    /// </summary>
    private static bool IsQuarryContextType(ITypeSymbol type)
    {
        var current = type;
        while (current != null)
        {
            if (current.Name == "QuarryContext")
                return true;
            current = current.BaseType;
        }
        return false;
    }

    /// <summary>
    /// Checks if any branch point has excessive nesting depth.
    /// </summary>
    private static bool HasExcessiveNesting(List<BranchPoint> branchPoints)
    {
        foreach (var bp in branchPoints)
        {
            var depth = GetIfNestingDepth(bp.IfStatement);
            if (depth > MaxIfNestingDepth)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Gets the nesting depth of an if-statement within other if-statements.
    /// </summary>
    private static int GetIfNestingDepth(IfStatementSyntax ifStatement)
    {
        var depth = 0;
        var current = ifStatement.Parent;
        while (current != null)
        {
            if (current is MethodDeclarationSyntax or LocalFunctionStatementSyntax)
                break;

            if (current is IfStatementSyntax)
                depth++;

            current = current.Parent;
        }
        return depth;
    }

    /// <summary>
    /// Gets the branch kind for a conditional flow node.
    /// </summary>
    private static BranchKind GetBranchKind(FlowNode node, List<BranchPoint> branchPoints)
    {
        if (node.ContainingIf == null)
            return BranchKind.Independent;

        foreach (var bp in branchPoints)
        {
            if (bp.IfStatement == node.ContainingIf)
                return bp.Kind;
        }

        return BranchKind.Independent;
    }

    /// <summary>
    /// Enumerates all possible ClauseMask values from branch points and conditional clauses.
    /// </summary>
    private static IReadOnlyList<ulong> EnumerateMaskCombinations(
        List<BranchPoint> branchPoints,
        List<ConditionalClause> conditionalClauses)
    {
        if (conditionalClauses.Count == 0)
            return new[] { 0UL };

        // Group conditional clauses by their branch point
        var independentBits = new List<int>();
        var exclusiveGroups = new List<List<int>>();

        foreach (var bp in branchPoints)
        {
            var bitsInBranch = conditionalClauses
                .Where(cc => bp.Nodes.Any(n => n.MatchedSite == cc.Site))
                .Select(cc => cc.BitIndex)
                .ToList();

            if (bitsInBranch.Count == 0)
                continue;

            if (bp.Kind == BranchKind.Independent)
            {
                independentBits.AddRange(bitsInBranch);
            }
            else
            {
                exclusiveGroups.Add(bitsInBranch);
            }
        }

        // Also add any conditional clauses not associated with a branch point as independent
        var allBranchBits = new HashSet<int>(independentBits);
        foreach (var group in exclusiveGroups)
            foreach (var bit in group)
                allBranchBits.Add(bit);

        foreach (var cc in conditionalClauses)
        {
            if (!allBranchBits.Contains(cc.BitIndex))
                independentBits.Add(cc.BitIndex);
        }

        // Orphaned conditional clauses (not mapped to any branch point) are treated as independent.
        // This can happen when a clause's matched site wasn't grouped into a BranchPoint because
        // the FlowNode tracking matched a different site as primary. These are safely independent
        // since they represent a single conditional path with no mutual exclusivity.

        // Build combinations: independent bits contribute 2^N each,
        // exclusive groups contribute one-of-N each
        var masks = new List<ulong> { 0UL };

        // Independent bits: each can be on or off
        foreach (var bit in independentBits)
        {
            var newMasks = new List<ulong>(masks.Count * 2);
            foreach (var mask in masks)
            {
                newMasks.Add(mask);                      // bit off
                newMasks.Add(mask | (1UL << bit));       // bit on
            }
            masks = newMasks;
        }

        // Mutually exclusive groups: exactly one bit from the group is set
        foreach (var group in exclusiveGroups)
        {
            var newMasks = new List<ulong>(masks.Count * group.Count);
            foreach (var mask in masks)
            {
                foreach (var bit in group)
                {
                    newMasks.Add(mask | (1UL << bit));
                }
            }
            masks = newMasks;
        }

        return masks;
    }

    /// <summary>
    /// Finds a UsageSiteInfo matching a given invocation by comparing source spans.
    /// </summary>
    private static UsageSiteInfo? FindMatchingSite(
        InvocationExpressionSyntax invocation,
        IReadOnlyList<UsageSiteInfo> allSites)
    {
        var span = invocation.Span;

        foreach (var site in allSites)
        {
            if (site.InvocationSyntax is InvocationExpressionSyntax siteInvocation &&
                siteInvocation.Span == span &&
                siteInvocation.SyntaxTree == invocation.SyntaxTree)
            {
                return site;
            }
        }

        return null;
    }

    /// <summary>
    /// Maps an InterceptorKind to a ClauseRole.
    /// Returns null for kinds that are not clause roles (e.g., execution methods).
    /// </summary>
    private static ClauseRole? MapInterceptorKindToClauseRole(InterceptorKind kind)
    {
        return kind switch
        {
            InterceptorKind.Select => ClauseRole.Select,
            InterceptorKind.Where => ClauseRole.Where,
            InterceptorKind.OrderBy => ClauseRole.OrderBy,
            InterceptorKind.ThenBy => ClauseRole.ThenBy,
            InterceptorKind.GroupBy => ClauseRole.GroupBy,
            InterceptorKind.Having => ClauseRole.Having,
            InterceptorKind.Join => ClauseRole.Join,
            InterceptorKind.LeftJoin => ClauseRole.Join,
            InterceptorKind.RightJoin => ClauseRole.Join,
            InterceptorKind.Set => ClauseRole.Set,
            InterceptorKind.DeleteWhere => ClauseRole.DeleteWhere,
            InterceptorKind.UpdateSet => ClauseRole.UpdateSet,
            InterceptorKind.UpdateSetAction => ClauseRole.UpdateSet,
            InterceptorKind.UpdateSetPoco => ClauseRole.UpdateSet,
            InterceptorKind.UpdateWhere => ClauseRole.UpdateWhere,
            InterceptorKind.Limit => ClauseRole.Limit,
            InterceptorKind.Offset => ClauseRole.Offset,
            InterceptorKind.Distinct => ClauseRole.Distinct,
            InterceptorKind.WithTimeout => ClauseRole.WithTimeout,
            InterceptorKind.ChainRoot => ClauseRole.ChainRoot,
            InterceptorKind.DeleteTransition => ClauseRole.DeleteTransition,
            InterceptorKind.UpdateTransition => ClauseRole.UpdateTransition,
            InterceptorKind.AllTransition => ClauseRole.AllTransition,
            InterceptorKind.InsertTransition => ClauseRole.InsertTransition,
            _ => null
        };
    }

    /// <summary>
    /// Creates a tier 3 (RuntimeBuild) result with a reason.
    /// </summary>
    private static ChainAnalysisResult MakeRuntimeBuildResult(
        UsageSiteInfo executionSite,
        string reason)
    {
        return new ChainAnalysisResult(
            tier: OptimizationTier.RuntimeBuild,
            clauses: Array.Empty<ChainedClauseSite>(),
            executionSite: executionSite,
            conditionalClauses: Array.Empty<ConditionalClause>(),
            possibleMasks: Array.Empty<ulong>(),
            notAnalyzableReason: reason);
    }

    /// <summary>
    /// Detects forked chains: a builder variable consumed by multiple execution-terminating paths.
    /// Returns the variable name if a fork is detected, null otherwise.
    /// </summary>
    /// <remarks>
    /// This applies to all optimization tiers. A builder variable that forks into
    /// multiple execution paths is a compile error (QRY033) regardless of tier.
    /// </remarks>
    internal static string? DetectForkedChain(
        ILocalSymbol variable,
        InvocationExpressionSyntax executionInvocation,
        IReadOnlyList<UsageSiteInfo> allSitesInMethod,
        SemanticModel semanticModel)
    {
        // Only flag builder variables, not context variables.
        // Context variables (QuarryContext subclasses) are expected to be reused across
        // multiple queries. Builder variables should not be.
        if (IsQuarryContextType(variable.Type))
            return null;

        var executionCount = 0;

        foreach (var site in allSitesInMethod)
        {
            if (!IsExecutionKind(site.Kind))
                continue;

            if (site.InvocationSyntax is not InvocationExpressionSyntax siteInvocation)
                continue;

            // Resolve the receiver variable for this execution site
            var siteReceiver = ResolveReceiverVariable(siteInvocation, semanticModel);
            if (siteReceiver != null && SymbolEqualityComparer.Default.Equals(siteReceiver, variable))
            {
                executionCount++;
                if (executionCount > 1)
                    return variable.Name;
            }
        }

        return null;
    }

    /// <summary>
    /// Checks if an InterceptorKind represents an execution method.
    /// </summary>
    internal static bool IsExecutionKind(InterceptorKind kind)
    {
        return kind is InterceptorKind.ExecuteFetchAll
            or InterceptorKind.ExecuteFetchFirst
            or InterceptorKind.ExecuteFetchFirstOrDefault
            or InterceptorKind.ExecuteFetchSingle
            or InterceptorKind.ExecuteScalar
            or InterceptorKind.ExecuteNonQuery
            or InterceptorKind.ToAsyncEnumerable
            or InterceptorKind.ToDiagnostics
            or InterceptorKind.InsertExecuteNonQuery
            or InterceptorKind.InsertExecuteScalar
            or InterceptorKind.InsertToDiagnostics;
    }

    #region Internal Types

    /// <summary>
    /// Caches typed descendant node lists from a single tree walk of the method body.
    /// Avoids repeated O(N) DescendantNodes() traversals.
    /// </summary>
    private sealed class MethodBodyCache
    {
        public readonly List<StatementSyntax> Statements;
        public readonly List<InvocationExpressionSyntax> Invocations;
        public readonly List<LambdaExpressionSyntax> Lambdas;
        public readonly List<LocalFunctionStatementSyntax> LocalFunctions;

        public MethodBodyCache(BlockSyntax methodBody)
        {
            Statements = new List<StatementSyntax>();
            Invocations = new List<InvocationExpressionSyntax>();
            Lambdas = new List<LambdaExpressionSyntax>();
            LocalFunctions = new List<LocalFunctionStatementSyntax>();

            foreach (var node in methodBody.DescendantNodes())
            {
                if (node is StatementSyntax stmt)
                    Statements.Add(stmt);
                if (node is InvocationExpressionSyntax inv)
                    Invocations.Add(inv);
                if (node is LambdaExpressionSyntax lambda)
                    Lambdas.Add(lambda);
                if (node is LocalFunctionStatementSyntax localFunc)
                    LocalFunctions.Add(localFunc);
            }
        }
    }

    /// <summary>
    /// Represents the linear flow of assignments to a tracked variable.
    /// </summary>
    private sealed class VariableFlowGraph
    {
        public VariableFlowGraph(List<FlowNode> nodes)
        {
            Nodes = nodes;
        }

        public List<FlowNode> Nodes { get; }
    }

    /// <summary>
    /// A single assignment node in the flow graph.
    /// </summary>
    private sealed class FlowNode
    {
        public FlowNode(
            StatementSyntax statement,
            UsageSiteInfo? matchedSite,
            List<UsageSiteInfo> allMatchedSites,
            IfStatementSyntax? containingIf,
            bool isInElseBranch,
            bool isConditional)
        {
            Statement = statement;
            MatchedSite = matchedSite;
            AllMatchedSites = allMatchedSites;
            ContainingIf = containingIf;
            IsInElseBranch = isInElseBranch;
            IsConditional = isConditional;
        }

        public StatementSyntax Statement { get; }
        public UsageSiteInfo? MatchedSite { get; }
        public List<UsageSiteInfo> AllMatchedSites { get; }
        public IfStatementSyntax? ContainingIf { get; }
        public bool IsInElseBranch { get; }
        public bool IsConditional { get; }
    }

    /// <summary>
    /// A branch point in the flow graph where execution may diverge.
    /// </summary>
    private sealed class BranchPoint
    {
        public BranchPoint(
            IfStatementSyntax ifStatement,
            List<FlowNode> nodes,
            BranchKind kind)
        {
            IfStatement = ifStatement;
            Nodes = nodes;
            Kind = kind;
        }

        public IfStatementSyntax IfStatement { get; }
        public List<FlowNode> Nodes { get; }
        public BranchKind Kind { get; }
    }

    #endregion
}
