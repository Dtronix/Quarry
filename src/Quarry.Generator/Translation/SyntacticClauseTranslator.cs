using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Quarry.Generators.Models;
using Quarry.Generators.Sql;
using Quarry;

namespace Quarry.Generators.Translation;

/// <summary>
/// Translates syntactic expression trees to SQL using EntityInfo for column resolution.
/// This translator works without SemanticModel, making it suitable for deferred clause analysis.
/// </summary>
internal sealed class SyntacticClauseTranslator
{
    private readonly EntityInfo _entityInfo;
    private readonly SqlDialect _dialect;
    private readonly Dictionary<string, ColumnInfo> _columnLookup;
    private readonly List<ParameterInfo> _parameters;
    private int _parameterIndex;

    public SyntacticClauseTranslator(EntityInfo entityInfo, SqlDialect dialect)
    {
        _entityInfo = entityInfo;
        _dialect = dialect;
        _columnLookup = entityInfo.Columns.ToDictionary(c => c.PropertyName, StringComparer.Ordinal);
        _parameters = new List<ParameterInfo>();
        _parameterIndex = 0;
    }

    /// <summary>
    /// Translates a pending clause to a completed ClauseInfo.
    /// </summary>
    public ClauseInfo Translate(PendingClauseInfo pending)
    {
        try
        {
            // Where and Having clauses need boolean context for property access
            var inBooleanContext = pending.Kind == ClauseKind.Where || pending.Kind == ClauseKind.Having;
            var sql = TranslateExpression(pending.Expression, pending.LambdaParameterName, inBooleanContext);

            if (sql == null)
            {
                return ClauseInfo.Failure(pending.Kind, "Failed to translate syntactic expression");
            }

            if (pending.Kind == ClauseKind.OrderBy)
            {
                return new OrderByClauseInfo(sql, pending.IsDescending, _parameters);
            }

            return ClauseInfo.Success(pending.Kind, sql, _parameters);
        }
        catch (Exception ex)
        {
            return ClauseInfo.Failure(pending.Kind, $"Translation error: {ex.Message}");
        }
    }

    /// <summary>
    /// Translates a syntactic expression to SQL.
    /// </summary>
    private string? TranslateExpression(SyntacticExpression expr, string lambdaParameterName)
    {
        return TranslateExpression(expr, lambdaParameterName, inBooleanContext: false);
    }

    /// <summary>
    /// Translates a syntactic expression to SQL with context awareness.
    /// </summary>
    private string? TranslateExpression(SyntacticExpression expr, string lambdaParameterName, bool inBooleanContext)
    {
        switch (expr)
        {
            case SyntacticPropertyAccess propAccess:
                return TranslatePropertyAccess(propAccess, inBooleanContext);

            case SyntacticLiteral literal:
                return TranslateLiteral(literal);

            case SyntacticBinary binary:
                return TranslateBinary(binary, lambdaParameterName);

            case SyntacticUnary unary:
                return TranslateUnary(unary, lambdaParameterName);

            case SyntacticMethodCall methodCall:
                return TranslateMethodCall(methodCall, lambdaParameterName);

            case SyntacticParameter param:
                // A bare parameter in boolean context (u => u) is not valid SQL
                return null;

            case SyntacticMemberAccess memberAccess:
                return TranslateMemberAccess(memberAccess, lambdaParameterName);

            case SyntacticCapturedVariable capturedVar:
                return TranslateCapturedVariable(capturedVar);

            case SyntacticUnknown unknown:
                // Cannot translate unknown expressions
                return null;

            default:
                return null;
        }
    }

    /// <summary>
    /// Translates a property access to a quoted column name.
    /// </summary>
    private string? TranslatePropertyAccess(SyntacticPropertyAccess propAccess, bool inBooleanContext = false)
    {
        var propertyName = propAccess.PropertyName;

        // Handle Ref<T,K>.Id access - strip the .Id suffix
        if (propertyName.EndsWith(".Id"))
        {
            propertyName = propertyName.Substring(0, propertyName.Length - 3);
        }

        if (_columnLookup.TryGetValue(propertyName, out var column))
        {
            var quotedColumn = QuoteIdentifier(column.ColumnName);

            // If we're in a boolean context (e.g., Where clause) and this is a boolean column,
            // we need to add the comparison
            if (inBooleanContext && (column.ClrType == "bool" || column.ClrType == "Boolean"))
            {
                return $"{quotedColumn} = {FormatBoolean(true)}";
            }

            return quotedColumn;
        }

        // Property not found in entity - might be a navigation or error
        return null;
    }

    /// <summary>
    /// Translates a literal to SQL.
    /// </summary>
    private string TranslateLiteral(SyntacticLiteral literal)
    {
        if (literal.IsNull)
        {
            return "NULL";
        }

        switch (literal.ClrType)
        {
            case "bool":
                return FormatBoolean(literal.Value == "true");

            case "string":
                // Use parameter for string values
                return AddParameter("string", $"\"{EscapeString(literal.Value)}\"");

            case "int":
            case "long":
            case "float":
            case "double":
            case "decimal":
                return literal.Value;

            case "char":
                return AddParameter("char", $"'{literal.Value}'");

            default:
                return AddParameter(literal.ClrType, literal.Value);
        }
    }

    /// <summary>
    /// Translates a binary expression.
    /// </summary>
    private string? TranslateBinary(SyntacticBinary binary, string lambdaParameterName)
    {
        var left = TranslateExpression(binary.Left, lambdaParameterName);
        var right = TranslateExpression(binary.Right, lambdaParameterName);

        if (left == null || right == null)
            return null;

        // Handle NULL comparisons specially
        if (binary.Right is SyntacticLiteral { IsNull: true })
        {
            return binary.Operator switch
            {
                "==" => $"{left} IS NULL",
                "!=" => $"{left} IS NOT NULL",
                _ => null
            };
        }

        if (binary.Left is SyntacticLiteral { IsNull: true })
        {
            return binary.Operator switch
            {
                "==" => $"{right} IS NULL",
                "!=" => $"{right} IS NOT NULL",
                _ => null
            };
        }

        var sqlOp = TranslateBinaryOperator(binary.Operator);
        if (sqlOp == null)
            return null;

        return $"({left} {sqlOp} {right})";
    }

    /// <summary>
    /// Translates C# binary operators to SQL.
    /// </summary>
    private static string? TranslateBinaryOperator(string op)
    {
        return op switch
        {
            "==" => "=",
            "!=" => "<>",
            "<" => "<",
            "<=" => "<=",
            ">" => ">",
            ">=" => ">=",
            "&&" => "AND",
            "||" => "OR",
            "+" => "+",
            "-" => "-",
            "*" => "*",
            "/" => "/",
            "%" => "%",
            "&" => "&",
            "|" => "|",
            "^" => "^",
            _ => null
        };
    }

    /// <summary>
    /// Translates a unary expression.
    /// </summary>
    private string? TranslateUnary(SyntacticUnary unary, string lambdaParameterName)
    {
        var operand = TranslateExpression(unary.Operand, lambdaParameterName);
        if (operand == null)
            return null;

        return unary.Operator switch
        {
            "!" => $"NOT ({operand})",
            "-" => $"-{operand}",
            "+" => operand,
            "~" => $"~{operand}",
            _ => null
        };
    }

    /// <summary>
    /// Translates a method call expression.
    /// </summary>
    private string? TranslateMethodCall(SyntacticMethodCall methodCall, string lambdaParameterName)
    {
        // Translate target
        var target = methodCall.Target != null
            ? TranslateExpression(methodCall.Target, lambdaParameterName)
            : null;

        // Handle string methods
        if (methodCall.Target is SyntacticPropertyAccess && target != null)
        {
            switch (methodCall.MethodName)
            {
                case "Contains" when methodCall.Arguments.Count == 1:
                    if (methodCall.Arguments[0] is SyntacticLiteral strLiteral && strLiteral.ClrType == "string")
                    {
                        var escaped = SqlLikeHelpers.EscapeLikeMetaChars(strLiteral.Value);
                        var param = AddParameter("string", $"\"{EscapeString(escaped)}\"");
                        var sql = SqlLikeHelpers.FormatLikeWithParameter(_dialect, target, param, "%", "%");
                        if (escaped != strLiteral.Value)
                            sql += " ESCAPE '\\'";
                        return sql;
                    }
                    var containsArg = TranslateExpression(methodCall.Arguments[0], lambdaParameterName);
                    if (containsArg == null) return null;
                    return SqlLikeHelpers.FormatLikeWithParameter(_dialect, target, containsArg, "%", "%");

                case "StartsWith" when methodCall.Arguments.Count == 1:
                    if (methodCall.Arguments[0] is SyntacticLiteral startLiteral && startLiteral.ClrType == "string")
                    {
                        var escaped = SqlLikeHelpers.EscapeLikeMetaChars(startLiteral.Value);
                        var param = AddParameter("string", $"\"{EscapeString(escaped)}\"");
                        var sql = SqlLikeHelpers.FormatLikeWithParameter(_dialect, target, param, "", "%");
                        if (escaped != startLiteral.Value)
                            sql += " ESCAPE '\\'";
                        return sql;
                    }
                    var startsWithArg = TranslateExpression(methodCall.Arguments[0], lambdaParameterName);
                    if (startsWithArg == null) return null;
                    return SqlLikeHelpers.FormatLikeWithParameter(_dialect, target, startsWithArg, "", "%");

                case "EndsWith" when methodCall.Arguments.Count == 1:
                    if (methodCall.Arguments[0] is SyntacticLiteral endLiteral && endLiteral.ClrType == "string")
                    {
                        var escaped = SqlLikeHelpers.EscapeLikeMetaChars(endLiteral.Value);
                        var param = AddParameter("string", $"\"{EscapeString(escaped)}\"");
                        var sql = SqlLikeHelpers.FormatLikeWithParameter(_dialect, target, param, "%", "");
                        if (escaped != endLiteral.Value)
                            sql += " ESCAPE '\\'";
                        return sql;
                    }
                    var endsWithArg = TranslateExpression(methodCall.Arguments[0], lambdaParameterName);
                    if (endsWithArg == null) return null;
                    return SqlLikeHelpers.FormatLikeWithParameter(_dialect, target, endsWithArg, "%", "");

                case "ToLower":
                    return $"LOWER({target})";

                case "ToUpper":
                    return $"UPPER({target})";

                case "Trim":
                    return $"TRIM({target})";

                case "TrimStart":
                    return $"LTRIM({target})";

                case "TrimEnd":
                    return $"RTRIM({target})";
            }
        }

        // Handle collection Contains (IN clause)
        if (methodCall.MethodName == "Contains" && methodCall.Arguments.Count == 1)
        {
            var itemExpr = TranslateExpression(methodCall.Arguments[0], lambdaParameterName);
            if (itemExpr != null && target != null)
            {
                // target.Contains(item) -> item IN target
                return $"{itemExpr} IN {target}";
            }
        }

        // Handle Sql.* aggregate functions
        if (methodCall.Target == null || methodCall.Target is SyntacticMemberAccess { MemberName: "Sql" })
        {
            switch (methodCall.MethodName)
            {
                case "Count" when methodCall.Arguments.Count == 0:
                    return "COUNT(*)";

                case "Count" when methodCall.Arguments.Count == 1:
                    var countCol = TranslateExpression(methodCall.Arguments[0], lambdaParameterName);
                    return countCol != null ? $"COUNT({countCol})" : null;

                case "Sum" when methodCall.Arguments.Count == 1:
                    var sumCol = TranslateExpression(methodCall.Arguments[0], lambdaParameterName);
                    return sumCol != null ? $"SUM({sumCol})" : null;

                case "Avg" when methodCall.Arguments.Count == 1:
                    var avgCol = TranslateExpression(methodCall.Arguments[0], lambdaParameterName);
                    return avgCol != null ? $"AVG({avgCol})" : null;

                case "Min" when methodCall.Arguments.Count == 1:
                    var minCol = TranslateExpression(methodCall.Arguments[0], lambdaParameterName);
                    return minCol != null ? $"MIN({minCol})" : null;

                case "Max" when methodCall.Arguments.Count == 1:
                    var maxCol = TranslateExpression(methodCall.Arguments[0], lambdaParameterName);
                    return maxCol != null ? $"MAX({maxCol})" : null;
            }
        }

        // Unknown method call
        return null;
    }

    /// <summary>
    /// Translates a member access expression.
    /// </summary>
    private string? TranslateMemberAccess(SyntacticMemberAccess memberAccess, string lambdaParameterName)
    {
        // Try to resolve the full path
        if (memberAccess.Target is SyntacticPropertyAccess propAccess)
        {
            // Handle Ref<T,K>.Id access
            if (memberAccess.MemberName == "Id")
            {
                var baseProp = propAccess.PropertyName;
                if (_columnLookup.TryGetValue(baseProp, out var column))
                {
                    return QuoteIdentifier(column.ColumnName);
                }
            }
        }

        // Could not resolve
        return null;
    }

    /// <summary>
    /// Translates a captured variable to a SQL parameter placeholder.
    /// The actual value will be extracted at runtime from the expression.
    /// </summary>
    private string TranslateCapturedVariable(SyntacticCapturedVariable capturedVar)
    {
        // Create a parameter for the captured variable
        // The value expression is the syntax text that will be used to generate
        // code that extracts the value at runtime
        // The expression path enables direct path navigation for optimized extraction
        return AddCapturedParameter(capturedVar.SyntaxText, capturedVar.ExpressionPath);
    }

    /// <summary>
    /// Adds a captured parameter and returns the placeholder.
    /// </summary>
    /// <param name="valueExpression">The syntax text representing the captured variable.</param>
    /// <param name="expressionPath">The path from expression body to the captured variable, or null if not available.</param>
    private string AddCapturedParameter(string valueExpression, string? expressionPath)
    {
        var name = $"@p{_parameterIndex}";
        // For captured variables, we use "object" as CLR type since no semantic model is available
        // on the syntactic path. Enum types are normalized at runtime by QueryExecutor.NormalizeParameterValue.
        _parameters.Add(new ParameterInfo(
            _parameterIndex,
            name,
            "object",
            valueExpression,
            isCollection: false,
            isCaptured: true,
            expressionPath: expressionPath));
        _parameterIndex++;
        return name;
    }

    /// <summary>
    /// Quotes an identifier according to the dialect.
    /// </summary>
    private string QuoteIdentifier(string identifier)
    {
        return _dialect switch
        {
            SqlDialect.SQLite => $"\"{identifier}\"",
            SqlDialect.PostgreSQL => $"\"{identifier}\"",
            SqlDialect.MySQL => $"`{identifier}`",
            SqlDialect.SqlServer => $"[{identifier}]",
            _ => $"\"{identifier}\""
        };
    }

    /// <summary>
    /// Formats a boolean value according to the dialect.
    /// </summary>
    private string FormatBoolean(bool value)
    {
        return _dialect switch
        {
            SqlDialect.PostgreSQL => value ? "TRUE" : "FALSE",
            _ => value ? "1" : "0"
        };
    }

    /// <summary>
    /// Adds a parameter and returns the placeholder.
    /// </summary>
    private string AddParameter(string clrType, string valueExpression)
    {
        var name = $"@p{_parameterIndex}";
        _parameters.Add(new ParameterInfo(_parameterIndex, name, clrType, valueExpression));
        _parameterIndex++;
        return name;
    }

    /// <summary>
    /// Escapes a string for SQL.
    /// </summary>
    private static string EscapeString(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("'", "''");
    }

}
