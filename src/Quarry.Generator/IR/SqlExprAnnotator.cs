using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Quarry.Generators.IR;

/// <summary>
/// Enriches SqlExpr trees with semantic type information from the SemanticModel.
/// Best-effort: gracefully returns the original tree if resolution fails.
/// </summary>
internal static class SqlExprAnnotator
{
    /// <summary>
    /// Annotates an SqlExpr tree with type info from the semantic model.
    /// Returns the original tree if annotation fails (graceful degradation).
    /// </summary>
    /// <param name="expr">The SqlExpr tree to annotate.</param>
    /// <param name="syntax">The corresponding expression syntax.</param>
    /// <param name="semanticModel">The semantic model for type resolution.</param>
    /// <returns>The annotated SqlExpr tree, or the original if annotation fails.</returns>
    public static SqlExpr Annotate(
        SqlExpr expr,
        ExpressionSyntax syntax,
        SemanticModel semanticModel)
    {
        try
        {
            return AnnotateExpr(expr, syntax, semanticModel);
        }
        catch
        {
            // Graceful degradation — return original tree
            return expr;
        }
    }

    private static SqlExpr AnnotateExpr(SqlExpr expr, ExpressionSyntax syntax, SemanticModel semanticModel)
    {
        switch (expr)
        {
            case CapturedValueExpr captured:
                return AnnotateCapturedValue(captured, syntax, semanticModel);

            case BinaryOpExpr bin when syntax is BinaryExpressionSyntax binSyntax:
            {
                var left = AnnotateExpr(bin.Left, binSyntax.Left, semanticModel);
                var right = AnnotateExpr(bin.Right, binSyntax.Right, semanticModel);
                if (ReferenceEquals(left, bin.Left) && ReferenceEquals(right, bin.Right))
                    return bin;
                return new BinaryOpExpr(left, bin.Operator, right);
            }

            case UnaryOpExpr unary when syntax is PrefixUnaryExpressionSyntax unarySyntax:
            {
                var operand = AnnotateExpr(unary.Operand, unarySyntax.Operand, semanticModel);
                if (ReferenceEquals(operand, unary.Operand))
                    return unary;
                return new UnaryOpExpr(unary.Operator, operand);
            }

            case LiteralExpr literal:
                return AnnotateLiteral(literal, syntax, semanticModel);

            default:
                return expr;
        }
    }

    private static SqlExpr AnnotateCapturedValue(CapturedValueExpr captured, ExpressionSyntax syntax, SemanticModel semanticModel)
    {
        // Try constant folding for enum values and other compile-time constants
        var constantValue = semanticModel.GetConstantValue(syntax);
        if (constantValue.HasValue && constantValue.Value != null)
        {
            // The value is a compile-time constant — inline as a literal
            var resolved = constantValue.Value;
            if (resolved is int intVal)
                return new LiteralExpr(intVal.ToString(), "int");
            if (resolved is long longVal)
                return new LiteralExpr(longVal.ToString(), "long");
            if (resolved is byte byteVal)
                return new LiteralExpr(byteVal.ToString(), "int");
            if (resolved is short shortVal)
                return new LiteralExpr(shortVal.ToString(), "int");
            if (resolved is string strVal)
                return new LiteralExpr(strVal, "string");
        }

        // Try to resolve the type
        var typeInfo = semanticModel.GetTypeInfo(syntax);
        if (typeInfo.Type != null)
        {
            var clrType = typeInfo.Type.ToDisplayString();
            return captured.WithClrType(clrType);
        }

        return captured;
    }

    private static SqlExpr AnnotateLiteral(LiteralExpr literal, ExpressionSyntax syntax, SemanticModel semanticModel)
    {
        // Attempt constant folding for enum values via GetConstantValue
        var constantValue = semanticModel.GetConstantValue(syntax);
        if (constantValue.HasValue && constantValue.Value != null)
        {
            // If the semantic model resolved a constant that differs from the syntax text,
            // update the literal (useful for enum values resolved to their underlying int)
            var resolved = constantValue.Value;
            if (resolved is int intVal && literal.ClrType != "int")
            {
                return new LiteralExpr(intVal.ToString(), "int");
            }
        }

        return literal;
    }

    /// <summary>
    /// Enriches CapturedValueExpr nodes with CLR type info by looking up identifiers
    /// in the lambda body syntax via the semantic model.
    /// Call this after parsing to annotate captured variable types.
    /// </summary>
    public static SqlExpr AnnotateCapturedTypes(
        SqlExpr expr,
        ExpressionSyntax lambdaBody,
        SemanticModel semanticModel)
    {
        // Build a map of identifier name -> CLR type from the syntax tree
        var typeMap = new System.Collections.Generic.Dictionary<string, string>(StringComparer.Ordinal);
        // Build a map of member access text -> constant literal value + CLR type (e.g., "OrderPriority.Urgent" -> ("3", "int"))
        var constantMap = new System.Collections.Generic.Dictionary<string, (string Value, string ClrType)>(StringComparer.Ordinal);
        CollectCapturedTypes(lambdaBody, semanticModel, typeMap, constantMap);
        if (typeMap.Count == 0 && constantMap.Count == 0) return expr;

        return ApplyCapturedTypes(expr, typeMap, constantMap);
    }

    private static void CollectCapturedTypes(
        SyntaxNode node,
        SemanticModel semanticModel,
        System.Collections.Generic.Dictionary<string, string> typeMap,
        System.Collections.Generic.Dictionary<string, (string Value, string ClrType)> constantMap)
    {
        foreach (var identifier in node.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            var name = identifier.Identifier.ValueText;
            if (typeMap.ContainsKey(name)) continue;

            var typeInfo = semanticModel.GetTypeInfo(identifier);
            if (typeInfo.Type != null && typeInfo.Type.TypeKind != TypeKind.Error)
            {
                typeMap[name] = typeInfo.Type.ToDisplayString();
            }
        }

        // Collect constant values and resolved types from member access expressions
        foreach (var memberAccess in node.DescendantNodes().OfType<MemberAccessExpressionSyntax>())
        {
            var text = memberAccess.ToString();

            // Check for compile-time constants (e.g., EnumType.Member)
            if (!constantMap.ContainsKey(text))
            {
                var constant = semanticModel.GetConstantValue(memberAccess);
                if (constant.HasValue && constant.Value != null)
                {
                    if (constant.Value is int intVal)
                        constantMap[text] = (intVal.ToString(), "int");
                    else if (constant.Value is long longVal)
                        constantMap[text] = (longVal.ToString(), "long");
                    else if (constant.Value is byte byteVal)
                        constantMap[text] = (byteVal.ToString(), "int");
                    else if (constant.Value is short shortVal)
                        constantMap[text] = (shortVal.ToString(), "int");
                    else if (constant.Value is string strVal)
                        constantMap[text] = (strVal, "string");
                }
            }

            // Resolve the type of the member access expression itself (e.g., user.UserId → int)
            // This enables correct carrier field types for property access on captured variables
            if (!typeMap.ContainsKey(text))
            {
                var typeInfo = semanticModel.GetTypeInfo(memberAccess);
                if (typeInfo.Type != null && typeInfo.Type.TypeKind != TypeKind.Error)
                {
                    typeMap[text] = typeInfo.Type.ToDisplayString();
                }
            }
        }
    }

    private static SqlExpr ApplyCapturedTypes(
        SqlExpr expr,
        System.Collections.Generic.Dictionary<string, string> typeMap,
        System.Collections.Generic.Dictionary<string, (string Value, string ClrType)>? constantMap = null)
    {
        switch (expr)
        {
            case CapturedValueExpr captured:
                // Check if this captured value is a compile-time constant (e.g., enum member or string constant)
                if (constantMap != null && constantMap.TryGetValue(captured.SyntaxText, out var constInfo))
                    return new LiteralExpr(constInfo.Value, constInfo.ClrType);
                // Prefer the full expression type (e.g., "user.UserId" → int) over the variable type (e.g., "user" → User)
                if (typeMap.TryGetValue(captured.SyntaxText, out var exprType))
                    return captured.WithClrType(exprType);
                if (typeMap.TryGetValue(captured.VariableName, out var clrType))
                    return captured.WithClrType(clrType);
                return captured;

            case BinaryOpExpr bin:
            {
                var left = ApplyCapturedTypes(bin.Left, typeMap, constantMap);
                var right = ApplyCapturedTypes(bin.Right, typeMap, constantMap);
                if (ReferenceEquals(left, bin.Left) && ReferenceEquals(right, bin.Right))
                    return bin;
                return new BinaryOpExpr(left, bin.Operator, right);
            }

            case UnaryOpExpr unary:
            {
                var operand = ApplyCapturedTypes(unary.Operand, typeMap, constantMap);
                if (ReferenceEquals(operand, unary.Operand)) return unary;
                return new UnaryOpExpr(unary.Operator, operand);
            }

            case InExpr inExpr:
            {
                var operand = ApplyCapturedTypes(inExpr.Operand, typeMap, constantMap);
                var changed = !ReferenceEquals(operand, inExpr.Operand);
                var newValues = new SqlExpr[inExpr.Values.Count];
                for (int i = 0; i < inExpr.Values.Count; i++)
                {
                    newValues[i] = ApplyCapturedTypes(inExpr.Values[i], typeMap, constantMap);
                    if (!ReferenceEquals(newValues[i], inExpr.Values[i])) changed = true;
                }
                return changed ? new InExpr(operand, newValues, inExpr.IsNegated) : inExpr;
            }

            case FunctionCallExpr func:
            {
                var changed = false;
                var newArgs = new SqlExpr[func.Arguments.Count];
                for (int i = 0; i < func.Arguments.Count; i++)
                {
                    newArgs[i] = ApplyCapturedTypes(func.Arguments[i], typeMap, constantMap);
                    if (!ReferenceEquals(newArgs[i], func.Arguments[i])) changed = true;
                }
                return changed ? new FunctionCallExpr(func.FunctionName, newArgs, func.IsAggregate) : func;
            }

            case IsNullCheckExpr isNull:
            {
                var operand = ApplyCapturedTypes(isNull.Operand, typeMap, constantMap);
                if (ReferenceEquals(operand, isNull.Operand)) return isNull;
                return new IsNullCheckExpr(operand, isNull.IsNegated);
            }

            case LikeExpr like:
            {
                var operand = ApplyCapturedTypes(like.Operand, typeMap, constantMap);
                var pattern = ApplyCapturedTypes(like.Pattern, typeMap, constantMap);
                if (ReferenceEquals(operand, like.Operand) && ReferenceEquals(pattern, like.Pattern))
                    return like;
                return new LikeExpr(operand, pattern, like.IsNegated, like.LikePrefix, like.LikeSuffix, like.NeedsEscape);
            }

            case SubqueryExpr subquery when subquery.Predicate != null:
            {
                var predicate = ApplyCapturedTypes(subquery.Predicate, typeMap, constantMap);
                if (ReferenceEquals(predicate, subquery.Predicate))
                    return subquery;
                return new SubqueryExpr(subquery.OuterParameterName, subquery.NavigationPropertyName,
                    subquery.SubqueryKind, predicate, subquery.InnerParameterName);
            }

            default:
                return expr;
        }
    }

    /// <summary>
    /// Inlines constant collection values in InExpr nodes. When an InExpr contains
    /// a single CapturedValueExpr referencing a local/readonly/const array initialized
    /// with constant literals, replaces it with inline LiteralExpr values.
    /// </summary>
    public static SqlExpr InlineConstantCollections(
        SqlExpr expr,
        ExpressionSyntax lambdaBody,
        SemanticModel semanticModel)
    {
        try
        {
            return InlineCollectionsRecursive(expr, lambdaBody, semanticModel);
        }
        catch
        {
            return expr;
        }
    }

    private static SqlExpr InlineCollectionsRecursive(SqlExpr expr, ExpressionSyntax lambdaBody, SemanticModel semanticModel)
    {
        switch (expr)
        {
            case InExpr inExpr:
            {
                // Check if the InExpr has a single CapturedValueExpr (collection parameter)
                if (inExpr.Values.Count == 1 && inExpr.Values[0] is CapturedValueExpr captured)
                {
                    var inlinedValues = TryResolveConstantArray(captured.VariableName, lambdaBody, semanticModel);
                    if (inlinedValues != null)
                    {
                        var operand = InlineCollectionsRecursive(inExpr.Operand, lambdaBody, semanticModel);
                        return new InExpr(operand, inlinedValues, inExpr.IsNegated);
                    }
                }
                return inExpr;
            }

            case BinaryOpExpr bin:
            {
                var left = InlineCollectionsRecursive(bin.Left, lambdaBody, semanticModel);
                var right = InlineCollectionsRecursive(bin.Right, lambdaBody, semanticModel);
                if (ReferenceEquals(left, bin.Left) && ReferenceEquals(right, bin.Right))
                    return bin;
                return new BinaryOpExpr(left, bin.Operator, right);
            }

            case UnaryOpExpr unary:
            {
                var operand = InlineCollectionsRecursive(unary.Operand, lambdaBody, semanticModel);
                if (ReferenceEquals(operand, unary.Operand)) return unary;
                return new UnaryOpExpr(unary.Operator, operand);
            }

            default:
                return expr;
        }
    }

    /// <summary>
    /// Tries to resolve a variable name to a constant array initializer.
    /// Returns literal expressions for each element, or null if not resolvable.
    /// </summary>
    private static SqlExpr[]? TryResolveConstantArray(
        string variableName,
        SyntaxNode scope,
        SemanticModel semanticModel)
    {
        // Find the identifier in the lambda body
        var identifier = scope.DescendantNodesAndSelf()
            .OfType<IdentifierNameSyntax>()
            .FirstOrDefault(id => id.Identifier.ValueText == variableName);

        if (identifier == null) return null;

        // Try semantic resolution first
        var symbolInfo = semanticModel.GetSymbolInfo(identifier);
        var symbol = symbolInfo.Symbol;

        InitializerExpressionSyntax? arrayInit = null;

        if (symbol != null)
        {
            if (symbol is ILocalSymbol localSymbol)
            {
                var declRef = localSymbol.DeclaringSyntaxReferences.FirstOrDefault();
                if (declRef != null)
                {
                    var declNode = declRef.GetSyntax();
                    if (declNode is VariableDeclaratorSyntax declarator && declarator.Initializer?.Value != null)
                        arrayInit = ExtractArrayInitializer(declarator.Initializer.Value);
                }
            }
            else if (symbol is IFieldSymbol fieldSymbol && (fieldSymbol.IsReadOnly || fieldSymbol.IsConst))
            {
                var declRef = fieldSymbol.DeclaringSyntaxReferences.FirstOrDefault();
                if (declRef != null)
                {
                    var declNode = declRef.GetSyntax();
                    if (declNode is VariableDeclaratorSyntax declarator && declarator.Initializer?.Value != null)
                        arrayInit = ExtractArrayInitializer(declarator.Initializer.Value);
                }
            }
        }

        // Fallback: walk up to the enclosing block (matching old pipeline's
        // TryResolveVariableCollectionLiterals approach) and find the local declaration
        if (arrayInit == null)
        {
            for (var node = scope.Parent; node != null; node = node.Parent)
            {
                if (node is BlockSyntax block)
                {
                    foreach (var statement in block.Statements)
                    {
                        if (statement is LocalDeclarationStatementSyntax localDecl)
                        {
                            foreach (var variable in localDecl.Declaration.Variables)
                            {
                                if (variable.Identifier.ValueText == variableName &&
                                    variable.Initializer?.Value != null)
                                {
                                    arrayInit = ExtractArrayInitializer(variable.Initializer.Value);
                                    if (arrayInit != null) break;
                                }
                            }
                        }
                        if (arrayInit != null) break;
                    }
                    break; // Only check the first enclosing block
                }
            }
        }

        if (arrayInit == null) return null;

        // Extract constant values from the array initializer
        var literals = new List<SqlExpr>();
        foreach (var element in arrayInit.Expressions)
        {
            var constant = semanticModel.GetConstantValue(element);
            if (!constant.HasValue) return null; // All elements must be constants

            var value = constant.Value;
            if (value is string strVal)
            {
                literals.Add(new LiteralExpr(strVal, "string"));
            }
            else if (value is int intVal)
            {
                literals.Add(new LiteralExpr(intVal.ToString(), "int"));
            }
            else if (value is long longVal)
            {
                literals.Add(new LiteralExpr(longVal.ToString(), "long"));
            }
            else if (value is double doubleVal)
            {
                literals.Add(new LiteralExpr(doubleVal.ToString(System.Globalization.CultureInfo.InvariantCulture), "double"));
            }
            else if (value is decimal decVal)
            {
                literals.Add(new LiteralExpr(decVal.ToString(System.Globalization.CultureInfo.InvariantCulture), "decimal"));
            }
            else if (value is bool boolVal)
            {
                literals.Add(new LiteralExpr(boolVal ? "TRUE" : "FALSE", "bool"));
            }
            else
            {
                // Unknown constant type - can't inline
                return null;
            }
        }

        return literals.Count > 0 ? literals.ToArray() : null;
    }

    private static InitializerExpressionSyntax? ExtractArrayInitializer(ExpressionSyntax initValue)
    {
        // new[] { ... } or new string[] { ... }
        if (initValue is ImplicitArrayCreationExpressionSyntax implicitArray)
            return implicitArray.Initializer;
        if (initValue is ArrayCreationExpressionSyntax arrayCreation)
            return arrayCreation.Initializer;
        // Collection initializer: { ... }
        if (initValue is InitializerExpressionSyntax init)
            return init;
        return null;
    }

    /// <summary>
    /// Inlines constant string values in LikeExpr patterns. When a LikeExpr contains
    /// a CapturedValueExpr referencing a static readonly/const string field initialized
    /// with a string literal, replaces it with a LiteralExpr.
    /// </summary>
    public static SqlExpr InlineConstantLikePatterns(
        SqlExpr expr,
        ExpressionSyntax lambdaBody,
        SemanticModel semanticModel)
    {
        try
        {
            return InlineLikePatternsRecursive(expr, lambdaBody, semanticModel);
        }
        catch
        {
            return expr;
        }
    }

    private static SqlExpr InlineLikePatternsRecursive(SqlExpr expr, ExpressionSyntax lambdaBody, SemanticModel semanticModel)
    {
        switch (expr)
        {
            case LikeExpr like when like.Pattern is CapturedValueExpr captured:
            {
                var resolved = TryResolveConstantString(captured.VariableName, lambdaBody, semanticModel);
                if (resolved != null)
                {
                    var operand = InlineLikePatternsRecursive(like.Operand, lambdaBody, semanticModel);
                    var escapedPattern = Translation.SqlLikeHelpers.EscapeLikeMetaChars(resolved);
                    var needsEscape = like.NeedsEscape || escapedPattern != resolved;
                    return new LikeExpr(operand, new LiteralExpr(escapedPattern, "string"),
                        like.IsNegated, like.LikePrefix, like.LikeSuffix, needsEscape);
                }
                return like;
            }

            // Catch string literals folded early by AnnotateCapturedTypes (e.g., qualified const fields)
            // that bypassed the parser's CreateLikeExpr escaping. Only fire when NeedsEscape is still
            // false — if the parser already escaped, NeedsEscape is true and we skip.
            case LikeExpr like when like.Pattern is LiteralExpr literal
                && literal.ClrType == "string" && !like.NeedsEscape:
            {
                var escaped = Translation.SqlLikeHelpers.EscapeLikeMetaChars(literal.SqlText);
                if (escaped == literal.SqlText)
                    return like; // No metacharacters — no change needed
                var operand = InlineLikePatternsRecursive(like.Operand, lambdaBody, semanticModel);
                return new LikeExpr(operand, new LiteralExpr(escaped, "string"),
                    like.IsNegated, like.LikePrefix, like.LikeSuffix, needsEscape: true);
            }

            case BinaryOpExpr bin:
            {
                var left = InlineLikePatternsRecursive(bin.Left, lambdaBody, semanticModel);
                var right = InlineLikePatternsRecursive(bin.Right, lambdaBody, semanticModel);
                if (ReferenceEquals(left, bin.Left) && ReferenceEquals(right, bin.Right))
                    return bin;
                return new BinaryOpExpr(left, bin.Operator, right);
            }

            case UnaryOpExpr unary:
            {
                var operand = InlineLikePatternsRecursive(unary.Operand, lambdaBody, semanticModel);
                if (ReferenceEquals(operand, unary.Operand)) return unary;
                return new UnaryOpExpr(unary.Operator, operand);
            }

            case SubqueryExpr subquery when subquery.Predicate != null:
            {
                var predicate = InlineLikePatternsRecursive(subquery.Predicate, lambdaBody, semanticModel);
                if (ReferenceEquals(predicate, subquery.Predicate))
                    return subquery;
                return new SubqueryExpr(subquery.OuterParameterName, subquery.NavigationPropertyName,
                    subquery.SubqueryKind, predicate, subquery.InnerParameterName);
            }

            default:
                return expr;
        }
    }

    /// <summary>
    /// Tries to resolve a variable name to a constant string value.
    /// Returns the string value, or null if not resolvable as a constant.
    /// </summary>
    private static string? TryResolveConstantString(
        string variableName,
        SyntaxNode scope,
        SemanticModel semanticModel)
    {
        var identifier = scope.DescendantNodesAndSelf()
            .OfType<IdentifierNameSyntax>()
            .FirstOrDefault(id => id.Identifier.ValueText == variableName);

        if (identifier == null) return null;

        // Try GetConstantValue first (handles const fields and local consts)
        var constant = semanticModel.GetConstantValue(identifier);
        if (constant.HasValue && constant.Value is string constStr)
            return constStr;

        // Try symbol resolution for static readonly fields
        var symbolInfo = semanticModel.GetSymbolInfo(identifier);
        var symbol = symbolInfo.Symbol;

        if (symbol is IFieldSymbol fieldSymbol && (fieldSymbol.IsReadOnly || fieldSymbol.IsConst))
        {
            var declRef = fieldSymbol.DeclaringSyntaxReferences.FirstOrDefault();
            if (declRef != null)
            {
                var declNode = declRef.GetSyntax();
                if (declNode is VariableDeclaratorSyntax declarator && declarator.Initializer?.Value != null)
                {
                    var initConstant = semanticModel.GetConstantValue(declarator.Initializer.Value);
                    if (initConstant.HasValue && initConstant.Value is string initStr)
                        return initStr;
                }
            }
        }

        return null;
    }
}
