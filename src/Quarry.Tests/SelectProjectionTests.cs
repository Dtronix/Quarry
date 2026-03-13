using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Quarry.Generators.Models;
using Quarry.Shared.Sql;
using Quarry.Generators.Projection;
using GenSqlDialect = Quarry.Generators.Sql.SqlDialect;

namespace Quarry.Tests;

/// <summary>
/// Tests for Select() projection analysis and reader code generation (Phase 6c).
/// </summary>
[TestFixture]
public class SelectProjectionTests
{
    private static readonly string QuarryCoreAssemblyPath = typeof(Schema).Assembly.Location;
    private static readonly string SystemRuntimeAssemblyPath = typeof(object).Assembly.Location;

    #region Test Infrastructure

    /// <summary>
    /// Creates a compilation with a Select() call for testing projection analysis.
    /// </summary>
    private static CSharpCompilation CreateSelectCompilation(string selectExpression)
    {
        var source = $@"
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace TestApp
{{
    public class User
    {{
        public int UserId {{ get; set; }}
        public string Name {{ get; set; }}
        public string? Email {{ get; set; }}
        public bool IsActive {{ get; set; }}
        public int Age {{ get; set; }}
        public decimal Balance {{ get; set; }}
        public DateTime CreatedAt {{ get; set; }}
        public int? DepartmentId {{ get; set; }}
    }}

    public class UserDto
    {{
        public int Id {{ get; set; }}
        public string UserName {{ get; set; }}
        public bool Active {{ get; set; }}
    }}

    // Mock QueryBuilder for testing
    public class QueryBuilder<T>
    {{
        public QueryBuilder<T, TResult> Select<TResult>(Expression<Func<T, TResult>> selector) => null!;
    }}

    public static class Sql
    {{
        public static int Count() => 0;
        public static int Count<T>(T value) => 0;
        public static TResult Sum<TResult>(TResult value) => default!;
        public static decimal Avg<T>(T value) => 0;
        public static T Min<T>(T value) => default!;
        public static T Max<T>(T value) => default!;
    }}

    public class TestClass
    {{
        public void Method()
        {{
            var builder = new QueryBuilder<User>();
            var result = builder.{selectExpression};
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
        references.Add(MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Linq.Expressions.dll")));
        references.Add(MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "netstandard.dll")));
        references.Add(MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Core.dll")));

        return CSharpCompilation.Create(
            "TestAssembly",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithNullableContextOptions(NullableContextOptions.Enable));
    }

    /// <summary>
    /// Analyzes a Select() expression and returns projection info.
    /// </summary>
    private static ProjectionInfo AnalyzeSelectExpression(
        string selectExpression,
        GenSqlDialect dialect = GenSqlDialect.PostgreSQL)
    {
        var compilation = CreateSelectCompilation(selectExpression);
        var tree = compilation.SyntaxTrees.First();
        var root = tree.GetRoot();
        var semanticModel = compilation.GetSemanticModel(tree);

        // Find the Select invocation
        var selectInvocation = root.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .FirstOrDefault(i => i.Expression is MemberAccessExpressionSyntax ma &&
                                  ma.Name.Identifier.Text == "Select");

        if (selectInvocation == null)
        {
            throw new InvalidOperationException("No Select() invocation found in source");
        }

        // Get the User type symbol
        var userTypeSymbol = compilation.GetTypeByMetadataName("TestApp.User");
        if (userTypeSymbol == null)
        {
            throw new InvalidOperationException("User type not found");
        }

        return ProjectionAnalyzer.AnalyzeFromTypeSymbol(
            selectInvocation,
            semanticModel,
            userTypeSymbol,
            dialect);
    }

    #endregion

    #region Entity Projection Tests

    [Test]
    public void EntityProjection_SelectFullEntity_ReturnsAllColumns()
    {
        var projection = AnalyzeSelectExpression("Select(u => u)");

        Assert.That(projection.IsOptimalPath, Is.True);
        Assert.That(projection.Kind, Is.EqualTo(ProjectionKind.Entity));
        Assert.That(projection.Columns.Count, Is.GreaterThan(0));

        // Verify we have the UserId column
        var userIdColumn = projection.Columns.FirstOrDefault(c => c.PropertyName == "UserId");
        Assert.That(userIdColumn, Is.Not.Null);
        Assert.That(userIdColumn!.ColumnName, Is.EqualTo("UserId"));
        Assert.That(userIdColumn.ClrType, Is.EqualTo("int"));
    }

    #endregion

    #region Anonymous Type Projection Tests (QRY014 - Not Supported)

    [Test]
    public void AnonymousType_SingleProperty_ReturnsFailedProjection()
    {
        var projection = AnalyzeSelectExpression("Select(u => new { u.Name })");

        Assert.That(projection.IsOptimalPath, Is.False);
        Assert.That(projection.FailureReason, Is.EqualTo(ProjectionFailureReason.AnonymousTypeNotSupported));
        Assert.That(projection.NonOptimalReason, Does.Contain("Anonymous type"));
    }

    [Test]
    public void AnonymousType_MultipleProperties_ReturnsFailedProjection()
    {
        var projection = AnalyzeSelectExpression("Select(u => new { u.UserId, u.Name, u.Email })");

        Assert.That(projection.IsOptimalPath, Is.False);
        Assert.That(projection.FailureReason, Is.EqualTo(ProjectionFailureReason.AnonymousTypeNotSupported));
    }

    [Test]
    public void AnonymousType_WithRenamedProperty_ReturnsFailedProjection()
    {
        var projection = AnalyzeSelectExpression("Select(u => new { Id = u.UserId, UserName = u.Name })");

        Assert.That(projection.IsOptimalPath, Is.False);
        Assert.That(projection.FailureReason, Is.EqualTo(ProjectionFailureReason.AnonymousTypeNotSupported));
    }

    [Test]
    public void AnonymousType_WithAggregate_ReturnsFailedProjection()
    {
        var projection = AnalyzeSelectExpression("Select(u => new { Total = Sql.Count(), MaxAge = Sql.Max(u.Age) })");

        Assert.That(projection.IsOptimalPath, Is.False);
        Assert.That(projection.FailureReason, Is.EqualTo(ProjectionFailureReason.AnonymousTypeNotSupported));
    }

    #endregion

    #region DTO Projection Tests

    [Test]
    public void DtoProjection_ObjectInitializer_ExtractsColumns()
    {
        var projection = AnalyzeSelectExpression("Select(u => new UserDto { Id = u.UserId, UserName = u.Name, Active = u.IsActive })");

        Assert.That(projection.IsOptimalPath, Is.True);
        Assert.That(projection.Kind, Is.EqualTo(ProjectionKind.Dto));
        Assert.That(projection.Columns.Count, Is.EqualTo(3));

        Assert.That(projection.Columns[0].PropertyName, Is.EqualTo("Id"));
        Assert.That(projection.Columns[0].ColumnName, Is.EqualTo("UserId"));

        Assert.That(projection.Columns[1].PropertyName, Is.EqualTo("UserName"));
        Assert.That(projection.Columns[1].ColumnName, Is.EqualTo("Name"));

        Assert.That(projection.Columns[2].PropertyName, Is.EqualTo("Active"));
        Assert.That(projection.Columns[2].ColumnName, Is.EqualTo("IsActive"));
    }

    #endregion

    #region Tuple Projection Tests

    [Test]
    public void TupleProjection_SimpleTuple_ExtractsColumns()
    {
        var projection = AnalyzeSelectExpression("Select(u => (u.UserId, u.Name))");

        Assert.That(projection.IsOptimalPath, Is.True);
        Assert.That(projection.Kind, Is.EqualTo(ProjectionKind.Tuple));
        Assert.That(projection.Columns.Count, Is.EqualTo(2));

        Assert.That(projection.Columns[0].PropertyName, Is.EqualTo("UserId"));
        Assert.That(projection.Columns[1].PropertyName, Is.EqualTo("Name"));
    }

    [Test]
    public void TupleProjection_WithAvg_ResolvesReturnType()
    {
        var projection = AnalyzeSelectExpression("Select(u => (u.Name, Sql.Avg(u.Balance)))");

        Assert.That(projection.IsOptimalPath, Is.True);
        Assert.That(projection.Kind, Is.EqualTo(ProjectionKind.Tuple));
        Assert.That(projection.Columns.Count, Is.EqualTo(2));
        Assert.That(projection.Columns[1].ClrType, Is.EqualTo("decimal"));
        // ResultTypeName should not contain "object"
        Assert.That(projection.ResultTypeName, Does.Not.Contain("object"));
    }

    [Test]
    public void TupleProjection_WithMin_ResolvesReturnType()
    {
        var projection = AnalyzeSelectExpression("Select(u => (u.Name, Sql.Min(u.Age)))");

        Assert.That(projection.IsOptimalPath, Is.True);
        Assert.That(projection.Columns[1].ClrType, Is.EqualTo("int"));
        Assert.That(projection.ResultTypeName, Does.Not.Contain("object"));
    }

    [Test]
    public void TupleProjection_WithMax_ResolvesReturnType()
    {
        var projection = AnalyzeSelectExpression("Select(u => (u.Name, Sql.Max(u.Balance)))");

        Assert.That(projection.IsOptimalPath, Is.True);
        Assert.That(projection.Columns[1].ClrType, Is.EqualTo("decimal"));
        Assert.That(projection.ResultTypeName, Does.Not.Contain("object"));
    }

    [Test]
    public void TupleProjection_WithMultipleAggregates_ResolvesAllTypes()
    {
        var projection = AnalyzeSelectExpression("Select(u => (u.Name, Sql.Avg(u.Balance), Sql.Count()))");

        Assert.That(projection.IsOptimalPath, Is.True);
        Assert.That(projection.Columns.Count, Is.EqualTo(3));
        Assert.That(projection.Columns[0].ClrType, Is.EqualTo("string"));
        Assert.That(projection.Columns[1].ClrType, Is.EqualTo("decimal"));
        Assert.That(projection.Columns[2].ClrType, Is.EqualTo("int"));
        Assert.That(projection.ResultTypeName, Does.Not.Contain("object"));
    }

    #endregion

    #region Single Column Projection Tests

    [Test]
    public void SingleColumn_DirectProperty_ExtractsColumn()
    {
        var projection = AnalyzeSelectExpression("Select(u => u.Name)");

        Assert.That(projection.IsOptimalPath, Is.True);
        Assert.That(projection.Kind, Is.EqualTo(ProjectionKind.SingleColumn));
        Assert.That(projection.Columns.Count, Is.EqualTo(1));

        Assert.That(projection.Columns[0].PropertyName, Is.EqualTo("Name"));
        Assert.That(projection.Columns[0].ColumnName, Is.EqualTo("Name"));
        Assert.That(projection.Columns[0].ClrType, Is.EqualTo("string"));
    }

    [Test]
    public void SingleColumn_IntProperty_CorrectType()
    {
        var projection = AnalyzeSelectExpression("Select(u => u.Age)");

        Assert.That(projection.Columns[0].ClrType, Is.EqualTo("int"));
        Assert.That(projection.Columns[0].IsNullable, Is.False);
    }

    [Test]
    public void SingleColumn_NullableIntProperty_CorrectType()
    {
        var projection = AnalyzeSelectExpression("Select(u => u.DepartmentId)");

        Assert.That(projection.Columns[0].ClrType, Is.EqualTo("int"));
        Assert.That(projection.Columns[0].IsNullable, Is.True);
    }

    #endregion

    #region Single Column Result Type Inference Tests (Phase 1 - Fix for Bug 1)

    [Test]
    public void SingleColumn_IntProperty_ResultTypeIsValid()
    {
        var projection = AnalyzeSelectExpression("Select(u => u.UserId)");

        Assert.That(projection.ResultTypeName, Is.Not.EqualTo("?"));
        Assert.That(projection.ResultTypeName, Does.Not.Contain("?"));
        Assert.That(projection.ResultTypeName, Does.Contain("int").Or.Contain("Int32"));
    }

    [Test]
    public void SingleColumn_StringProperty_ResultTypeIsValid()
    {
        var projection = AnalyzeSelectExpression("Select(u => u.Name)");

        Assert.That(projection.ResultTypeName, Is.Not.EqualTo("?"));
        Assert.That(projection.ResultTypeName, Does.Not.Contain("?"));
        Assert.That(projection.ResultTypeName, Does.Contain("string").Or.Contain("String"));
    }

    [Test]
    public void SingleColumn_NullableStringProperty_ResultTypeIsValid()
    {
        var projection = AnalyzeSelectExpression("Select(u => u.Email)");

        Assert.That(projection.ResultTypeName, Is.Not.EqualTo("?"));
        // Nullable string may contain '?' in the type, but should not be just "?"
        Assert.That(projection.ResultTypeName.Length, Is.GreaterThan(1));
    }

    [Test]
    public void SingleColumn_BoolProperty_ResultTypeIsValid()
    {
        var projection = AnalyzeSelectExpression("Select(u => u.IsActive)");

        Assert.That(projection.ResultTypeName, Is.Not.EqualTo("?"));
        Assert.That(projection.ResultTypeName, Does.Not.Contain("?"));
        Assert.That(projection.ResultTypeName, Does.Contain("bool").Or.Contain("Boolean"));
    }

    [Test]
    public void SingleColumn_DecimalProperty_ResultTypeIsValid()
    {
        var projection = AnalyzeSelectExpression("Select(u => u.Balance)");

        Assert.That(projection.ResultTypeName, Is.Not.EqualTo("?"));
        Assert.That(projection.ResultTypeName, Does.Not.Contain("?"));
        Assert.That(projection.ResultTypeName, Does.Contain("decimal").Or.Contain("Decimal"));
    }

    [Test]
    public void SingleColumn_DateTimeProperty_ResultTypeIsValid()
    {
        var projection = AnalyzeSelectExpression("Select(u => u.CreatedAt)");

        Assert.That(projection.ResultTypeName, Is.Not.EqualTo("?"));
        Assert.That(projection.ResultTypeName, Does.Not.Contain("?"));
        Assert.That(projection.ResultTypeName, Does.Contain("DateTime"));
    }

    [Test]
    public void SingleColumn_NullableIntProperty_ResultTypeIsValid()
    {
        var projection = AnalyzeSelectExpression("Select(u => u.DepartmentId)");

        Assert.That(projection.ResultTypeName, Is.Not.EqualTo("?"));
        // Nullable int may contain '?' in the type (int?), but should not be just "?"
        Assert.That(projection.ResultTypeName.Length, Is.GreaterThan(1));
    }

    #endregion

    #region Aggregate Function Tests

    [Test]
    public void AggregateFunction_Count_GeneratesCorrectSQL()
    {
        var projection = AnalyzeSelectExpression("Select(u => Sql.Count())");

        Assert.That(projection.IsOptimalPath, Is.True);
        Assert.That(projection.Columns.Count, Is.EqualTo(1));

        var column = projection.Columns[0];
        Assert.That(column.SqlExpression, Is.EqualTo("COUNT(*)"));
        Assert.That(column.IsAggregateFunction, Is.True);
    }

    [Test]
    public void AggregateFunction_CountColumn_GeneratesCorrectSQL()
    {
        var projection = AnalyzeSelectExpression("Select(u => Sql.Count(u.Email))");

        Assert.That(projection.Columns[0].SqlExpression, Does.Contain("COUNT"));
        Assert.That(projection.Columns[0].IsAggregateFunction, Is.True);
    }

    [Test]
    public void AggregateFunction_Sum_GeneratesCorrectSQL()
    {
        var projection = AnalyzeSelectExpression("Select(u => Sql.Sum(u.Balance))");

        Assert.That(projection.Columns[0].SqlExpression, Does.Contain("SUM"));
        Assert.That(projection.Columns[0].IsAggregateFunction, Is.True);
    }

    // Note: Anonymous type aggregate test removed - anonymous types are not supported (QRY014)
    // See AnonymousType_WithAggregate_ReturnsFailedProjection test instead

    #endregion

    #region ReaderCodeGenerator Tests

    [Test]
    public void GenerateColumnList_SingleColumn_GeneratesQuotedColumn()
    {
        // Use single column projection instead of anonymous type
        var projection = AnalyzeSelectExpression("Select(u => u.Name)");
        var columnList = ReaderCodeGenerator.GenerateColumnList(projection, GenSqlDialect.PostgreSQL);

        Assert.That(columnList, Does.Contain("\"Name\""));
    }

    [Test]
    public void GenerateColumnList_MultipleColumns_GeneratesCommaSeparated()
    {
        // Use tuple projection instead of anonymous type
        var projection = AnalyzeSelectExpression("Select(u => (u.UserId, u.Name))");
        var columnList = ReaderCodeGenerator.GenerateColumnList(projection, GenSqlDialect.PostgreSQL);

        Assert.That(columnList, Does.Contain("\"UserId\""));
        Assert.That(columnList, Does.Contain("\"Name\""));
        Assert.That(columnList, Does.Contain(", "));
    }

    [Test]
    public void GenerateColumnList_MySQLDialect_UsesBackticks()
    {
        // Use single column projection instead of anonymous type
        var projection = AnalyzeSelectExpression("Select(u => u.Name)");
        var columnList = ReaderCodeGenerator.GenerateColumnList(projection, GenSqlDialect.MySQL);

        Assert.That(columnList, Does.Contain("`Name`"));
    }

    [Test]
    public void GenerateColumnList_SqlServerDialect_UsesBrackets()
    {
        // Use single column projection instead of anonymous type
        var projection = AnalyzeSelectExpression("Select(u => u.Name)");
        var columnList = ReaderCodeGenerator.GenerateColumnList(projection, GenSqlDialect.SqlServer);

        Assert.That(columnList, Does.Contain("[Name]"));
    }

    [Test]
    public void GenerateColumnList_WithAggregate_GeneratesSQL()
    {
        // Single aggregate - no alias since it's a direct aggregate result
        var projection = AnalyzeSelectExpression("Select(u => Sql.Count())");
        var columnList = ReaderCodeGenerator.GenerateColumnList(projection, GenSqlDialect.PostgreSQL);

        Assert.That(columnList, Does.Contain("COUNT(*)"));
    }

    [Test]
    public void GenerateColumnNamesArray_WithAggregate_EmitsSqlExpression()
    {
        // Regression: parameterless Sql.Count() was emitting empty string instead of COUNT(*)
        var projection = AnalyzeSelectExpression("Select(u => Sql.Count())");
        var columnNames = ReaderCodeGenerator.GenerateColumnNamesArray(projection);

        Assert.That(columnNames, Does.Contain("COUNT(*)"));
        Assert.That(columnNames, Does.Not.Contain("\"\""));
    }

    [Test]
    public void GenerateColumnNamesArray_WithColumnAggregate_EmitsSqlExpression()
    {
        var projection = AnalyzeSelectExpression("Select(u => Sql.Sum(u.Balance))");
        var columnNames = ReaderCodeGenerator.GenerateColumnNamesArray(projection);

        Assert.That(columnNames, Does.Contain("SUM"));
    }

    [Test]
    public void GenerateReaderDelegate_DtoProjection_GeneratesCorrectCode()
    {
        // Use DTO projection instead of anonymous type
        var projection = AnalyzeSelectExpression("Select(u => new UserDto { Id = u.UserId, UserName = u.Name, Active = u.IsActive })");
        var readerCode = ReaderCodeGenerator.GenerateReaderDelegate(projection, "User");

        Assert.That(readerCode, Does.Contain("static (DbDataReader r)"));
        Assert.That(readerCode, Does.Contain("Id = "));
        Assert.That(readerCode, Does.Contain("UserName = "));
        Assert.That(readerCode, Does.Contain("GetInt32"));
        Assert.That(readerCode, Does.Contain("GetString"));
    }

    [Test]
    public void GenerateReaderDelegate_NullableColumn_IncludesNullCheck()
    {
        // Use single column projection for nullable column
        var projection = AnalyzeSelectExpression("Select(u => u.Email)");
        var readerCode = ReaderCodeGenerator.GenerateReaderDelegate(projection, "User");

        Assert.That(readerCode, Does.Contain("IsDBNull"));
    }

    [Test]
    public void GenerateReaderDelegate_Tuple_GeneratesCorrectCode()
    {
        var projection = AnalyzeSelectExpression("Select(u => (u.UserId, u.Name))");
        var readerCode = ReaderCodeGenerator.GenerateReaderDelegate(projection, "User");

        Assert.That(readerCode, Does.Contain("static (DbDataReader r) =>"));
        Assert.That(readerCode, Does.Contain("("));
    }

    [Test]
    public void GenerateReaderDelegate_SingleColumn_GeneratesSimpleReader()
    {
        var projection = AnalyzeSelectExpression("Select(u => u.Age)");
        var readerCode = ReaderCodeGenerator.GenerateReaderDelegate(projection, "User");

        Assert.That(readerCode, Does.Contain("GetInt32(0)"));
    }

    [Test]
    public void GenerateColumnNamesArray_GeneratesCorrectArray()
    {
        // Use tuple projection instead of anonymous type
        var projection = AnalyzeSelectExpression("Select(u => (u.UserId, u.Name))");
        var arrayCode = ReaderCodeGenerator.GenerateColumnNamesArray(projection);

        Assert.That(arrayCode, Does.Contain("new[] { "));
        Assert.That(arrayCode, Does.Contain("\"UserId\""));
        Assert.That(arrayCode, Does.Contain("\"Name\""));
    }

    #endregion

    #region Type Mapping Tests

    [Test]
    public void ProjectedColumn_BoolType_MapsToBoolean()
    {
        var projection = AnalyzeSelectExpression("Select(u => u.IsActive)");

        Assert.That(projection.Columns[0].ClrType, Is.EqualTo("bool"));
    }

    [Test]
    public void ProjectedColumn_DecimalType_MapsToDecimal()
    {
        var projection = AnalyzeSelectExpression("Select(u => u.Balance)");

        Assert.That(projection.Columns[0].ClrType, Is.EqualTo("decimal"));
    }

    [Test]
    public void ProjectedColumn_DateTimeType_MapsToDateTime()
    {
        var projection = AnalyzeSelectExpression("Select(u => u.CreatedAt)");

        Assert.That(projection.Columns[0].ClrType, Is.EqualTo("DateTime"));
    }

    #endregion

    #region Edge Cases

    [Test]
    public void NoLambdaArgument_ReturnsFailedProjection()
    {
        // This tests the failure case - can't actually compile without argument
        // but we verify our error handling works
        var projection = ProjectionInfo.CreateFailed("object", "Test error");

        Assert.That(projection.IsOptimalPath, Is.False);
        Assert.That(projection.NonOptimalReason, Is.EqualTo("Test error"));
    }

    [Test]
    public void TupleProjection_OrdinalAssignment_IsSequential()
    {
        // Use tuple projection instead of anonymous type
        var projection = AnalyzeSelectExpression("Select(u => (u.UserId, u.Name, u.Email))");

        for (int i = 0; i < projection.Columns.Count; i++)
        {
            Assert.That(projection.Columns[i].Ordinal, Is.EqualTo(i));
        }
    }

    #endregion
}
