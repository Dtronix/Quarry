using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Quarry.Generators.Models;
using Quarry.Generators.Sql;
using Quarry;
using Quarry.Shared.Migration;

namespace Quarry.Generators.Translation;

/// <summary>
/// Translates C# expression syntax (Roslyn AST) to SQL fragments at compile-time.
/// This is the core expression translator for the Quarry source generator.
/// </summary>
internal static class ExpressionSyntaxTranslator
{
    /// <summary>
    /// Translates a lambda expression body to SQL.
    /// </summary>
    /// <param name="expression">The expression syntax to translate.</param>
    /// <param name="context">The translation context.</param>
    /// <returns>The translation result.</returns>
    public static ExpressionTranslationResult Translate(
        ExpressionSyntax expression,
        ExpressionTranslationContext context)
    {
        try
        {
            var sql = TranslateExpression(expression, context);
            if (sql == null)
            {
                return ExpressionTranslationResult.Failure(
                    $"Unsupported expression: {expression.Kind()}");
            }
            return ExpressionTranslationResult.Success(sql, context.Parameters);
        }
        catch (TranslationException ex)
        {
            return ExpressionTranslationResult.Failure(ex.Message);
        }
    }

    /// <summary>
    /// Translates an expression and returns the SQL fragment.
    /// </summary>
    private static string? TranslateExpression(
        ExpressionSyntax expression,
        ExpressionTranslationContext context)
    {
        return expression switch
        {
            // Member access: u.Name, u.Age
            MemberAccessExpressionSyntax memberAccess => TranslateMemberAccess(memberAccess, context),

            // Binary: ==, !=, <, >, &&, ||, +, -, etc.
            BinaryExpressionSyntax binary => TranslateBinaryExpression(binary, context),

            // Parenthesized: (expr)
            ParenthesizedExpressionSyntax paren => TranslateParenthesized(paren, context),

            // Prefix unary: !expr
            PrefixUnaryExpressionSyntax prefixUnary => TranslatePrefixUnary(prefixUnary, context),

            // Literals: "string", 123, true
            LiteralExpressionSyntax literal => TranslateLiteral(literal, context),

            // Method invocation: s.Contains("x"), list.Contains(x)
            InvocationExpressionSyntax invocation => TranslateMethodInvocation(invocation, context),

            // is pattern: x is null, x is not null
            IsPatternExpressionSyntax isPattern => TranslateIsPattern(isPattern, context),

            // Conditional access: x?.Property (not supported in SQL)
            ConditionalAccessExpressionSyntax => null,

            // Cast expression: (int)x
            CastExpressionSyntax cast => TranslateExpression(cast.Expression, context),

            // Identifier (lambda parameter or captured variable)
            IdentifierNameSyntax identifier => TranslateIdentifier(identifier, context),

            // Implicit array creation: new[] { 1, 2, 3 }
            ImplicitArrayCreationExpressionSyntax implicitArray => TranslateArrayCreation(implicitArray, context),

            // Explicit array creation: new int[] { 1, 2, 3 }
            ArrayCreationExpressionSyntax arrayCreation => TranslateExplicitArrayCreation(arrayCreation, context),

            // Collection expression: [1, 2, 3] (C# 12+)
            CollectionExpressionSyntax collection => TranslateCollectionExpression(collection, context),

            _ => null
        };
    }

    /// <summary>
    /// Translates member access expressions (u.Name → "column_name").
    /// </summary>
    private static string? TranslateMemberAccess(
        MemberAccessExpressionSyntax memberAccess,
        ExpressionTranslationContext context)
    {
        var memberName = memberAccess.Name.Identifier.Text;

        // Check if this is accessing a lambda parameter's property
        if (memberAccess.Expression is IdentifierNameSyntax identifier)
        {
            var paramName = identifier.Identifier.Text;

            // Check subquery scopes first (innermost-first) and joined/primary entities
            var resolvedEntity = context.GetEntityInfo(paramName);
            if (resolvedEntity != null)
            {
                // Use qualified name when in subquery scope, join context, or alias context
                if (context.SubqueryDepth > 0 || context.JoinedEntities.Count > 0 ||
                    (context.TableAliases != null && context.TableAliases.Count > 0))
                {
                    var qualified = context.GetQualifiedColumnName(paramName, memberName);
                    if (qualified != null) return qualified;
                }

                // Simple unqualified column for primary entity with no joins/subqueries
                if (paramName == context.LambdaParameterName)
                {
                    var quotedColumn = context.GetQuotedColumnName(memberName);
                    if (quotedColumn != null) return quotedColumn;
                }

                throw new TranslationException($"Unknown column: {memberName}");
            }
        }

        // Check for nested member access (e.g., u.UserId.Id for Ref<> types)
        if (memberAccess.Expression is MemberAccessExpressionSyntax nestedAccess)
        {
            // Handle Ref<T,K>.Id access
            if (memberName == "Id")
            {
                if (nestedAccess.Expression is IdentifierNameSyntax nestedId)
                {
                    var paramName = nestedId.Identifier.Text;
                    var entityInfo = context.GetEntityInfo(paramName);
                    if (entityInfo != null)
                    {
                        var refPropertyName = nestedAccess.Name.Identifier.Text;
                        var column = context.GetColumnInfo(paramName, refPropertyName);
                        if (column != null && column.Kind == ColumnKind.ForeignKey)
                        {
                            if (context.JoinedEntities.Count > 0)
                            {
                                // Use positional alias if available
                                string qualifier;
                                if (context.TableAliases != null && context.TableAliases.TryGetValue(paramName, out var alias))
                                    qualifier = context.QuoteIdentifier(alias);
                                else
                                    qualifier = context.QuoteIdentifier(entityInfo.TableName);
                                return $"{qualifier}.{context.QuoteIdentifier(column.ColumnName)}";
                            }
                            return context.QuoteIdentifier(column.ColumnName);
                        }
                    }
                }
            }

            // Check if nested expression resolves to a captured variable
            var nestedSql = TranslateMemberAccess(nestedAccess, context);
            if (nestedSql != null)
            {
                // This might be a captured variable access - treat as parameter
                return TranslateCapturedValue(memberAccess, context);
            }
        }

        // This might be a captured variable (closure)
        return TranslateCapturedValue(memberAccess, context);
    }

    /// <summary>
    /// Translates a captured variable to a parameter.
    /// If the expression resolves to a compile-time constant (enum member, const field),
    /// inlines the value as a SQL literal instead of creating a parameter.
    /// </summary>
    private static string? TranslateCapturedValue(
        ExpressionSyntax expression,
        ExpressionTranslationContext context)
    {
        // SemanticModel may be null during deferred enrichment path
        if (context.SemanticModel == null)
        {
            // Try compilation fallback for constants (available during deferred enrichment)
            var inlined = TryInlineConstantFromCompilation(expression, context);
            if (inlined != null)
                return inlined;

            // Fallback: use "object" type and mark as captured, matching
            // the pattern in SyntacticClauseTranslator.AddCapturedParameter
            var valueExpr = expression.ToFullString().Trim();
            return context.AddParameter("object", valueExpr, isCaptured: true);
        }

        // Single semantic model query: check for constant first, then use TypeInfo.
        // This avoids separate GetConstantValue + GetTypeInfo calls on the same node.
        var constantValue = context.SemanticModel.GetConstantValue(expression);
        if (constantValue.HasValue)
            return FormatConstantAsSqlLiteral(constantValue.Value, context);

        // Get the type of the expression (only needed for the non-constant parameter path)
        var typeInfo = context.SemanticModel.GetTypeInfo(expression);
        if (typeInfo.Type == null)
            return null;

        var clrType = typeInfo.Type.ToDisplayString();
        var valueExpression = expression.ToFullString().Trim();

        return context.AddParameter(clrType, valueExpression, isCaptured: true, typeSymbol: typeInfo.Type);
    }

    /// <summary>
    /// Tries to resolve a compile-time constant using the Compilation fallback
    /// (available during deferred enrichment when SemanticModel is null).
    /// </summary>
    private static string? TryInlineConstantFromCompilation(ExpressionSyntax expression, ExpressionTranslationContext context)
    {
        if (context.Compilation != null)
        {
            var tree = expression.SyntaxTree;
            if (tree != null)
            {
                var sm = context.Compilation.GetSemanticModel(tree);
                var constantValue = sm.GetConstantValue(expression);
                if (constantValue.HasValue)
                    return FormatConstantAsSqlLiteral(constantValue.Value, context);
            }
        }

        return null;
    }

    /// <summary>
    /// Formats a compile-time constant value as a SQL literal.
    /// </summary>
    private static string? FormatConstantAsSqlLiteral(object? value, ExpressionTranslationContext context)
    {
        return value switch
        {
            null => "NULL",
            int i => i.ToString(CultureInfo.InvariantCulture),
            long l => l.ToString(CultureInfo.InvariantCulture),
            short s => s.ToString(CultureInfo.InvariantCulture),
            byte b => b.ToString(CultureInfo.InvariantCulture),
            sbyte sb => sb.ToString(CultureInfo.InvariantCulture),
            uint ui => ui.ToString(CultureInfo.InvariantCulture),
            ulong ul => ul.ToString(CultureInfo.InvariantCulture),
            ushort us => us.ToString(CultureInfo.InvariantCulture),
            float f => f.ToString(CultureInfo.InvariantCulture),
            double d => d.ToString(CultureInfo.InvariantCulture),
            decimal m => m.ToString(CultureInfo.InvariantCulture),
            bool bv => context.FormatBooleanLiteral(bv),
            char c => EscapeSqlString(c.ToString()),
            string str => EscapeSqlString(str),
            _ => null
        };
    }

    /// <summary>
    /// Translates binary expressions.
    /// </summary>
    private static string? TranslateBinaryExpression(
        BinaryExpressionSyntax binary,
        ExpressionTranslationContext context)
    {
        var paramCountBefore = context.Parameters.Count;
        var left = TranslateExpression(binary.Left, context);
        var right = TranslateExpression(binary.Right, context);

        if (left == null || right == null)
            return null;

        // Handle null comparisons specially
        if (IsNullComparison(binary, out var operand, out var isEquals))
        {
            var operandSql = TranslateExpression(operand, context);
            if (operandSql == null) return null;

            return isEquals
                ? $"{operandSql} IS NULL"
                : $"{operandSql} IS NOT NULL";
        }

        var op = GetSqlOperator(binary.Kind(), context);
        if (op == null)
            return null;

        // Handle string concatenation specially for MySQL
        if (binary.Kind() == SyntaxKind.AddExpression && IsStringType(binary, context))
        {
            return FormatStringConcat(context.Dialect, left, right);
        }

        // For comparison operators, apply TypeMapping ToDb wrapping on parameters
        // compared against mapped columns
        if (IsComparisonOperator(binary.Kind()) && context.Parameters.Count > paramCountBefore)
        {
            TryApplyTypeMappingToParameters(binary, context, paramCountBefore);
        }

        return $"{left} {op} {right}";
    }

    /// <summary>
    /// Checks if a binary expression kind is a comparison operator.
    /// </summary>
    private static bool IsComparisonOperator(SyntaxKind kind) => kind is
        SyntaxKind.EqualsExpression or SyntaxKind.NotEqualsExpression or
        SyntaxKind.LessThanExpression or SyntaxKind.LessThanOrEqualExpression or
        SyntaxKind.GreaterThanExpression or SyntaxKind.GreaterThanOrEqualExpression;

    /// <summary>
    /// If one side of a comparison is a mapped column and the other side produced parameters,
    /// sets CustomTypeMappingClass on those parameters so the interceptor wraps with ToDb().
    /// </summary>
    private static void TryApplyTypeMappingToParameters(
        BinaryExpressionSyntax binary,
        ExpressionTranslationContext context,
        int paramCountBefore)
    {
        // Check if the left or right side is a mapped column reference
        var mappingClass = TryGetMappedColumnMapping(binary.Left, context)
                        ?? TryGetMappedColumnMapping(binary.Right, context);

        if (mappingClass == null)
            return;

        // Apply mapping to all parameters added during this binary expression
        for (int i = paramCountBefore; i < context.Parameters.Count; i++)
        {
            context.Parameters[i].CustomTypeMappingClass = mappingClass;
        }
    }

    /// <summary>
    /// If the expression is a member access on a lambda parameter that refers to a mapped column,
    /// returns the CustomTypeMappingClass. Otherwise returns null.
    /// </summary>
    private static string? TryGetMappedColumnMapping(
        ExpressionSyntax expression,
        ExpressionTranslationContext context)
    {
        if (expression is not MemberAccessExpressionSyntax memberAccess)
            return null;

        if (memberAccess.Expression is not IdentifierNameSyntax identifier)
            return null;

        var paramName = identifier.Identifier.Text;
        var memberName = memberAccess.Name.Identifier.Text;

        // Look up the column in the context
        var column = context.GetColumnInfo(paramName, memberName);
        return column?.CustomTypeMappingClass;
    }

    /// <summary>
    /// Checks if a binary expression is a null comparison.
    /// </summary>
    private static bool IsNullComparison(
        BinaryExpressionSyntax binary,
        out ExpressionSyntax operand,
        out bool isEquals)
    {
        operand = null!;
        isEquals = false;

        if (binary.Kind() != SyntaxKind.EqualsExpression &&
            binary.Kind() != SyntaxKind.NotEqualsExpression)
        {
            return false;
        }

        var isLeftNull = binary.Left is LiteralExpressionSyntax left &&
                         left.Kind() == SyntaxKind.NullLiteralExpression;
        var isRightNull = binary.Right is LiteralExpressionSyntax right &&
                          right.Kind() == SyntaxKind.NullLiteralExpression;

        if (isLeftNull)
        {
            operand = binary.Right;
            isEquals = binary.Kind() == SyntaxKind.EqualsExpression;
            return true;
        }

        if (isRightNull)
        {
            operand = binary.Left;
            isEquals = binary.Kind() == SyntaxKind.EqualsExpression;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Gets the SQL operator for a binary expression kind.
    /// </summary>
    private static string? GetSqlOperator(SyntaxKind kind, ExpressionTranslationContext context)
    {
        return kind switch
        {
            // Comparison operators
            SyntaxKind.EqualsExpression => "=",
            SyntaxKind.NotEqualsExpression => "<>",
            SyntaxKind.LessThanExpression => "<",
            SyntaxKind.LessThanOrEqualExpression => "<=",
            SyntaxKind.GreaterThanExpression => ">",
            SyntaxKind.GreaterThanOrEqualExpression => ">=",

            // Logical operators
            SyntaxKind.LogicalAndExpression => "AND",
            SyntaxKind.LogicalOrExpression => "OR",

            // Arithmetic operators
            SyntaxKind.AddExpression => "+",
            SyntaxKind.SubtractExpression => "-",
            SyntaxKind.MultiplyExpression => "*",
            SyntaxKind.DivideExpression => "/",
            SyntaxKind.ModuloExpression => "%",

            // Bitwise operators
            SyntaxKind.BitwiseAndExpression => "&",
            SyntaxKind.BitwiseOrExpression => "|",
            SyntaxKind.ExclusiveOrExpression => "^",

            _ => null
        };
    }

    /// <summary>
    /// Checks if a binary expression operates on strings.
    /// </summary>
    private static bool IsStringType(BinaryExpressionSyntax binary, ExpressionTranslationContext context)
    {
        if (context.SemanticModel != null)
        {
            var typeInfo = context.SemanticModel.GetTypeInfo(binary);
            return typeInfo.Type?.SpecialType == SpecialType.System_String;
        }

        // Fallback: check if either operand is a known string column
        return IsStringMemberAccess(binary.Left, context) || IsStringMemberAccess(binary.Right, context);
    }

    /// <summary>
    /// Checks if an expression is a member access on a known string column (no SemanticModel needed).
    /// </summary>
    private static bool IsStringMemberAccess(ExpressionSyntax expression, ExpressionTranslationContext context)
    {
        if (expression is MemberAccessExpressionSyntax ma &&
            ma.Expression is IdentifierNameSyntax paramIdent)
        {
            var col = context.GetColumnInfo(paramIdent.Identifier.Text, ma.Name.Identifier.Text);
            return col != null && (col.ClrType == "string" || col.ClrType == "string?");
        }
        return false;
    }

    /// <summary>
    /// Formats string concatenation according to dialect.
    /// </summary>
    private static string FormatStringConcat(SqlDialect dialect, string left, string right)
    {
        return dialect switch
        {
            SqlDialect.MySQL => $"CONCAT({left}, {right})",
            SqlDialect.SqlServer => $"{left} + {right}",
            _ => $"{left} || {right}" // SQLite, PostgreSQL
        };
    }

    /// <summary>
    /// Translates parenthesized expressions.
    /// </summary>
    private static string? TranslateParenthesized(
        ParenthesizedExpressionSyntax paren,
        ExpressionTranslationContext context)
    {
        var inner = TranslateExpression(paren.Expression, context);
        return inner != null ? $"({inner})" : null;
    }

    /// <summary>
    /// Translates prefix unary expressions (e.g., !condition).
    /// </summary>
    private static string? TranslatePrefixUnary(
        PrefixUnaryExpressionSyntax prefixUnary,
        ExpressionTranslationContext context)
    {
        if (prefixUnary.Kind() == SyntaxKind.LogicalNotExpression)
        {
            var operand = TranslateExpression(prefixUnary.Operand, context);
            return operand != null ? $"NOT ({operand})" : null;
        }

        if (prefixUnary.Kind() == SyntaxKind.UnaryMinusExpression)
        {
            var operand = TranslateExpression(prefixUnary.Operand, context);
            return operand != null ? $"-{operand}" : null;
        }

        return null;
    }

    /// <summary>
    /// Translates literal expressions.
    /// </summary>
    private static string? TranslateLiteral(
        LiteralExpressionSyntax literal,
        ExpressionTranslationContext context)
    {
        return literal.Kind() switch
        {
            SyntaxKind.NumericLiteralExpression => literal.Token.ValueText,
            SyntaxKind.StringLiteralExpression => EscapeSqlString(literal.Token.ValueText),
            SyntaxKind.CharacterLiteralExpression => EscapeSqlString(literal.Token.ValueText),
            SyntaxKind.TrueLiteralExpression => context.FormatBooleanLiteral(true),
            SyntaxKind.FalseLiteralExpression => context.FormatBooleanLiteral(false),
            SyntaxKind.NullLiteralExpression => "NULL",
            _ => null
        };
    }

    /// <summary>
    /// Escapes a string for SQL.
    /// </summary>
    private static string EscapeSqlString(string value)
    {
        // Escape single quotes by doubling them
        var escaped = value.Replace("'", "''");
        return $"'{escaped}'";
    }

    /// <summary>
    /// Translates method invocations.
    /// </summary>
    private static string? TranslateMethodInvocation(
        InvocationExpressionSyntax invocation,
        ExpressionTranslationContext context)
    {
        // Get the method name
        string? methodName = null;
        ExpressionSyntax? receiver = null;

        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            methodName = memberAccess.Name.Identifier.Text;
            receiver = memberAccess.Expression;
        }
        else if (invocation.Expression is IdentifierNameSyntax identifier)
        {
            methodName = identifier.Identifier.Text;
        }

        if (methodName == null)
            return null;

        // Check for Sql.* aggregate functions
        if (receiver is IdentifierNameSyntax receiverIdent && receiverIdent.Identifier.Text == "Sql")
        {
            return TranslateSqlFunction(methodName, invocation.ArgumentList, context);
        }

        // Check for navigation subquery methods (Any, All, Count on Many<T>)
        if ((methodName == "Any" || methodName == "All" || methodName == "Count") &&
            IsNavigationReceiver(receiver, context, out var navInfo, out var outerParamName))
        {
            return methodName switch
            {
                "Any" => TranslateNavigationAny(navInfo!, outerParamName!, invocation.ArgumentList, context),
                "All" => TranslateNavigationAll(navInfo!, outerParamName!, invocation.ArgumentList, context),
                "Count" => TranslateNavigationCount(navInfo!, outerParamName!, invocation.ArgumentList, context),
                _ => null
            };
        }

        return methodName switch
        {
            // String methods
            "Contains" when IsStringReceiver(receiver, context) =>
                TranslateStringContains(receiver!, invocation.ArgumentList, context),
            "StartsWith" when IsStringReceiver(receiver, context) =>
                TranslateStringStartsWith(receiver!, invocation.ArgumentList, context),
            "EndsWith" when IsStringReceiver(receiver, context) =>
                TranslateStringEndsWith(receiver!, invocation.ArgumentList, context),
            "ToLower" when IsStringReceiver(receiver, context) =>
                TranslateToLower(receiver!, context),
            "ToLowerInvariant" when IsStringReceiver(receiver, context) =>
                TranslateToLower(receiver!, context),
            "ToUpper" when IsStringReceiver(receiver, context) =>
                TranslateToUpper(receiver!, context),
            "ToUpperInvariant" when IsStringReceiver(receiver, context) =>
                TranslateToUpper(receiver!, context),
            "Trim" when IsStringReceiver(receiver, context) =>
                TranslateTrim(receiver!, context),
            "Substring" when IsStringReceiver(receiver, context) =>
                TranslateSubstring(receiver!, invocation.ArgumentList, context),

            // Collection Contains (IN clause)
            "Contains" when IsCollectionReceiver(receiver, context) =>
                TranslateCollectionContains(receiver!, invocation.ArgumentList, context),

            _ => null
        };
    }

    /// <summary>
    /// Translates Sql.* static function calls to SQL.
    /// </summary>
    private static string? TranslateSqlFunction(
        string methodName,
        ArgumentListSyntax arguments,
        ExpressionTranslationContext context)
    {
        return methodName switch
        {
            // Sql.Count() → COUNT(*)
            "Count" when arguments.Arguments.Count == 0 => "COUNT(*)",

            // Sql.Count(expr) → COUNT(column)
            "Count" when arguments.Arguments.Count >= 1 =>
                TranslateAggregateFunction("COUNT", arguments.Arguments[0].Expression, context),

            // Sql.Sum(expr) → SUM(column)
            "Sum" when arguments.Arguments.Count >= 1 =>
                TranslateAggregateFunction("SUM", arguments.Arguments[0].Expression, context),

            // Sql.Avg(expr) → AVG(column)
            "Avg" when arguments.Arguments.Count >= 1 =>
                TranslateAggregateFunction("AVG", arguments.Arguments[0].Expression, context),

            // Sql.Min(expr) → MIN(column)
            "Min" when arguments.Arguments.Count >= 1 =>
                TranslateAggregateFunction("MIN", arguments.Arguments[0].Expression, context),

            // Sql.Max(expr) → MAX(column)
            "Max" when arguments.Arguments.Count >= 1 =>
                TranslateAggregateFunction("MAX", arguments.Arguments[0].Expression, context),

            // Sql.Raw<T>(sql, params) → inline SQL
            "Raw" when arguments.Arguments.Count >= 1 =>
                TranslateRawSql(arguments, context),

            // Sql.Exists(subquery) → EXISTS (SELECT ...)
            "Exists" when arguments.Arguments.Count >= 1 =>
                TranslateSqlExists(arguments.Arguments[0].Expression, context),

            _ => null
        };
    }

    /// <summary>
    /// Translates an aggregate function call.
    /// </summary>
    private static string? TranslateAggregateFunction(
        string functionName,
        ExpressionSyntax argument,
        ExpressionTranslationContext context)
    {
        var columnSql = TranslateExpression(argument, context);
        if (columnSql == null) return null;
        return $"{functionName}({columnSql})";
    }

    /// <summary>
    /// Translates Sql.Raw() to inline SQL.
    /// </summary>
    private static string? TranslateRawSql(
        ArgumentListSyntax arguments,
        ExpressionTranslationContext context)
    {
        if (arguments.Arguments.Count < 1)
            return null;

        var sqlArg = arguments.Arguments[0].Expression;

        // Extract the SQL string literal
        if (sqlArg is LiteralExpressionSyntax literal &&
            literal.Kind() == SyntaxKind.StringLiteralExpression)
        {
            // Process additional parameters if provided, using StringBuilder to avoid repeated string allocations
            var sbRawSql = new System.Text.StringBuilder(literal.Token.ValueText);

            for (int i = 1; i < arguments.Arguments.Count; i++)
            {
                var paramArg = arguments.Arguments[i].Expression;
                var placeholder = $"@p{i - 1}";

                // Try to translate as a column reference (e.g., u.UserId → quoted column name).
                // Column references must be inlined into the SQL, not added as runtime parameters,
                // because the lambda parameter is not in scope in the generated interceptor.
                // Translate the parameter expression through the normal expression translator.
                // This correctly handles column references (u.UserId → quoted column name),
                // captured variables, and literals, instead of copying the C# text literally
                // which would reference the lambda parameter out of scope in the interceptor.
                var translatedSql = TranslateExpression(paramArg, context);
                if (translatedSql != null)
                {
                    sbRawSql.Replace(placeholder, translatedSql);
                }
                else
                {
                    // Fallback: add as a runtime parameter
                    var typeInfo = context.SemanticModel.GetTypeInfo(paramArg);
                    var clrType = typeInfo.Type?.ToDisplayString() ?? "object";
                    var valueExpr = paramArg.ToFullString().Trim();
                    var paramPlaceholder = context.AddParameter(clrType, valueExpr, typeSymbol: typeInfo.Type);
                    sbRawSql.Replace(placeholder, paramPlaceholder);
                }
            }

            return sbRawSql.ToString();
        }

        return null;
    }

    /// <summary>
    /// Translates Sql.Exists() to EXISTS SQL.
    /// </summary>
    private static string? TranslateSqlExists(
        ExpressionSyntax subqueryExpr,
        ExpressionTranslationContext context)
    {
        // For now, we just support a simple EXISTS check
        // Full subquery translation would require more complex handling
        var subquerySql = TranslateExpression(subqueryExpr, context);
        if (subquerySql != null)
        {
            return $"EXISTS ({subquerySql})";
        }
        return null;
    }

    /// <summary>
    /// Checks if the receiver is a string expression.
    /// </summary>
    private static bool IsStringReceiver(ExpressionSyntax? receiver, ExpressionTranslationContext context)
    {
        if (receiver == null) return false;

        // Prefer SemanticModel when available (most accurate)
        if (context.SemanticModel != null)
        {
            var typeInfo = context.SemanticModel.GetTypeInfo(receiver);
            return typeInfo.Type?.SpecialType == SpecialType.System_String;
        }

        // Fallback: resolve from ColumnInfo.ClrType (deferred enrichment path)
        if (receiver is MemberAccessExpressionSyntax ma &&
            ma.Expression is IdentifierNameSyntax paramIdent)
        {
            var col = context.GetColumnInfo(paramIdent.Identifier.Text, ma.Name.Identifier.Text);
            return col != null && (col.ClrType == "string" || col.ClrType == "string?");
        }

        return false;
    }

    /// <summary>
    /// Checks if the receiver is a collection (array, list, etc.).
    /// </summary>
    private static bool IsCollectionReceiver(ExpressionSyntax? receiver, ExpressionTranslationContext context)
    {
        if (receiver == null) return false;

        // Prefer SemanticModel when available (most accurate).
        // Fall back to Compilation-recovered SemanticModel for the enrichment path.
        var sm = context.SemanticModel;
        if (sm == null && context.Compilation != null && receiver.SyntaxTree != null)
            sm = context.Compilation.GetSemanticModel(receiver.SyntaxTree);

        if (sm != null)
        {
            var typeInfo = sm.GetTypeInfo(receiver);
            if (typeInfo.Type == null) return false;

            // Check for array type
            if (typeInfo.Type is IArrayTypeSymbol)
                return true;

            // Check for IEnumerable<T>
            if (typeInfo.Type.AllInterfaces.Length > 0)
            {
                foreach (var iface in typeInfo.Type.AllInterfaces)
                {
                    if (iface.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T)
                        return true;
                }
            }

            // Check if type itself is IEnumerable<T>
            if (typeInfo.Type.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T)
                return true;

            return false;
        }

        // Fallback: when SemanticModel is not available (deferred enrichment path for
        // joined queries), use syntactic heuristics. If the receiver is a captured variable
        // (not a lambda parameter) and the argument to Contains is a column, this is a
        // collection.Contains(column) pattern for an IN clause.
        if (receiver is IdentifierNameSyntax ident)
        {
            // If the identifier is not a lambda parameter, it's a captured variable —
            // likely an array/list/collection passed to .Contains().
            return context.GetEntityInfo(ident.Identifier.Text) == null;
        }

        // Inline array/collection expressions are always collections
        if (receiver is ImplicitArrayCreationExpressionSyntax
            or ArrayCreationExpressionSyntax
            or CollectionExpressionSyntax)
            return true;

        return false;
    }

    /// <summary>
    /// Translates string.Contains to LIKE '%value%'.
    /// </summary>
    private static string? TranslateStringContains(
        ExpressionSyntax receiver,
        ArgumentListSyntax arguments,
        ExpressionTranslationContext context)
    {
        if (arguments.Arguments.Count < 1)
            return null;

        var columnSql = TranslateExpression(receiver, context);
        if (columnSql == null) return null;

        var arg = arguments.Arguments[0].Expression;

        // If argument is a literal string, parameterize it
        if (arg is LiteralExpressionSyntax literal &&
            literal.Kind() == SyntaxKind.StringLiteralExpression)
        {
            var rawValue = literal.Token.ValueText;
            var escaped = SqlLikeHelpers.EscapeLikeMetaChars(rawValue);
            var csharpEscaped = EscapeForCSharpString(escaped);
            var param = context.AddParameter("string", $"\"{csharpEscaped}\"");
            var sql = SqlLikeHelpers.FormatLikeWithParameter(context.Dialect, columnSql, param, "%", "%");
            if (escaped != rawValue)
                sql += " ESCAPE '\\'";
            return sql;
        }

        // Otherwise use parameter
        var argSql = TranslateExpression(arg, context);
        if (argSql == null) return null;

        return SqlLikeHelpers.FormatLikeWithParameter(context.Dialect, columnSql, argSql, "%", "%");
    }

    /// <summary>
    /// Translates string.StartsWith to LIKE 'value%'.
    /// </summary>
    private static string? TranslateStringStartsWith(
        ExpressionSyntax receiver,
        ArgumentListSyntax arguments,
        ExpressionTranslationContext context)
    {
        if (arguments.Arguments.Count < 1)
            return null;

        var columnSql = TranslateExpression(receiver, context);
        if (columnSql == null) return null;

        var arg = arguments.Arguments[0].Expression;

        if (arg is LiteralExpressionSyntax literal &&
            literal.Kind() == SyntaxKind.StringLiteralExpression)
        {
            var rawValue = literal.Token.ValueText;
            var escaped = SqlLikeHelpers.EscapeLikeMetaChars(rawValue);
            var csharpEscaped = EscapeForCSharpString(escaped);
            var param = context.AddParameter("string", $"\"{csharpEscaped}\"");
            var sql = SqlLikeHelpers.FormatLikeWithParameter(context.Dialect, columnSql, param, "", "%");
            if (escaped != rawValue)
                sql += " ESCAPE '\\'";
            return sql;
        }

        var argSql = TranslateExpression(arg, context);
        if (argSql == null) return null;

        return SqlLikeHelpers.FormatLikeWithParameter(context.Dialect, columnSql, argSql, "", "%");
    }

    /// <summary>
    /// Translates string.EndsWith to LIKE '%value'.
    /// </summary>
    private static string? TranslateStringEndsWith(
        ExpressionSyntax receiver,
        ArgumentListSyntax arguments,
        ExpressionTranslationContext context)
    {
        if (arguments.Arguments.Count < 1)
            return null;

        var columnSql = TranslateExpression(receiver, context);
        if (columnSql == null) return null;

        var arg = arguments.Arguments[0].Expression;

        if (arg is LiteralExpressionSyntax literal &&
            literal.Kind() == SyntaxKind.StringLiteralExpression)
        {
            var rawValue = literal.Token.ValueText;
            var escaped = SqlLikeHelpers.EscapeLikeMetaChars(rawValue);
            var csharpEscaped = EscapeForCSharpString(escaped);
            var param = context.AddParameter("string", $"\"{csharpEscaped}\"");
            var sql = SqlLikeHelpers.FormatLikeWithParameter(context.Dialect, columnSql, param, "%", "");
            if (escaped != rawValue)
                sql += " ESCAPE '\\'";
            return sql;
        }

        var argSql = TranslateExpression(arg, context);
        if (argSql == null) return null;

        return SqlLikeHelpers.FormatLikeWithParameter(context.Dialect, columnSql, argSql, "%", "");
    }


    /// <summary>
    /// Translates ToLower to LOWER().
    /// </summary>
    private static string? TranslateToLower(
        ExpressionSyntax receiver,
        ExpressionTranslationContext context)
    {
        var sql = TranslateExpression(receiver, context);
        return sql != null ? $"LOWER({sql})" : null;
    }

    /// <summary>
    /// Translates ToUpper to UPPER().
    /// </summary>
    private static string? TranslateToUpper(
        ExpressionSyntax receiver,
        ExpressionTranslationContext context)
    {
        var sql = TranslateExpression(receiver, context);
        return sql != null ? $"UPPER({sql})" : null;
    }

    /// <summary>
    /// Translates Trim to TRIM().
    /// </summary>
    private static string? TranslateTrim(
        ExpressionSyntax receiver,
        ExpressionTranslationContext context)
    {
        var sql = TranslateExpression(receiver, context);
        return sql != null ? $"TRIM({sql})" : null;
    }

    /// <summary>
    /// Translates Substring to SUBSTRING().
    /// </summary>
    private static string? TranslateSubstring(
        ExpressionSyntax receiver,
        ArgumentListSyntax arguments,
        ExpressionTranslationContext context)
    {
        var stringSql = TranslateExpression(receiver, context);
        if (stringSql == null) return null;

        if (arguments.Arguments.Count < 1)
            return null;

        var startArg = TranslateExpression(arguments.Arguments[0].Expression, context);
        if (startArg == null) return null;

        // SQL SUBSTRING is 1-based, C# Substring is 0-based
        var startSql = $"({startArg} + 1)";

        if (arguments.Arguments.Count >= 2)
        {
            var lengthArg = TranslateExpression(arguments.Arguments[1].Expression, context);
            if (lengthArg == null) return null;
            return $"SUBSTRING({stringSql}, {startSql}, {lengthArg})";
        }

        // Without length, different dialects handle this differently
        return context.Dialect switch
        {
            SqlDialect.SqlServer => $"SUBSTRING({stringSql}, {startSql}, LEN({stringSql}))",
            _ => $"SUBSTRING({stringSql} FROM {startSql})"
        };
    }

    /// <summary>
    /// Translates collection.Contains(x) to x IN (...).
    /// </summary>
    private static string? TranslateCollectionContains(
        ExpressionSyntax receiver,
        ArgumentListSyntax arguments,
        ExpressionTranslationContext context)
    {
        if (arguments.Arguments.Count < 1)
            return null;

        var elementArg = arguments.Arguments[0].Expression;
        var elementSql = TranslateExpression(elementArg, context);
        if (elementSql == null) return null;

        // Check if the collection is a constant array or inline
        var collectionValues = TryExtractCollectionLiterals(receiver);

        // Try to resolve variable references to their initializers so we can inline the values.
        // This handles both: (1) deferred enrichment path (no SemanticModel) where parameter
        // extraction code cannot be generated, and (2) captured collection variables like
        // var ids = new[] { 1, 2, 3 }; ... ids.Contains(u.Id) where the variable's initializer
        // contains extractable literals.
        if (collectionValues == null && receiver is IdentifierNameSyntax varIdent)
        {
            collectionValues = TryResolveVariableCollectionLiterals(varIdent);
        }

        if (collectionValues != null && collectionValues.Count > 0)
        {
            // Inline the values
            var inlineValues = new List<string>();
            foreach (var value in collectionValues)
            {
                if (value is LiteralExpressionSyntax lit)
                {
                    var litSql = TranslateLiteral(lit, context);
                    if (litSql != null)
                        inlineValues.Add(litSql);
                }
                else
                {
                    // Complex expression - use parameter
                    var exprSql = TranslateExpression(value, context);
                    if (exprSql != null)
                        inlineValues.Add(exprSql);
                }
            }
            return $"{elementSql} IN ({string.Join(", ", inlineValues)})";
        }

        // Collection is a variable - generate parameter with collection marker.
        // Use SemanticModel directly, or recover one from Compilation for the enrichment path.
        SemanticModel? sm = context.SemanticModel;
        if (sm == null && context.Compilation != null && receiver.SyntaxTree != null)
            sm = context.Compilation.GetSemanticModel(receiver.SyntaxTree);
        if (sm == null) return null;

        var typeInfo = sm.GetTypeInfo(receiver);
        if (typeInfo.Type == null) return null;

        var clrType = typeInfo.Type.ToDisplayString();
        var valueExpression = receiver.ToFullString().Trim();

        // Collection variables captured from the enclosing scope need isCaptured=true
        // so the carrier clause interceptor can extract them via expression tree navigation.
        // The expression path "__CONTAINS_COLLECTION__" is a sentinel recognized by the carrier
        // code generator to emit MethodCallExpression-based extraction for Contains() receivers.
        var isCaptured = receiver is IdentifierNameSyntax;
        var param = context.AddParameter(clrType, valueExpression, isCollection: true, isCaptured: isCaptured, typeSymbol: typeInfo.Type);
        if (isCaptured)
        {
            // Find the ParameterInfo we just added and set the expression path
            var lastParam = context.Parameters[context.Parameters.Count - 1];
            lastParam.ExpressionPath = "__CONTAINS_COLLECTION__";

            // Resolve the receiver symbol for direct-access classification in BuildChainParameters.
            // Public static fields/properties can be accessed directly without expression tree extraction.
            var symbolInfo = sm.GetSymbolInfo(receiver);
            lastParam.CollectionReceiverSymbol = symbolInfo.Symbol;
        }

        // The runtime will expand this to individual parameters
        return $"{elementSql} IN ({param})";
    }

    /// <summary>
    /// Tries to extract literal values from an inline collection.
    /// </summary>
    private static List<ExpressionSyntax>? TryExtractCollectionLiterals(ExpressionSyntax expression)
    {
        // Array creation: new[] { 1, 2, 3 }
        if (expression is ImplicitArrayCreationExpressionSyntax implicitArray)
        {
            return new List<ExpressionSyntax>(
                implicitArray.Initializer.Expressions);
        }

        // Explicit array: new int[] { 1, 2, 3 }
        if (expression is ArrayCreationExpressionSyntax arrayCreation &&
            arrayCreation.Initializer != null)
        {
            return new List<ExpressionSyntax>(
                arrayCreation.Initializer.Expressions);
        }

        // Collection expression: [1, 2, 3]
        if (expression is CollectionExpressionSyntax collection)
        {
            var elements = new List<ExpressionSyntax>();
            foreach (var element in collection.Elements)
            {
                if (element is ExpressionElementSyntax exprElement)
                {
                    elements.Add(exprElement.Expression);
                }
            }
            return elements;
        }

        return null;
    }

    /// <summary>
    /// Tries to resolve a variable to its initializer and extract collection literals.
    /// Used when SemanticModel is not available (deferred enrichment for joined queries)
    /// and we cannot generate captured-variable parameter extraction code.
    /// </summary>
    private static List<ExpressionSyntax>? TryResolveVariableCollectionLiterals(IdentifierNameSyntax identifier)
    {
        var variableName = identifier.Identifier.Text;

        // Walk up the syntax tree to find the enclosing block
        for (var node = identifier.Parent; node != null; node = node.Parent)
        {
            if (node is BlockSyntax block)
            {
                foreach (var statement in block.Statements)
                {
                    if (statement is LocalDeclarationStatementSyntax localDecl)
                    {
                        foreach (var variable in localDecl.Declaration.Variables)
                        {
                            if (variable.Identifier.Text == variableName &&
                                variable.Initializer?.Value != null)
                            {
                                return TryExtractCollectionLiterals(variable.Initializer.Value);
                            }
                        }
                    }
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Translates "is pattern" expressions.
    /// </summary>
    private static string? TranslateIsPattern(
        IsPatternExpressionSyntax isPattern,
        ExpressionTranslationContext context)
    {
        var expression = TranslateExpression(isPattern.Expression, context);
        if (expression == null) return null;

        return isPattern.Pattern switch
        {
            // x is null
            ConstantPatternSyntax { Expression: LiteralExpressionSyntax literal }
                when literal.Kind() == SyntaxKind.NullLiteralExpression =>
                $"{expression} IS NULL",

            // x is not null
            UnaryPatternSyntax { Pattern: ConstantPatternSyntax { Expression: LiteralExpressionSyntax literal } }
                when literal.Kind() == SyntaxKind.NullLiteralExpression =>
                $"{expression} IS NOT NULL",

            // x is SomeConstant
            ConstantPatternSyntax constantPattern =>
                TranslateConstantPattern(expression, constantPattern, context),

            _ => null
        };
    }

    /// <summary>
    /// Translates a constant pattern (x is 5, x is "value", etc.).
    /// </summary>
    private static string? TranslateConstantPattern(
        string expression,
        ConstantPatternSyntax pattern,
        ExpressionTranslationContext context)
    {
        var valueSql = TranslateExpression(pattern.Expression, context);
        if (valueSql == null) return null;
        return $"{expression} = {valueSql}";
    }

    /// <summary>
    /// Translates an identifier (lambda parameter or captured variable).
    /// </summary>
    private static string? TranslateIdentifier(
        IdentifierNameSyntax identifier,
        ExpressionTranslationContext context)
    {
        var name = identifier.Identifier.Text;

        // If it's the lambda parameter or a known subquery scope parameter, invalid on its own
        if (name == context.LambdaParameterName || context.GetEntityInfo(name) != null)
        {
            return null; // Lambda/scope parameter should only appear in member access
        }

        // This is likely a captured variable
        return TranslateCapturedValue(identifier, context);
    }

    /// <summary>
    /// Translates implicit array creation.
    /// </summary>
    private static string? TranslateArrayCreation(
        ImplicitArrayCreationExpressionSyntax array,
        ExpressionTranslationContext context)
    {
        // Return null - arrays are handled by Contains() translation
        return null;
    }

    /// <summary>
    /// Translates explicit array creation.
    /// </summary>
    private static string? TranslateExplicitArrayCreation(
        ArrayCreationExpressionSyntax array,
        ExpressionTranslationContext context)
    {
        // Return null - arrays are handled by Contains() translation
        return null;
    }

    /// <summary>
    /// Translates collection expressions.
    /// </summary>
    private static string? TranslateCollectionExpression(
        CollectionExpressionSyntax collection,
        ExpressionTranslationContext context)
    {
        // Return null - collections are handled by Contains() translation
        return null;
    }
    // ─── Navigation subquery translation ───────────────────────────────

    /// <summary>
    /// Checks if the receiver is a Many&lt;T&gt; navigation property (param.Navigation).
    /// </summary>
    private static bool IsNavigationReceiver(
        ExpressionSyntax? receiver,
        ExpressionTranslationContext context,
        out NavigationInfo? navInfo,
        out string? outerParameterName)
    {
        navInfo = null;
        outerParameterName = null;

        // Receiver must be MemberAccess: param.NavigationProperty
        if (receiver is not MemberAccessExpressionSyntax memberAccess)
            return false;

        if (memberAccess.Expression is not IdentifierNameSyntax identifierSyntax)
            return false;

        var paramName = identifierSyntax.Identifier.Text;
        var navPropertyName = memberAccess.Name.Identifier.Text;

        // Resolve the parameter to an entity
        var entityInfo = context.GetEntityInfo(paramName);
        if (entityInfo == null)
            return false;

        // Look up navigation by property name
        foreach (var nav in entityInfo.Navigations)
        {
            if (nav.PropertyName == navPropertyName)
            {
                navInfo = nav;
                outerParameterName = paramName;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Public wrapper for FK-to-PK correlation resolution.
    /// Used by ClauseTranslator for navigation-based join ON condition generation.
    /// </summary>
    public static (string FkColumnName, string PkColumnName)? ResolveForeignKeyCorrelationPublic(
        NavigationInfo navInfo,
        EntityInfo innerEntity,
        EntityInfo outerEntity)
    {
        return ResolveForeignKeyCorrelation(navInfo, innerEntity, outerEntity);
    }

    /// <summary>
    /// Resolves the FK-to-PK correlation columns for a subquery.
    /// Returns (fkColumnName on inner entity, pkColumnName on outer entity).
    /// </summary>
    private static (string FkColumnName, string PkColumnName)? ResolveForeignKeyCorrelation(
        NavigationInfo navInfo,
        EntityInfo innerEntity,
        EntityInfo outerEntity)
    {
        // Find FK column on inner entity
        string? fkColumnName = null;
        foreach (var col in innerEntity.Columns)
        {
            if (col.PropertyName == navInfo.ForeignKeyPropertyName)
            {
                fkColumnName = col.ColumnName;
                break;
            }
        }

        if (fkColumnName == null)
            return null;

        // Step 1: Check if outer entity has a column with same property name as the FK
        foreach (var col in outerEntity.Columns)
        {
            if (col.PropertyName == navInfo.ForeignKeyPropertyName)
                return (fkColumnName, col.ColumnName);
        }

        // Step 2: Fall back to the outer entity's single primary key
        string? pkColumnName = null;
        int pkCount = 0;
        foreach (var col in outerEntity.Columns)
        {
            if (col.Kind == ColumnKind.PrimaryKey)
            {
                pkColumnName = col.ColumnName;
                pkCount++;
            }
        }

        if (pkCount == 1 && pkColumnName != null)
            return (fkColumnName, pkColumnName);

        if (pkCount > 1)
            throw new TranslationException(
                $"Navigation subquery on entity '{outerEntity.EntityName}' is not supported because it has a composite primary key ({pkCount} PK columns)");

        return null;
    }

    /// <summary>
    /// Translates u.Navigation.Any() or u.Navigation.Any(pred) to EXISTS subquery SQL.
    /// </summary>
    private static string? TranslateNavigationAny(
        NavigationInfo navInfo,
        string outerParameterName,
        ArgumentListSyntax arguments,
        ExpressionTranslationContext context)
    {
        // Extract inner lambda if provided
        LambdaExpressionSyntax? innerLambda = null;
        if (arguments.Arguments.Count >= 1 &&
            arguments.Arguments[0].Expression is LambdaExpressionSyntax lambda)
        {
            innerLambda = lambda;
        }

        var innerSql = TranslateSubqueryInner(
            navInfo, outerParameterName, context, innerLambda,
            out var tableSql, out var correlationSql);

        if (tableSql == null || correlationSql == null)
            return null;

        var whereParts = correlationSql;
        if (innerSql != null)
            whereParts += $" AND ({innerSql})";

        return $"EXISTS (SELECT 1 FROM {tableSql} WHERE {whereParts})";
    }

    /// <summary>
    /// Translates u.Navigation.All(pred) to NOT EXISTS (... AND NOT pred) SQL.
    /// </summary>
    private static string? TranslateNavigationAll(
        NavigationInfo navInfo,
        string outerParameterName,
        ArgumentListSyntax arguments,
        ExpressionTranslationContext context)
    {
        // All() requires a predicate
        if (arguments.Arguments.Count < 1 ||
            arguments.Arguments[0].Expression is not LambdaExpressionSyntax innerLambda)
        {
            return null;
        }

        var innerSql = TranslateSubqueryInner(
            navInfo, outerParameterName, context, innerLambda,
            out var tableSql, out var correlationSql);

        if (tableSql == null || correlationSql == null || innerSql == null)
            return null;

        return $"NOT EXISTS (SELECT 1 FROM {tableSql} WHERE {correlationSql} AND NOT ({innerSql}))";
    }

    /// <summary>
    /// Translates u.Navigation.Count() or u.Navigation.Count(pred) to scalar count subquery SQL.
    /// </summary>
    private static string? TranslateNavigationCount(
        NavigationInfo navInfo,
        string outerParameterName,
        ArgumentListSyntax arguments,
        ExpressionTranslationContext context)
    {
        // Extract inner lambda if provided
        LambdaExpressionSyntax? innerLambda = null;
        if (arguments.Arguments.Count >= 1 &&
            arguments.Arguments[0].Expression is LambdaExpressionSyntax lambda)
        {
            innerLambda = lambda;
        }

        var innerSql = TranslateSubqueryInner(
            navInfo, outerParameterName, context, innerLambda,
            out var tableSql, out var correlationSql);

        if (tableSql == null || correlationSql == null)
            return null;

        var whereParts = correlationSql;
        if (innerSql != null)
            whereParts += $" AND ({innerSql})";

        return $"(SELECT COUNT(*) FROM {tableSql} WHERE {whereParts})";
    }

    /// <summary>
    /// Common infrastructure for subquery translation: resolves inner entity,
    /// pushes scope, builds table reference and correlation, translates inner predicate.
    /// Returns the inner predicate SQL (null if no predicate).
    /// Sets tableSql and correlationSql via out params.
    /// </summary>
    private static string? TranslateSubqueryInner(
        NavigationInfo navInfo,
        string outerParameterName,
        ExpressionTranslationContext context,
        LambdaExpressionSyntax? innerLambda,
        out string? tableSql,
        out string? correlationSql)
    {
        tableSql = null;
        correlationSql = null;

        // Resolve inner entity from registry
        if (context.EntityRegistry == null ||
            !context.EntityRegistry.TryGetValue(navInfo.RelatedEntityName, out var innerEntity))
        {
            return null;
        }

        // Resolve outer entity
        var outerEntity = context.GetEntityInfo(outerParameterName);
        if (outerEntity == null)
            return null;

        // Resolve FK-to-PK correlation
        var correlation = ResolveForeignKeyCorrelation(navInfo, innerEntity, outerEntity);
        if (!correlation.HasValue)
            return null;

        var (fkColumnName, pkColumnName) = correlation.Value;

        // Extract inner lambda parameter name
        string innerParamName;
        ExpressionSyntax? lambdaBody = null;
        if (innerLambda is SimpleLambdaExpressionSyntax simpleLambda)
        {
            innerParamName = simpleLambda.Parameter.Identifier.Text;
            lambdaBody = simpleLambda.Body as ExpressionSyntax;
        }
        else if (innerLambda is ParenthesizedLambdaExpressionSyntax parenLambda &&
                 parenLambda.ParameterList.Parameters.Count > 0)
        {
            innerParamName = parenLambda.ParameterList.Parameters[0].Identifier.Text;
            lambdaBody = parenLambda.Body as ExpressionSyntax;
        }
        else
        {
            innerParamName = "_sq";
        }

        // Push subquery scope
        var alias = context.PushSubqueryScope(innerParamName, innerEntity);

        // Build table reference
        tableSql = $"{context.QuoteIdentifier(innerEntity.TableName)} AS {context.QuoteIdentifier(alias)}";

        // Build correlation condition
        var qualifiedFk = $"{context.QuoteIdentifier(alias)}.{context.QuoteIdentifier(fkColumnName)}";
        var pkPropertyName = GetPropertyNameForColumn(outerEntity, pkColumnName);
        var qualifiedPk = pkPropertyName != null
            ? context.GetQualifiedColumnName(outerParameterName, pkPropertyName)
            : null;

        // If outer entity has no alias (outermost query with no joins), use table name directly
        if (qualifiedPk == null)
        {
            qualifiedPk = $"{context.QuoteIdentifier(outerEntity.TableName)}.{context.QuoteIdentifier(pkColumnName)}";
        }

        correlationSql = $"{qualifiedFk} = {qualifiedPk}";

        // Translate inner predicate if present, ensuring scope is always popped
        string? innerPredicateSql = null;
        try
        {
            if (lambdaBody != null)
            {
                innerPredicateSql = TranslateExpression(lambdaBody, context);
            }
        }
        finally
        {
            context.PopSubqueryScope();
        }

        return innerPredicateSql;
    }

    /// <summary>
    /// Gets the property name for a column by its database column name.
    /// </summary>
    private static string? GetPropertyNameForColumn(EntityInfo entity, string columnName)
    {
        foreach (var col in entity.Columns)
        {
            if (col.ColumnName == columnName)
                return col.PropertyName;
        }
        return null;
    }

    private static string EscapeForCSharpString(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}

/// <summary>
/// Exception thrown when expression translation fails.
/// </summary>
internal sealed class TranslationException : Exception
{
    public TranslationException(string message) : base(message) { }
}
