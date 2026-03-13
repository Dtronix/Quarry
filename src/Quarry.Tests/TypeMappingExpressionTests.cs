using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Quarry.Generators.Models;
using Quarry.Shared.Sql;
using Quarry.Generators.Translation;
using Quarry.Shared.Migration;
using GenSqlDialect = Quarry.Generators.Sql.SqlDialect;

namespace Quarry.Tests;

/// <summary>
/// Layer 6: ExpressionSyntaxTranslator tests for Where clause parameter mapping detection.
/// Verifies that comparisons against mapped columns set CustomTypeMappingClass on parameters.
/// </summary>
[TestFixture]
public class TypeMappingExpressionTests
{
    private static readonly string QuarryCoreAssemblyPath = typeof(Schema).Assembly.Location;
    private static readonly string SystemRuntimeAssemblyPath = typeof(object).Assembly.Location;

    private const string MappingFqn = "TestApp.MoneyMapping";

    #region Test Infrastructure

    private static CSharpCompilation CreateCompilation(string lambdaExpression)
    {
        var source = $@"
using System;

namespace TestApp
{{
    public readonly struct Money
    {{
        public decimal Amount {{ get; }}
        public Money(decimal amount) => Amount = amount;
    }}

    public class TestEntity
    {{
        public int Id {{ get; set; }}
        public string Name {{ get; set; }}
        public Money Balance {{ get; set; }}
        public Money CreditLimit {{ get; set; }}
        public decimal PlainDecimal {{ get; set; }}
    }}

    public class TestClass
    {{
        public Money capturedMoney = new(100);
        public decimal capturedDecimal = 200;

        public void Method()
        {{
            var localMoney = new Money(50);
            var localDecimal = 75m;
            System.Func<TestEntity, bool> predicate = {lambdaExpression};
        }}
    }}
}}
";

        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(QuarryCoreAssemblyPath),
            MetadataReference.CreateFromFile(SystemRuntimeAssemblyPath),
        };

        var runtimeDir = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
        references.Add(MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Runtime.dll")));
        references.Add(MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Collections.dll")));
        references.Add(MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Linq.dll")));
        references.Add(MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "netstandard.dll")));
        references.Add(MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Core.dll")));

        return CSharpCompilation.Create(
            "TestAssembly",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithNullableContextOptions(NullableContextOptions.Enable));
    }

    private static EntityInfo CreateTestEntityInfo()
    {
        var columns = new List<ColumnInfo>
        {
            new("Id", "id", "int", "System.Int32", false, ColumnKind.PrimaryKey, null,
                new ColumnModifiers(isIdentity: true)),
            new("Name", "name", "string", "System.String", false, ColumnKind.Standard, null,
                new ColumnModifiers()),
            new("Balance", "balance", "Money", "TestApp.Money", false, ColumnKind.Standard, null,
                new ColumnModifiers(),
                customTypeMappingClass: MappingFqn, dbClrType: "decimal", dbReaderMethodName: "GetDecimal"),
            new("CreditLimit", "credit_limit", "Money", "TestApp.Money", false, ColumnKind.Standard, null,
                new ColumnModifiers(),
                customTypeMappingClass: MappingFqn, dbClrType: "decimal", dbReaderMethodName: "GetDecimal"),
            new("PlainDecimal", "plain_decimal", "decimal", "System.Decimal", false, ColumnKind.Standard, null,
                new ColumnModifiers()),
        };

        return new EntityInfo(
            "TestEntity",
            "TestEntitySchema",
            "TestApp",
            "test_entities",
            NamingStyleKind.SnakeCase,
            columns,
            new List<NavigationInfo>(),
            Array.Empty<IndexInfo>(),
            Location.None);
    }

    private static ExpressionTranslationResult TranslateLambda(
        string lambdaExpression,
        GenSqlDialect dialect = GenSqlDialect.PostgreSQL)
    {
        var compilation = CreateCompilation(lambdaExpression);
        var tree = compilation.SyntaxTrees.First();
        var root = tree.GetRoot();
        var semanticModel = compilation.GetSemanticModel(tree);

        var lambda = root.DescendantNodes()
            .OfType<SimpleLambdaExpressionSyntax>()
            .FirstOrDefault();

        if (lambda == null)
            throw new InvalidOperationException("No lambda expression found in source");

        var entityInfo = CreateTestEntityInfo();
        var paramName = lambda.Parameter.Identifier.Text;
        var context = new ExpressionTranslationContext(semanticModel, entityInfo, dialect, paramName);

        return ExpressionSyntaxTranslator.Translate(
            lambda.Body as ExpressionSyntax ?? throw new InvalidOperationException(),
            context);
    }

    #endregion

    #region Mapped Column Comparison – Parameter Gets CustomTypeMappingClass

    [Test]
    public void Translate_MappedColumnEquals_CapturedVar_SetsMapping()
    {
        var result = TranslateLambda("u => u.Balance == capturedMoney");

        Assert.That(result.IsSuccess, Is.True, result.ErrorMessage);
        Assert.That(result.Parameters.Count, Is.GreaterThan(0));
        Assert.That(result.Parameters[0].CustomTypeMappingClass, Is.EqualTo(MappingFqn),
            "Parameter compared against mapped column should have CustomTypeMappingClass set");
    }

    [Test]
    public void Translate_MappedColumnNotEquals_SetsMapping()
    {
        var result = TranslateLambda("u => u.Balance != capturedMoney");

        Assert.That(result.IsSuccess, Is.True, result.ErrorMessage);
        Assert.That(result.Parameters.Count, Is.GreaterThan(0));
        Assert.That(result.Parameters[0].CustomTypeMappingClass, Is.EqualTo(MappingFqn));
    }

    [Test]
    public void Translate_NonMappedColumn_DoesNotSetMapping()
    {
        var result = TranslateLambda("u => u.Name == capturedString");

        Assert.That(result.IsSuccess, Is.True, result.ErrorMessage);
        // Parameters from non-mapped column comparisons should not have mapping set
        foreach (var param in result.Parameters)
        {
            Assert.That(param.CustomTypeMappingClass, Is.Null,
                "Non-mapped column comparison should not set CustomTypeMappingClass");
        }
    }

    [Test]
    public void Translate_PlainDecimalColumn_DoesNotSetMapping()
    {
        var result = TranslateLambda("u => u.PlainDecimal == capturedDecimal");

        Assert.That(result.IsSuccess, Is.True, result.ErrorMessage);
        foreach (var param in result.Parameters)
        {
            Assert.That(param.CustomTypeMappingClass, Is.Null,
                "Plain decimal column should not set mapping even though mapped columns also use decimal");
        }
    }

    [Test]
    public void Translate_MixedCondition_OnlyMappedParamGetsMapping()
    {
        // u => u.Balance == capturedMoney && u.Name == capturedString
        var result = TranslateLambda("u => u.Balance == capturedMoney && u.Name == capturedString");

        Assert.That(result.IsSuccess, Is.True, result.ErrorMessage);
        Assert.That(result.Parameters.Count, Is.GreaterThanOrEqualTo(2));

        // Find which parameter has the mapping
        var mappedParams = result.Parameters.Where(p => p.CustomTypeMappingClass != null).ToList();
        var unmappedParams = result.Parameters.Where(p => p.CustomTypeMappingClass == null).ToList();

        Assert.That(mappedParams.Count, Is.GreaterThan(0),
            "At least one parameter should have mapping (Balance comparison)");
        Assert.That(unmappedParams.Count, Is.GreaterThan(0),
            "At least one parameter should NOT have mapping (Name comparison)");
    }

    [Test]
    public void Translate_IntColumnEquals_Literal_NoMapping()
    {
        var result = TranslateLambda("u => u.Id == 42");

        Assert.That(result.IsSuccess, Is.True, result.ErrorMessage);
        // Literal comparisons don't produce parameters, but verify no crash
        Assert.That(result.Sql, Does.Contain("\"id\" = 42"));
    }

    #endregion
}
