using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Quarry.Tool.Schema;

/// <summary>
/// Compiles migration classes from the user's project via Roslyn in-memory compilation
/// and invokes their Upgrade() method to obtain the generated SQL.
/// </summary>
internal static class MigrationCompiler
{
    /// <summary>
    /// Compiles and invokes the Upgrade() method for a migration at the given version,
    /// returning the SQL rendered for the specified dialect.
    /// </summary>
    public static string? CompileAndBuildSql(Compilation compilation, int targetVersion, SqlDialect dialect)
    {
        // 1. Find the migration class with [Migration(Version = targetVersion)]
        string? migrationClassName = null;

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

                    foreach (var arg in attr.NamedArguments)
                    {
                        if (arg.Key == "Version" && arg.Value.Value is int v && v == targetVersion)
                        {
                            migrationClassName = symbol.Name;
                            break;
                        }
                    }

                    if (migrationClassName != null) break;
                }

                if (migrationClassName != null) break;
            }

            if (migrationClassName != null) break;
        }

        if (migrationClassName == null)
            return null;

        // 2. Find the Upgrade() method across all partial declarations
        MethodDeclarationSyntax? upgradeMethod = null;
        SyntaxTree? upgradeTree = null;

        foreach (var tree in compilation.SyntaxTrees)
        {
            foreach (var classDecl in tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>())
            {
                if (classDecl.Identifier.Text != migrationClassName) continue;

                var method = classDecl.Members
                    .OfType<MethodDeclarationSyntax>()
                    .FirstOrDefault(m => m.Identifier.Text == "Upgrade");

                if (method != null)
                {
                    upgradeMethod = method;
                    upgradeTree = tree;
                    break;
                }
            }
            if (upgradeMethod != null) break;
        }

        if (upgradeMethod == null || upgradeTree == null)
            return null;

        // 3. Rebuild minimal source: usings + namespace + class with Upgrade() + stub hooks
        var root = upgradeTree.GetRoot();
        var usings = root.DescendantNodes().OfType<UsingDirectiveSyntax>().ToList();
        var ns = root.DescendantNodes().OfType<FileScopedNamespaceDeclarationSyntax>().FirstOrDefault()
            ?? (SyntaxNode?)root.DescendantNodes().OfType<NamespaceDeclarationSyntax>().FirstOrDefault();

        var sb = new System.Text.StringBuilder();
        foreach (var u in usings)
            sb.AppendLine(u.ToFullString().Trim());
        sb.AppendLine();
        if (ns is FileScopedNamespaceDeclarationSyntax fsns)
            sb.Append("namespace ").Append(fsns.Name).AppendLine(";");
        else if (ns is NamespaceDeclarationSyntax nds)
            sb.Append("namespace ").Append(nds.Name).AppendLine(";");
        sb.AppendLine();
        sb.Append("internal static class ").AppendLine(migrationClassName);
        sb.AppendLine("{");
        sb.AppendLine(upgradeMethod.ToFullString());
        // Empty hook stubs so partial calls compile
        sb.AppendLine("    static void BeforeUpgrade(Quarry.Migration.MigrationBuilder builder) { }");
        sb.AppendLine("    static void AfterUpgrade(Quarry.Migration.MigrationBuilder builder) { }");
        sb.AppendLine("    static void BeforeDowngrade(Quarry.Migration.MigrationBuilder builder) { }");
        sb.AppendLine("    static void AfterDowngrade(Quarry.Migration.MigrationBuilder builder) { }");
        sb.AppendLine("}");

        var sourceText = sb.ToString();
        var syntaxTree = CSharpSyntaxTree.ParseText(sourceText);

        // 4. Build compilation with required references
        var references = new List<MetadataReference>(compilation.References);

        var quarryAssembly = typeof(Quarry.Migration.MigrationBuilder).Assembly;
        references.Add(MetadataReference.CreateFromFile(quarryAssembly.Location));

        var sharedAssembly = typeof(Quarry.Shared.Migration.SchemaSnapshot).Assembly;
        if (sharedAssembly.Location != quarryAssembly.Location)
            references.Add(MetadataReference.CreateFromFile(sharedAssembly.Location));

        var trustedAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        if (trustedAssemblies != null)
        {
            var essentialPrefixes = new[] { "System.Runtime", "System.Private.CoreLib", "System.Collections", "netstandard" };
            foreach (var assemblyPath in trustedAssemblies.Split(Path.PathSeparator))
            {
                var fileName = Path.GetFileNameWithoutExtension(assemblyPath);
                foreach (var prefix in essentialPrefixes)
                {
                    if (string.Equals(fileName, prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        references.Add(MetadataReference.CreateFromFile(assemblyPath));
                        break;
                    }
                }
            }
        }
        else
        {
            var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
            foreach (var essential in new[] { "System.Private.CoreLib.dll", "System.Runtime.dll", "System.Collections.dll", "netstandard.dll" })
            {
                var path = Path.Combine(runtimeDir, essential);
                if (File.Exists(path))
                    references.Add(MetadataReference.CreateFromFile(path));
            }
        }

        var newCompilation = CSharpCompilation.Create(
            "QuarryMigrationCompilation",
            syntaxTrees: new[] { syntaxTree },
            references: references.Distinct(),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        // 5. Emit to memory
        using var ms = new MemoryStream();
        var emitResult = newCompilation.Emit(ms);

        if (!emitResult.Success)
        {
            foreach (var diag in emitResult.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error))
                Console.Error.WriteLine($"Migration compilation error: {diag.GetMessage()}");
            return null;
        }

        ms.Seek(0, SeekOrigin.Begin);

        // 6. Load, invoke Upgrade(builder), render SQL, unload
        var context = new AssemblyLoadContext("MigrationCompiler", isCollectible: true);
        try
        {
            var assembly = context.LoadFromStream(ms);
            var migrationType = assembly.GetTypes().FirstOrDefault(t => t.Name == migrationClassName);
            if (migrationType == null) return null;

            var upgradeMethodInfo = migrationType.GetMethod("Upgrade", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (upgradeMethodInfo == null) return null;

            var builder = new Quarry.Migration.MigrationBuilder();
            upgradeMethodInfo.Invoke(null, new object[] { builder });

            return builder.BuildSql(dialect);
        }
        finally
        {
            context.Unload();
        }
    }
}
