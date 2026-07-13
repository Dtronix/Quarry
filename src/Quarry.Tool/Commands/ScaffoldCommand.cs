using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Quarry.Shared.Migration;
using Quarry.Shared.Scaffold;
using Quarry.Tool.Interactive;
using Quarry.Tool.Schema;

namespace Quarry.Tool.Commands;

internal static class ScaffoldCommand
{
    public static async Task RunAsync(
        string dialect,
        string? server,
        string? port,
        string? user,
        string? password,
        string database,
        string? connectionString,
        string? schemaFilter,
        string? tables,
        string output,
        string? namespaceName,
        string namingStyleStr,
        bool noSingularize,
        bool noNavigations,
        bool nonInteractive,
        string? contextName)
    {
        // Resolve naming style
        var namingStyle = ParseNamingStyle(namingStyleStr);

        // Build connection string
        var connStr = connectionString ?? DatabaseSchemaReader.BuildConnectionString(dialect, server, port, user, password, database);
        Console.WriteLine($"Connecting to {dialect} database...");

        // Phase 1-3: Introspect tables (apply filter + gather full per-table metadata)
        Console.WriteLine("Introspecting database schema...");
        var introspectedTables = await DatabaseSchemaReader.ReadTablesAsync(dialect, connStr, schemaFilter, tables);

        if (introspectedTables.Count == 0)
        {
            Console.WriteLine("No tables found matching the specified criteria.");
            return;
        }

        Console.WriteLine($"Found {introspectedTables.Count} table(s).");

        var tableData = new Dictionary<string, TableIntrospectionData>(StringComparer.OrdinalIgnoreCase);
        foreach (var data in introspectedTables)
            tableData[data.Table.Name] = data;

        // Build class name map
        var tableClassMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (tableName, _) in tableData)
        {
            tableClassMap[tableName] = ScaffoldCodeGenerator.ToClassName(tableName, noSingularize);
        }

        var isInteractive = InteractivePrompt.IsInteractive(nonInteractive);

        // Phase 4: Junction table detection
        var junctionTables = new Dictionary<string, JunctionTableDetector.JunctionTableResult>(StringComparer.OrdinalIgnoreCase);
        foreach (var (tableName, data) in tableData)
        {
            var junction = JunctionTableDetector.Detect(
                tableName, data.Table.Schema,
                data.Columns, data.PrimaryKey, data.ForeignKeys, data.Indexes);

            if (junction != null)
            {
                junctionTables[tableName] = junction;
                Console.WriteLine($"  Detected junction table: {tableName}");
            }
        }

        // Phase 5: Implicit FK heuristics
        var implicitFks = new Dictionary<string, List<ForeignKeyMetadata>>(StringComparer.OrdinalIgnoreCase);
        var allTablePkInfo = tableData.ToDictionary(
            kvp => kvp.Key,
            kvp => (kvp.Value.PrimaryKey, kvp.Value.Columns),
            StringComparer.OrdinalIgnoreCase);

        foreach (var (tableName, data) in tableData)
        {
            var candidates = ImplicitForeignKeyDetector.Detect(
                tableName, data.Columns, data.ForeignKeys, allTablePkInfo, data.Indexes);

            if (candidates.Count == 0)
                continue;

            var accepted = new List<ForeignKeyMetadata>();
            var skipAll = false;
            var acceptAll = false;

            foreach (var candidate in candidates)
            {
                if (skipAll) break;

                var shouldAccept = acceptAll || !isInteractive;

                if (isInteractive && !acceptAll)
                {
                    Console.WriteLine();
                    Console.WriteLine($"Possible foreign key detected (no constraint in database):");
                    Console.WriteLine($"  {candidate.SourceTable}.{candidate.SourceColumn} -> {candidate.TargetTable}.{candidate.TargetColumn}");
                    Console.WriteLine($"  Confidence: {candidate.Confidence:P0}");

                    var choice = InteractivePrompt.Choose("Action:", new List<(string Label, string Value)>
                    {
                        ("Accept", "y"),
                        ("Skip", "n"),
                        ("Accept all >=80%", "a"),
                        ("Skip all implicit FKs", "s")
                    });

                    switch (choice)
                    {
                        case "y":
                            shouldAccept = true;
                            break;
                        case "n":
                            shouldAccept = false;
                            break;
                        case "a":
                            acceptAll = true;
                            shouldAccept = candidate.Score >= 80;
                            break;
                        case "s":
                            skipAll = true;
                            continue;
                    }
                }

                if (shouldAccept)
                {
                    accepted.Add(new ForeignKeyMetadata(
                        constraintName: $"FK_implicit_{candidate.SourceTable}_{candidate.SourceColumn}",
                        columnName: candidate.SourceColumn,
                        referencedTable: candidate.TargetTable,
                        referencedColumn: candidate.TargetColumn,
                        onDelete: "NO ACTION",
                        onUpdate: "NO ACTION"));
                }
            }

            if (accepted.Count > 0)
                implicitFks[tableName] = accepted;
        }

        // Phase 6: Build relationships — compute incoming relationships for Many<T>
        var incomingRelationships = new Dictionary<string, List<ScaffoldCodeGenerator.IncomingRelationship>>(StringComparer.OrdinalIgnoreCase);

        if (!noNavigations)
        {
            foreach (var (tableName, data) in tableData)
            {
                var className = tableClassMap[tableName];
                var allFksForTable = data.ForeignKeys.ToList();
                if (implicitFks.TryGetValue(tableName, out var iFks))
                    allFksForTable.AddRange(iFks);

                foreach (var fk in allFksForTable)
                {
                    // Determine cardinality: check if FK column has a unique index
                    var isOneToOne = data.Indexes.Any(idx =>
                        idx.IsUnique && idx.Columns.Count == 1 &&
                        idx.Columns[0].Equals(fk.ColumnName, StringComparison.OrdinalIgnoreCase));

                    if (!incomingRelationships.ContainsKey(fk.ReferencedTable))
                        incomingRelationships[fk.ReferencedTable] = new List<ScaffoldCodeGenerator.IncomingRelationship>();

                    incomingRelationships[fk.ReferencedTable].Add(
                        new ScaffoldCodeGenerator.IncomingRelationship(tableName, className, fk.ColumnName, isOneToOne));
                }
            }
        }

        // Phase 7: Type mapping and schema file generation
        var outputDir = Path.GetFullPath(output);
        Directory.CreateDirectory(outputDir);

        var filesWritten = 0;
        var warnings = new List<string>();

        foreach (var (tableName, data) in tableData)
        {
            var className = tableClassMap[tableName];

            // Type mapping
            var typeResults = new List<ReverseTypeResult>();
            var pkColumns = data.PrimaryKey?.Columns ?? Array.Empty<string>();

            foreach (var col in data.Columns)
            {
                var isPk = pkColumns.Any(c => c.Equals(col.Name, StringComparison.OrdinalIgnoreCase));
                var typeResult = ReverseTypeMapper.MapSqlType(
                    col.DataType, dialect, col.Name, col.IsNullable, col.IsIdentity, isPk);

                // Phase 6: Interactive type resolution for ambiguous cases
                if (typeResult.Warning != null && isInteractive && IsAmbiguousTypeMapping(typeResult))
                {
                    typeResult = ResolveAmbiguousType(col, typeResult);
                }

                if (typeResult.Warning != null)
                    warnings.Add($"  {tableName}.{col.Name}: {typeResult.Warning}");

                typeResults.Add(typeResult);
            }

            var tableImplicitFks = implicitFks.TryGetValue(tableName, out var tiFks) ? tiFks : new List<ForeignKeyMetadata>();
            var tableIncoming = incomingRelationships.TryGetValue(tableName, out var tIncoming) ? tIncoming : new List<ScaffoldCodeGenerator.IncomingRelationship>();
            var junctionInfo = junctionTables.TryGetValue(tableName, out var ji) ? ji : null;

            var scaffoldedTable = new ScaffoldCodeGenerator.ScaffoldedTable(
                tableName, data.Table.Schema, className,
                data.Columns, data.PrimaryKey,
                data.ForeignKeys, tableImplicitFks,
                data.Indexes, typeResults,
                junctionInfo, tableIncoming);

            var databaseName = ExtractDatabaseName(database, connectionString);
            var code = ScaffoldCodeGenerator.GenerateSchemaFile(
                scaffoldedTable, namespaceName, namingStyle, noSingularize, noNavigations, databaseName, tableClassMap);

            var filePath = Path.Combine(outputDir, $"{className}.cs");
            await File.WriteAllTextAsync(filePath, code);
            Console.WriteLine($"  Created: {Path.GetRelativePath(".", filePath)}");
            filesWritten++;
        }

        // Generate context file
        var databaseNameForContext = ExtractDatabaseName(database, connectionString);
        var contextClassName = contextName ?? ScaffoldCodeGenerator.ToContextClassName(databaseNameForContext);
        var contextCode = ScaffoldCodeGenerator.GenerateContextFile(
            contextClassName, dialect, namespaceName, databaseNameForContext, tableClassMap);

        var contextFilePath = Path.Combine(outputDir, $"{contextClassName}.cs");
        await File.WriteAllTextAsync(contextFilePath, contextCode);
        Console.WriteLine($"  Created: {Path.GetRelativePath(".", contextFilePath)}");
        filesWritten++;

        // Summary
        Console.WriteLine();
        Console.WriteLine($"Scaffolded {filesWritten} file(s) to {Path.GetRelativePath(".", outputDir)}/");

        if (junctionTables.Count > 0)
            Console.WriteLine($"  {junctionTables.Count} junction table(s) detected");

        var totalImplicit = implicitFks.Values.Sum(l => l.Count);
        if (totalImplicit > 0)
            Console.WriteLine($"  {totalImplicit} implicit FK(s) accepted");

        if (warnings.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Warnings:");
            foreach (var w in warnings)
                Console.Error.WriteLine(w);
        }

        Console.WriteLine();
        Console.WriteLine("Next steps:");
        Console.WriteLine("  1. Review and adjust the generated schema files");
        Console.WriteLine("  2. Run: quarry migrate add InitialBaseline");
    }

    private static NamingStyleKind ParseNamingStyle(string style)
    {
        return style.ToLowerInvariant() switch
        {
            "snakecase" or "snake_case" or "snake" => NamingStyleKind.SnakeCase,
            "camelcase" or "camel" => NamingStyleKind.CamelCase,
            "lowercase" or "lower" => NamingStyleKind.LowerCase,
            _ => NamingStyleKind.Exact
        };
    }

    private static string ExtractDatabaseName(string database, string? connectionString)
    {
        if (!string.IsNullOrEmpty(database))
            return Path.GetFileNameWithoutExtension(database);

        if (connectionString == null)
            return "unknown";

        // Try to extract Database= from connection string
        foreach (var part in connectionString.Split(';'))
        {
            var kv = part.Split('=', 2);
            if (kv.Length == 2 && kv[0].Trim().Equals("Database", StringComparison.OrdinalIgnoreCase))
                return kv[1].Trim();
        }

        return "unknown";
    }

    private static bool IsAmbiguousTypeMapping(ReverseTypeResult result)
    {
        return result.Warning != null &&
               (result.Warning.Contains("TINYINT", StringComparison.OrdinalIgnoreCase) ||
                result.Warning.Contains("CHAR(36)", StringComparison.OrdinalIgnoreCase));
    }

    private static ReverseTypeResult ResolveAmbiguousType(ColumnMetadata col, ReverseTypeResult current)
    {
        Console.WriteLine();
        Console.WriteLine($"Ambiguous type mapping:");
        Console.WriteLine($"  Column '{col.Name}' has type {col.DataType} -- map as:");

        var options = new List<(string Label, string Value)>
        {
            ($"{current.ClrType} (Recommended)", current.ClrType),
        };

        // Add alternatives
        if (current.ClrType == "bool")
            options.Add(("byte", "byte"));
        else if (current.ClrType == "byte")
            options.Add(("bool", "bool"));
        else if (current.ClrType == "Guid")
            options.Add(("string", "string"));
        else if (current.ClrType == "string" && col.DataType.Contains("CHAR(36)", StringComparison.OrdinalIgnoreCase))
            options.Add(("Guid", "Guid"));

        var choice = InteractivePrompt.Choose("Type:", options);

        if (choice != current.ClrType)
        {
            return new ReverseTypeResult(choice, current.IsNullable, current.MaxLength, current.Precision, current.Scale);
        }

        return current;
    }
}
