using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Quarry.Shared.Sql.Parser;

namespace Quarry.Migration;

/// <summary>
/// Translates a parsed SQL AST into a Quarry chain API C# source string.
/// </summary>
internal sealed class ChainEmitter
{
    private readonly SchemaMap _schema;
    private readonly List<ConversionDiagnostic> _diagnostics = new List<ConversionDiagnostic>();

    // Table alias → (EntityMapping, lambda variable name)
    private readonly Dictionary<string, TableRef> _tables = new Dictionary<string, TableRef>(StringComparer.OrdinalIgnoreCase);

    // Ordered lambda variables for multi-table lambdas
    private readonly List<string> _lambdaVars = new List<string>();

    public ChainEmitter(SchemaMap schema)
    {
        _schema = schema;
    }

    public ConversionResult Translate(SqlParseResult parseResult, DapperCallSite callSite)
    {
        if (parseResult.Statement == null)
        {
            _diagnostics.Add(new ConversionDiagnostic(
                ConversionDiagnosticSeverity.Error, "SQL could not be parsed"));
            return new ConversionResult(callSite.Sql, null, _diagnostics);
        }

        switch (parseResult.Statement)
        {
            case SqlSelectStatement select:
                return TranslateSelect(select, callSite);
            case SqlDeleteStatement delete:
                return TranslateDelete(delete, callSite);
            case SqlUpdateStatement update:
                return TranslateUpdate(update, callSite);
            case SqlInsertStatement insert:
                return TranslateInsert(insert, callSite);
            default:
                _diagnostics.Add(new ConversionDiagnostic(
                    ConversionDiagnosticSeverity.Error, "Unsupported statement type"));
                return new ConversionResult(callSite.Sql, null, _diagnostics);
        }
    }

    // ─── SELECT translation ───────────────────────────────

    private ConversionResult TranslateSelect(SqlSelectStatement stmt, DapperCallSite callSite)
    {
        var sb = new StringBuilder();

        // FROM → db.Entity()
        if (stmt.From == null)
        {
            _diagnostics.Add(new ConversionDiagnostic(
                ConversionDiagnosticSeverity.Error, "No FROM clause found"));
            return new ConversionResult(callSite.Sql, null, _diagnostics);
        }

        if (!RegisterPrimaryTable(stmt.From))
            return new ConversionResult(callSite.Sql, null, _diagnostics);

        sb.Append($"db.{_tables.Values.First().Entity.AccessorName}()");

        // JOINs
        foreach (var join in stmt.Joins)
        {
            EmitJoin(sb, join, callSite);
        }

        // WHERE
        if (stmt.Where != null)
        {
            var lambdaParams = BuildLambdaParams();
            var body = EmitExpression(stmt.Where, callSite);
            sb.Append($"\n    .Where({lambdaParams} => {body})");
        }

        // GROUP BY
        if (stmt.GroupBy != null && stmt.GroupBy.Count > 0)
        {
            EmitGroupBy(sb, stmt.GroupBy, callSite);
        }

        // HAVING
        if (stmt.Having != null)
        {
            EmitHaving(sb, stmt.Having, callSite);
        }

        // SELECT
        EmitSelect(sb, stmt.Columns);

        // ORDER BY
        if (stmt.OrderBy != null && stmt.OrderBy.Count > 0)
        {
            EmitOrderBy(sb, stmt.OrderBy, callSite);
        }

        // LIMIT / OFFSET
        if (stmt.Limit != null)
        {
            EmitLimit(sb, stmt.Limit, callSite);
        }

        if (stmt.Offset != null)
        {
            EmitOffset(sb, stmt.Offset, callSite);
        }

        // Terminal
        sb.Append($"\n    .{MapTerminal(callSite.MethodName)}");

        return new ConversionResult(callSite.Sql, sb.ToString(), _diagnostics);
    }

    // ─── DELETE translation ───────────────────────────────

    private ConversionResult TranslateDelete(SqlDeleteStatement stmt, DapperCallSite callSite)
    {
        var sb = new StringBuilder();

        if (!RegisterPrimaryTable(stmt.Table))
            return new ConversionResult(callSite.Sql, null, _diagnostics);

        sb.Append($"db.{_tables.Values.First().Entity.AccessorName}()");
        sb.Append("\n    .Delete()");

        if (stmt.Where != null)
        {
            var lambdaParams = BuildLambdaParams();
            var body = EmitExpression(stmt.Where, callSite);
            sb.Append($"\n    .Where({lambdaParams} => {body})");
        }
        else
        {
            sb.Append("\n    .All()");
            _diagnostics.Add(new ConversionDiagnostic(
                ConversionDiagnosticSeverity.Warning,
                "DELETE without WHERE — .All() added to confirm full-table delete"));
        }

        sb.Append("\n    .ExecuteNonQueryAsync()");

        return new ConversionResult(callSite.Sql, sb.ToString(), _diagnostics);
    }

    // ─── UPDATE translation ──────────────────────────────

    private ConversionResult TranslateUpdate(SqlUpdateStatement stmt, DapperCallSite callSite)
    {
        var sb = new StringBuilder();

        if (!RegisterPrimaryTable(stmt.Table))
            return new ConversionResult(callSite.Sql, null, _diagnostics);

        var primaryVar = _lambdaVars[0];

        sb.Append($"db.{_tables.Values.First().Entity.AccessorName}()");
        sb.Append("\n    .Update()");

        // Emit .Set(u => { u.Col1 = val1; u.Col2 = val2; })
        sb.Append($"\n    .Set({primaryVar} => {{ ");
        for (var i = 0; i < stmt.Assignments.Count; i++)
        {
            var assignment = stmt.Assignments[i];
            var colAccess = ResolveColumnAccess(assignment.Column);
            var value = EmitExpression(assignment.Value, callSite);
            sb.Append($"{colAccess} = {value}; ");
        }
        sb.Append("})");

        if (stmt.Where != null)
        {
            var lambdaParams = BuildLambdaParams();
            var body = EmitExpression(stmt.Where, callSite);
            sb.Append($"\n    .Where({lambdaParams} => {body})");
        }
        else
        {
            sb.Append("\n    .All()");
            _diagnostics.Add(new ConversionDiagnostic(
                ConversionDiagnosticSeverity.Warning,
                "UPDATE without WHERE — .All() added to confirm full-table update"));
        }

        sb.Append("\n    .ExecuteNonQueryAsync()");

        return new ConversionResult(callSite.Sql, sb.ToString(), _diagnostics);
    }

    // ─── INSERT translation ──────────────────────────────

    private ConversionResult TranslateInsert(SqlInsertStatement stmt, DapperCallSite callSite)
    {
        if (!RegisterPrimaryTable(stmt.Table))
            return new ConversionResult(callSite.Sql, null, _diagnostics);

        var entity = _tables.Values.First().Entity;

        // Build a comment showing the approximate chain pattern
        var sb = new StringBuilder();
        sb.Append($"// TODO: Construct {entity.ClassName} entity and use:\n");
        sb.Append($"// db.{entity.AccessorName}().Insert(entity).ExecuteNonQueryAsync()");

        if (stmt.Columns != null && stmt.Columns.Count > 0)
        {
            sb.Append("\n// Columns: ");
            for (var i = 0; i < stmt.Columns.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                if (entity.TryGetProperty(stmt.Columns[i].ColumnName, out var propName))
                    sb.Append(propName);
                else
                    sb.Append(ToPascalCase(stmt.Columns[i].ColumnName));
            }
        }

        _diagnostics.Add(new ConversionDiagnostic(
            ConversionDiagnosticSeverity.Warning,
            "INSERT requires entity construction — emitted as comment"));

        return new ConversionResult(callSite.Sql, sb.ToString(), _diagnostics);
    }

    // ─── Common table registration ────────────────────────

    private bool RegisterPrimaryTable(SqlTableSource tableSource)
    {
        if (!_schema.TryGetEntity(tableSource.TableName, out var primaryEntity))
        {
            _diagnostics.Add(new ConversionDiagnostic(
                ConversionDiagnosticSeverity.Warning,
                $"Table '{tableSource.TableName}' not found in schema — cannot convert"));
            return false;
        }

        var primaryVar = DeriveVariable(primaryEntity.AccessorName);
        var alias = tableSource.Alias ?? tableSource.TableName;
        _tables[alias] = new TableRef(primaryEntity, primaryVar);
        _lambdaVars.Add(primaryVar);
        return true;
    }

    // ─── SELECT ────────────────────────────────────────────

    private void EmitSelect(StringBuilder sb, IReadOnlyList<SqlNode> columns)
    {
        var lambdaParams = BuildLambdaParams();

        // SELECT with qualified star: u.* (must check before unqualified star)
        if (columns.Count == 1 && columns[0] is SqlStarColumn star && star.TableAlias != null)
        {
            var v = ResolveTableVariable(star.TableAlias);
            sb.Append($"\n    .Select({lambdaParams} => {v})");
            return;
        }

        // SELECT * (unqualified)
        if (columns.Count == 1 && columns[0] is SqlStarColumn)
        {
            sb.Append($"\n    .Select({lambdaParams} => {_lambdaVars[0]})");
            return;
        }

        // SELECT column list
        var parts = new List<string>();
        foreach (var col in columns)
        {
            if (col is SqlSelectColumn selectCol)
            {
                parts.Add(EmitSelectExpression(selectCol.Expression));
            }
            else if (col is SqlStarColumn starCol)
            {
                var v = starCol.TableAlias != null ? ResolveTableVariable(starCol.TableAlias) : _lambdaVars[0];
                parts.Add(v);
            }
        }

        if (parts.Count == 1)
        {
            sb.Append($"\n    .Select({lambdaParams} => {parts[0]})");
        }
        else
        {
            sb.Append($"\n    .Select({lambdaParams} => ({string.Join(", ", parts)}))");
        }
    }

    private string EmitSelectExpression(SqlExpr expr)
    {
        // Aggregate functions handled here
        if (expr is SqlFunctionCall func)
        {
            var funcNameUpper = func.FunctionName.ToUpperInvariant();
            switch (funcNameUpper)
            {
                case "COUNT" when func.Arguments.Count == 0 || IsStarArgument(func.Arguments):
                    return "Sql.Count()";
                case "COUNT":
                    var countArg = EmitColumnAccess(func.Arguments[0]);
                    return $"Sql.Count({countArg})";
                case "SUM":
                    return $"Sql.Sum({EmitColumnAccess(func.Arguments[0])})";
                case "AVG":
                    return $"Sql.Avg({EmitColumnAccess(func.Arguments[0])})";
                case "MIN":
                    return $"Sql.Min({EmitColumnAccess(func.Arguments[0])})";
                case "MAX":
                    return $"Sql.Max({EmitColumnAccess(func.Arguments[0])})";
            }
        }

        return EmitColumnAccess(expr);
    }

    private static bool IsStarArgument(IReadOnlyList<SqlExpr> args)
    {
        return args.Count == 1 && args[0] is SqlColumnRef colRef && colRef.ColumnName == "*";
    }

    private string EmitColumnAccess(SqlExpr expr)
    {
        if (expr is SqlColumnRef colRef)
            return ResolveColumnAccess(colRef);

        // Fallback for complex expressions in SELECT
        return EmitExpression(expr, null);
    }

    // ─── Expression emission ───────────────────────────────

    internal string EmitExpression(SqlExpr expr, DapperCallSite? callSite)
    {
        switch (expr)
        {
            case SqlBinaryExpr binary:
                return EmitBinary(binary, callSite);

            case SqlUnaryExpr unary:
                return EmitUnary(unary, callSite);

            case SqlColumnRef colRef:
                return ResolveColumnAccess(colRef);

            case SqlParameter param:
                return ResolveParameter(param, callSite);

            case SqlLiteral literal:
                return EmitLiteral(literal);

            case SqlParenExpr paren:
                return $"({EmitExpression(paren.Inner, callSite)})";

            case SqlIsNullExpr isNull:
                var nullExpr = EmitExpression(isNull.Expression, callSite);
                return isNull.IsNegated ? $"{nullExpr} != null" : $"{nullExpr} == null";

            case SqlInExpr inExpr:
                return EmitInExpression(inExpr, callSite);

            case SqlBetweenExpr between:
                return EmitBetweenExpression(between, callSite);

            case SqlFunctionCall func:
                return EmitFunctionCall(func, callSite);

            case SqlCaseExpr _:
            case SqlCastExpr _:
            case SqlExistsExpr _:
                // Sql.Raw fallback for unsupported expressions
                return EmitRawFallback(expr);

            default:
                return EmitRawFallback(expr);
        }
    }

    private string EmitBinary(SqlBinaryExpr binary, DapperCallSite? callSite)
    {
        // Special case: LIKE
        if (binary.Operator == SqlBinaryOp.Like)
        {
            var left = EmitExpression(binary.Left, callSite);
            var right = EmitExpression(binary.Right, callSite);
            return $"Sql.Like({left}, {right})";
        }

        var l = EmitExpression(binary.Left, callSite);
        var r = EmitExpression(binary.Right, callSite);
        var op = MapBinaryOp(binary.Operator);
        return $"{l} {op} {r}";
    }

    private string EmitUnary(SqlUnaryExpr unary, DapperCallSite? callSite)
    {
        var operand = EmitExpression(unary.Operand, callSite);
        return unary.Operator switch
        {
            SqlUnaryOp.Not => $"!{operand}",
            SqlUnaryOp.Negate => $"-{operand}",
            _ => operand,
        };
    }

    private string EmitInExpression(SqlInExpr inExpr, DapperCallSite? callSite)
    {
        var target = EmitExpression(inExpr.Expression, callSite);
        var values = inExpr.Values.Select(v => EmitExpression(v, callSite));
        var arrayExpr = $"new[] {{ {string.Join(", ", values)} }}";
        var contains = $"{arrayExpr}.Contains({target})";
        return inExpr.IsNegated ? $"!{contains}" : contains;
    }

    private string EmitBetweenExpression(SqlBetweenExpr between, DapperCallSite? callSite)
    {
        var target = EmitExpression(between.Expression, callSite);
        var low = EmitExpression(between.Low, callSite);
        var high = EmitExpression(between.High, callSite);
        if (between.IsNegated)
            return $"({target} < {low} || {target} > {high})";
        return $"({target} >= {low} && {target} <= {high})";
    }

    private string EmitFunctionCall(SqlFunctionCall func, DapperCallSite? callSite)
    {
        var funcNameUpper = func.FunctionName.ToUpperInvariant();
        switch (funcNameUpper)
        {
            case "COUNT" when func.Arguments.Count == 0 || IsStarArgument(func.Arguments):
                return "Sql.Count()";
            case "COUNT":
                return $"Sql.Count({EmitExpression(func.Arguments[0], callSite)})";
            case "SUM":
                return $"Sql.Sum({EmitExpression(func.Arguments[0], callSite)})";
            case "AVG":
                return $"Sql.Avg({EmitExpression(func.Arguments[0], callSite)})";
            case "MIN":
                return $"Sql.Min({EmitExpression(func.Arguments[0], callSite)})";
            case "MAX":
                return $"Sql.Max({EmitExpression(func.Arguments[0], callSite)})";
            default:
                return EmitRawFallback(func);
        }
    }

    private string EmitRawFallback(SqlExpr expr)
    {
        var rawText = ExtractSourceText(expr);
        _diagnostics.Add(new ConversionDiagnostic(
            ConversionDiagnosticSeverity.Warning,
            $"Expression could not be fully translated, using Sql.Raw: {rawText}"));
        return $"Sql.Raw<bool>(\"{EscapeString(rawText)}\")";
    }

    private static string ExtractSourceText(SqlNode node)
    {
        // Fallback: reconstruct from node type
        return node switch
        {
            SqlCaseExpr _ => "CASE ... END",
            SqlCastExpr cast => $"CAST(... AS {cast.TypeName})",
            SqlExistsExpr _ => "EXISTS (...)",
            SqlFunctionCall func => $"{func.FunctionName}(...)",
            _ => "/* unknown expression */",
        };
    }

    // ─── JOIN (Phase 5 implementation) ─────────────────────

    private void EmitJoin(StringBuilder sb, SqlJoin join, DapperCallSite callSite)
    {
        if (!_schema.TryGetEntity(join.Table.TableName, out var joinEntity))
        {
            _diagnostics.Add(new ConversionDiagnostic(
                ConversionDiagnosticSeverity.Warning,
                $"Joined table '{join.Table.TableName}' not found in schema"));
            return;
        }

        var joinVar = DeriveVariable(joinEntity.AccessorName);
        var joinAlias = join.Table.Alias ?? join.Table.TableName;
        _tables[joinAlias] = new TableRef(joinEntity, joinVar);
        _lambdaVars.Add(joinVar);

        var joinMethod = join.JoinKind switch
        {
            SqlJoinKind.Inner => "Join",
            SqlJoinKind.Left => "LeftJoin",
            SqlJoinKind.Right => "RightJoin",
            SqlJoinKind.Cross => "CrossJoin",
            SqlJoinKind.FullOuter => "FullOuterJoin",
            _ => "Join",
        };

        var lambdaParams = BuildLambdaParams();

        if (join.JoinKind == SqlJoinKind.Cross)
        {
            sb.Append($"\n    .{joinMethod}<{joinEntity.ClassName}>()");
        }
        else if (join.Condition != null)
        {
            var condition = EmitExpression(join.Condition, callSite);
            sb.Append($"\n    .{joinMethod}<{joinEntity.ClassName}>({lambdaParams} => {condition})");
        }
        else
        {
            sb.Append($"\n    .{joinMethod}<{joinEntity.ClassName}>()");
        }
    }

    // ─── GROUP BY / HAVING / ORDER BY / LIMIT (Phase 5) ───

    private void EmitGroupBy(StringBuilder sb, IReadOnlyList<SqlExpr> groupBy, DapperCallSite callSite)
    {
        var lambdaParams = BuildLambdaParams();

        if (groupBy.Count == 1)
        {
            var expr = EmitExpression(groupBy[0], callSite);
            sb.Append($"\n    .GroupBy({lambdaParams} => {expr})");
        }
        else
        {
            var parts = groupBy.Select(g => EmitExpression(g, callSite));
            sb.Append($"\n    .GroupBy({lambdaParams} => ({string.Join(", ", parts)}))");
        }
    }

    private void EmitHaving(StringBuilder sb, SqlExpr having, DapperCallSite callSite)
    {
        var lambdaParams = BuildLambdaParams();
        var body = EmitExpression(having, callSite);
        sb.Append($"\n    .Having({lambdaParams} => {body})");
    }

    private void EmitOrderBy(StringBuilder sb, IReadOnlyList<SqlOrderTerm> orderBy, DapperCallSite callSite)
    {
        var lambdaParams = BuildLambdaParams();

        for (var i = 0; i < orderBy.Count; i++)
        {
            var term = orderBy[i];
            var expr = EmitExpression(term.Expression, callSite);
            var method = i == 0 ? "OrderBy" : "ThenBy";

            if (term.IsDescending)
                sb.Append($"\n    .{method}({lambdaParams} => {expr}, Direction.Descending)");
            else
                sb.Append($"\n    .{method}({lambdaParams} => {expr})");
        }
    }

    private void EmitLimit(StringBuilder sb, SqlExpr limit, DapperCallSite callSite)
    {
        var value = EmitExpression(limit, callSite);
        sb.Append($"\n    .Limit({value})");
    }

    private void EmitOffset(StringBuilder sb, SqlExpr offset, DapperCallSite callSite)
    {
        var value = EmitExpression(offset, callSite);
        sb.Append($"\n    .Offset({value})");
    }

    // ─── Helpers ───────────────────────────────────────────

    private string ResolveColumnAccess(SqlColumnRef colRef)
    {
        // If table alias is specified, look up that table
        if (colRef.TableAlias != null && _tables.TryGetValue(colRef.TableAlias, out var tableRef))
        {
            if (tableRef.Entity.TryGetProperty(colRef.ColumnName, out var propName))
                return $"{tableRef.Variable}.{propName}";

            // Column not found — use raw column name with PascalCase guess
            return $"{tableRef.Variable}.{ToPascalCase(colRef.ColumnName)}";
        }

        // No alias — search all tables
        foreach (var kvp in _tables)
        {
            if (kvp.Value.Entity.TryGetProperty(colRef.ColumnName, out var propName))
                return $"{kvp.Value.Variable}.{propName}";
        }

        // Fallback: use first table variable with PascalCase column name
        var defaultVar = _lambdaVars.Count > 0 ? _lambdaVars[0] : "x";
        return $"{defaultVar}.{ToPascalCase(colRef.ColumnName)}";
    }

    private string ResolveParameter(SqlParameter param, DapperCallSite? callSite)
    {
        // Strip @, $, : prefix
        var name = param.RawText.TrimStart('@', '$', ':');

        if (callSite != null)
        {
            // Try to find a matching parameter name (case-insensitive)
            foreach (var p in callSite.ParameterNames)
            {
                if (string.Equals(p, name, StringComparison.OrdinalIgnoreCase))
                    return p;
            }
        }

        // Use the stripped name as-is (camelCase)
        return ToCamelCase(name);
    }

    private string ResolveTableVariable(string tableAlias)
    {
        return _tables.TryGetValue(tableAlias, out var tableRef) ? tableRef.Variable : _lambdaVars[0];
    }

    private string BuildLambdaParams()
    {
        if (_lambdaVars.Count == 1)
            return _lambdaVars[0];
        return $"({string.Join(", ", _lambdaVars)})";
    }

    private string DeriveVariable(string accessorName)
    {
        // First letter lowercase: Users → u, Orders → o
        var candidate = accessorName.Length > 0
            ? char.ToLowerInvariant(accessorName[0]).ToString()
            : "x";

        // Avoid collisions
        if (_lambdaVars.Contains(candidate))
        {
            // Try two letters: Users → us, Orders → or
            if (accessorName.Length > 1)
                candidate = accessorName.Substring(0, 2).ToLowerInvariant();

            // Append digits if still colliding
            var i = 2;
            while (_lambdaVars.Contains(candidate))
                candidate = char.ToLowerInvariant(accessorName[0]) + (i++).ToString();
        }

        return candidate;
    }

    internal static string MapTerminal(string dapperMethodName)
    {
        // Strip "Async" suffix for mapping, then add it back
        var baseName = dapperMethodName.Replace("Async", "");

        return baseName switch
        {
            "Query" => "ExecuteFetchAllAsync()",
            "QueryFirst" => "ExecuteFetchFirstAsync()",
            "QueryFirstOrDefault" => "ExecuteFetchFirstOrDefaultAsync()",
            "QuerySingle" => "ExecuteFetchSingleAsync()",
            "QuerySingleOrDefault" => "ExecuteFetchSingleOrDefaultAsync()",
            "Execute" => "ExecuteNonQueryAsync()",
            "ExecuteScalar" => "ExecuteScalarAsync()",
            _ => $"ExecuteFetchAllAsync() /* unmapped: {dapperMethodName} */",
        };
    }

    private static string MapBinaryOp(SqlBinaryOp op)
    {
        return op switch
        {
            SqlBinaryOp.Equal => "==",
            SqlBinaryOp.NotEqual => "!=",
            SqlBinaryOp.LessThan => "<",
            SqlBinaryOp.GreaterThan => ">",
            SqlBinaryOp.LessThanOrEqual => "<=",
            SqlBinaryOp.GreaterThanOrEqual => ">=",
            SqlBinaryOp.And => "&&",
            SqlBinaryOp.Or => "||",
            SqlBinaryOp.Add => "+",
            SqlBinaryOp.Subtract => "-",
            SqlBinaryOp.Multiply => "*",
            SqlBinaryOp.Divide => "/",
            SqlBinaryOp.Modulo => "%",
            SqlBinaryOp.Like => "/* LIKE */",
            _ => "==",
        };
    }

    private static string EmitLiteral(SqlLiteral literal)
    {
        return literal.LiteralKind switch
        {
            SqlLiteralKind.String => $"\"{EscapeString(StripQuotes(literal.Value))}\"",
            SqlLiteralKind.Number => literal.Value,
            SqlLiteralKind.Boolean => literal.Value.Equals("true", StringComparison.OrdinalIgnoreCase) ? "true" : "false",
            SqlLiteralKind.Null => "null",
            _ => literal.Value,
        };
    }

    private static string StripQuotes(string value)
    {
        if (value.Length >= 2 &&
            ((value[0] == '\'' && value[value.Length - 1] == '\'') ||
             (value[0] == '"' && value[value.Length - 1] == '"')))
            return value.Substring(1, value.Length - 2);
        return value;
    }

    private static string EscapeString(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    private static string ToPascalCase(string columnName)
    {
        // snake_case → PascalCase: user_id → UserId
        if (!columnName.Contains("_"))
        {
            if (columnName.Length == 0) return columnName;
            return char.ToUpperInvariant(columnName[0]) + columnName.Substring(1);
        }

        var parts = columnName.Split('_');
        var sb = new StringBuilder();
        foreach (var part in parts)
        {
            if (part.Length > 0)
            {
                sb.Append(char.ToUpperInvariant(part[0]));
                if (part.Length > 1)
                    sb.Append(part.Substring(1).ToLowerInvariant());
            }
        }

        return sb.ToString();
    }

    private static string ToCamelCase(string name)
    {
        if (name.Length == 0) return name;
        return char.ToLowerInvariant(name[0]) + name.Substring(1);
    }

    private sealed class TableRef
    {
        public EntityMapping Entity { get; }
        public string Variable { get; }

        public TableRef(EntityMapping entity, string variable)
        {
            Entity = entity;
            Variable = variable;
        }
    }
}
