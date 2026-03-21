using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Quarry.Tool.Commands;

internal static class CommandHelpers
{
    /// <summary>
    /// Resolves a .csproj file path from a project path or directory.
    /// </summary>
    public static string ResolveCsproj(string project)
    {
        if (project.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            return Path.GetFullPath(project);

        var csprojs = Directory.GetFiles(project, "*.csproj");
        if (csprojs.Length == 1) return Path.GetFullPath(csprojs[0]);
        if (csprojs.Length == 0) throw new InvalidOperationException($"No .csproj found in '{project}'");
        throw new InvalidOperationException($"Multiple .csproj files found in '{project}'. Specify one with -p.");
    }

    /// <summary>
    /// Finds all migration classes in a compilation by scanning for [Migration] attributes.
    /// </summary>
    public static List<MigrationInfo> FindMigrations(Compilation compilation)
    {
        var migrations = new List<MigrationInfo>();
        foreach (var tree in compilation.SyntaxTrees)
        {
            var model = compilation.GetSemanticModel(tree);
            foreach (var classDecl in tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>())
            {
                var symbol = Microsoft.CodeAnalysis.CSharp.CSharpExtensions.GetDeclaredSymbol(model, classDecl);
                if (symbol == null) continue;

                foreach (var attr in symbol.GetAttributes())
                {
                    if (attr.AttributeClass?.Name != "MigrationAttribute") continue;

                    int? ver = null;
                    string? migName = null;
                    foreach (var arg in attr.NamedArguments)
                    {
                        if (arg.Key == "Version" && arg.Value.Value is int v) ver = v;
                        if (arg.Key == "Name" && arg.Value.Value is string n) migName = n;
                    }

                    if (ver.HasValue)
                    {
                        migrations.Add(new MigrationInfo(
                            ver.Value,
                            migName ?? "",
                            symbol.Name,
                            symbol.ContainingNamespace.ToDisplayString()));
                    }
                }
            }
        }

        return migrations.OrderBy(m => m.Version).ToList();
    }

    internal sealed class MigrationInfo(int version, string name, string className, string @namespace)
    {
        public int Version { get; } = version;
        public string Name { get; } = name;
        public string ClassName { get; } = className;
        public string Namespace { get; } = @namespace;
    }
}
