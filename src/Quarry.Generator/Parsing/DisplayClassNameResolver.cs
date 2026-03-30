using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Quarry.Generators.Parsing;

/// <summary>
/// Provides utilities for predicting compiler-generated display class names
/// and analyzing closure captures within methods. Used by
/// <see cref="DisplayClassEnricher"/> for batch enrichment.
/// Display class naming convention:
///   ContainingType+&lt;&gt;c__DisplayClass{methodOrdinal}_{closureOrdinal}
/// </summary>
internal static class DisplayClassNameResolver
{
    /// <summary>
    /// Computes the method ordinal: the index of the method in the containing type's
    /// GetMembers() array. ALL members count (backing fields, properties, accessor
    /// methods, events, fields, methods) because the C# compiler uses the same ordering.
    /// For local functions, matches against the synthesized method name pattern
    /// (e.g., &lt;&lt;Main&gt;$&gt;g__MethodName|N_M).
    /// </summary>
    internal static int ComputeMethodOrdinal(INamedTypeSymbol containingType, IMethodSymbol methodSymbol)
    {
        var members = containingType.GetMembers();

        // Direct match (regular methods, constructors, accessors)
        for (int i = 0; i < members.Length; i++)
        {
            if (SymbolEqualityComparer.Default.Equals(members[i], methodSymbol))
                return i;
        }

        return -1;
    }

    /// <summary>
    /// Attempts to resolve the type of a captured variable when the semantic model
    /// reports TypeKind.Error (typically because the variable is assigned from a
    /// generator-produced method whose return type isn't yet resolved).
    /// Handles patterns like: var x = await something.Method&lt;T&gt;()
    /// </summary>
    private static string? TryResolveErrorType(ISymbol symbol, SemanticModel semanticModel)
    {
        var declRef = symbol.DeclaringSyntaxReferences.FirstOrDefault();
        if (declRef == null)
            return null;

        var declSyntax = declRef.GetSyntax();
        if (declSyntax is not VariableDeclaratorSyntax { Initializer.Value: { } initValue })
            return null;

        // Unwrap await expressions: var x = await expr → analyze expr
        if (initValue is AwaitExpressionSyntax awaitExpr)
            initValue = awaitExpr.Expression;

        // Look for generic method invocations: something.Method<T>()
        // Extract T from the type argument list.
        if (initValue is InvocationExpressionSyntax invocation)
        {
            GenericNameSyntax? genericName = null;
            if (invocation.Expression is MemberAccessExpressionSyntax memberAccess
                && memberAccess.Name is GenericNameSyntax g1)
            {
                genericName = g1;
            }
            else if (invocation.Expression is GenericNameSyntax g2)
            {
                genericName = g2;
            }

            if (genericName != null && genericName.TypeArgumentList.Arguments.Count == 1)
            {
                var typeArg = genericName.TypeArgumentList.Arguments[0];
                var typeInfo = semanticModel.GetTypeInfo(typeArg);
                if (typeInfo.Type != null && typeInfo.Type.TypeKind != TypeKind.Error)
                    return typeInfo.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            }
        }

        return null;
    }

    /// <summary>
    /// Attempts to qualify an error type by checking the source file's using directives
    /// against known schema types in the compilation. Generated entity types are error
    /// types at generator time, but the corresponding Schema classes (user-defined) are
    /// resolvable. E.g., for error type "User", checks each imported namespace for
    /// "UserSchema". The entity is generated into the owning QuarryContext's namespace
    /// (not the schema namespace), so we search for the context class to resolve the
    /// correct namespace.
    /// </summary>
    private static string? TryQualifyErrorTypeFromUsings(
        ITypeSymbol errorType, ISymbol symbol, SemanticModel semanticModel)
    {
        var typeName = errorType.Name;
        if (string.IsNullOrEmpty(typeName) || typeName == "?")
            return null;

        var declRef = symbol.DeclaringSyntaxReferences.FirstOrDefault();
        if (declRef == null)
            return null;

        if (!(declRef.SyntaxTree.GetRoot() is CompilationUnitSyntax root))
            return null;

        var compilation = semanticModel.Compilation;
        var schemaName = typeName + "Schema";

        foreach (var usingDir in root.Usings)
        {
            if (usingDir.Alias != null || usingDir.Name == null)
                continue;

            var ns = usingDir.Name.ToString();
            var candidate = ns + "." + schemaName;
            var schemaType = compilation.GetTypeByMetadataName(candidate);
            if (schemaType != null)
            {
                // The schema lives in ns, but the generated entity is placed in
                // the owning context's namespace (which may differ from the schema's).
                var contextNs = FindContextNamespaceForSchema(compilation, schemaType);
                if (contextNs != null)
                    return "global::" + contextNs + "." + typeName;
                return "global::" + ns + "." + typeName;
            }
        }

        return null;
    }

    /// <summary>
    /// Searches the compilation for a [QuarryContext]-decorated class that references
    /// the given schema type via a QueryBuilder&lt;T&gt; member. Returns the context
    /// class's namespace, since generated entity types are placed there rather than
    /// in the schema namespace. Returns null if no owning context is found.
    /// </summary>
    private static string? FindContextNamespaceForSchema(
        Compilation compilation, INamedTypeSymbol schemaType)
    {
        foreach (var tree in compilation.SyntaxTrees)
        {
            foreach (var node in tree.GetRoot().DescendantNodes())
            {
                if (node is not ClassDeclarationSyntax classDecl)
                    continue;

                // Quick syntax filter: skip classes without a QuarryContext attribute
                bool maybeContext = false;
                foreach (var attrList in classDecl.AttributeLists)
                {
                    foreach (var attr in attrList.Attributes)
                    {
                        var name = attr.Name.ToString();
                        if (name == "QuarryContext" || name == "QuarryContextAttribute"
                            || name.EndsWith(".QuarryContext") || name.EndsWith(".QuarryContextAttribute"))
                        {
                            maybeContext = true;
                            break;
                        }
                    }
                    if (maybeContext) break;
                }
                if (!maybeContext) continue;

                var sm = compilation.GetSemanticModel(tree);
                if (sm.GetDeclaredSymbol(classDecl) is not INamedTypeSymbol classSymbol)
                    continue;

                // Check if any member returns a generic type parameterized with the schema
                // (e.g., QueryBuilder<FileSchema>)
                foreach (var member in classSymbol.GetMembers())
                {
                    ITypeSymbol? returnType = null;
                    if (member is IMethodSymbol m)
                        returnType = m.ReturnType;
                    else if (member is IPropertySymbol p)
                        returnType = p.Type;

                    if (returnType is INamedTypeSymbol nt
                        && nt.IsGenericType
                        && nt.TypeArguments.Length == 1
                        && (SymbolEqualityComparer.Default.Equals(nt.TypeArguments[0], schemaType)
                            || nt.TypeArguments[0].Name + "Schema" == schemaType.Name))
                    {
                        var ns = classSymbol.ContainingNamespace?.ToDisplayString();
                        if (!string.IsNullOrEmpty(ns))
                            return ns;
                    }
                }
            }
        }

        return null;
    }

    private static void AssignOrdinalsPreOrder(
        SyntaxNode node,
        HashSet<SyntaxNode> scopesWithCaptures,
        Dictionary<SyntaxNode, int> scopeOrdinals,
        ref int nextOrdinal)
    {
        if (scopesWithCaptures.Contains(node) && !scopeOrdinals.ContainsKey(node))
            scopeOrdinals[node] = nextOrdinal++;

        foreach (var child in node.ChildNodes())
        {
            AssignOrdinalsPreOrder(child, scopesWithCaptures, scopeOrdinals, ref nextOrdinal);
        }
    }

    private static SyntaxNode FindDeclaringScope(ISymbol variable, SyntaxNode methodRoot)
    {
        var declRef = variable.DeclaringSyntaxReferences.FirstOrDefault();
        if (declRef == null)
            return methodRoot;

        var declNode = declRef.GetSyntax();
        var current = declNode.Parent;
        while (current != null)
        {
            if (current is BlockSyntax)
                return current;
            if (current == methodRoot)
                return methodRoot;
            current = current.Parent;
        }
        return methodRoot;
    }

    /// <summary>
    /// Builds the scope-ordinal map and dataflow cache for all closures in a method.
    /// Called once per method by DisplayClassEnricher.
    /// </summary>
    internal static MethodClosureAnalysis AnalyzeMethodClosures(
        SyntaxNode methodSyntax,
        SemanticModel semanticModel)
    {
        var scopesWithCaptures = new HashSet<SyntaxNode>(SyntaxNodeComparer.Instance);
        var dataFlowByNode = new Dictionary<SyntaxNode, DataFlowAnalysis>(SyntaxNodeComparer.Instance);

        var allClosures = methodSyntax.DescendantNodes()
            .Where(n => n is LambdaExpressionSyntax || n is LocalFunctionStatementSyntax)
            .ToArray();

        foreach (var closure in allClosures)
        {
            DataFlowAnalysis? dataFlow = null;

            if (closure is LambdaExpressionSyntax lambda)
                dataFlow = semanticModel.AnalyzeDataFlow(lambda);
            else if (closure is LocalFunctionStatementSyntax localFunc && localFunc.Body != null)
                dataFlow = semanticModel.AnalyzeDataFlow(localFunc.Body);

            if (dataFlow == null || !dataFlow.Succeeded)
                continue;

            if (closure is LambdaExpressionSyntax lam)
                dataFlowByNode[lam] = dataFlow;

            foreach (var capturedVar in dataFlow.CapturedInside)
            {
                if (capturedVar is ILocalSymbol || (capturedVar is IParameterSymbol p && !p.IsThis))
                {
                    var declScope = FindDeclaringScope(capturedVar, methodSyntax);
                    if (declScope != null)
                        scopesWithCaptures.Add(declScope);
                }
            }
        }

        var scopeOrdinals = new Dictionary<SyntaxNode, int>(SyntaxNodeComparer.Instance);
        int nextOrdinal = 0;
        AssignOrdinalsPreOrder(methodSyntax, scopesWithCaptures, scopeOrdinals, ref nextOrdinal);

        return new MethodClosureAnalysis(dataFlowByNode, scopeOrdinals);
    }

    /// <summary>
    /// Looks up the closure ordinal for a lambda using pre-computed analysis.
    /// Returns 0 if the lambda has no captured local/parameter variables.
    /// </summary>
    internal static int LookupClosureOrdinal(
        MethodClosureAnalysis analysis,
        LambdaExpressionSyntax lambda,
        SyntaxNode methodSyntax)
    {
        if (!analysis.DataFlowByNode.TryGetValue(lambda, out var dataFlow))
            return 0;

        foreach (var capturedVar in dataFlow.CapturedInside)
        {
            if (capturedVar is ILocalSymbol || (capturedVar is IParameterSymbol p && !p.IsThis))
            {
                var declScope = FindDeclaringScope(capturedVar, methodSyntax);
                if (declScope != null && analysis.ScopeOrdinals.TryGetValue(declScope, out int ordinal))
                    return ordinal;
            }
        }

        return 0;
    }

    /// <summary>
    /// Overload that uses a pre-computed DataFlowAnalysis instead of calling AnalyzeDataFlow.
    /// </summary>
    public static Dictionary<string, string>? CollectCapturedVariableTypes(
        DataFlowAnalysis dataFlow,
        SemanticModel semanticModel)
    {
        if (!dataFlow.Succeeded)
            return null;

        var captured = dataFlow.CapturedInside
            .Where(s => s is ILocalSymbol || (s is IParameterSymbol p && !p.IsThis))
            .Distinct<ISymbol>(SymbolEqualityComparer.Default)
            .ToArray();

        if (captured.Length == 0)
            return null;

        var result = new Dictionary<string, string>();
        foreach (var symbol in captured)
        {
            string varName;
            ITypeSymbol? typeSymbol;
            if (symbol is ILocalSymbol local)
            {
                varName = local.Name;
                typeSymbol = local.Type;
            }
            else if (symbol is IParameterSymbol param)
            {
                varName = param.Name;
                typeSymbol = param.Type;
            }
            else continue;

            var varType = typeSymbol.TypeKind == TypeKind.Error
                ? (TryResolveErrorType(symbol, semanticModel)
                    ?? TryQualifyErrorTypeFromUsings(typeSymbol, symbol, semanticModel)
                    ?? "object")
                : typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            if (string.IsNullOrWhiteSpace(varType))
                varType = "object";

            result[varName] = varType;
        }

        return result.Count > 0 ? result : null;
    }

    internal sealed class MethodClosureAnalysis
    {
        public MethodClosureAnalysis(
            Dictionary<SyntaxNode, DataFlowAnalysis> dataFlowByNode,
            Dictionary<SyntaxNode, int> scopeOrdinals)
        {
            DataFlowByNode = dataFlowByNode;
            ScopeOrdinals = scopeOrdinals;
        }

        public Dictionary<SyntaxNode, DataFlowAnalysis> DataFlowByNode { get; }
        public Dictionary<SyntaxNode, int> ScopeOrdinals { get; }
    }

    internal sealed class SyntaxNodeComparer : IEqualityComparer<SyntaxNode>
    {
        public static readonly SyntaxNodeComparer Instance = new SyntaxNodeComparer();
        public bool Equals(SyntaxNode x, SyntaxNode y) => ReferenceEquals(x, y);
        public int GetHashCode(SyntaxNode obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
}
