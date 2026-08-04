using Microsoft.CodeAnalysis;

namespace Quarry.Tests.Testing;

/// <summary>
/// The metadata reference set for synthetic <see cref="Microsoft.CodeAnalysis.CSharp.CSharpCompilation"/>s
/// that run the Quarry generator without a database.
/// </summary>
/// <remarks>
/// <para>
/// Shared deliberately. Three copies of this list existed and had already drifted apart:
/// only one included <c>System.ComponentModel.Primitives.dll</c>, whose absence degrades
/// the semantic model enough to flip the generator's projection classification into the
/// identity-projection fallback. Two fixtures were therefore exercising the generator under
/// measurably different semantics from the third, with nothing at the call sites to show it.
/// </para>
/// <para>
/// If you add a reference here, say what breaks without it — the whole point of one list is
/// that the reason survives next to the entry.
/// </para>
/// </remarks>
internal static class GeneratorTestReferences
{
    private static readonly IReadOnlyList<MetadataReference> Cached = Build();

    /// <summary>The shared reference set. Built once per process.</summary>
    public static IReadOnlyList<MetadataReference> All => Cached;

    private static IReadOnlyList<MetadataReference> Build()
    {
        var references = new List<MetadataReference>
        {
            // Quarry's own surface: Schema, Col<T>, Key<T>, QuarryContext, the builder interfaces.
            MetadataReference.CreateFromFile(typeof(Schema).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            // System.Data: IDbConnection, DbConnection — the context base type's surface.
            MetadataReference.CreateFromFile(typeof(System.Data.IDbConnection).Assembly.Location),
        };

        var runtimeDir = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
        foreach (var dll in new[]
        {
            "System.Runtime.dll",
            "System.Collections.dll",
            "System.Linq.dll",
            "System.Linq.Expressions.dll",
            "netstandard.dll",
            "System.Threading.Tasks.dll",

            // Without this the semantic model cannot resolve some of the attribute and
            // component types reachable from the generated entity surface, several symbols
            // degrade to TypeKind.Error, and ProjectionAnalyzer silently falls back to the
            // identity-projection path — changing what the generator emits rather than
            // failing outright. Found while investigating #329; see the step-6/7 notes.
            "System.ComponentModel.Primitives.dll",
        })
        {
            references.Add(MetadataReference.CreateFromFile(Path.Combine(runtimeDir, dll)));
        }

        return references;
    }
}
