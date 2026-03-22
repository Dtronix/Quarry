using System;
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
}
