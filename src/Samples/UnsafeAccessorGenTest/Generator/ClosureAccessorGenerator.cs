using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace UnsafeAccessorGenTest.Generator;

/// <summary>
/// Proof-of-concept source generator that:
/// 1. Finds calls to QueryLike.Where(Func&lt;T, bool&gt;)
/// 2. Analyzes the lambda for captured variables
/// 3. Predicts the compiler-generated display class name
/// 4. Emits [UnsafeAccessor] + [UnsafeAccessorType] extern methods to extract captured values
/// 5. Emits an interceptor that uses the accessors instead of reflection
/// </summary>
[Generator]
public class ClosureAccessorGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var whereCalls = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => IsWhereInvocation(node),
                transform: static (ctx, ct) => AnalyzeWhereLambda(ctx, ct))
            .Where(static info => info != null);

        context.RegisterSourceOutput(whereCalls.Collect(), GenerateAccessors);
    }

    private static bool IsWhereInvocation(SyntaxNode node)
    {
        return node is InvocationExpressionSyntax invocation
            && invocation.Expression is MemberAccessExpressionSyntax memberAccess
            && memberAccess.Name.Identifier.Text == "Where"
            && invocation.ArgumentList.Arguments.Count == 1
            && invocation.ArgumentList.Arguments[0].Expression is LambdaExpressionSyntax;
    }

    private static CaptureInfo? AnalyzeWhereLambda(GeneratorSyntaxContext ctx, System.Threading.CancellationToken ct)
    {
        var invocation = (InvocationExpressionSyntax)ctx.Node;
        var lambda = (LambdaExpressionSyntax)invocation.ArgumentList.Arguments[0].Expression;

        var semanticModel = ctx.SemanticModel;
        var dataFlow = semanticModel.AnalyzeDataFlow(lambda);
        if (dataFlow == null || !dataFlow.Succeeded)
            return null;

        // Use only CapturedInside: variables declared outside the lambda and
        // directly referenced inside it. Do NOT use Captured (which includes
        // variables captured by sibling closures in the same method scope).
        var captured = dataFlow.CapturedInside
            .Where(s => s is ILocalSymbol || s is IParameterSymbol)
            .Distinct<ISymbol>(SymbolEqualityComparer.Default)
            .ToArray();

        if (captured.Length == 0)
            return null;

        var containingMethod = semanticModel.GetEnclosingSymbol(invocation.SpanStart);
        while (containingMethod != null && containingMethod is not IMethodSymbol)
            containingMethod = containingMethod.ContainingSymbol;

        if (containingMethod is not IMethodSymbol methodSymbol)
            return null;

        var containingType = methodSymbol.ContainingType;
        if (containingType == null)
            return null;

        // --- Method ordinal: index of this method among ALL members in the type ---
        // The C# compiler uses GetMembers() ordering (lexical source order).
        // ALL members count: backing fields, properties, accessor methods, events, fields, methods.
        int methodOrdinal = ComputeMethodOrdinal(containingType, methodSymbol);
        if (methodOrdinal < 0)
            return null;

        // --- Closure ordinal: which closure scope this lambda's captures belong to ---
        var methodSyntax = methodSymbol.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(ct);
        int scopeOrdinal = 0;
        if (methodSyntax != null)
            scopeOrdinal = ComputeClosureOrdinal(methodSyntax, lambda, semanticModel);

        // Build display class name
        var typeName = containingType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        var displayClassName = $"{typeName}+<>c__DisplayClass{methodOrdinal}_{scopeOrdinal}";

        // Collect captured variable info
        var variables = new List<CapturedVariable>();
        foreach (var symbol in captured)
        {
            string varName;
            string varType;
            if (symbol is ILocalSymbol local)
            {
                varName = local.Name;
                varType = local.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            }
            else if (symbol is IParameterSymbol param)
            {
                varName = param.Name;
                varType = param.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            }
            else continue;

            variables.Add(new CapturedVariable(varName, varType));
        }

        var lineSpan = invocation.GetLocation().GetLineSpan();
        var filePath = lineSpan.Path;
        var line = lineSpan.StartLinePosition.Line + 1;
        var memberAccess = (MemberAccessExpressionSyntax)invocation.Expression;
        var column = memberAccess.Name.SpanStart - invocation.SyntaxTree.GetText(ct).Lines[lineSpan.StartLinePosition.Line].Start + 1;

        return new CaptureInfo(
            displayClassName,
            variables.ToArray(),
            typeName,
            filePath,
            line,
            column);
    }

    /// <summary>
    /// Computes the method ordinal: the index of <paramref name="methodSymbol"/> in the
    /// containing type's GetMembers() array. ALL members count (backing fields, properties,
    /// accessor methods, events, fields, methods) because the C# compiler uses the same
    /// GetMembers() ordering for display class naming.
    /// </summary>
    internal static int ComputeMethodOrdinal(INamedTypeSymbol containingType, IMethodSymbol methodSymbol)
    {
        var members = containingType.GetMembers();
        for (int i = 0; i < members.Length; i++)
        {
            if (SymbolEqualityComparer.Default.Equals(members[i], methodSymbol))
                return i;
        }
        return -1;
    }

    /// <summary>
    /// Computes the closure ordinal for a lambda within a method.
    /// Roslyn assigns display class ordinals in scope-tree pre-order: the method body
    /// scope (if it has captured variables) gets ordinal 0, then child scopes in
    /// source order. Only scopes that declare captured variables get an ordinal.
    /// </summary>
    internal static int ComputeClosureOrdinal(
        SyntaxNode methodSyntax,
        LambdaExpressionSyntax targetLambda,
        SemanticModel semanticModel)
    {
        // Step 1: Find ALL scopes that declare captured variables.
        // A scope gets a display class if ANY closure (lambda or local function)
        // captures a variable declared in that scope.
        var scopesWithCaptures = new HashSet<SyntaxNode>(SyntaxNodeComparer.Instance);

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

            foreach (var capturedVar in dataFlow.CapturedInside)
            {
                if (capturedVar is ILocalSymbol || capturedVar is IParameterSymbol)
                {
                    var declScope = FindDeclaringScope(capturedVar, methodSyntax);
                    if (declScope != null)
                        scopesWithCaptures.Add(declScope);
                }
            }
        }

        // Step 2: Walk all block scopes in pre-order (parent before children,
        // method body first). Assign ordinals only to scopes that have captures.
        var scopeOrdinals = new Dictionary<SyntaxNode, int>(SyntaxNodeComparer.Instance);
        int nextOrdinal = 0;
        AssignOrdinalsPreOrder(methodSyntax, scopesWithCaptures, scopeOrdinals, ref nextOrdinal);

        // Step 3: Find the target lambda's scope ordinal
        var targetDataFlow = semanticModel.AnalyzeDataFlow(targetLambda);
        if (targetDataFlow != null && targetDataFlow.Succeeded)
        {
            foreach (var capturedVar in targetDataFlow.CapturedInside)
            {
                if (capturedVar is ILocalSymbol || capturedVar is IParameterSymbol)
                {
                    var declScope = FindDeclaringScope(capturedVar, methodSyntax);
                    if (declScope != null && scopeOrdinals.TryGetValue(declScope, out int ordinal))
                        return ordinal;
                }
            }
        }

        return 0;
    }

    /// <summary>
    /// Walks the scope tree in pre-order, assigning ordinals to scopes that have captures.
    /// </summary>
    private static void AssignOrdinalsPreOrder(
        SyntaxNode node,
        HashSet<SyntaxNode> scopesWithCaptures,
        Dictionary<SyntaxNode, int> scopeOrdinals,
        ref int nextOrdinal)
    {
        // Check if this node is a scope with captures
        if (scopesWithCaptures.Contains(node) && !scopeOrdinals.ContainsKey(node))
            scopeOrdinals[node] = nextOrdinal++;

        // Recurse into child nodes, looking for block scopes
        foreach (var child in node.ChildNodes())
        {
            if (child is BlockSyntax)
                AssignOrdinalsPreOrder(child, scopesWithCaptures, scopeOrdinals, ref nextOrdinal);
            else
                AssignOrdinalsPreOrder(child, scopesWithCaptures, scopeOrdinals, ref nextOrdinal);
        }
    }

    /// <summary>
    /// Finds the scope (block) where a variable is declared.
    /// </summary>
    private static SyntaxNode? FindDeclaringScope(ISymbol variable, SyntaxNode methodRoot)
    {
        var declRef = variable.DeclaringSyntaxReferences.FirstOrDefault();
        if (declRef == null)
            return methodRoot; // Parameters are declared at method scope

        var declNode = declRef.GetSyntax();

        // Walk up to find the enclosing block scope
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

    /// <summary>Reference equality comparer for syntax nodes.</summary>
    private sealed class SyntaxNodeComparer : IEqualityComparer<SyntaxNode>
    {
        public static readonly SyntaxNodeComparer Instance = new SyntaxNodeComparer();
        public bool Equals(SyntaxNode? x, SyntaxNode? y) => ReferenceEquals(x, y);
        public int GetHashCode(SyntaxNode obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }

    private static void GenerateAccessors(SourceProductionContext ctx, ImmutableArray<CaptureInfo?> captures)
    {
        if (captures.IsDefaultOrEmpty)
            return;

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("using System.Runtime.CompilerServices;");
        sb.AppendLine();
        sb.AppendLine("namespace UnsafeAccessorGenTest;");
        sb.AppendLine();
        sb.AppendLine("internal static class GeneratedClosureAccessors");
        sb.AppendLine("{");

        int index = 0;
        foreach (var capture in captures)
        {
            if (capture == null) continue;

            sb.AppendLine($"    // ── Capture site {index}: {capture.FilePath}:{capture.Line} ──");
            sb.AppendLine($"    // Predicted display class: {capture.DisplayClassName}");
            sb.AppendLine();

            // Emit [UnsafeAccessor] extern method for each captured variable
            foreach (var v in capture.Variables)
            {
                var accessorName = $"__Extract_{v.Name}_{index}";
                sb.AppendLine($"    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = \"{v.Name}\")]");
                sb.AppendLine($"    internal extern static ref {v.Type} {accessorName}(");
                sb.AppendLine($"        [UnsafeAccessorType(\"{capture.DisplayClassName}\")] object target);");
                sb.AppendLine();
            }

            // Emit a verification + extraction method
            var escapedPath = capture.FilePath.Replace("\\", "\\\\");
            sb.AppendLine($"    internal static void Verify_{index}(Delegate func)");
            sb.AppendLine("    {");
            sb.AppendLine($"        var actual = func.Target?.GetType().FullName;");
            sb.AppendLine($"        if (actual != \"{capture.DisplayClassName}\")");
            sb.AppendLine($"            throw new System.InvalidOperationException(");
            sb.AppendLine($"                $\"Display class name mismatch at {escapedPath}:{capture.Line}: \" +");
            sb.AppendLine($"                $\"predicted '{capture.DisplayClassName}', actual '{{actual}}'. \" +");
            sb.AppendLine($"                \"This is a code generation bug.\");");
            sb.AppendLine("    }");
            sb.AppendLine();

            // Emit convenience extraction method with all variables
            var outParams = string.Join(", ", capture.Variables.Select(v => $"out {v.Type} {v.Name}"));
            sb.AppendLine($"    internal static void Extract_{index}(Delegate func, {outParams})");
            sb.AppendLine("    {");
            sb.AppendLine("        var target = func.Target!;");
            foreach (var v in capture.Variables)
            {
                sb.AppendLine($"        {v.Name} = __Extract_{v.Name}_{index}(target);");
            }
            sb.AppendLine("    }");
            sb.AppendLine();

            index++;
        }

        sb.AppendLine("}");

        ctx.AddSource("GeneratedClosureAccessors.g.cs", sb.ToString());
    }
}

internal sealed class CaptureInfo
{
    public CaptureInfo(string displayClassName, CapturedVariable[] variables,
        string containingTypeName, string filePath, int line, int column)
    {
        DisplayClassName = displayClassName;
        Variables = variables;
        ContainingTypeName = containingTypeName;
        FilePath = filePath;
        Line = line;
        Column = column;
    }

    public string DisplayClassName { get; }
    public CapturedVariable[] Variables { get; }
    public string ContainingTypeName { get; }
    public string FilePath { get; }
    public int Line { get; }
    public int Column { get; }
}

internal sealed class CapturedVariable
{
    public CapturedVariable(string name, string type)
    {
        Name = name;
        Type = type;
    }

    public string Name { get; }
    public string Type { get; }
}
