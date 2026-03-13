using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Quarry.Tool.Schema;

internal static class DialectResolver
{
    public static string? ResolveDialect(Compilation compilation, string? explicitDialect)
    {
        if (explicitDialect != null) return explicitDialect;

        foreach (var tree in compilation.SyntaxTrees)
        {
            var model = compilation.GetSemanticModel(tree);
            foreach (var classDecl in tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>())
            {
                var symbol = Microsoft.CodeAnalysis.CSharp.CSharpExtensions.GetDeclaredSymbol(model, classDecl);
                if (symbol == null) continue;

                foreach (var attr in symbol.GetAttributes())
                {
                    if (attr.AttributeClass?.Name == "QuarryContextAttribute")
                    {
                        foreach (var arg in attr.NamedArguments)
                        {
                            if (arg.Key == "Dialect" && arg.Value.Value is int dialectValue)
                            {
                                return dialectValue switch
                                {
                                    0 => "sqlite",
                                    1 => "postgresql",
                                    2 => "mysql",
                                    3 => "sqlserver",
                                    _ => null
                                };
                            }
                        }
                    }
                }
            }
        }

        return null;
    }
}
