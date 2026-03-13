using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Quarry.Generators.Generation;
using Quarry.Generators.Models;
using Quarry.Generators.Translation;
using Quarry.Shared.Migration;

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
        var usageSites = new List<UsageSiteInfo>
        {
            CreateInsertUsageSite(new[]
            {
                CreateInsertColumn("AccountId", "int", false, customTypeMappingClass: null),
                CreateInsertColumn("Balance", "decimal", false, customTypeMappingClass: MappingFqn),
            })
        };

        var result = InterceptorCodeGenerator.GenerateInterceptorsFile(
            "AppDbContext", "TestApp", usageSites);

        Assert.That(result, Does.Contain("private static readonly TestApp.MoneyMapping _mapper_TestApp_MoneyMapping = new();"),
            "Should emit cached static readonly mapping instance");
    }

    [Test]
    public void GenerateInterceptorsFile_WithMultipleMappedColumns_EmitsSingleField()
    {
        var usageSites = new List<UsageSiteInfo>
        {
            CreateInsertUsageSite(new[]
            {
                CreateInsertColumn("Balance", "decimal", false, customTypeMappingClass: MappingFqn),
                CreateInsertColumn("CreditLimit", "decimal", false, customTypeMappingClass: MappingFqn),
            })
        };

        var result = InterceptorCodeGenerator.GenerateInterceptorsFile(
            "AppDbContext", "TestApp", usageSites);

        // Count occurrences of the field declaration
        var fieldDecl = "private static readonly TestApp.MoneyMapping _mapper_TestApp_MoneyMapping = new();";
        var count = CountOccurrences(result, fieldDecl);
        Assert.That(count, Is.EqualTo(1),
            "Should emit only one static field even when multiple columns use the same mapping");
    }

    [Test]
    public void GenerateInterceptorsFile_WithNoMappings_DoesNotEmitMappingField()
    {
        var usageSites = new List<UsageSiteInfo>
        {
            CreateInsertUsageSite(new[]
            {
                CreateInsertColumn("AccountId", "int", false, customTypeMappingClass: null),
                CreateInsertColumn("Name", "string", false, customTypeMappingClass: null),
            })
        };

        var result = InterceptorCodeGenerator.GenerateInterceptorsFile(
            "AppDbContext", "TestApp", usageSites);

        Assert.That(result, Does.Not.Contain("_mapper_"),
            "Should not emit any mapping fields when no columns use TypeMapping");
    }

    #endregion

    #region Layer 3: InterceptorCodeGenerator – Insert ToDb Wrapping

    [Test]
    public void GenerateInterceptorsFile_InsertMappedColumn_WrapsWithToDb()
    {
        var usageSites = new List<UsageSiteInfo>
        {
            CreateInsertUsageSite(new[]
            {
                CreateInsertColumn("Balance", "decimal", false, customTypeMappingClass: MappingFqn),
            })
        };

        var result = InterceptorCodeGenerator.GenerateInterceptorsFile(
            "AppDbContext", "TestApp", usageSites);

        Assert.That(result, Does.Contain("_mapper_TestApp_MoneyMapping.ToDb(entity.Balance)"),
            "Insert interceptor should wrap mapped column value with ToDb()");
    }

    [Test]
    public void GenerateInterceptorsFile_InsertNonMappedColumn_DoesNotWrapWithToDb()
    {
        var usageSites = new List<UsageSiteInfo>
        {
            CreateInsertUsageSite(new[]
            {
                CreateInsertColumn("AccountId", "int", false, customTypeMappingClass: null),
                CreateInsertColumn("Balance", "decimal", false, customTypeMappingClass: MappingFqn),
            })
        };

        var result = InterceptorCodeGenerator.GenerateInterceptorsFile(
            "AppDbContext", "TestApp", usageSites);

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

        var clauseInfo = ClauseInfo.Success(ClauseKind.Where, "\"Balance\" = @p0", parameters);
        var usageSites = new List<UsageSiteInfo>
        {
            CreateWhereUsageSite(clauseInfo)
        };

        var result = InterceptorCodeGenerator.GenerateInterceptorsFile(
            "AppDbContext", "TestApp", usageSites);

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

        var setClause = new SetClauseInfo("\"Balance\"", 0, parameters, MappingFqn);
        var usageSites = new List<UsageSiteInfo>
        {
            CreateSetUsageSite(setClause)
        };

        var result = InterceptorCodeGenerator.GenerateInterceptorsFile(
            "AppDbContext", "TestApp", usageSites);

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

        var setClause = new SetClauseInfo("\"AccountName\"", 0, parameters);
        var usageSites = new List<UsageSiteInfo>
        {
            CreateSetUsageSite(setClause)
        };

        var result = InterceptorCodeGenerator.GenerateInterceptorsFile(
            "AppDbContext", "TestApp", usageSites);

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

    private static EntityInfo CreateTestEntity(string name, ColumnInfo[] columns)
    {
        return new EntityInfo(
            entityName: name,
            schemaClassName: $"{name}Schema",
            schemaNamespace: "TestApp",
            tableName: name.ToLowerInvariant() + "s",
            namingStyle: NamingStyleKind.Exact,
            columns: columns,
            navigations: Array.Empty<NavigationInfo>(),
            indexes: Array.Empty<IndexInfo>(),
            location: Location.None);
    }

    private static ColumnInfo CreateColumn(string name, string clrType, bool isNullable, ColumnKind kind)
    {
        return new ColumnInfo(
            propertyName: name,
            columnName: name,
            clrType: clrType,
            fullClrType: clrType,
            isNullable: isNullable,
            kind: kind,
            referencedEntityName: null,
            modifiers: new ColumnModifiers());
    }

    private static ColumnInfo CreateMappedColumn(
        string name, string clrType, string fullClrType, bool isNullable,
        string mappingClass, string dbClrType, string dbReaderMethodName)
    {
        return new ColumnInfo(
            propertyName: name,
            columnName: name,
            clrType: clrType,
            fullClrType: fullClrType,
            isNullable: isNullable,
            kind: ColumnKind.Standard,
            referencedEntityName: null,
            modifiers: new ColumnModifiers(),
            customTypeMappingClass: mappingClass,
            dbClrType: dbClrType,
            dbReaderMethodName: dbReaderMethodName);
    }

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

    private static UsageSiteInfo CreateInsertUsageSite(InsertColumnInfo[] columns)
    {
        var insertInfo = new InsertInfo(
            columns: columns,
            identityColumnName: null,
            identityPropertyName: null,
            quotedIdentityColumnName: null);

        return new UsageSiteInfo(
            methodName: "Insert",
            filePath: "TestFile.cs",
            line: 10,
            column: 10,
            builderTypeName: "QueryBuilder<Account>",
            entityTypeName: "Account",
            isAnalyzable: true,
            kind: InterceptorKind.InsertExecuteNonQuery,
            invocationSyntax: SyntaxFactory.ParseExpression("test"),
            uniqueId: "insert_test",
            insertInfo: insertInfo,
            interceptableLocationData: "dGVzdGRhdGE=",
            interceptableLocationVersion: 1);
    }

    private static UsageSiteInfo CreateWhereUsageSite(ClauseInfo clauseInfo)
    {
        return new UsageSiteInfo(
            methodName: "Where",
            filePath: "TestFile.cs",
            line: 10,
            column: 10,
            builderTypeName: "QueryBuilder<Account, Account>",
            entityTypeName: "Account",
            isAnalyzable: true,
            kind: InterceptorKind.Where,
            invocationSyntax: SyntaxFactory.ParseExpression("test"),
            uniqueId: "where_test",
            resultTypeName: "Account",
            clauseInfo: clauseInfo,
            interceptableLocationData: "dGVzdGRhdGE=",
            interceptableLocationVersion: 1);
    }

    private static UsageSiteInfo CreateSetUsageSite(SetClauseInfo setClause)
    {
        return new UsageSiteInfo(
            methodName: "Set",
            filePath: "TestFile.cs",
            line: 10,
            column: 10,
            builderTypeName: "UpdateBuilder<Account>",
            entityTypeName: "Account",
            isAnalyzable: true,
            kind: InterceptorKind.Set,
            invocationSyntax: SyntaxFactory.ParseExpression("test"),
            uniqueId: "set_test",
            resultTypeName: "Account",
            clauseInfo: setClause,
            interceptableLocationData: "dGVzdGRhdGE=",
            interceptableLocationVersion: 1);
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
