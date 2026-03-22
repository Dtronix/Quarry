using System.Text;
using Quarry.Generators.Generation;
using Quarry.Generators.Models;
using Quarry.Generators.Sql;
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
                sb.Append(column.SqlExpression);
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
    /// <returns>The C# code for the reader delegate.</returns>
    public static string GenerateReaderDelegate(ProjectionInfo projection, string entityTypeName)
    {
        // When a custom EntityReader is active, delegate to its Read() method
        if (projection.Kind == ProjectionKind.Entity && projection.CustomEntityReaderClass != null)
        {
            var fieldName = InterceptorCodeGenerator.GetEntityReaderFieldName(projection.CustomEntityReaderClass);
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
            sb.Append(readerCall);
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

        // Handle FK columns — wrap raw key value in new Ref<TEntity, TKey>(...)
        if (column.IsForeignKey && column.ForeignKeyEntityName != null)
        {
            var refType = $"EntityRef<{column.ForeignKeyEntityName}, {column.ClrType}>";

            // Handle nullable types - need null check
            if (column.IsNullable)
            {
                return $"r.IsDBNull({ordinal}) ? default({refType}?) : new {refType}({rawRead})";
            }

            return $"new {refType}({rawRead})";
        }

        // Handle enum columns — cast integral value to enum type
        if (column.IsEnum)
        {
            if (column.IsNullable)
            {
                return $"r.IsDBNull({ordinal}) ? default({column.FullClrType}?) : ({column.FullClrType}){rawRead}";
            }

            return $"({column.FullClrType}){rawRead}";
        }

        // Handle custom type mapping - wrap with FromDb()
        if (column.CustomTypeMapping != null)
        {
            var fieldName = InterceptorCodeGenerator.GetMappingFieldName(column.CustomTypeMapping);
            if (column.IsNullable)
            {
                var nullableType = column.IsValueType ? $"{column.ClrType}?" : column.ClrType;
                return $"r.IsDBNull({ordinal}) ? default({nullableType}) : {fieldName}.FromDb(r.{readerMethod}({ordinal}))";
            }
            return $"{fieldName}.FromDb(r.{readerMethod}({ordinal}))";
        }

        // Handle nullable types - need null check
        if (column.IsNullable)
        {
            // For value types: default(DateTime?) returns null, not DateTime.MinValue
            // For reference types: default(string) returns null
            var nullableType = column.IsValueType ? $"{column.ClrType}?" : column.ClrType;
            return $"r.IsDBNull({ordinal}) ? default({nullableType}) : r.{readerMethod}({ordinal})";
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
                // Aggregate or computed expression with no column name (e.g., COUNT(*), SUM("Total"))
                // Escape inner double quotes so the C# string literal is valid
                var escapedExpr = column.SqlExpression!.Replace("\"", "\\\"");
                sb.Append($"\"{escapedExpr}\"");
            }
            else if (!string.IsNullOrEmpty(column.TableAlias))
            {
                // Joined query: emit dialect-quoted aliased column name
                var quoted = $"{SqlFormatting.QuoteIdentifier(dialect, column.TableAlias)}.{SqlFormatting.QuoteIdentifier(dialect, column.ColumnName)}";
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
