using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Quarry.Generators.IR;
using Quarry.Generators.Models;
using Quarry.Generators.Utilities;

namespace Quarry.Generators.Parsing;

/// <summary>
/// Resolves chain result types for captured variables whose types are error types
/// at generator time (because their type depends on a generator-produced entity).
/// Walks invocation chains to find the entity accessor and Select projection,
/// then reconstructs the result type from EntityRegistry column metadata.
/// <para>
/// Extracted from <see cref="DisplayClassNameResolver"/> to isolate the type
/// resolution logic from closure/display-class analysis.
/// </para>
/// </summary>
internal static class ChainResultTypeResolver
{
    /// <summary>
    /// Resolves a captured variable whose initializer is a Quarry chain terminal
    /// (e.g., <c>var x = await db.T().Select(...).ExecuteFetchFirstOrDefaultAsync()</c>),
    /// or a member access on such a variable (e.g., <c>var y = x.Name</c>).
    /// </summary>
    internal static string? TryResolveChainResultType(ISymbol symbol, EntityRegistry entityRegistry)
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

        // Handle out var declarations: out var dbEquip from receiver.TryGetValue(key, out var dbEquip)
        if (declSyntax is SingleVariableDesignationSyntax svd)
            return TryResolveOutVarType(svd, entityRegistry, depth);

        if (declSyntax is not VariableDeclaratorSyntax { Initializer.Value: { } initValue })
            return null;

        // Unwrap await
        if (initValue is AwaitExpressionSyntax awaitExpr)
            initValue = awaitExpr.Expression;

        // Case 1: member access on another local (var y = x.Name)
        if (initValue is MemberAccessExpressionSyntax derivedAccess
            && derivedAccess.Expression is IdentifierNameSyntax sourceIdent)
        {
            var sourceDecl = FindLocalDeclarator(sourceIdent.Identifier.Text, declSyntax);
            if (sourceDecl != null)
            {
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

        // Chain invocation (Quarry terminal or element-extracting method)
        if (initValue is InvocationExpressionSyntax invocation)
        {
            var quarryResult = TryResolveChainInvocation(invocation, entityRegistry);
            if (quarryResult != null)
                return quarryResult;

            // Non-Quarry invocations: single-element methods (First, FirstOrDefault, etc.)
            // return the collection's element type directly.
            return TryResolveSingleElementMethod(invocation, declarator, entityRegistry, depth);
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
    /// Resolves an <c>out var</c> declaration by walking up the syntax tree to the containing
    /// invocation, then tracing the receiver's collection element type.
    /// For <c>dict.TryGetValue(key, out var entity)</c>, the out var type equals the dictionary's
    /// value type, which traces back to the original collection's element type.
    /// </summary>
    private static string? TryResolveOutVarType(
        SingleVariableDesignationSyntax designation, EntityRegistry entityRegistry, int depth)
    {
        if (depth > 3) return null;

        // Walk up: SingleVariableDesignation → DeclarationExpression → Argument → ArgumentList → Invocation
        if (designation.Parent is not DeclarationExpressionSyntax)
            return null;
        if (designation.Parent.Parent is not ArgumentSyntax)
            return null;
        if (designation.Parent.Parent.Parent is not ArgumentListSyntax)
            return null;
        if (designation.Parent.Parent.Parent.Parent is not InvocationExpressionSyntax invocation)
            return null;

        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return null;

        var methodName = memberAccess.Name.Identifier.Text;

        // TryGetValue(key, out var entity) — the out param type is the collection element type
        if (methodName == "TryGetValue")
            return TryResolveCollectionElementType(memberAccess.Expression, designation, entityRegistry, depth + 1);

        return null;
    }

    /// <summary>
    /// Traces the element type of a collection expression through variable declarations
    /// and element-preserving method chains back to a Quarry chain result.
    /// </summary>
    private static string? TryResolveCollectionElementType(
        ExpressionSyntax expression, SyntaxNode contextNode, EntityRegistry entityRegistry, int depth)
    {
        if (depth > 3) return null;

        if (expression is IdentifierNameSyntax ident)
        {
            var decl = FindLocalDeclarator(ident.Identifier.Text, contextNode);
            if (decl == null || decl.Initializer?.Value == null)
                return null;

            var initValue = decl.Initializer.Value;
            if (initValue is AwaitExpressionSyntax awaitExpr)
                initValue = awaitExpr.Expression;

            // Quarry chain invocation → extract element type from List<T> / T result
            if (initValue is InvocationExpressionSyntax invocation)
            {
                var chainResult = TryResolveChainInvocation(invocation, entityRegistry);
                if (chainResult != null)
                    return ExtractCollectionElementType(chainResult) ?? chainResult;

                // Element-preserving method (ToDictionary, ToList, Where, etc.) → recurse on receiver
                if (invocation.Expression is MemberAccessExpressionSyntax ma)
                {
                    var methodName = ma.Name.Identifier.Text;
                    if (IsElementPreservingMethod(methodName))
                    {
                        // ToDictionary with value selector (2+ lambda args) changes the value type — bail
                        if (methodName == "ToDictionary" && invocation.ArgumentList.Arguments.Count > 1
                            && invocation.ArgumentList.Arguments[1].Expression is LambdaExpressionSyntax)
                            return null;

                        return TryResolveCollectionElementType(ma.Expression, decl, entityRegistry, depth + 1);
                    }
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Resolves single-element extraction methods (First, FirstOrDefault, Single, etc.)
    /// on collections to their element type.
    /// </summary>
    private static string? TryResolveSingleElementMethod(
        InvocationExpressionSyntax invocation, SyntaxNode contextNode, EntityRegistry entityRegistry, int depth)
    {
        if (depth > 3) return null;

        if (invocation.Expression is not MemberAccessExpressionSyntax ma)
            return null;

        var methodName = ma.Name.Identifier.Text;
        if (methodName != "First" && methodName != "FirstOrDefault"
            && methodName != "Single" && methodName != "SingleOrDefault"
            && methodName != "Last" && methodName != "LastOrDefault"
            && methodName != "ElementAt" && methodName != "ElementAtOrDefault")
            return null;

        return TryResolveCollectionElementType(ma.Expression, contextNode, entityRegistry, depth + 1);
    }

    /// <summary>
    /// Returns true for methods that preserve the collection's element type as
    /// either the output element type or the dictionary value type.
    /// </summary>
    private static bool IsElementPreservingMethod(string methodName)
    {
        switch (methodName)
        {
            case "ToDictionary":
            case "ToLookup":
            case "ToList":
            case "ToArray":
            case "ToHashSet":
            case "AsEnumerable":
            case "Where":
            case "OrderBy":
            case "OrderByDescending":
            case "ThenBy":
            case "ThenByDescending":
            case "Distinct":
            case "Take":
            case "Skip":
            case "Reverse":
            case "First":
            case "FirstOrDefault":
            case "Single":
            case "SingleOrDefault":
            case "Last":
            case "LastOrDefault":
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Extracts the element type from a collection type string.
    /// <c>global::System.Collections.Generic.List&lt;T&gt;</c> → <c>T</c>.
    /// Returns null if the type is not a recognized collection wrapper.
    /// </summary>
    internal static string? ExtractCollectionElementType(string typeString)
    {
        // Find the outermost generic argument: List<T> → T, IReadOnlyList<T> → T
        var genericStart = typeString.IndexOf('<');
        if (genericStart < 0) return null;

        // Verify it's a single-type-arg collection (not Dictionary<K,V>)
        var prefix = typeString.Substring(0, genericStart);
        if (prefix.Contains("Dictionary") || prefix.Contains("IDictionary"))
            return null;

        // Extract content between outermost < and >
        var genericEnd = typeString.LastIndexOf('>');
        if (genericEnd <= genericStart) return null;

        return typeString.Substring(genericStart + 1, genericEnd - genericStart - 1);
    }

    /// <summary>
    /// Resolves a member access on a resolved type string.
    /// For tuples like "(long Id, int Status, string Name)", looks up the named element.
    /// For entity types, looks up the column in EntityRegistry.
    /// </summary>
    internal static string? ResolveMemberOnType(string typeString, string memberName, EntityRegistry entityRegistry)
    {
        // Strip trailing ? for nullable types
        var baseType = typeString.EndsWith("?") ? typeString.Substring(0, typeString.Length - 1) : typeString;

        // Tuple type: "(long Id, int Status, string Name)"
        if (baseType.StartsWith("(") && baseType.EndsWith(")"))
        {
            var inner = baseType.Substring(1, baseType.Length - 2);
            foreach (var element in TypeClassification.SplitTupleElements(inner))
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
}
