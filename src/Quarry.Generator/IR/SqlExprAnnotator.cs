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
        // Try to resolve the type
        var typeInfo = semanticModel.GetTypeInfo(syntax);
        if (typeInfo.Type != null)
        {
            var clrType = typeInfo.Type.ToDisplayString();
            return captured.WithClrType(clrType);
        }

        // Try constant folding for enum values
        var constantValue = semanticModel.GetConstantValue(syntax);
        if (constantValue.HasValue && constantValue.Value != null)
        {
            // The value is a compile-time constant — it could be inlined as a literal
            // But for now, just update the type if we can
            if (typeInfo.Type != null)
                return captured.WithClrType(typeInfo.Type.ToDisplayString());
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
        CollectCapturedTypes(lambdaBody, semanticModel, typeMap);
        if (typeMap.Count == 0) return expr;

        return ApplyCapturedTypes(expr, typeMap);
    }

    private static void CollectCapturedTypes(
        SyntaxNode node,
        SemanticModel semanticModel,
        System.Collections.Generic.Dictionary<string, string> typeMap)
    {
        foreach (var identifier in node.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            var name = identifier.Identifier.ValueText;
            if (typeMap.ContainsKey(name)) continue;

            var typeInfo = semanticModel.GetTypeInfo(identifier);
            if (typeInfo.Type != null)
            {
                typeMap[name] = typeInfo.Type.ToDisplayString();
            }
        }
    }

    private static SqlExpr ApplyCapturedTypes(SqlExpr expr, System.Collections.Generic.Dictionary<string, string> typeMap)
    {
        switch (expr)
        {
            case CapturedValueExpr captured:
                if (typeMap.TryGetValue(captured.VariableName, out var clrType))
                    return captured.WithClrType(clrType);
                return captured;

            case BinaryOpExpr bin:
            {
                var left = ApplyCapturedTypes(bin.Left, typeMap);
                var right = ApplyCapturedTypes(bin.Right, typeMap);
                if (ReferenceEquals(left, bin.Left) && ReferenceEquals(right, bin.Right))
                    return bin;
                return new BinaryOpExpr(left, bin.Operator, right);
            }

            case UnaryOpExpr unary:
            {
                var operand = ApplyCapturedTypes(unary.Operand, typeMap);
                if (ReferenceEquals(operand, unary.Operand)) return unary;
                return new UnaryOpExpr(unary.Operator, operand);
            }

            case InExpr inExpr:
            {
                var operand = ApplyCapturedTypes(inExpr.Operand, typeMap);
                var changed = !ReferenceEquals(operand, inExpr.Operand);
                var newValues = new SqlExpr[inExpr.Values.Count];
                for (int i = 0; i < inExpr.Values.Count; i++)
                {
                    newValues[i] = ApplyCapturedTypes(inExpr.Values[i], typeMap);
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
                    newArgs[i] = ApplyCapturedTypes(func.Arguments[i], typeMap);
                    if (!ReferenceEquals(newArgs[i], func.Arguments[i])) changed = true;
                }
                return changed ? new FunctionCallExpr(func.FunctionName, newArgs, func.IsAggregate) : func;
            }

            case IsNullCheckExpr isNull:
            {
                var operand = ApplyCapturedTypes(isNull.Operand, typeMap);
                if (ReferenceEquals(operand, isNull.Operand)) return isNull;
                return new IsNullCheckExpr(operand, isNull.IsNegated);
            }

            case LikeExpr like:
            {
                var operand = ApplyCapturedTypes(like.Operand, typeMap);
                var pattern = ApplyCapturedTypes(like.Pattern, typeMap);
                if (ReferenceEquals(operand, like.Operand) && ReferenceEquals(pattern, like.Pattern))
                    return like;
                return new LikeExpr(operand, pattern, like.IsNegated, like.LikePrefix, like.LikeSuffix, like.NeedsEscape);
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
            else if (symbol is IFieldSymbol fieldSymbol && (fieldSymbol.IsReadOnly || fieldSymbol.IsConst || fieldSymbol.IsStatic))
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
}
