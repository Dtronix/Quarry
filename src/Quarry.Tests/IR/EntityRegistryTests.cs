using System.Collections.Immutable;
using System.Threading;
using Microsoft.CodeAnalysis;
using NUnit.Framework;
using Quarry.Generators.IR;
using Quarry.Generators.Models;
using Quarry.Shared.Migration;
using GenSqlDialect = Quarry.Generators.Sql.SqlDialect;

namespace Quarry.Tests.IR;

[TestFixture]
public class EntityRegistryTests
{
    [Test]
    public void Build_CreatesRegistryFromContexts()
    {
        var contexts = CreateTestContexts();
        var registry = EntityRegistry.Build(contexts, CancellationToken.None);

        Assert.That(registry, Is.Not.Null);
    }

    [Test]
    public void Resolve_FindsEntityByTypeName()
    {
        var contexts = CreateTestContexts();
        var registry = EntityRegistry.Build(contexts, CancellationToken.None);

        var entry = registry.Resolve("User");
        Assert.That(entry, Is.Not.Null);
        Assert.That(entry!.Entity.EntityName, Is.EqualTo("User"));
    }

    [Test]
    public void Resolve_FindsEntityByFullTypeName()
    {
        var contexts = CreateTestContexts();
        var registry = EntityRegistry.Build(contexts, CancellationToken.None);

        var entry = registry.Resolve("TestApp.Schema.User");
        Assert.That(entry, Is.Not.Null);
        Assert.That(entry!.Entity.EntityName, Is.EqualTo("User"));
    }

    [Test]
    public void Resolve_ScopedToContext()
    {
        var contexts = CreateTestContexts();
        var registry = EntityRegistry.Build(contexts, CancellationToken.None);

        var entry = registry.Resolve("User", "TestContext");
        Assert.That(entry, Is.Not.Null);
        Assert.That(entry!.Context.ClassName, Is.EqualTo("TestContext"));
    }

    [Test]
    public void Resolve_UnknownEntity_ReturnsNull()
    {
        var contexts = CreateTestContexts();
        var registry = EntityRegistry.Build(contexts, CancellationToken.None);

        var entry = registry.Resolve("NonExistent");
        Assert.That(entry, Is.Null);
    }

    [Test]
    public void Equality_SameEntities()
    {
        var contexts = CreateTestContexts();
        var a = EntityRegistry.Build(contexts, CancellationToken.None);
        var b = EntityRegistry.Build(contexts, CancellationToken.None);

        Assert.That(a.Equals(b), Is.True);
    }

    private static ImmutableArray<ContextInfo> CreateTestContexts()
    {
        var mods = new ColumnModifiers();
        var userEntity = new EntityInfo(
            entityName: "User",
            schemaClassName: "UserSchema",
            schemaNamespace: "TestApp.Schema",
            tableName: "users",
            namingStyle: NamingStyleKind.SnakeCase,
            columns: new[]
            {
                new ColumnInfo("Name", "name", "string", "string", false, ColumnKind.Standard, null, mods),
                new ColumnInfo("Age", "age", "int", "int", false, ColumnKind.Standard, null, mods, isValueType: true)
            },
            navigations: System.Array.Empty<NavigationInfo>(),
            indexes: System.Array.Empty<IndexInfo>(),
            location: Location.None);

        var orderEntity = new EntityInfo(
            entityName: "Order",
            schemaClassName: "OrderSchema",
            schemaNamespace: "TestApp.Schema",
            tableName: "orders",
            namingStyle: NamingStyleKind.SnakeCase,
            columns: new[]
            {
                new ColumnInfo("OrderId", "order_id", "int", "int", false, ColumnKind.PrimaryKey, null, mods, isValueType: true),
                new ColumnInfo("Total", "total", "decimal", "decimal", false, ColumnKind.Standard, null, mods, isValueType: true)
            },
            navigations: System.Array.Empty<NavigationInfo>(),
            indexes: System.Array.Empty<IndexInfo>(),
            location: Location.None);

        var context = new ContextInfo(
            className: "TestContext",
            @namespace: "TestApp",
            dialect: GenSqlDialect.SQLite,
            schema: null,
            entities: new[] { userEntity, orderEntity },
            entityMappings: System.Array.Empty<EntityMapping>(),
            location: Location.None);

        return ImmutableArray.Create(context);
    }
}
