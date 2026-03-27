using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;

namespace Quarry.Internal;

/// <summary>
/// Runtime helper for extracting collection values from expression trees.
/// Used by generated interceptors when the collection receiver cannot be
/// accessed directly (non-public, instance, local, or complex expression).
/// </summary>
public static class ExpressionHelper
{
    /// <summary>
    /// Extracts the collection argument from a <c>Contains()</c> method call expression.
    /// Unwraps layers of <c>UnaryExpression</c> (Convert) and <c>MethodCallExpression</c>
    /// (op_Implicit) to find the underlying <c>MemberExpression</c>, then extracts via reflection.
    /// </summary>
    /// <typeparam name="T">The expected collection type (e.g., <c>IReadOnlyList&lt;string&gt;</c>).</typeparam>
    /// <param name="containsCall">The <c>MethodCallExpression</c> for the Contains() call.</param>
    /// <returns>The extracted collection value.</returns>
    [UnconditionalSuppressMessage("Trimming", "IL2075",
        Justification = "Closure fields are preserved by the expression tree that references them.")]
    public static T ExtractContainsCollection<T>(MethodCallExpression containsCall) where T : class
    {
        // Resolve the collection expression: Object for instance method, Arguments[0] for extension
        Expression collectionExpr = containsCall.Object ?? containsCall.Arguments[0];

        // Unwrap layers: UnaryExpression (Convert), MethodCallExpression (op_Implicit)
        while (collectionExpr is not MemberExpression)
        {
            if (collectionExpr is UnaryExpression unary)
                collectionExpr = unary.Operand;
            else if (collectionExpr is MethodCallExpression methodCall)
                collectionExpr = methodCall.Arguments.Count > 0 ? methodCall.Arguments[0] : methodCall.Object!;
            else
                break;
        }

        // Fast path: MemberExpression → direct field/property extraction
        if (collectionExpr is MemberExpression memberExpr)
        {
            var target = memberExpr.Expression is ConstantExpression constant ? constant.Value : null;

            if (memberExpr.Member is FieldInfo field)
                return (T)field.GetValue(target)!;

            if (memberExpr.Member is PropertyInfo property)
                return (T)property.GetValue(target)!;

            // Nested member access (e.g., obj.Inner.Collection) — fall through to compile path
        }

        // Fallback: compile and invoke the expression to extract the value
        var lambda = Expression.Lambda<Func<T>>(collectionExpr);
        return lambda.Compile().Invoke();
    }

    /// <summary>
    /// Extracts a captured value from a member expression chain of arbitrary depth.
    /// Walks inward from the outermost <c>MemberExpression</c> to the <c>ConstantExpression</c>
    /// root, then evaluates outward through each member access.
    /// </summary>
    /// <remarks>
    /// Handles single-hop captures (<c>name</c> → FieldInfo on closure), property chains
    /// (<c>input.Email</c> → PropertyInfo on FieldInfo on closure), and deeper chains
    /// (<c>input.Address.City</c>). Also handles static member access where
    /// <c>Expression</c> is <c>null</c>.
    /// </remarks>
    [UnconditionalSuppressMessage("Trimming", "IL2075",
        Justification = "Closure fields and properties are preserved by the expression tree that references them.")]
    public static object? ExtractMemberChainValue(MemberExpression memberExpr)
    {
        // Walk inward collecting MemberExpressions until we hit ConstantExpression (or null for static)
        var chain = new List<MemberExpression>(4);
        var current = memberExpr;
        while (current != null)
        {
            chain.Add(current);
            if (current.Expression is ConstantExpression)
                break;
            current = current.Expression as MemberExpression;
        }

        // Determine the root object
        object? value;
        var innermost = chain[chain.Count - 1];
        if (innermost.Expression is ConstantExpression constant)
            value = constant.Value;
        else
            value = null; // static member access

        // Walk outward (from innermost to outermost) extracting each member value
        for (int i = chain.Count - 1; i >= 0; i--)
        {
            var member = chain[i].Member;
            value = member is FieldInfo field
                ? field.GetValue(value)
                : ((PropertyInfo)member).GetValue(value);
        }

        return value;
    }
}
