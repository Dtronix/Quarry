using Quarry.Generators.Generation;
using Quarry.Generators.IR;
using Quarry.Generators.Models;
using Quarry.Generators.Translation;
using Quarry.Tests.Testing;

namespace Quarry.Tests;

/// <summary>
/// Layers 3 + 4: InterceptorCodeGenerator ToDb/FromDb wrapping and static caching tests.
/// </summary>
[TestFixture]
public class TypeMappingInterceptorTests
{
    private const string MappingFqn = "TestApp.MoneyMapping";

    #region Layer 3: InterceptorCodeGenerator – Static Field Caching

    [Test]
    public void GenerateInterceptorsFile_WithMappedInsertColumn_EmitsStaticMappingField()
    {
        var usageSites = new List<TranslatedCallSite>
        {
            CreateInsertUsageSite(new[]
            {
                CreateInsertColumn("AccountId", "int", false, customTypeMappingClass: null),
                CreateInsertColumn("Balance", "decimal", false, customTypeMappingClass: MappingFqn),
            })
        };

        var result = InterceptorCodeGenerator.GenerateInterceptorsFile(
            "AppDbContext", "TestApp", "test0000", usageSites);

        Assert.That(result, Does.Contain("private static readonly TestApp.MoneyMapping _mapper_TestApp_MoneyMapping = new();"),
            "Should emit cached static readonly mapping instance");
    }

    [Test]
    public void GenerateInterceptorsFile_WithMultipleMappedColumns_EmitsSingleField()
    {
        var usageSites = new List<TranslatedCallSite>
        {
            CreateInsertUsageSite(new[]
            {
                CreateInsertColumn("Balance", "decimal", false, customTypeMappingClass: MappingFqn),
                CreateInsertColumn("CreditLimit", "decimal", false, customTypeMappingClass: MappingFqn),
            })
        };

        var result = InterceptorCodeGenerator.GenerateInterceptorsFile(
            "AppDbContext", "TestApp", "test0000", usageSites);

        // Count occurrences of the field declaration
        var fieldDecl = "private static readonly TestApp.MoneyMapping _mapper_TestApp_MoneyMapping = new();";
        var count = CountOccurrences(result, fieldDecl);
        Assert.That(count, Is.EqualTo(1),
            "Should emit only one static field even when multiple columns use the same mapping");
    }

    [Test]
    public void GenerateInterceptorsFile_WithNoMappings_DoesNotEmitMappingField()
    {
        var usageSites = new List<TranslatedCallSite>
        {
            CreateInsertUsageSite(new[]
            {
                CreateInsertColumn("AccountId", "int", false, customTypeMappingClass: null),
                CreateInsertColumn("Name", "string", false, customTypeMappingClass: null),
            })
        };

        var result = InterceptorCodeGenerator.GenerateInterceptorsFile(
            "AppDbContext", "TestApp", "test0000", usageSites);

        Assert.That(result, Does.Not.Contain("_mapper_"),
            "Should not emit any mapping fields when no columns use TypeMapping");
    }

    #endregion

    #region Layer 3: InterceptorCodeGenerator – Insert ToDb Wrapping

    [Test]
    public void GenerateInterceptorsFile_InsertMappedColumn_WrapsWithToDb()
    {
        var usageSites = new List<TranslatedCallSite>
        {
            CreateInsertUsageSite(new[]
            {
                CreateInsertColumn("Balance", "decimal", false, customTypeMappingClass: MappingFqn),
            })
        };

        var result = InterceptorCodeGenerator.GenerateInterceptorsFile(
            "AppDbContext", "TestApp", "test0000", usageSites);

        Assert.That(result, Does.Contain("_mapper_TestApp_MoneyMapping.ToDb(entity.Balance)"),
            "Insert interceptor should wrap mapped column value with ToDb()");
    }

    [Test]
    public void GenerateInterceptorsFile_InsertNonMappedColumn_DoesNotWrapWithToDb()
    {
        var usageSites = new List<TranslatedCallSite>
        {
            CreateInsertUsageSite(new[]
            {
                CreateInsertColumn("AccountId", "int", false, customTypeMappingClass: null),
                CreateInsertColumn("Balance", "decimal", false, customTypeMappingClass: MappingFqn),
            })
        };

        var result = InterceptorCodeGenerator.GenerateInterceptorsFile(
            "AppDbContext", "TestApp", "test0000", usageSites);

        Assert.That(result, Does.Contain("entity.AccountId"),
            "Non-mapped column should use entity property directly");
        Assert.That(result, Does.Not.Contain("ToDb(entity.AccountId)"),
            "Non-mapped column should not be wrapped with ToDb()");
    }

    #endregion

    #region Layer 3: InterceptorCodeGenerator – Where/Set ToDb Wrapping

    [Test]
    public void GenerateInterceptorsFile_WhereMappedParam_WrapsWithToDb()
    {
        var parameters = new List<ParameterInfo>
        {
            new(0, "@p0", "decimal", "100m")
            {
                CustomTypeMappingClass = MappingFqn
            }
        };

        var clause = new TranslatedClause(
            ClauseKind.Where,
            new SqlRawExpr("\"Balance\" = @p0"),
            parameters);

        var usageSites = new List<TranslatedCallSite>
        {
            CreateWhereUsageSite(clause)
        };

        var result = InterceptorCodeGenerator.GenerateInterceptorsFile(
            "AppDbContext", "TestApp", "test0000", usageSites);

        Assert.That(result, Does.Contain("_mapper_TestApp_MoneyMapping.ToDb("),
            "Where interceptor should wrap mapped parameter with ToDb()");
    }

    [Test]
    public void GenerateInterceptorsFile_SetMappedColumn_WrapsWithToDb()
    {
        var parameters = new List<ParameterInfo>
        {
            new(0, "@p0", "decimal", "value")
        };

        var setAssignments = new List<SetActionAssignment>
        {
            new("\"Balance\"", "decimal", MappingFqn)
        };

        var clause = new TranslatedClause(
            ClauseKind.Set,
            new SqlRawExpr("\"Balance\" = @p0"),
            parameters,
            setAssignments: setAssignments);

        var usageSites = new List<TranslatedCallSite>
        {
            CreateSetUsageSite(clause)
        };

        var result = InterceptorCodeGenerator.GenerateInterceptorsFile(
            "AppDbContext", "TestApp", "test0000", usageSites);

        Assert.That(result, Does.Contain("_mapper_TestApp_MoneyMapping.ToDb(value)"),
            "Set interceptor should wrap value with ToDb() for mapped column");
    }

    [Test]
    public void GenerateInterceptorsFile_SetNonMappedColumn_DoesNotWrapWithToDb()
    {
        var parameters = new List<ParameterInfo>
        {
            new(0, "@p0", "decimal", "value")
        };

        var setAssignments = new List<SetActionAssignment>
        {
            new("\"AccountName\"", "decimal", null)
        };

        var clause = new TranslatedClause(
            ClauseKind.Set,
            new SqlRawExpr("\"AccountName\" = @p0"),
            parameters,
            setAssignments: setAssignments);

        var usageSites = new List<TranslatedCallSite>
        {
            CreateSetUsageSite(clause)
        };

        var result = InterceptorCodeGenerator.GenerateInterceptorsFile(
            "AppDbContext", "TestApp", "test0000", usageSites);

        Assert.That(result, Does.Not.Contain("ToDb(value)"),
            "Set interceptor should not wrap value for non-mapped column");
    }

    #endregion

    #region Layer 3: GetMappingFieldName

    [Test]
    public void GetMappingFieldName_DottedNamespace_ReplacesDotsWithUnderscores()
    {
        var result = InterceptorCodeGenerator.GetMappingFieldName("TestApp.MoneyMapping");
        Assert.That(result, Is.EqualTo("_mapper_TestApp_MoneyMapping"));
    }

    [Test]
    public void GetMappingFieldName_NestedClass_ReplacesPlusWithUnderscore()
    {
        var result = InterceptorCodeGenerator.GetMappingFieldName("TestApp.Outer+InnerMapping");
        Assert.That(result, Is.EqualTo("_mapper_TestApp_Outer_InnerMapping"));
    }

    #endregion

    #region Helper Methods

    private static InsertColumnInfo CreateInsertColumn(
        string name, string clrType, bool isNullable, string? customTypeMappingClass)
    {
        return new InsertColumnInfo(
            propertyName: name,
            columnName: name,
            quotedColumnName: $"\"{name}\"",
            clrType: clrType,
            fullClrType: clrType,
            isNullable: isNullable,
            isValueType: clrType != "string",
            customTypeMappingClass: customTypeMappingClass);
    }

    private static TranslatedCallSite CreateInsertUsageSite(InsertColumnInfo[] columns)
    {
        var insertInfo = new InsertInfo(
            columns: columns,
            identityColumnName: null,
            identityPropertyName: null,
            quotedIdentityColumnName: null);

        return new TestCallSiteBuilder()
            .WithMethodName("Insert")
            .WithKind(InterceptorKind.InsertExecuteNonQuery)
            .WithEntityType("Account")
            .WithBuilderKind(BuilderKind.Query)
            .WithBuilderTypeName("QueryBuilder<Account>")
            .WithUniqueId("insert_test")
            .WithContext("AppDbContext", "TestApp")
            .WithTable("accounts")
            .WithInsertInfo(insertInfo)
            .Build();
    }

    private static TranslatedCallSite CreateWhereUsageSite(TranslatedClause clause)
    {
        return new TestCallSiteBuilder()
            .WithMethodName("Where")
            .WithKind(InterceptorKind.Where)
            .WithEntityType("Account")
            .WithResultType("Account")
            .WithBuilderTypeName("QueryBuilder<Account, Account>")
            .WithUniqueId("where_test")
            .WithContext("AppDbContext", "TestApp")
            .WithTable("accounts")
            .WithClause(clause)
            .Build();
    }

    private static TranslatedCallSite CreateSetUsageSite(TranslatedClause clause)
    {
        return new TestCallSiteBuilder()
            .WithMethodName("Set")
            .WithKind(InterceptorKind.Set)
            .WithEntityType("Account")
            .WithResultType("Account")
            .WithBuilderKind(BuilderKind.Update)
            .WithBuilderTypeName("UpdateBuilder<Account>")
            .WithUniqueId("set_test")
            .WithContext("AppDbContext", "TestApp")
            .WithTable("accounts")
            .WithClause(clause)
            .Build();
    }

    private static int CountOccurrences(string source, string search)
    {
        int count = 0;
        int index = 0;
        while ((index = source.IndexOf(search, index, StringComparison.Ordinal)) != -1)
        {
            count++;
            index += search.Length;
        }
        return count;
    }

    #endregion
}
