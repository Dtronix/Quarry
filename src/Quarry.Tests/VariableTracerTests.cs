using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Quarry.Generators.Parsing;

namespace Quarry.Tests;

/// <summary>
/// Tests for VariableTracer.IsBuilderType (ITypeSymbol overload).
/// Verifies exact name matching to prevent false positives from substring matching.
/// </summary>
[TestFixture]
public class VariableTracerTests
{
    private static CSharpCompilation CreateMinimalCompilation(string source)
    {
        var runtimeDir = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
        var references = new MetadataReference[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Runtime.dll")),
            MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "netstandard.dll")),
        };

        return CSharpCompilation.Create(
            "TestAssembly",
            new[] { CSharpSyntaxTree.ParseText(source) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static INamedTypeSymbol GetTypeSymbol(CSharpCompilation compilation, string typeName)
    {
        var symbol = compilation.GetTypeByMetadataName(typeName);
        Assert.That(symbol, Is.Not.Null, $"Type '{typeName}' not found in compilation");
        return symbol!;
    }

    [Test]
    public void IsBuilderType_NamedTypeWithBuilderName_ReturnsTrue()
    {
        var compilation = CreateMinimalCompilation(@"
namespace TestNs
{
    public interface IQueryBuilder<T> { }
    public class DeleteBuilder<T> { }
    public interface IBatchInsertBuilder { }
}");
        Assert.Multiple(() =>
        {
            Assert.That(VariableTracer.IsBuilderType(GetTypeSymbol(compilation, "TestNs.IQueryBuilder`1")), Is.True);
            Assert.That(VariableTracer.IsBuilderType(GetTypeSymbol(compilation, "TestNs.DeleteBuilder`1")), Is.True);
            Assert.That(VariableTracer.IsBuilderType(GetTypeSymbol(compilation, "TestNs.IBatchInsertBuilder")), Is.True);
        });
    }

    [Test]
    public void IsBuilderType_NamedTypeWithNonBuilderName_ReturnsFalse()
    {
        var compilation = CreateMinimalCompilation(@"
namespace TestNs
{
    public class IQueryBuilderExtensions { }
    public class NotIQueryBuilder { }
    public class SomeOtherType { }
}");
        Assert.Multiple(() =>
        {
            Assert.That(VariableTracer.IsBuilderType(GetTypeSymbol(compilation, "TestNs.IQueryBuilderExtensions")), Is.False);
            Assert.That(VariableTracer.IsBuilderType(GetTypeSymbol(compilation, "TestNs.NotIQueryBuilder")), Is.False);
            Assert.That(VariableTracer.IsBuilderType(GetTypeSymbol(compilation, "TestNs.SomeOtherType")), Is.False);
        });
    }

    [Test]
    public void IsBuilderType_ArrayType_ReturnsFalse()
    {
        // Array types are IArrayTypeSymbol, not INamedTypeSymbol
        var compilation = CreateMinimalCompilation(@"
namespace TestNs
{
    public class Holder { public int[] Values; }
}");
        var holderType = GetTypeSymbol(compilation, "TestNs.Holder");
        var arrayField = holderType.GetMembers("Values").OfType<IFieldSymbol>().Single();
        Assert.That(VariableTracer.IsBuilderType(arrayField.Type), Is.False);
    }
}
