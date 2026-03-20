using System;
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
}
