using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Quarry.Tool.Commands;

namespace Quarry.Tests.Migration;

/// <summary>
/// Guards installed by #313 at the command layer: a snapshot version that was discovered but
/// cannot be built must abort — it must never flow into SchemaDiffer.Diff as a null (empty)
/// baseline, which is exactly how migrate add/diff silently scaffolded full schemas.
/// </summary>
public class MigrateCommandsGuardTests
{
    [Test]
    public void FindAndBuildSnapshot_SnapshotNotBuildable_ThrowsInsteadOfReturningNull()
    {
        // A compilation with no snapshot classes at all: CompileAndBuild returns null
        // (not found), and FindAndBuildSnapshot must convert that into a loud abort
        // rather than handing a null baseline to the differ.
        var compilation = CSharpCompilation.Create(
            "GuardTestProject",
            new[] { CSharpSyntaxTree.ParseText("namespace TestApp; public class Empty { }") },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        Assert.That(
            () => MigrateCommands.FindAndBuildSnapshot(compilation, 99),
            Throws.InvalidOperationException.With.Message.Contains("could not be built"));
    }
}
