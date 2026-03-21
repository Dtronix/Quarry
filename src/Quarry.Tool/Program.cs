using Quarry.Tool.Commands;

if (args.Length == 0)
{
    PrintUsage();
    return 1;
}

var command = args[0];
if (args.Length > 1 && !args[1].StartsWith("-"))
{
    command = $"{args[0]} {args[1]}";
}

try
{
    return await DispatchAsync(command, args);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Error: {ex.Message}");
    return 1;
}

async Task<int> DispatchAsync(string command, string[] args)
{
    var opts = ParseOptions(args);

    switch (command)
    {
        case "migrate add":
            await MigrateCommands.MigrateAdd(
                GetPositional(opts, "name", args, 2),
                GetOpt(opts, "p", "project", "."),
                GetOpt(opts, "o", "output", "Migrations"),
                HasFlag(opts, "ni", "non-interactive"));
            return 0;

        case "migrate add-empty":
            await MigrateCommands.MigrateAddEmpty(
                GetPositional(opts, "name", args, 2),
                GetOpt(opts, "p", "project", "."),
                GetOpt(opts, "o", "output", "Migrations"));
            return 0;

        case "migrate list":
            await MigrateCommands.MigrateList(
                GetOpt(opts, "p", "project", "."));
            return 0;

        case "migrate validate":
            await MigrateCommands.MigrateValidate(
                GetOpt(opts, "p", "project", "."));
            return 0;

        case "migrate remove":
            await MigrateCommands.MigrateRemove(
                GetOpt(opts, "p", "project", "."));
            return 0;

        case "migrate diff":
            await MigrateCommands.MigrateDiff(
                GetOpt(opts, "p", "project", "."),
                HasFlag(opts, "ni", "non-interactive"));
            return 0;

        case "migrate script":
            await MigrateCommands.MigrateScript(
                GetOpt(opts, "p", "project", "."),
                GetOptOrNull(opts, "d", "dialect"),
                GetOptOrNull(opts, "o", "output"),
                GetOptOrNull(opts, null, "from") is string fromStr ? int.Parse(fromStr) : null,
                GetOptOrNull(opts, null, "to") is string toStr ? int.Parse(toStr) : null);
            return 0;

        case "migrate status":
            var statusConnection = GetOptOrNull(opts, "c", "connection");
            if (statusConnection == null)
            {
                Console.Error.WriteLine("--connection / -c is required for migrate status.");
                return 1;
            }
            await MigrateCommands.MigrateStatus(
                GetOpt(opts, "p", "project", "."),
                GetOptOrNull(opts, "d", "dialect"),
                statusConnection);
            return 0;

        case "migrate squash":
            await MigrateCommands.MigrateSquash(
                GetOpt(opts, "p", "project", "."),
                GetOpt(opts, "o", "output", "Migrations"),
                HasFlag(opts, "ni", "non-interactive"),
                GetOptOrNull(opts, "d", "dialect"),
                GetOptOrNull(opts, "c", "connection"));
            return 0;

        case "create-scripts":
            await MigrateCommands.CreateScripts(
                GetOpt(opts, "p", "project", "."),
                GetOptOrNull(opts, "d", "dialect"),
                GetOptOrNull(opts, "o", "output"));
            return 0;

        case "scaffold":
            await ScaffoldCommand.RunAsync(
                dialect: GetOpt(opts, "d", "dialect", ""),
                server: GetOptOrNull(opts, null, "server"),
                port: GetOptOrNull(opts, null, "port"),
                user: GetOptOrNull(opts, "u", "user"),
                password: GetOptOrNull(opts, null, "password"),
                database: GetOpt(opts, null, "database", ""),
                connectionString: GetOptOrNull(opts, "c", "connection"),
                schemaFilter: GetOptOrNull(opts, null, "schema"),
                tables: GetOptOrNull(opts, null, "tables"),
                output: GetOpt(opts, "o", "output", "."),
                namespaceName: GetOptOrNull(opts, null, "namespace"),
                namingStyleStr: GetOpt(opts, null, "naming-style", "Exact"),
                noSingularize: HasFlag(opts, null, "no-singularize"),
                noNavigations: HasFlag(opts, null, "no-navigations"),
                nonInteractive: HasFlag(opts, "ni", "non-interactive"),
                contextName: GetOptOrNull(opts, null, "context"));
            return 0;

        default:
            Console.Error.WriteLine($"Unknown command: {command}");
            PrintUsage();
            return 1;
    }
}

Dictionary<string, string> ParseOptions(string[] args)
{
    var opts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < args.Length; i++)
    {
        var arg = args[i];
        if (arg.StartsWith("--"))
        {
            var key = arg[2..];
            if (i + 1 < args.Length && !args[i + 1].StartsWith("-"))
                opts[key] = args[++i];
            else
                opts[key] = "true";
        }
        else if (arg.StartsWith("-") && arg.Length == 2)
        {
            var key = arg[1..];
            if (i + 1 < args.Length && !args[i + 1].StartsWith("-"))
                opts[key] = args[++i];
            else
                opts[key] = "true";
        }
    }
    return opts;
}

string GetOpt(Dictionary<string, string> opts, string? shortKey, string longKey, string defaultValue)
{
    if (shortKey != null && opts.TryGetValue(shortKey, out var v1)) return v1;
    if (opts.TryGetValue(longKey, out var v2)) return v2;
    return defaultValue;
}

string? GetOptOrNull(Dictionary<string, string> opts, string? shortKey, string longKey)
{
    if (shortKey != null && opts.TryGetValue(shortKey, out var v1)) return v1;
    if (opts.TryGetValue(longKey, out var v2)) return v2;
    return null;
}

bool HasFlag(Dictionary<string, string> opts, string? shortKey, string longKey)
{
    return GetOpt(opts, shortKey, longKey, "false") == "true";
}

string GetPositional(Dictionary<string, string> opts, string positionalName, string[] args, int positionalIndex)
{
    // Try positional first
    if (positionalIndex < args.Length && !args[positionalIndex].StartsWith("-"))
        return args[positionalIndex];
    // Try named
    var val = GetOptOrNull(opts, null, positionalName);
    if (val == null) throw new InvalidOperationException($"Required argument '{positionalName}' is missing.");
    return val;
}

void PrintUsage()
{
    Console.WriteLine("Quarry Migration Tool");
    Console.WriteLine();
    Console.WriteLine("Usage: quarry <command> [options]");
    Console.WriteLine();
    Console.WriteLine("Commands:");
    Console.WriteLine("  migrate add <name>       Scaffold a new migration from schema changes");
    Console.WriteLine("  migrate add-empty <name> Create an empty migration for manual operations");
    Console.WriteLine("  migrate list             List all migrations");
    Console.WriteLine("  migrate validate         Validate migration integrity");
    Console.WriteLine("  migrate remove           Remove the latest unapplied migration");
    Console.WriteLine("  migrate diff             Preview schema changes without generating files");
    Console.WriteLine("  migrate script           Generate incremental migration SQL for a version range");
    Console.WriteLine("  migrate status           Show applied vs pending migration status (requires --connection)");
    Console.WriteLine("  migrate squash           Collapse all migrations into a single baseline");
    Console.WriteLine("  create-scripts           Generate full CREATE TABLE DDL from current schema");
    Console.WriteLine("  scaffold                 Reverse-engineer an existing database to schema files");
}
