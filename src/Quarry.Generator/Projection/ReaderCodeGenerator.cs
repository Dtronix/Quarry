using System.Text;
using Quarry.Generators.Generation;
using Quarry.Generators.Models;
using Quarry.Generators.Sql;
using Quarry.Generators.Utilities;
using Quarry;

namespace Quarry.Generators.Projection;

/// <summary>
/// Generates reader delegate code for projections.
/// Creates compile-time optimized code that reads from DbDataReader.
/// </summary>
internal static class ReaderCodeGenerator
{
    /// <summary>
    /// Generates the column list SQL for a SELECT clause.
    /// </summary>
    /// <param name="projection">The projection info.</param>
    /// <param name="dialect">The SQL dialect.</param>
    /// <returns>The comma-separated column list SQL.</returns>
    public static string GenerateColumnList(ProjectionInfo projection, SqlDialect dialect)
    {
        var sb = new StringBuilder();
        var first = true;

        foreach (var column in projection.Columns)
        {
            if (!first)
            {
                sb.Append(", ");
            }
            first = false;

            if (!string.IsNullOrEmpty(column.SqlExpression))
            {
                // Computed expression or aggregate
                var rendered = SqlFormatting.QuoteSqlExpression(column.SqlExpression, dialect);
                // SQL Server's ROW_NUMBER/RANK/DENSE_RANK/NTILE return BIGINT; SqlDataReader.GetInt32
                // does not auto-narrow. ProjectionAnalyzer flags int-typed window-function projections
                // so the rendered expression is wrapped server-side, letting GetInt32 succeed. See #274.
                if (column.RequiresSqlServerIntCast && dialect == SqlDialect.SqlServer)
                    rendered = $"CAST({rendered} AS INT)";
                sb.Append(rendered);
                if (!string.IsNullOrEmpty(column.Alias))
                {
                    sb.Append(" AS ");
                    sb.Append(QuoteIdentifier(column.Alias!, dialect));
                }
            }
            else if (!string.IsNullOrEmpty(column.ColumnName))
            {
                // Simple column reference - with table alias for joined queries
                if (!string.IsNullOrEmpty(column.TableAlias))
                {
                    sb.Append(column.TableAlias);
                    sb.Append('.');
                }
                sb.Append(QuoteIdentifier(column.ColumnName, dialect));
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Generates a typed reader delegate for the projection.
    /// </summary>
    /// <param name="projection">The projection info.</param>
    /// <param name="entityTypeName">The entity type name.</param>
    /// <param name="contextNamespace">
    /// The namespace of the consuming <see cref="QuarryContext"/>. Used to rewrite
    /// the schema-namespace [EntityReader] FQN to a per-context FQN
    /// (<c>contextNamespace + "." + readerSimpleName</c>) so the generated reference
    /// targets the per-context reader class. When null/empty, the schema-namespace
    /// FQN is used unchanged.
    /// </param>
    /// <returns>The C# code for the reader delegate.</returns>
    public static string GenerateReaderDelegate(ProjectionInfo projection, string entityTypeName, string? contextNamespace = null)
    {
        // When a custom EntityReader is active, delegate to its Read() method
        if (projection.Kind == ProjectionKind.Entity && projection.CustomEntityReaderClass != null)
        {
            var fqn = InterceptorCodeGenerator.ResolvePerContextReaderFqn(projection.CustomEntityReaderClass, contextNamespace);
            var fieldName = InterceptorCodeGenerator.GetEntityReaderFieldName(fqn);
            return $"static (DbDataReader r) => {fieldName}.Read(r)";
        }

        return projection.Kind switch
        {
            ProjectionKind.Entity => GenerateEntityReader(projection, entityTypeName),
            ProjectionKind.Anonymous => GenerateAnonymousReader(projection),
            ProjectionKind.Dto => GenerateDtoReader(projection),
            ProjectionKind.Tuple => GenerateTupleReader(projection),
            ProjectionKind.SingleColumn => GenerateSingleColumnReader(projection),
            _ => throw new InvalidOperationException($"Unsupported projection kind: {projection.Kind}")
        };
    }

    /// <summary>
    /// Generates a reader for full entity projection.
    /// </summary>
    private static string GenerateEntityReader(ProjectionInfo projection, string entityTypeName)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"static (DbDataReader r) => new {entityTypeName}");
        sb.AppendLine("            {");

        foreach (var column in projection.Columns)
        {
            var readerCall = GetReaderCall(column);
            sb.AppendLine($"                {column.PropertyName} = {readerCall},");
        }

        sb.Append("            }");
        return sb.ToString();
    }

    /// <summary>
    /// Generates a reader for anonymous type projection.
    /// </summary>
    private static string GenerateAnonymousReader(ProjectionInfo projection)
    {
        var sb = new StringBuilder();
        sb.Append("static (DbDataReader r) => new { ");

        var first = true;
        foreach (var column in projection.Columns)
        {
            if (!first) sb.Append(", ");
            first = false;

            var readerCall = GetReaderCall(column);
            sb.Append($"{column.PropertyName} = {readerCall}");
        }

        sb.Append(" }");
        return sb.ToString();
    }

    /// <summary>
    /// Generates a reader for DTO projection.
    /// </summary>
    private static string GenerateDtoReader(ProjectionInfo projection)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"static (DbDataReader r) => new {projection.ResultTypeName}");
        sb.AppendLine("            {");

        foreach (var column in projection.Columns)
        {
            var readerCall = GetReaderCall(column);
            sb.AppendLine($"                {column.PropertyName} = {readerCall},");
        }

        sb.Append("            }");
        return sb.ToString();
    }

    /// <summary>
    /// Generates a reader for tuple projection.
    /// </summary>
    private static string GenerateTupleReader(ProjectionInfo projection)
    {
        var sb = new StringBuilder();
        sb.Append("static (DbDataReader r) => (");

        var first = true;
        foreach (var column in projection.Columns)
        {
            if (!first) sb.Append(", ");
            first = false;

            var readerCall = GetReaderCall(column);

            // Include explicit element names so the generated tuple matches the
            // user's named-element syntax (e.g. (ProductName: r.GetString(0), …)).
            // Default ItemN names are omitted to avoid CS9154 warnings.
            var isDefaultName = column.PropertyName.StartsWith("Item") &&
                                int.TryParse(column.PropertyName.Substring(4), out var idx) &&
                                idx == column.Ordinal + 1;

            if (isDefaultName)
                sb.Append(readerCall);
            else
                sb.Append($"{column.PropertyName}: {readerCall}");
        }

        sb.Append(")");
        return sb.ToString();
    }

    /// <summary>
    /// Generates a reader for single column projection.
    /// </summary>
    private static string GenerateSingleColumnReader(ProjectionInfo projection)
    {
        var column = projection.Columns[0];
        var readerCall = GetReaderCall(column);
        return $"static (DbDataReader r) => {readerCall}";
    }

    /// <summary>
    /// Gets the DbDataReader method call for a column.
    /// Uses pre-computed ReaderMethodName and IsValueType from the column,
    /// which were derived from ITypeSymbol during analysis.
    /// </summary>
    private static string GetReaderCall(ProjectedColumn column)
    {
        var ordinal = column.Ordinal;
        var readerMethod = column.ReaderMethodName;
        var rawRead = $"r.{readerMethod}({ordinal})";
        // Use EffectivelyNullable to include join-forced nullability for reader null checks
        var needsNullCheck = column.EffectivelyNullable;
        // Join-only nullable: null check needed but return type must stay non-nullable
        // (the user declared a non-nullable type, but the join can produce NULL at runtime)
        var joinOnlyNullable = column.IsJoinNullable && !column.IsNullable;

        // Handle FK columns — wrap raw key value in new Ref<TEntity, TKey>(...)
        if (column.IsForeignKey && column.ForeignKeyEntityName != null)
        {
            var refType = $"EntityRef<{column.ForeignKeyEntityName}, {column.ClrType}>";

            // Handle nullable types - need null check
            if (needsNullCheck)
            {
                var defaultExpr = joinOnlyNullable ? $"default({refType})" : $"default({refType}?)";
                return $"r.IsDBNull({ordinal}) ? {defaultExpr} : new {refType}({rawRead})";
            }

            return $"new {refType}({rawRead})";
        }

        // Handle enum columns — cast integral value to enum type
        if (column.IsEnum)
        {
            if (needsNullCheck)
            {
                var defaultExpr = joinOnlyNullable ? $"default({column.FullClrType})" : $"default({column.FullClrType}?)";
                return $"r.IsDBNull({ordinal}) ? {defaultExpr} : ({column.FullClrType}){rawRead}";
            }

            return $"({column.FullClrType}){rawRead}";
        }

        // Handle mismatched-sign integer types — cast from reader method to target type
        // (e.g., (uint)r.GetInt32(0) for unsigned, (sbyte)r.GetByte(0) for signed byte)
        if (TypeClassification.NeedsSignCast(column.ClrType))
        {
            if (needsNullCheck)
            {
                var defaultExpr = joinOnlyNullable ? $"default({column.ClrType})" : $"default({column.ClrType}?)";
                return $"r.IsDBNull({ordinal}) ? {defaultExpr} : ({column.ClrType}){rawRead}";
            }

            return $"({column.ClrType}){rawRead}";
        }

        // Handle custom type mapping - wrap with FromDb()
        if (column.CustomTypeMapping != null)
        {
            var fieldName = InterceptorCodeGenerator.GetMappingFieldName(column.CustomTypeMapping);
            if (needsNullCheck)
            {
                var nullableType = column.IsValueType ? $"{column.ClrType}?" : column.ClrType;
                var defaultExpr = joinOnlyNullable
                    ? (column.IsValueType ? $"default({column.ClrType})" : "default!")
                    : $"default({nullableType})";
                return $"r.IsDBNull({ordinal}) ? {defaultExpr} : {fieldName}.FromDb(r.{readerMethod}({ordinal}))";
            }
            return $"{fieldName}.FromDb(r.{readerMethod}({ordinal}))";
        }

        // Handle types that use GetValue() fallback — need explicit cast (e.g., byte[], DateTimeOffset)
        if (readerMethod == "GetValue")
        {
            var castType = column.FullClrType ?? column.ClrType;
            if (needsNullCheck)
            {
                var nullableType = column.IsValueType ? $"{castType}?" : castType;
                var defaultExpr = joinOnlyNullable
                    ? (column.IsValueType ? $"default({castType})" : "default!")
                    : $"default({nullableType})";
                return $"r.IsDBNull({ordinal}) ? {defaultExpr} : ({castType})r.GetValue({ordinal})";
            }
            return $"({castType})r.GetValue({ordinal})";
        }

        // Handle nullable types - need null check
        if (needsNullCheck)
        {
            if (column.IsValueType)
            {
                // Join-only: return non-nullable default (e.g., 0 for int) to match declared type
                // Schema-nullable: return nullable default (e.g., default(int?) = null)
                var defaultExpr = joinOnlyNullable ? "default" : $"default({column.ClrType}?)";
                return $"r.IsDBNull({ordinal}) ? {defaultExpr} : r.{readerMethod}({ordinal})";
            }
            else
            {
                // Join-only: use default! to suppress nullability warning (type stays non-nullable)
                // Schema-nullable: use null (type is already nullable)
                var defaultExpr = joinOnlyNullable ? "default!" : "null";
                return $"r.IsDBNull({ordinal}) ? {defaultExpr} : r.{readerMethod}({ordinal})";
            }
        }

        return rawRead;
    }

    /// <summary>
    /// Quotes an identifier according to the SQL dialect.
    /// </summary>
    private static string QuoteIdentifier(string identifier, SqlDialect dialect)
    {
        return dialect switch
        {
            SqlDialect.MySQL => $"`{identifier}`",
            SqlDialect.SqlServer => $"[{identifier}]",
            _ => $"\"{identifier}\"" // SQLite, PostgreSQL
        };
    }

    /// <summary>
    /// Generates a string array literal of column names for runtime use.
    /// </summary>
    public static string GenerateColumnNamesArray(ProjectionInfo projection, SqlDialect dialect = SqlDialect.SQLite)
    {
        var sb = new StringBuilder();
        sb.Append("new[] { ");

        var first = true;
        foreach (var column in projection.Columns)
        {
            if (!first) sb.Append(", ");
            first = false;

            if (string.IsNullOrEmpty(column.ColumnName) && !string.IsNullOrEmpty(column.SqlExpression))
            {
                // Aggregate or computed expression — resolve {identifier} placeholders
                // to dialect-quoted form, then escape for C# string literal.
                var resolved = SqlFormatting.QuoteSqlExpression(column.SqlExpression!, dialect);
                // Match the wrap applied in GenerateColumnList so dynamic-SQL paths emit the
                // same shape as the static SELECT list. See #274.
                if (column.RequiresSqlServerIntCast && dialect == SqlDialect.SqlServer)
                    resolved = $"CAST({resolved} AS INT)";
                var escapedExpr = resolved!.Replace("\"", "\\\"");
                sb.Append($"\"{escapedExpr}\"");
            }
            else if (!string.IsNullOrEmpty(column.TableAlias))
            {
                // Joined query: emit dialect-quoted aliased column name
                var quoted = $"{SqlFormatting.QuoteIdentifier(dialect, column.TableAlias!)}.{SqlFormatting.QuoteIdentifier(dialect, column.ColumnName)}";
                var escaped = quoted.Replace("\"", "\\\"");
                sb.Append($"\"{escaped}\"");
            }
            else
            {
                sb.Append($"\"{column.ColumnName}\"");
            }
        }

        sb.Append(" }");
        return sb.ToString();
    }

}
