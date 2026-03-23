using Microsoft.CodeAnalysis;
using Quarry.Generators.Generation;
using Quarry.Generators.IR;
using Quarry.Generators.Models;
using Quarry.Shared.Sql;
using Quarry.Generators.Projection;
using Quarry.Shared.Migration;
using Quarry.Tests.Testing;
using GenSqlDialect = Quarry.Generators.Sql.SqlDialect;

namespace Quarry.Tests;

/// <summary>
/// Tests for execution method interceptor generation (Phase 6e).
/// </summary>
[TestFixture]
public class ExecutionInterceptorTests
{

    #region InterceptorCodeGenerator Execution Tests

    [Test]
    public void GenerateInterceptorsFile_WithExecuteFetchAll_SkipsInterceptor()
    {
        // Execution interceptors are intentionally skipped — the built-in execution methods
        // work correctly because the Select interceptor provides the reader delegate.
        var usageSites = new List<TranslatedCallSite>
        {
            CreateExecutionUsageSite(InterceptorKind.ExecuteFetchAll, "User", "User")
        };

        var result = InterceptorCodeGenerator.GenerateInterceptorsFile(
            "AppDbContext",
            "TestApp",
            "test0000",
            usageSites);

        Assert.That(result, Does.Contain("Standalone Interceptors"));
        Assert.That(result, Does.Not.Contain("async Task<List<User>>"));
    }

    [Test]
    public void GenerateInterceptorsFile_WithExecuteFetchFirst_SkipsInterceptor()
    {
        var usageSites = new List<TranslatedCallSite>
        {
            CreateExecutionUsageSite(InterceptorKind.ExecuteFetchFirst, "User", "User")
        };

        var result = InterceptorCodeGenerator.GenerateInterceptorsFile(
            "AppDbContext",
            "TestApp",
            "test0000",
            usageSites);

        Assert.That(result, Does.Not.Contain("async Task<User>"));
    }

    [Test]
    public void GenerateInterceptorsFile_WithExecuteFetchFirstOrDefault_SkipsInterceptor()
    {
        var usageSites = new List<TranslatedCallSite>
        {
            CreateExecutionUsageSite(InterceptorKind.ExecuteFetchFirstOrDefault, "User", "User")
        };

        var result = InterceptorCodeGenerator.GenerateInterceptorsFile(
            "AppDbContext",
            "TestApp",
            "test0000",
            usageSites);

        Assert.That(result, Does.Not.Contain("async Task<User?>"));
    }

    [Test]
    public void GenerateInterceptorsFile_WithExecuteFetchSingle_SkipsInterceptor()
    {
        var usageSites = new List<TranslatedCallSite>
        {
            CreateExecutionUsageSite(InterceptorKind.ExecuteFetchSingle, "User", "User")
        };

        var result = InterceptorCodeGenerator.GenerateInterceptorsFile(
            "AppDbContext",
            "TestApp",
            "test0000",
            usageSites);

        Assert.That(result, Does.Not.Contain("ExecuteFetchSingleAsyncFallback"));
    }

    [Test]
    public void GenerateInterceptorsFile_WithExecuteScalar_SkipsInterceptor()
    {
        var usageSites = new List<TranslatedCallSite>
        {
            CreateExecutionUsageSite(InterceptorKind.ExecuteScalar, "User", "User")
        };

        var result = InterceptorCodeGenerator.GenerateInterceptorsFile(
            "AppDbContext",
            "TestApp",
            "test0000",
            usageSites);

        Assert.That(result, Does.Not.Contain("ExecuteScalarCoreAsync"));
    }

    [Test]
    public void GenerateInterceptorsFile_WithExecuteNonQuery_SkipsInterceptor()
    {
        var usageSites = new List<TranslatedCallSite>
        {
            CreateExecutionUsageSite(InterceptorKind.ExecuteNonQuery, "User", "User")
        };

        var result = InterceptorCodeGenerator.GenerateInterceptorsFile(
            "AppDbContext",
            "TestApp",
            "test0000",
            usageSites);

        Assert.That(result, Does.Not.Contain("ExecuteNonQueryCoreAsync"));
    }

    [Test]
    public void GenerateInterceptorsFile_WithToAsyncEnumerable_SkipsInterceptor()
    {
        var usageSites = new List<TranslatedCallSite>
        {
            CreateExecutionUsageSite(InterceptorKind.ToAsyncEnumerable, "User", "User")
        };

        var result = InterceptorCodeGenerator.GenerateInterceptorsFile(
            "AppDbContext",
            "TestApp",
            "test0000",
            usageSites);

        Assert.That(result, Does.Not.Contain("IAsyncEnumerable<User>"));
    }

    [Test]
    public void GenerateInterceptorsFile_WithOptimalProjection_SkipsExecutionInterceptor()
    {
        // Even with optimal projection, execution interceptors are skipped.
        // The Select interceptor provides the reader; the built-in ExecuteFetchAllAsync uses it.
        var projection = CreateProjectionInfo(
            ProjectionKind.Entity,
            "User",
            new[]
            {
                CreateProjectedColumn("UserId", "UserId", "int", 0),
                CreateProjectedColumn("Name", "Name", "string", 1)
            });

        var usageSites = new List<TranslatedCallSite>
        {
            CreateExecutionUsageSite(InterceptorKind.ExecuteFetchAll, "User", "User", projection)
        };

        var result = InterceptorCodeGenerator.GenerateInterceptorsFile(
            "AppDbContext",
            "TestApp",
            "test0000",
            usageSites);

        Assert.That(result, Does.Not.Contain("ExecuteWithReaderAsync"));
    }

    [Test]
    public void GenerateInterceptorsFile_WithoutProjection_SkipsExecutionInterceptor()
    {
        var usageSites = new List<TranslatedCallSite>
        {
            CreateExecutionUsageSite(InterceptorKind.ExecuteFetchAll, "User", "User", null)
        };

        var result = InterceptorCodeGenerator.GenerateInterceptorsFile(
            "AppDbContext",
            "TestApp",
            "test0000",
            usageSites);

        Assert.That(result, Does.Not.Contain("ExecuteFetchAllAsyncFallback"));
    }

    [Test]
    public void GenerateInterceptorsFile_IncludesRequiredUsings()
    {
        // Arrange
        var usageSites = new List<TranslatedCallSite>
        {
            CreateExecutionUsageSite(InterceptorKind.ExecuteFetchAll, "User", "User")
        };

        // Act
        var result = InterceptorCodeGenerator.GenerateInterceptorsFile(
            "AppDbContext",
            "TestApp",
            "test0000",
            usageSites);

        // Assert
        Assert.That(result, Does.Contain("using System;"));
        Assert.That(result, Does.Contain("using System.Data.Common;"));
        Assert.That(result, Does.Contain("using System.Runtime.CompilerServices;"));
    }

    [Test]
    public void GenerateInterceptorsFile_IncludesInterceptsLocationAttributeDefinition()
    {
        // The attribute definition is always included in the file even when execution interceptors are skipped.
        var usageSites = new List<TranslatedCallSite>
        {
            CreateExecutionUsageSite(InterceptorKind.ExecuteFetchAll, "User", "User")
        };

        var result = InterceptorCodeGenerator.GenerateInterceptorsFile(
            "AppDbContext",
            "TestApp",
            "test0000",
            usageSites);

        Assert.That(result, Does.Contain("sealed class InterceptsLocationAttribute"));
        // No [InterceptsLocation] usages since execution interceptors are skipped
        Assert.That(result, Does.Not.Contain("[InterceptsLocation(1,"));
    }

    #endregion

    #region ExecutionInfo Model Tests


    [Test]
    public void OrderByClause_StoresPropertiesCorrectly()
    {
        // Act
        var ascending = new OrderByClause("\"name\"", isDescending: false);
        var descending = new OrderByClause("\"created_at\"", isDescending: true);

        // Assert
        Assert.That(ascending.ColumnSql, Is.EqualTo("\"name\""));
        Assert.That(ascending.IsDescending, Is.False);
        Assert.That(descending.ColumnSql, Is.EqualTo("\"created_at\""));
        Assert.That(descending.IsDescending, Is.True);
    }

    [Test]
    public void JoinClause_StoresPropertiesCorrectly()
    {
        // Act
        var joinClause = new JoinClause(
            JoinKind.Left,
            "Order",
            "orders",
            "\"users\".\"id\" = \"orders\".\"user_id\"");

        // Assert
        Assert.That(joinClause.Kind, Is.EqualTo(JoinKind.Left));
        Assert.That(joinClause.JoinedEntityName, Is.EqualTo("Order"));
        Assert.That(joinClause.JoinedTableName, Is.EqualTo("orders"));
        Assert.That(joinClause.OnConditionSql, Does.Contain("user_id"));
    }

    #endregion

    #region InsertInfo Initializer-Aware Column Selection Tests

    [Test]
    public void InsertInfo_WithInitializedPropertyNames_IncludesOnlySpecifiedColumns()
    {
        // Simulates: db.Users().Insert(new User { UserName = "x", IsActive = true }).ExecuteNonQueryAsync()
        // — inline initializer, generator extracts property names from syntax
        var entity = CreateTestEntity("User", new[]
        {
            CreateColumn("UserId", "int", false, ColumnKind.PrimaryKey, isIdentity: true),
            CreateColumn("UserName", "string", false, ColumnKind.Standard),
            CreateColumn("Email", "string", true, ColumnKind.Standard),
            CreateColumn("IsActive", "bool", false, ColumnKind.Standard),
            CreateColumn("CreatedAt", "DateTime", false, ColumnKind.Standard)
        });

        var initializedProps = new HashSet<string>(StringComparer.Ordinal) { "UserName", "IsActive" };
        var insertInfo = InsertInfo.FromEntityInfo(entity, GenSqlDialect.SQLite, initializedProps);

        var columnNames = insertInfo.Columns.Select(c => c.PropertyName).ToList();
        Assert.That(columnNames, Is.EquivalentTo(new[] { "UserName", "IsActive" }));
        Assert.That(columnNames, Does.Not.Contain("Email"));
        Assert.That(columnNames, Does.Not.Contain("CreatedAt"));
    }

    [Test]
    public void InsertInfo_WithNullInitializedPropertyNames_IncludesAllNonIdentityColumns()
    {
        // Simulates: var user = new User { UserName = "x" }; db.Users().Insert(user).ExecuteNonQueryAsync()
        // — variable reference, generator cannot extract property names, passes null
        var entity = CreateTestEntity("User", new[]
        {
            CreateColumn("UserId", "int", false, ColumnKind.PrimaryKey, isIdentity: true),
            CreateColumn("UserName", "string", false, ColumnKind.Standard),
            CreateColumn("Email", "string", true, ColumnKind.Standard),
            CreateColumn("IsActive", "bool", false, ColumnKind.Standard),
            CreateColumn("CreatedAt", "DateTime", false, ColumnKind.Standard)
        });

        var insertInfo = InsertInfo.FromEntityInfo(entity, GenSqlDialect.SQLite, initializedPropertyNames: null);

        var columnNames = insertInfo.Columns.Select(c => c.PropertyName).ToList();
        Assert.That(columnNames, Is.EquivalentTo(new[] { "UserName", "Email", "IsActive", "CreatedAt" }));
    }

    [Test]
    public void InsertInfo_AlwaysExcludesIdentityColumns()
    {
        var entity = CreateTestEntity("User", new[]
        {
            CreateColumn("UserId", "int", false, ColumnKind.PrimaryKey, isIdentity: true),
            CreateColumn("UserName", "string", false, ColumnKind.Standard)
        });

        // Even if identity is in the initialized set, it should be excluded from columns
        // and tracked separately for RETURNING clause
        var initializedProps = new HashSet<string>(StringComparer.Ordinal) { "UserId", "UserName" };
        var insertInfo = InsertInfo.FromEntityInfo(entity, GenSqlDialect.SQLite, initializedProps);

        var columnNames = insertInfo.Columns.Select(c => c.PropertyName).ToList();
        Assert.That(columnNames, Is.EquivalentTo(new[] { "UserName" }));
        Assert.That(insertInfo.IdentityPropertyName, Is.EqualTo("UserId"));
    }

    [Test]
    public void InsertInfo_AlwaysExcludesComputedColumns()
    {
        var entity = CreateTestEntity("User", new[]
        {
            CreateColumn("UserId", "int", false, ColumnKind.PrimaryKey, isIdentity: true),
            CreateColumn("UserName", "string", false, ColumnKind.Standard),
            CreateColumn("FullName", "string", false, ColumnKind.Standard, isComputed: true)
        });

        // null = variable path (all columns), but computed should still be excluded
        var insertInfo = InsertInfo.FromEntityInfo(entity, GenSqlDialect.SQLite, initializedPropertyNames: null);

        var columnNames = insertInfo.Columns.Select(c => c.PropertyName).ToList();
        Assert.That(columnNames, Is.EquivalentTo(new[] { "UserName" }));
        Assert.That(columnNames, Does.Not.Contain("FullName"));
    }

    [Test]
    public void InsertInfo_InlineVsVariable_ProduceDifferentColumnLists()
    {
        // The core distinction: same entity, same properties set at runtime,
        // but different column lists depending on whether the generator could
        // see the initializer syntax.
        var entity = CreateTestEntity("User", new[]
        {
            CreateColumn("UserId", "int", false, ColumnKind.PrimaryKey, isIdentity: true),
            CreateColumn("UserName", "string", false, ColumnKind.Standard),
            CreateColumn("Email", "string", true, ColumnKind.Standard),
            CreateColumn("IsActive", "bool", false, ColumnKind.Standard)
        });

        // Inline: new User { UserName = "x" } — only UserName
        var inlineProps = new HashSet<string>(StringComparer.Ordinal) { "UserName" };
        var inlineInfo = InsertInfo.FromEntityInfo(entity, GenSqlDialect.SQLite, inlineProps);

        // Variable: var u = new User { UserName = "x" }; Insert(u) — all columns
        var variableInfo = InsertInfo.FromEntityInfo(entity, GenSqlDialect.SQLite, null);

        Assert.That(inlineInfo.Columns, Has.Count.EqualTo(1));
        Assert.That(variableInfo.Columns, Has.Count.EqualTo(3));
        Assert.That(inlineInfo.Columns[0].PropertyName, Is.EqualTo("UserName"));
    }

    [Test]
    public void InsertInfo_PropagatesSensitiveFlag()
    {
        var entity = CreateTestEntity("Widget", new[]
        {
            CreateColumn("WidgetId", "Guid", false, ColumnKind.PrimaryKey),
            CreateColumn("WidgetName", "string", false, ColumnKind.Standard),
            CreateColumnWithSensitive("Secret", "string", false, ColumnKind.Standard)
        });

        var insertInfo = InsertInfo.FromEntityInfo(entity, GenSqlDialect.SQLite, initializedPropertyNames: null);

        Assert.That(insertInfo.Columns, Has.Count.EqualTo(3));

        var widgetNameCol = insertInfo.Columns.First(c => c.PropertyName == "WidgetName");
        Assert.That(widgetNameCol.IsSensitive, Is.False);

        var secretCol = insertInfo.Columns.First(c => c.PropertyName == "Secret");
        Assert.That(secretCol.IsSensitive, Is.True);
    }

    [Test]
    public void InterceptorCodeGen_EmitsSensitiveFlag_ForInsert()
    {
        var entity = CreateTestEntity("Widget", new[]
        {
            CreateColumn("WidgetId", "Guid", false, ColumnKind.PrimaryKey),
            CreateColumn("WidgetName", "string", false, ColumnKind.Standard),
            CreateColumnWithSensitive("Secret", "string", false, ColumnKind.Standard)
        });

        var insertInfo = InsertInfo.FromEntityInfo(entity, GenSqlDialect.SQLite, initializedPropertyNames: null);

        var site = new TestCallSiteBuilder()
            .WithMethodName("ExecuteNonQueryAsync")
            .WithKind(InterceptorKind.InsertExecuteNonQuery)
            .WithEntityType("Widget")
            .WithBuilderKind(BuilderKind.Query)
            .WithBuilderTypeName("InsertBuilder<Widget>")
            .WithUniqueId("test_insert_sensitive")
            .WithInsertInfo(insertInfo)
            .Build();

        var result = InterceptorCodeGenerator.GenerateInterceptorsFile(
            "AppDbContext", "TestApp", "test0000", new List<TranslatedCallSite> { site });

        // Sensitive column should have isSensitive: true
        Assert.That(result, Does.Contain("isSensitive: true"));
        // Non-sensitive columns should NOT have isSensitive
        Assert.That(result, Does.Contain("__b.AddParameter(entity.WidgetName)"));
        Assert.That(result, Does.Contain("__b.AddParameter(entity.Secret, isSensitive: true)"));
    }

    [Test]
    public void InterceptorCodeGen_EmitsSensitiveFlag_ForUpdateSetPoco()
    {
        var entity = CreateTestEntity("Widget", new[]
        {
            CreateColumn("WidgetId", "Guid", false, ColumnKind.PrimaryKey),
            CreateColumn("WidgetName", "string", false, ColumnKind.Standard),
            CreateColumnWithSensitive("Secret", "string", false, ColumnKind.Standard)
        });

        var updateInfo = InsertInfo.FromEntityInfo(entity, GenSqlDialect.SQLite, initializedPropertyNames: null);

        var site = new TestCallSiteBuilder()
            .WithMethodName("Set")
            .WithKind(InterceptorKind.UpdateSetPoco)
            .WithEntityType("Widget")
            .WithBuilderKind(BuilderKind.Update)
            .WithBuilderTypeName("UpdateBuilder<Widget>")
            .WithUniqueId("test_update_sensitive")
            .WithUpdateInfo(updateInfo)
            .Build();

        var result = InterceptorCodeGenerator.GenerateInterceptorsFile(
            "AppDbContext", "TestApp", "test0000", new List<TranslatedCallSite> { site });

        // Sensitive column's AddSetClause should have isSensitive: true
        Assert.That(result, Does.Contain("isSensitive: true"));
    }

    #endregion

    #region Ref<> FK Column Handling Tests


    [Test]
    public void EntityReader_WithForeignKeyColumn_WrapsInRefConstructor()
    {
        // Arrange: ProjectedColumns including FK column
        var columns = new[]
        {
            CreateProjectedColumn("OrderId", "OrderId", "int", 0),
            CreateForeignKeyProjectedColumn("UserId", "UserId", "int", 1, "User"),
            CreateProjectedColumn("Total", "Total", "decimal", 2)
        };

        var projection = CreateProjectionInfo(ProjectionKind.Entity, "Order", columns);

        // Act
        var readerCode = ReaderCodeGenerator.GenerateReaderDelegate(projection, "Order");

        // Assert: FK column wrapped in new EntityRef<User, int>(...)
        Assert.That(readerCode, Does.Contain("UserId = new EntityRef<User, int>(r.GetInt32(1))"));
        // Non-FK columns remain as plain reader calls (no EntityRef<> wrapping)
        Assert.That(readerCode, Does.Not.Contain("new EntityRef<User, decimal>"));
        Assert.That(readerCode, Does.Contain("OrderId = r.GetValue(0)"));
    }

    [Test]
    public void EntityReader_WithNonFkColumn_DoesNotWrapInRef()
    {
        var columns = new[]
        {
            CreateProjectedColumn("UserId", "UserId", "int", 0),
            CreateProjectedColumn("Name", "Name", "string", 1)
        };

        var projection = CreateProjectionInfo(ProjectionKind.Entity, "User", columns);

        var readerCode = ReaderCodeGenerator.GenerateReaderDelegate(projection, "User");

        Assert.That(readerCode, Does.Not.Contain("new Ref<"));
        Assert.That(readerCode, Does.Contain("UserId = r.GetValue(0)"));
    }

    [Test]
    public void InsertInfo_FromEntityInfo_PropagatesForeignKeyFlag()
    {
        var entity = CreateTestEntity("Order", new[]
        {
            CreateColumn("OrderId", "int", false, ColumnKind.PrimaryKey, isIdentity: true),
            CreateForeignKeyColumn("UserId", "int", false, "User"),
            CreateColumn("Total", "decimal", false, ColumnKind.Standard)
        });

        var insertInfo = InsertInfo.FromEntityInfo(entity, GenSqlDialect.PostgreSQL);

        var fkColumn = insertInfo.Columns.First(c => c.PropertyName == "UserId");
        var normalColumn = insertInfo.Columns.First(c => c.PropertyName == "Total");

        Assert.That(fkColumn.IsForeignKey, Is.True);
        Assert.That(fkColumn.ForeignKeyEntityName, Is.EqualTo("User"));
        Assert.That(normalColumn.IsForeignKey, Is.False);
        Assert.That(normalColumn.ForeignKeyEntityName, Is.Null);
    }

    [Test]
    public void InsertExecuteNonQuery_WithForeignKeyColumn_ExtractsId()
    {
        var entity = CreateTestEntity("Order", new[]
        {
            CreateColumn("OrderId", "int", false, ColumnKind.PrimaryKey, isIdentity: true),
            CreateForeignKeyColumn("UserId", "int", false, "User"),
            CreateColumn("Total", "decimal", false, ColumnKind.Standard)
        });
        var insertInfo = InsertInfo.FromEntityInfo(entity, GenSqlDialect.PostgreSQL);

        var site = new TestCallSiteBuilder()
            .WithMethodName("ExecuteNonQueryAsync")
            .WithKind(InterceptorKind.InsertExecuteNonQuery)
            .WithEntityType("Order")
            .WithBuilderKind(BuilderKind.Query)
            .WithBuilderTypeName("InsertBuilder<Order>")
            .WithUniqueId("test_insert_fk")
            .WithInsertInfo(insertInfo)
            .Build();

        var result = InterceptorCodeGenerator.GenerateInterceptorsFile(
            "AppDbContext", "TestApp", "test0000", new List<TranslatedCallSite> { site });

        // FK column should have .Id extraction
        Assert.That(result, Does.Contain("entity.UserId.Id"));
        // Non-FK column should NOT have .Id
        Assert.That(result, Does.Contain("entity.Total)"));
        Assert.That(result, Does.Not.Contain("entity.Total.Id"));
    }

    [Test]
    public void InsertExecuteScalar_WithForeignKeyColumn_ExtractsId()
    {
        var entity = CreateTestEntity("Order", new[]
        {
            CreateColumn("OrderId", "int", false, ColumnKind.PrimaryKey, isIdentity: true),
            CreateForeignKeyColumn("UserId", "int", false, "User"),
            CreateColumn("Total", "decimal", false, ColumnKind.Standard)
        });
        var insertInfo = InsertInfo.FromEntityInfo(entity, GenSqlDialect.PostgreSQL);

        var site = new TestCallSiteBuilder()
            .WithMethodName("ExecuteScalarAsync")
            .WithKind(InterceptorKind.InsertExecuteScalar)
            .WithEntityType("Order")
            .WithBuilderKind(BuilderKind.Query)
            .WithBuilderTypeName("InsertBuilder<Order>")
            .WithUniqueId("test_insert_scalar_fk")
            .WithInsertInfo(insertInfo)
            .Build();

        var result = InterceptorCodeGenerator.GenerateInterceptorsFile(
            "AppDbContext", "TestApp", "test0000", new List<TranslatedCallSite> { site });

        // FK column should have .Id extraction
        Assert.That(result, Does.Contain("entity.UserId.Id"));
    }

    [Test]
    public void UpdateSetPoco_WithForeignKeyColumn_ExtractsId()
    {
        var entity = CreateTestEntity("Order", new[]
        {
            CreateColumn("OrderId", "int", false, ColumnKind.PrimaryKey),
            CreateForeignKeyColumn("UserId", "int", false, "User"),
            CreateColumn("Total", "decimal", false, ColumnKind.Standard)
        });
        var updateInfo = InsertInfo.FromEntityInfo(entity, GenSqlDialect.PostgreSQL);

        var site = new TestCallSiteBuilder()
            .WithMethodName("Set")
            .WithKind(InterceptorKind.UpdateSetPoco)
            .WithEntityType("Order")
            .WithBuilderKind(BuilderKind.Update)
            .WithBuilderTypeName("UpdateBuilder<Order>")
            .WithUniqueId("test_update_fk")
            .WithUpdateInfo(updateInfo)
            .Build();

        var result = InterceptorCodeGenerator.GenerateInterceptorsFile(
            "AppDbContext", "TestApp", "test0000", new List<TranslatedCallSite> { site });

        // FK column should extract .Id
        Assert.That(result, Does.Contain("e.UserId.Id"));
        // Non-FK column should NOT extract .Id
        Assert.That(result, Does.Contain("e.Total)"));
        Assert.That(result, Does.Not.Contain("e.Total.Id"));
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

    private static ColumnInfo CreateColumn(string name, string clrType, bool isNullable, ColumnKind kind,
        bool isIdentity = false, bool isComputed = false)
    {
        return new ColumnInfo(
            propertyName: name,
            columnName: name,
            clrType: clrType,
            fullClrType: clrType,
            isNullable: isNullable,
            kind: kind,
            referencedEntityName: null,
            modifiers: new ColumnModifiers(isIdentity: isIdentity, isComputed: isComputed));
    }

    private static ColumnInfo CreateColumnWithSensitive(string name, string clrType, bool isNullable, ColumnKind kind)
    {
        return new ColumnInfo(
            propertyName: name,
            columnName: name,
            clrType: clrType,
            fullClrType: clrType,
            isNullable: isNullable,
            kind: kind,
            referencedEntityName: null,
            modifiers: new ColumnModifiers(isSensitive: true));
    }

    private static TranslatedCallSite CreateExecutionUsageSite(
        InterceptorKind kind,
        string entityType,
        string resultType,
        ProjectionInfo? projection = null)
    {
        return TestCallSiteBuilder.CreateExecutionSite(kind, entityType, resultType, projection);
    }

    private static ProjectionInfo CreateProjectionInfo(
        ProjectionKind kind,
        string resultType,
        ProjectedColumn[] columns)
    {
        return new ProjectionInfo(kind, resultType, columns, isOptimalPath: true);
    }

    private static ProjectedColumn CreateProjectedColumn(
        string propertyName,
        string columnName,
        string clrType,
        int ordinal,
        bool isNullable = false)
    {
        return new ProjectedColumn(
            propertyName: propertyName,
            columnName: columnName,
            clrType: clrType,
            fullClrType: clrType,
            isNullable: isNullable,
            ordinal: ordinal);
    }

    private static ProjectedColumn CreateForeignKeyProjectedColumn(
        string propertyName,
        string columnName,
        string keyType,
        int ordinal,
        string entityName,
        bool isNullable = false)
    {
        var readerMethod = keyType switch
        {
            "int" => "GetInt32",
            "long" => "GetInt64",
            "Guid" => "GetGuid",
            _ => "GetValue"
        };

        return new ProjectedColumn(
            propertyName: propertyName,
            columnName: columnName,
            clrType: keyType,
            fullClrType: keyType,
            isNullable: isNullable,
            ordinal: ordinal,
            isValueType: true,
            readerMethodName: readerMethod,
            isForeignKey: true,
            foreignKeyEntityName: entityName);
    }

    private static ColumnInfo CreateForeignKeyColumn(
        string name,
        string keyType,
        bool isNullable,
        string referencedEntityName)
    {
        var readerMethod = keyType switch
        {
            "int" => "GetInt32",
            "long" => "GetInt64",
            "Guid" => "GetGuid",
            _ => "GetValue"
        };

        return new ColumnInfo(
            propertyName: name,
            columnName: name,
            clrType: keyType,
            fullClrType: keyType,
            isNullable: isNullable,
            kind: ColumnKind.ForeignKey,
            referencedEntityName: referencedEntityName,
            modifiers: new ColumnModifiers(isForeignKey: true),
            isValueType: true,
            readerMethodName: readerMethod);
    }

    #endregion
}
