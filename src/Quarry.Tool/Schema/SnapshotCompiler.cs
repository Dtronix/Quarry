using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Quarry.Shared.Migration;

namespace Quarry.Tool.Schema;

/// <summary>
/// Compiles a snapshot class from the user's project via Roslyn in-memory compilation
/// and invokes its Build() method to obtain the SchemaSnapshot.
/// </summary>
internal static class SnapshotCompiler
{
    // Allowed method names within Build() body — anything else is rejected.
    private static readonly HashSet<string> AllowedMethods = new(StringComparer.Ordinal)
    {
        "SetVersion", "SetName", "SetTimestamp", "SetParentVersion",
        "AddTable", "Name", "Schema", "NamingStyle",
        "AddColumn", "ClrType", "PrimaryKey", "ForeignKey",
        "Nullable", "Identity", "ClientGenerated", "Computed",
        "Length", "Precision", "Default", "HasDefault", "MapTo", "CustomTypeMapping", "NotNull",
        "AddForeignKey", "AddIndex", "CompositeKey",
        "Build", "Parse",
    };

    public static SchemaSnapshot? CompileAndBuild(Compilation compilation, int targetVersion)
    {
        // 1. Find the syntax tree containing the snapshot class with the target version
        SyntaxTree? snapshotTree = null;
        string? snapshotClassName = null;

        foreach (var tree in compilation.SyntaxTrees)
        {
            var model = compilation.GetSemanticModel(tree);
            foreach (var classDecl in tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>())
            {
                var symbol = Microsoft.CodeAnalysis.CSharp.CSharpExtensions.GetDeclaredSymbol(model, classDecl);
                if (symbol == null) continue;

                foreach (var attr in symbol.GetAttributes())
                {
                    if (attr.AttributeClass?.Name != "MigrationSnapshotAttribute") continue;

                    foreach (var arg in attr.NamedArguments)
                    {
                        if (arg.Key == "Version" && arg.Value.Value is int v && v == targetVersion)
                        {
                            snapshotTree = tree;
                            snapshotClassName = symbol.Name;
                            break;
                        }
                    }

                    if (snapshotTree != null) break;
                }

                if (snapshotTree != null) break;
            }

            if (snapshotTree != null) break;
        }

        if (snapshotTree == null || snapshotClassName == null)
        {
            return null;
        }

        // 2. Find the Build() method across all partial declarations of this class.
        //    The [MigrationSnapshot] attribute may be discovered on any partial declaration,
        //    but Build() lives in the .g.cs file.
        MethodDeclarationSyntax? buildMethod = null;
        SyntaxTree? buildTree = null;

        foreach (var tree in compilation.SyntaxTrees)
        {
            foreach (var classDecl in tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>())
            {
                if (classDecl.Identifier.Text != snapshotClassName) continue;

                var method = classDecl.Members
                    .OfType<MethodDeclarationSyntax>()
                    .FirstOrDefault(m => m.Identifier.Text == "Build");

                if (method != null)
                {
                    buildMethod = method;
                    buildTree = tree;
                    break;
                }
            }
            if (buildMethod != null) break;
        }

        if (buildMethod == null || buildTree == null)
            return null;

        snapshotTree = buildTree;

        // Rebuild minimal source: usings + namespace + class with only Build()
        var root = snapshotTree.GetRoot();
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
        sb.Append("internal static class ").AppendLine(snapshotClassName);
        sb.AppendLine("{");
        sb.AppendLine(buildMethod.ToFullString());
        sb.AppendLine("}");

        var sourceText = sb.ToString()
            .Replace("using Quarry.Migration;", "using Quarry.Shared.Migration;");
        snapshotTree = CSharpSyntaxTree.ParseText(sourceText);

        // Validate that Build() only calls whitelisted builder methods.
        // This prevents arbitrary code execution via crafted snapshot files.
        if (!ValidateBuildMethod(snapshotTree))
        {
            Console.Error.WriteLine("Snapshot Build() method contains disallowed method calls. Aborting compilation.");
            return null;
        }

        // 3. Build a new compilation with the snapshot source + required references
        var references = new List<MetadataReference>(compilation.References);

        // Add reference to the Tool's own assembly for shared types (SchemaSnapshotBuilder, etc.)
        var sharedAssembly = typeof(SchemaSnapshotBuilder).Assembly;
        references.Add(MetadataReference.CreateFromFile(sharedAssembly.Location));

        // Add reference to Quarry.dll for MigrationSnapshotAttribute
        var quarryAssembly = typeof(Quarry.MigrationSnapshotAttribute).Assembly;
        if (quarryAssembly.Location != sharedAssembly.Location)
        {
            references.Add(MetadataReference.CreateFromFile(quarryAssembly.Location));
        }

        // Ensure core runtime assemblies are present.
        // Use trusted platform assemblies for comprehensive coverage.
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
            "QuarrySnapshotCompilation",
            syntaxTrees: new[] { snapshotTree },
            references: references.Distinct(),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        // 3. Emit to memory
        using var ms = new MemoryStream();
        var emitResult = newCompilation.Emit(ms);

        if (!emitResult.Success)
        {
            foreach (var diag in emitResult.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error))
            {
                Console.Error.WriteLine($"Snapshot compilation error: {diag.GetMessage()}");
            }
            return null;
        }

        ms.Seek(0, SeekOrigin.Begin);

        // 4. Load into a collectible AssemblyLoadContext and invoke Build()
        var context = new AssemblyLoadContext("SnapshotCompiler", isCollectible: true);
        try
        {
            var assembly = context.LoadFromStream(ms);

            // Find the snapshot type
            var snapshotType = assembly.GetTypes()
                .FirstOrDefault(t => t.Name == snapshotClassName);

            if (snapshotType == null)
                return null;

            var buildMethodInfo = snapshotType.GetMethod("Build", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (buildMethodInfo == null)
                return null;

            return buildMethodInfo.Invoke(null, null) as SchemaSnapshot;
        }
        finally
        {
            context.Unload();
        }
    }

    /// <summary>
    /// Validates that the Build() method body only contains whitelisted method invocations.
    /// Rejects snapshots with arbitrary code (e.g., Process.Start, File.Delete).
    /// </summary>
    private static bool ValidateBuildMethod(SyntaxTree tree)
    {
        var root = tree.GetRoot();
        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            var methodName = invocation.Expression switch
            {
                MemberAccessExpressionSyntax ma => ma.Name.Identifier.Text,
                IdentifierNameSyntax id => id.Identifier.Text,
                _ => null
            };

            if (methodName != null && !AllowedMethods.Contains(methodName))
            {
                Console.Error.WriteLine($"Disallowed method call in snapshot Build(): '{methodName}'");
                return false;
            }
        }
        return true;
    }
}
