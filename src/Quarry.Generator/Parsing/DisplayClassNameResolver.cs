using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Quarry.Generators.IR;
using Quarry.Generators.Models;

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

        // Collect all imported namespaces for context disambiguation
        var importedNamespaces = new HashSet<string>(System.StringComparer.Ordinal);
        foreach (var usingDir in root.Usings)
        {
            if (usingDir.Alias == null && usingDir.Name != null)
                importedNamespaces.Add(usingDir.Name.ToString());
        }

        foreach (var ns in importedNamespaces)
        {
            var candidate = ns + "." + schemaName;
            var schemaType = compilation.GetTypeByMetadataName(candidate);
            if (schemaType != null)
            {
                // The schema lives in ns, but the generated entity is placed in
                // the owning context's namespace (which may differ from the schema's).
                var contextNs = FindContextNamespaceForSchema(compilation, schemaType, importedNamespaces);
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
    /// in the schema namespace. When multiple contexts reference the same schema,
    /// prefers the context whose namespace is imported by the source file.
    /// Returns null if no owning context is found.
    /// </summary>
    private static string? FindContextNamespaceForSchema(
        Compilation compilation, INamedTypeSymbol schemaType, HashSet<string>? importedNamespaces = null)
    {
        string? firstMatch = null;

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
                        {
                            // When the source file imports this context's namespace, it's the right one
                            if (importedNamespaces != null && importedNamespaces.Contains(ns!))
                                return ns;
                            firstMatch ??= ns;
                        }
                    }
                }
            }
        }

        return firstMatch;
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
        SemanticModel semanticModel,
        EntityRegistry? entityRegistry = null)
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
                    ?? (entityRegistry != null ? TryResolveChainResultType(symbol, entityRegistry) : null)
                    ?? "object")
                : typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            if (string.IsNullOrWhiteSpace(varType))
                varType = "object";

            result[varName] = varType;
        }

        // Second pass: resolve derived locals (var x = resolvedVar.Property)
        // whose types depend on variables resolved in the first pass.
        if (entityRegistry != null)
            ResolveDerivedLocals(captured, result, entityRegistry);

        return result.Count > 0 ? result : null;
    }

    /// <summary>
    /// Resolves a captured variable whose initializer is a Quarry chain terminal
    /// (e.g., <c>var x = await db.T().Select(...).ExecuteFetchFirstOrDefaultAsync()</c>),
    /// or a member access on such a variable (e.g., <c>var y = x.Name</c>).
    /// Walks the invocation chain to find the entity accessor and Select projection,
    /// then reconstructs the result type from EntityRegistry column metadata.
    /// </summary>
    private static string? TryResolveChainResultType(ISymbol symbol, EntityRegistry entityRegistry)
    {
        return TryResolveChainResultTypeCore(symbol, entityRegistry, depth: 0);
    }

    private static string? TryResolveChainResultTypeCore(ISymbol symbol, EntityRegistry entityRegistry, int depth)
    {
        if (depth > 3) return null; // Guard against deep/circular chains

        var declRef = symbol.DeclaringSyntaxReferences.FirstOrDefault();
        if (declRef == null)
            return null;

        var declSyntax = declRef.GetSyntax();
        if (declSyntax is not VariableDeclaratorSyntax { Initializer.Value: { } initValue })
            return null;

        // Unwrap await
        if (initValue is AwaitExpressionSyntax awaitExpr)
            initValue = awaitExpr.Expression;

        // Case 1: member access on another local (var y = x.Name)
        if (initValue is MemberAccessExpressionSyntax derivedAccess
            && derivedAccess.Expression is IdentifierNameSyntax sourceIdent)
        {
            // Find the source variable's symbol by walking up to the enclosing block
            var sourceDecl = FindLocalDeclarator(sourceIdent.Identifier.Text, declSyntax);
            if (sourceDecl != null)
            {
                // Create a temporary symbol-like lookup via the declarator syntax
                var sourceType = TryResolveInitializerType(sourceDecl, entityRegistry, depth + 1);
                if (sourceType != null)
                    return ResolveMemberOnType(sourceType, derivedAccess.Name.Identifier.Text, entityRegistry);
            }
            return null;
        }

        // Case 2: conditional member access (var y = x?.Name)
        if (initValue is ConditionalAccessExpressionSyntax conditional
            && conditional.Expression is IdentifierNameSyntax condSourceIdent
            && conditional.WhenNotNull is MemberBindingExpressionSyntax memberBinding)
        {
            var sourceDecl = FindLocalDeclarator(condSourceIdent.Identifier.Text, declSyntax);
            if (sourceDecl != null)
            {
                var sourceType = TryResolveInitializerType(sourceDecl, entityRegistry, depth + 1);
                if (sourceType != null)
                    return ResolveMemberOnType(sourceType, memberBinding.Name.Identifier.Text, entityRegistry);
            }
            return null;
        }

        // Case 3: Quarry chain invocation
        return initValue is InvocationExpressionSyntax terminalInvocation
            ? TryResolveChainInvocation(terminalInvocation, entityRegistry)
            : null;
    }

    /// <summary>
    /// Finds a local variable declarator by name in the enclosing block scope.
    /// </summary>
    private static VariableDeclaratorSyntax? FindLocalDeclarator(string varName, SyntaxNode fromNode)
    {
        var current = fromNode.Parent;
        while (current != null)
        {
            if (current is BlockSyntax block)
            {
                foreach (var statement in block.Statements)
                {
                    if (statement is LocalDeclarationStatementSyntax localDecl)
                    {
                        foreach (var declarator in localDecl.Declaration.Variables)
                        {
                            if (declarator.Identifier.Text == varName)
                                return declarator;
                        }
                    }
                }
            }
            current = current.Parent;
        }
        return null;
    }

    /// <summary>
    /// Resolves the type of a local variable from its declarator syntax by recursively
    /// analyzing its initializer (chain result or member access on another resolved variable).
    /// </summary>
    private static string? TryResolveInitializerType(
        VariableDeclaratorSyntax declarator, EntityRegistry entityRegistry, int depth)
    {
        if (depth > 3 || declarator.Initializer?.Value == null)
            return null;

        var initValue = declarator.Initializer.Value;

        // Unwrap await
        if (initValue is AwaitExpressionSyntax awaitExpr)
            initValue = awaitExpr.Expression;

        // Chain invocation
        if (initValue is InvocationExpressionSyntax)
        {
            // Create a synthetic symbol-less resolution using the declarator syntax.
            // TryResolveChainResultTypeCore needs an ISymbol, but we can call the chain
            // resolver directly with the initValue.
            return TryResolveChainInvocation(initValue as InvocationExpressionSyntax, entityRegistry);
        }

        // Member access: sourceVar.Property
        if (initValue is MemberAccessExpressionSyntax ma
            && ma.Expression is IdentifierNameSyntax sourceIdent)
        {
            var sourceDecl = FindLocalDeclarator(sourceIdent.Identifier.Text, declarator);
            if (sourceDecl != null)
            {
                var sourceType = TryResolveInitializerType(sourceDecl, entityRegistry, depth + 1);
                if (sourceType != null)
                    return ResolveMemberOnType(sourceType, ma.Name.Identifier.Text, entityRegistry);
            }
        }

        // Conditional member access: sourceVar?.Property
        if (initValue is ConditionalAccessExpressionSyntax cond
            && cond.Expression is IdentifierNameSyntax condSourceIdent
            && cond.WhenNotNull is MemberBindingExpressionSyntax binding)
        {
            var sourceDecl = FindLocalDeclarator(condSourceIdent.Identifier.Text, declarator);
            if (sourceDecl != null)
            {
                var sourceType = TryResolveInitializerType(sourceDecl, entityRegistry, depth + 1);
                if (sourceType != null)
                    return ResolveMemberOnType(sourceType, binding.Name.Identifier.Text, entityRegistry);
            }
        }

        return null;
    }

    /// <summary>
    /// Resolves the type of a chain invocation expression without requiring an ISymbol.
    /// Extracted from TryResolveChainResultTypeCore for use when walking declarators.
    /// </summary>
    private static string? TryResolveChainInvocation(
        InvocationExpressionSyntax? terminalInvocation, EntityRegistry entityRegistry)
    {
        if (terminalInvocation == null)
            return null;

        string? terminalName = null;
        if (terminalInvocation.Expression is MemberAccessExpressionSyntax terminalAccess)
            terminalName = terminalAccess.Name.Identifier.Text;

        if (terminalName == null)
            return null;

        bool isNullableResult = terminalName == "ExecuteFetchFirstOrDefaultAsync"
            || terminalName == "ExecuteFetchSingleOrDefaultAsync";
        bool isListResult = terminalName == "ExecuteFetchAllAsync";
        bool isScalarResult = terminalName.StartsWith("ExecuteScalar");
        bool isKnownTerminal = isNullableResult || isListResult || isScalarResult
            || terminalName == "ExecuteFetchFirstAsync"
            || terminalName == "ExecuteFetchSingleAsync";

        if (!isKnownTerminal)
            return null;

        LambdaExpressionSyntax? selectLambda = null;
        EntityInfo? entityInfo = null;

        var current = terminalInvocation;
        while (current != null)
        {
            if (current.Expression is MemberAccessExpressionSyntax ma)
            {
                var methodName = ma.Name.Identifier.Text;
                if (methodName == "Select"
                    && current.ArgumentList.Arguments.Count == 1
                    && current.ArgumentList.Arguments[0].Expression is LambdaExpressionSyntax lambda)
                {
                    selectLambda = lambda;
                }

                if (ma.Expression is InvocationExpressionSyntax receiver)
                {
                    if (receiver.Expression is MemberAccessExpressionSyntax rootAccess
                        && receiver.ArgumentList.Arguments.Count == 0)
                    {
                        var accessorName = rootAccess.Name.Identifier.Text;
                        entityInfo = TryFindEntityByAccessorName(accessorName, entityRegistry);
                    }
                    current = receiver;
                }
                else
                    break;
            }
            else
                break;
        }

        if (entityInfo == null)
            return null;

        // Note: we intentionally do NOT append "?" for nullable results (FirstOrDefault).
        // The CapturedVariableTypes value is used for carrier field types and value expressions
        // (e.g., fetchedPackage.Id). Nullable wrapping would make member access invalid.
        // The UnsafeAccessor field type is resolved separately by CarrierAnalyzer.
        if (selectLambda != null)
        {
            var projectionType = ResolveProjectionType(selectLambda, entityInfo, isListResult);
            if (projectionType != null)
                return projectionType;
            // Identity projection (f => f) returns null — fall through to entity type path
        }

        var contextNs = FindContextNamespaceForEntityInfo(entityInfo, entityRegistry);
        var entityNs = contextNs ?? entityInfo.SchemaNamespace;
        var entityType = "global::" + entityNs + "." + entityInfo.EntityName;

        if (isListResult)
            return "global::System.Collections.Generic.List<" + entityType + ">";
        return entityType;
    }

    /// <summary>
    /// Finds an EntityInfo by accessor method name (e.g., "Packages" → Package entity).
    /// Uses the EntityRegistry's accessor name index for exact lookup.
    /// </summary>
    private static EntityInfo? TryFindEntityByAccessorName(string accessorName, EntityRegistry entityRegistry)
    {
        return entityRegistry.GetByAccessorName(accessorName);
    }

    /// <summary>
    /// Reconstructs the projection type from a Select lambda body using EntityRegistry column metadata.
    /// For tuple projections like <c>p => (p.Id, p.Status, p.Name)</c>, builds a ValueTuple type string.
    /// </summary>
    private static string? ResolveProjectionType(
        LambdaExpressionSyntax selectLambda, EntityInfo entityInfo, bool isListResult)
    {
        var body = selectLambda.Body;

        // Tuple projection: p => (p.Id, p.Status, p.Name)
        if (body is TupleExpressionSyntax tuple)
        {
            var elementTypes = new List<string>();
            foreach (var arg in tuple.Arguments)
            {
                string? elementType = null;
                string? elementName = null;

                // Simple member access: p.PropertyName
                if (arg.Expression is MemberAccessExpressionSyntax memberAccess)
                {
                    var propName = memberAccess.Name.Identifier.Text;
                    elementType = FindColumnClrType(propName, entityInfo);
                    elementName = propName; // Inferred tuple element name
                }

                if (elementType == null)
                    return null; // Can't resolve an element — bail

                // Explicit name (p.Id: myName) takes precedence over inferred name
                if (arg.NameColon != null)
                    elementName = arg.NameColon.Name.Identifier.Text;

                elementTypes.Add(elementName != null ? elementType + " " + elementName : elementType);
            }

            var tupleType = "(" + string.Join(", ", elementTypes) + ")";
            if (isListResult)
                return "global::System.Collections.Generic.List<" + tupleType + ">";
            return tupleType;
        }

        // Identity projection: p => p (result is the entity type)
        if (body is IdentifierNameSyntax)
        {
            // No Select needed — handled by caller
            return null;
        }

        // Single column projection: p => p.Name
        if (body is MemberAccessExpressionSyntax singleMember)
        {
            var propName = singleMember.Name.Identifier.Text;
            var elementType = FindColumnClrType(propName, entityInfo);
            if (elementType == null) return null;

            if (isListResult)
                return "global::System.Collections.Generic.List<" + elementType + ">";
            return elementType;
        }

        return null;
    }

    /// <summary>
    /// Looks up the CLR type of a column by property name from EntityInfo.
    /// </summary>
    private static string? FindColumnClrType(string propertyName, EntityInfo entityInfo)
    {
        foreach (var col in entityInfo.Columns)
        {
            if (col.PropertyName == propertyName)
                return col.FullClrType;
        }
        return null;
    }

    /// <summary>
    /// Finds the context namespace for an entity to construct fully-qualified entity type names.
    /// </summary>
    private static string? FindContextNamespaceForEntityInfo(EntityInfo entityInfo, EntityRegistry entityRegistry)
    {
        var entry = entityRegistry.Resolve(entityInfo.EntityName);
        return entry?.Context.Namespace;
    }

    /// <summary>
    /// Second pass: resolves derived locals whose types depend on variables resolved in the first pass.
    /// Handles patterns like <c>var name = resolvedVar.PropertyName</c> where resolvedVar was
    /// resolved as a tuple or entity type.
    /// </summary>
    private static void ResolveDerivedLocals(
        ISymbol[] captured, Dictionary<string, string> result, EntityRegistry entityRegistry)
    {
        bool changed;
        do
        {
            changed = false;
            foreach (var symbol in captured)
            {
                if (symbol is not ILocalSymbol local)
                    continue;

                if (result.TryGetValue(local.Name, out var existing) && existing != "object" && existing != "?")
                    continue; // Already resolved

                var declRef = symbol.DeclaringSyntaxReferences.FirstOrDefault();
                if (declRef == null) continue;

                var declSyntax = declRef.GetSyntax();
                if (declSyntax is not VariableDeclaratorSyntax { Initializer.Value: { } initValue })
                    continue;

                // Pattern: sourceVar.PropertyName or sourceVar?.PropertyName
                MemberAccessExpressionSyntax? memberAccess = null;
                if (initValue is MemberAccessExpressionSyntax ma)
                    memberAccess = ma;
                else if (initValue is ConditionalAccessExpressionSyntax conditional
                    && conditional.WhenNotNull is MemberBindingExpressionSyntax memberBinding)
                {
                    // sourceVar?.PropertyName — check the source expression
                    if (conditional.Expression is IdentifierNameSyntax sourceId)
                    {
                        var sourceName = sourceId.Identifier.Text;
                        if (result.TryGetValue(sourceName, out var sourceType) && sourceType != "object" && sourceType != "?")
                        {
                            var propName = memberBinding.Name.Identifier.Text;
                            var resolved = ResolveMemberOnType(sourceType, propName, entityRegistry);
                            if (resolved != null)
                            {
                                result[local.Name] = resolved;
                                changed = true;
                            }
                        }
                    }
                    continue;
                }

                if (memberAccess != null && memberAccess.Expression is IdentifierNameSyntax sourceIdent)
                {
                    var sourceName = sourceIdent.Identifier.Text;
                    if (result.TryGetValue(sourceName, out var sourceType) && sourceType != "object" && sourceType != "?")
                    {
                        var propName = memberAccess.Name.Identifier.Text;
                        var resolved = ResolveMemberOnType(sourceType, propName, entityRegistry);
                        if (resolved != null)
                        {
                            result[local.Name] = resolved;
                            changed = true;
                        }
                    }
                }
            }
        } while (changed); // Iterate until no more progress (handles chains of derivations)
    }

    /// <summary>
    /// Resolves a member access on a resolved type string.
    /// For tuples like "(long Id, int Status, string Name)", looks up the named element.
    /// For entity types, looks up the column in EntityRegistry.
    /// </summary>
    private static string? ResolveMemberOnType(string typeString, string memberName, EntityRegistry entityRegistry)
    {
        // Strip trailing ? for nullable types
        var baseType = typeString.EndsWith("?") ? typeString.Substring(0, typeString.Length - 1) : typeString;

        // Tuple type: "(long Id, int Status, string Name)"
        if (baseType.StartsWith("(") && baseType.EndsWith(")"))
        {
            var inner = baseType.Substring(1, baseType.Length - 2);
            foreach (var element in inner.Split(','))
            {
                var trimmed = element.Trim();
                var spaceIdx = trimmed.LastIndexOf(' ');
                if (spaceIdx > 0)
                {
                    var elemName = trimmed.Substring(spaceIdx + 1);
                    var elemType = trimmed.Substring(0, spaceIdx).Trim();
                    if (elemName == memberName)
                        return elemType;
                }
            }
            return null;
        }

        // Entity type: look up column in EntityRegistry
        // Extract entity name from fully-qualified name (global::Ns.EntityName → EntityName)
        var entityName = baseType;
        var lastDot = entityName.LastIndexOf('.');
        if (lastDot >= 0)
            entityName = entityName.Substring(lastDot + 1);
        if (entityName.StartsWith("global::"))
            entityName = entityName.Substring(8);

        var entityInfo = entityRegistry.GetByName(entityName);
        if (entityInfo != null)
            return FindColumnClrType(memberName, entityInfo);

        return null;
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
