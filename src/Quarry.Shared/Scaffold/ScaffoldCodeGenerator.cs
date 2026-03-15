using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Quarry.Shared.Migration;

namespace Quarry.Shared.Scaffold;

internal static class ScaffoldCodeGenerator
{
    public sealed class ScaffoldedTable
    {
        public string TableName { get; }
        public string? Schema { get; }
        public string ClassName { get; }
        public List<ColumnMetadata> Columns { get; }
        public PrimaryKeyMetadata? PrimaryKey { get; }
        public List<ForeignKeyMetadata> ForeignKeys { get; }
        public List<ForeignKeyMetadata> ImplicitForeignKeys { get; }
        public List<IndexMetadata> Indexes { get; }
        public List<ReverseTypeResult> TypeResults { get; }
        public JunctionTableDetector.JunctionTableResult? JunctionInfo { get; }
        public List<IncomingRelationship> IncomingRelationships { get; }

        public ScaffoldedTable(
            string tableName, string? schema, string className,
            List<ColumnMetadata> columns, PrimaryKeyMetadata? primaryKey,
            List<ForeignKeyMetadata> foreignKeys, List<ForeignKeyMetadata> implicitForeignKeys,
            List<IndexMetadata> indexes, List<ReverseTypeResult> typeResults,
            JunctionTableDetector.JunctionTableResult? junctionInfo,
            List<IncomingRelationship> incomingRelationships)
        {
            TableName = tableName;
            Schema = schema;
            ClassName = className;
            Columns = columns;
            PrimaryKey = primaryKey;
            ForeignKeys = foreignKeys;
            ImplicitForeignKeys = implicitForeignKeys;
            Indexes = indexes;
            TypeResults = typeResults;
            JunctionInfo = junctionInfo;
            IncomingRelationships = incomingRelationships;
        }
    }

    public sealed class IncomingRelationship
    {
        public string FromTable { get; }
        public string FromClassName { get; }
        public string FromColumn { get; }
        public bool IsOneToOne { get; }

        public IncomingRelationship(string fromTable, string fromClassName, string fromColumn, bool isOneToOne)
        {
            FromTable = fromTable;
            FromClassName = fromClassName;
            FromColumn = fromColumn;
            IsOneToOne = isOneToOne;
        }
    }

    public static string GenerateSchemaFile(
        ScaffoldedTable table,
        string? namespaceName,
        NamingStyleKind namingStyle,
        bool noSingularize,
        bool noNavigations,
        string databaseName,
        Dictionary<string, string> tableClassMap)
    {
        var sb = new StringBuilder();
        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd");

        sb.AppendLine($"// Auto-scaffolded by quarry from database '{CodeGenHelpers.EscapeCSharpString(databaseName)}' on {timestamp}");
        sb.AppendLine("// Review and adjust before using with quarry migrate");
        sb.AppendLine();

        if (namespaceName != null)
        {
            sb.AppendLine("using Quarry;");
            sb.AppendLine("using Index = Quarry.Index;");
            sb.AppendLine();
            sb.Append("namespace ").Append(namespaceName).AppendLine(";");
            sb.AppendLine();
        }

        // Junction table comment
        if (table.JunctionInfo != null)
        {
            var leftEntity = ResolveEntityName(table.JunctionInfo.LeftFk.ReferencedTable, tableClassMap);
            var rightEntity = ResolveEntityName(table.JunctionInfo.RightFk.ReferencedTable, tableClassMap);
            sb.AppendLine($"// Junction table: Many-to-many between {leftEntity} and {rightEntity}");
        }

        sb.Append("public class ").Append(table.ClassName).AppendLine(" : Schema");
        sb.AppendLine("{");

        // Table property
        sb.Append("    public static string Table => \"").Append(CodeGenHelpers.EscapeCSharpString(table.TableName)).AppendLine("\";");

        // Schema property (if not default)
        if (!string.IsNullOrEmpty(table.Schema) && table.Schema != "public" && table.Schema != "dbo")
        {
            sb.Append("    public static string SchemaName => \"").Append(CodeGenHelpers.EscapeCSharpString(table.Schema)).AppendLine("\";");
        }

        // NamingStyle property
        if (namingStyle != NamingStyleKind.Exact)
        {
            sb.Append("    protected override NamingStyle NamingStyle => NamingStyle.").Append(namingStyle).AppendLine(";");
        }

        sb.AppendLine();

        // Columns
        var allFks = table.ForeignKeys.Concat(table.ImplicitForeignKeys).ToList();
        var fkColumnMap = new Dictionary<string, ForeignKeyMetadata>(StringComparer.OrdinalIgnoreCase);
        foreach (var fk in allFks)
            fkColumnMap.TryAdd(fk.ColumnName, fk);
        var isCompositePk = table.PrimaryKey != null && table.PrimaryKey.Columns.Count > 1;

        for (var i = 0; i < table.Columns.Count; i++)
        {
            var col = table.Columns[i];
            var typeResult = table.TypeResults[i];

            // Emit type warning as comment
            if (typeResult.Warning != null)
            {
                sb.Append("    // WARNING: Column '").Append(col.Name).Append("' has type '").Append(col.DataType).AppendLine("'");
                sb.Append("    // ").AppendLine(typeResult.Warning);
            }

            var propName = ToPascalCase(col.Name, namingStyle);
            var isPk = table.PrimaryKey?.Columns.Any(c => c.Equals(col.Name, StringComparison.OrdinalIgnoreCase)) == true;
            var isFk = fkColumnMap.TryGetValue(col.Name, out var fkMeta);

            if (isPk && !isCompositePk && !isFk)
            {
                // Single-column PK: emit as Key<T>
                var clrType = typeResult.ClrType;
                sb.Append("    public Key<").Append(clrType).Append("> ").Append(propName);

                if (col.IsIdentity)
                    sb.AppendLine(" => Identity();");
                else if (clrType == "Guid")
                    sb.AppendLine(" => ClientGenerated();");
                else
                    sb.AppendLine(" { get; }");
            }
            else if (isFk)
            {
                // FK column: emit as Ref<TSchema, TKey> using schema class name (with Schema suffix)
                var refClassName = ResolveClassName(fkMeta!.ReferencedTable, tableClassMap);
                var clrType = typeResult.ClrType;
                sb.Append("    public Ref<").Append(refClassName).Append(", ").Append(clrType).Append("> ").Append(propName);
                sb.Append(" => ForeignKey<").Append(refClassName).Append(", ").Append(clrType).Append(">();");

                // FK actions as comments (migration system handles these from the DB constraint)
                var hasActions = false;
                if (fkMeta.OnDelete != "NO ACTION" && fkMeta.OnDelete != "RESTRICT")
                    hasActions = true;
                if (fkMeta.OnUpdate != "NO ACTION" && fkMeta.OnUpdate != "RESTRICT")
                    hasActions = true;

                if (hasActions)
                {
                    sb.Append(" // ");
                    if (fkMeta.OnDelete != "NO ACTION" && fkMeta.OnDelete != "RESTRICT")
                        sb.Append("ON DELETE ").Append(fkMeta.OnDelete);
                    if (fkMeta.OnUpdate != "NO ACTION" && fkMeta.OnUpdate != "RESTRICT")
                    {
                        if (fkMeta.OnDelete != "NO ACTION" && fkMeta.OnDelete != "RESTRICT")
                            sb.Append(", ");
                        sb.Append("ON UPDATE ").Append(fkMeta.OnUpdate);
                    }
                }

                sb.AppendLine();

                // Check if referenced table wasn't scaffolded
                if (!tableClassMap.ContainsKey(fkMeta.ReferencedTable))
                {
                    sb.Append("    // WARNING: Referenced table '").Append(fkMeta.ReferencedTable).AppendLine("' was not scaffolded");
                }
            }
            else
            {
                // Regular column or composite PK column
                var clrType = FormatClrType(typeResult);
                sb.Append("    public Col<").Append(clrType).Append("> ").Append(propName);

                var modifiers = BuildColumnModifiers(col, typeResult);
                if (modifiers != null)
                    sb.Append(" => ").Append(modifiers).AppendLine(";");
                else
                    sb.AppendLine(" { get; }");
            }
        }

        // Composite PK
        if (isCompositePk)
        {
            sb.AppendLine();
            sb.Append("    public CompositeKey PK => PrimaryKey(");
            sb.Append(string.Join(", ", table.PrimaryKey!.Columns.Select(c => ToPascalCase(c, namingStyle))));
            sb.AppendLine(");");
        }

        // Navigations (Many<T>)
        if (!noNavigations && table.IncomingRelationships.Count > 0)
        {
            var manyRels = table.IncomingRelationships.Where(r => !r.IsOneToOne).ToList();
            if (manyRels.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("    // Navigations");

                // Detect duplicate nav property names to disambiguate
                var navNames = manyRels.Select(r => PluralizeForNavigation(r.FromClassName)).ToList();
                var duplicateNames = new HashSet<string>(
                    navNames.GroupBy(n => n).Where(g => g.Count() > 1).Select(g => g.Key));

                for (var i = 0; i < manyRels.Count; i++)
                {
                    var rel = manyRels[i];
                    var fromClassName = rel.FromClassName;
                    var navPropName = PluralizeForNavigation(rel.FromClassName);

                    // Disambiguate when multiple FKs from the same entity
                    if (duplicateNames.Contains(navPropName))
                        navPropName = navPropName + "By" + ToPascalCase(rel.FromColumn, namingStyle);

                    sb.Append("    public Many<").Append(fromClassName).Append("> ").Append(navPropName);
                    sb.Append(" => HasMany<").Append(fromClassName).Append(">(x => x.");
                    sb.Append(ToPascalCase(rel.FromColumn, namingStyle));
                    sb.AppendLine(");");
                }
            }
        }

        // Indexes
        var nonPkIndexes = table.Indexes.Where(idx => !idx.IsPrimaryKey).ToList();
        if (nonPkIndexes.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("    // Indexes");
            foreach (var idx in nonPkIndexes)
            {
                var idxPropName = SanitizeIndexName(idx.Name);
                var colParams = string.Join(", ", idx.Columns.Select(c => ToPascalCase(c, namingStyle)));
                sb.Append("    public Index ").Append(idxPropName).Append(" => Index(").Append(colParams).Append(")");
                if (idx.IsUnique)
                    sb.Append(".Unique()");
                sb.AppendLine(";");
            }
        }

        sb.AppendLine("}");

        return sb.ToString();
    }

    public static string GenerateContextFile(
        string contextClassName,
        string dialect,
        string? namespaceName,
        string databaseName,
        Dictionary<string, string> tableClassMap)
    {
        var sb = new StringBuilder();
        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd");

        sb.AppendLine($"// Auto-scaffolded by quarry from database '{CodeGenHelpers.EscapeCSharpString(databaseName)}' on {timestamp}");
        sb.AppendLine("// Review and adjust before using with quarry migrate");
        sb.AppendLine();

        sb.AppendLine("using Quarry;");
        sb.AppendLine();

        if (namespaceName != null)
        {
            sb.Append("namespace ").Append(namespaceName).AppendLine(";");
            sb.AppendLine();
        }

        var sqlDialectName = MapDialectToEnum(dialect);
        sb.Append("[QuarryContext(Dialect = SqlDialect.").Append(sqlDialectName).AppendLine(")]");
        sb.Append("public partial class ").Append(contextClassName).AppendLine(" : QuarryContext");
        sb.AppendLine("{");

        foreach (var (_, className) in tableClassMap.OrderBy(kvp => kvp.Value))
        {
            var entityName = ToEntityName(className);
            var propName = PluralizeForNavigation(className);
            sb.Append("    public partial IQueryBuilder<").Append(entityName).Append("> ").Append(propName).AppendLine("();");
        }

        sb.AppendLine("}");

        return sb.ToString();
    }

    internal static string MapDialectToEnum(string dialect)
    {
        return dialect.ToLowerInvariant() switch
        {
            "sqlite" => "SQLite",
            "postgresql" or "postgres" or "pg" => "PostgreSQL",
            "sqlserver" or "mssql" => "SqlServer",
            "mysql" => "MySQL",
            _ => "SQLite"
        };
    }

    internal static string ToContextClassName(string databaseName)
    {
        var name = ToPascalCase(databaseName, NamingStyleKind.SnakeCase);
        if (!name.EndsWith("DbContext", StringComparison.Ordinal))
            name += "DbContext";
        return name;
    }

    private static string FormatClrType(ReverseTypeResult typeResult)
    {
        if (typeResult.IsNullable)
            return typeResult.ClrType + "?";
        return typeResult.ClrType;
    }

    private static string? BuildColumnModifiers(ColumnMetadata col, ReverseTypeResult typeResult)
    {
        if (typeResult.MaxLength.HasValue)
            return $"Length({typeResult.MaxLength.Value})";

        if (typeResult.Precision.HasValue && typeResult.Scale.HasValue)
            return $"Precision({typeResult.Precision.Value}, {typeResult.Scale.Value})";

        if (col.IsIdentity)
            return "Identity()";

        if (col.IsComputed())
            return $"Computed<{typeResult.ClrType}>()";

        if (col.DefaultExpression != null)
        {
            var defaultExpr = TryParseSimpleDefault(col.DefaultExpression, typeResult.ClrType);
            if (defaultExpr != null)
                return defaultExpr;

            // Complex default: skip with comment (handled in caller via warning)
            return null;
        }

        return null;
    }

    private static string? TryParseSimpleDefault(string expression, string clrType)
    {
        // Strip parens from expressions like ((0)), ('value')
        var expr = expression.Trim();
        while (expr.StartsWith("(") && expr.EndsWith(")") && expr.Length > 2)
            expr = expr.Substring(1, expr.Length - 2);

        // Boolean defaults
        if (clrType == "bool")
        {
            if (expr == "1" || expr.Equals("true", StringComparison.OrdinalIgnoreCase) || expr.Equals("'1'", StringComparison.OrdinalIgnoreCase))
                return "Default(true)";
            if (expr == "0" || expr.Equals("false", StringComparison.OrdinalIgnoreCase) || expr.Equals("'0'", StringComparison.OrdinalIgnoreCase))
                return "Default(false)";
        }

        // Integer defaults
        if ((clrType == "int" || clrType == "long" || clrType == "short" || clrType == "byte") && int.TryParse(expr, out var intVal))
            return $"Default({intVal})";

        // String defaults
        if (clrType == "string" && expr.StartsWith("'") && expr.EndsWith("'"))
        {
            var strVal = expr.Substring(1, expr.Length - 2).Replace("''", "'");
            return $"Default(\"{CodeGenHelpers.EscapeCSharpString(strVal)}\")";
        }

        // Skip complex expressions (functions, GETDATE(), etc.)
        return null;
    }

    internal static string ToPascalCase(string name, NamingStyleKind namingStyle)
    {
        if (namingStyle == NamingStyleKind.Exact)
            return CodeGenHelpers.SanitizeCSharpName(name);

        // Convert snake_case or camelCase to PascalCase
        var sb = new StringBuilder();
        var capitalizeNext = true;

        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (c == '_' || c == '-' || c == ' ')
            {
                capitalizeNext = true;
                continue;
            }

            if (capitalizeNext)
            {
                sb.Append(char.ToUpperInvariant(c));
                capitalizeNext = false;
            }
            else
            {
                sb.Append(c);
            }
        }

        var result = sb.ToString();
        return CodeGenHelpers.SanitizeCSharpName(result);
    }

    internal static string ToClassName(string tableName, bool noSingularize)
    {
        var name = ToPascalCase(tableName, NamingStyleKind.SnakeCase);

        if (!noSingularize)
            name = Singularizer.Singularize(name);

        // Ensure first char is uppercase
        if (name.Length > 0 && !char.IsUpper(name[0]))
            name = char.ToUpperInvariant(name[0]) + name.Substring(1);

        return name + "Schema";
    }

    private static string ResolveClassName(string tableName, Dictionary<string, string> tableClassMap)
    {
        if (tableClassMap.TryGetValue(tableName, out var className))
            return className;
        // Fallback: generate a class name even for non-scaffolded tables
        return ToClassName(tableName, false);
    }

    internal static string ToEntityName(string className)
    {
        if (className.EndsWith("Schema"))
            return className.Substring(0, className.Length - 6);
        return className;
    }

    private static string ResolveEntityName(string tableName, Dictionary<string, string> tableClassMap)
    {
        return ToEntityName(ResolveClassName(tableName, tableClassMap));
    }

    private static string PluralizeForNavigation(string className)
    {
        // Remove "Schema" suffix for the navigation property name
        var name = className;
        if (name.EndsWith("Schema"))
            name = name.Substring(0, name.Length - 6);

        // Simple pluralization
        if (name.EndsWith("s") || name.EndsWith("x") || name.EndsWith("ch") || name.EndsWith("sh"))
            return name + "es";
        if (name.EndsWith("y") && name.Length > 1 && !IsVowel(name[name.Length - 2]))
            return name.Substring(0, name.Length - 1) + "ies";
        return name + "s";
    }

    private static bool IsVowel(char c)
    {
        return "aeiouAEIOU".IndexOf(c) >= 0;
    }

    private static string SanitizeIndexName(string indexName)
    {
        var name = ToPascalCase(indexName, NamingStyleKind.SnakeCase);
        if (name.StartsWith("Ix", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("Idx", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("Index", StringComparison.OrdinalIgnoreCase))
            return name;
        return "Idx" + name;
    }
}

internal static class ColumnMetadataExtensions
{
    public static bool IsComputed(this ColumnMetadata col)
    {
        // Heuristic: if DefaultExpression contains "GENERATED ALWAYS" it's computed
        return col.DefaultExpression?.Contains("GENERATED ALWAYS", StringComparison.OrdinalIgnoreCase) == true;
    }
}
