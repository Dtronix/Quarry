using Microsoft.CodeAnalysis;
using Quarry.Generators.Models;
using Quarry.Shared.Migration;
using GenSqlDialect = Quarry.Generators.Sql.SqlDialect;

namespace Quarry.Tests;

[TestFixture]
public class PatchInfoTests
{
    [Test]
    public void FromEntityInfo_ExcludesIdentityAndComputed()
    {
        var entity = MakeEntity(
            ("Id", true, false),
            ("Name", false, false),
            ("Email", false, false),
            ("FullName", false, true));

        var patch = PatchInfo.FromEntityInfo(entity, GenSqlDialect.SQLite, isLambdaForm: false);

        Assert.That(patch.Columns.Select(c => c.PropertyName), Is.EquivalentTo(new[] { "Name", "Email" }));
        Assert.That(patch.EntityTypeName, Is.EqualTo("User"));
        Assert.That(patch.IsLambdaForm, Is.False);
    }

    [Test]
    public void FromEntityInfo_PreservesColumnDeclarationOrder()
    {
        var entity = MakeEntity(
            ("A", false, false),
            ("B", false, false),
            ("C", false, false));

        var patch = PatchInfo.FromEntityInfo(entity, GenSqlDialect.SQLite, isLambdaForm: true);

        Assert.That(patch.Columns.Select(c => c.PropertyName), Is.EqualTo(new[] { "A", "B", "C" }));
    }

    [Test]
    public void FromEntityInfo_AppliesDialectQuoting()
    {
        var entity = MakeEntity(("Name", false, false));

        Assert.That(PatchInfo.FromEntityInfo(entity, GenSqlDialect.SqlServer, isLambdaForm: false).Columns[0].QuotedColumnName,
            Is.EqualTo("[name]"));
        Assert.That(PatchInfo.FromEntityInfo(entity, GenSqlDialect.MySQL, isLambdaForm: false).Columns[0].QuotedColumnName,
            Is.EqualTo("`name`"));
        Assert.That(PatchInfo.FromEntityInfo(entity, GenSqlDialect.PostgreSQL, isLambdaForm: false).Columns[0].QuotedColumnName,
            Is.EqualTo("\"name\""));
        Assert.That(PatchInfo.FromEntityInfo(entity, GenSqlDialect.SQLite, isLambdaForm: false).Columns[0].QuotedColumnName,
            Is.EqualTo("\"name\""));
    }

    [Test]
    public void Equals_SameContent_ReturnsTrue()
    {
        var entity = MakeEntity(("Name", false, false), ("Age", false, false));
        var a = PatchInfo.FromEntityInfo(entity, GenSqlDialect.SQLite, isLambdaForm: false);
        var b = PatchInfo.FromEntityInfo(entity, GenSqlDialect.SQLite, isLambdaForm: false);

        Assert.That(a.Equals(b), Is.True);
        Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
    }

    [Test]
    public void Equals_DifferentLambdaForm_ReturnsFalse()
    {
        var entity = MakeEntity(("Name", false, false));
        var value = PatchInfo.FromEntityInfo(entity, GenSqlDialect.SQLite, isLambdaForm: false);
        var lambda = PatchInfo.FromEntityInfo(entity, GenSqlDialect.SQLite, isLambdaForm: true);

        Assert.That(value.Equals(lambda), Is.False);
    }

    [Test]
    public void Equals_DifferentColumnCount_ReturnsFalse()
    {
        var a = PatchInfo.FromEntityInfo(MakeEntity(("Name", false, false)), GenSqlDialect.SQLite, isLambdaForm: false);
        var b = PatchInfo.FromEntityInfo(MakeEntity(("Name", false, false), ("Age", false, false)), GenSqlDialect.SQLite, isLambdaForm: false);

        Assert.That(a.Equals(b), Is.False);
    }

    [Test]
    public void Equals_DifferentEntityName_ReturnsFalse()
    {
        var user = MakeEntity("User", ("Name", false, false));
        var order = MakeEntity("Order", ("Name", false, false));

        var a = PatchInfo.FromEntityInfo(user, GenSqlDialect.SQLite, isLambdaForm: false);
        var b = PatchInfo.FromEntityInfo(order, GenSqlDialect.SQLite, isLambdaForm: false);

        Assert.That(a.Equals(b), Is.False);
    }

    private static EntityInfo MakeEntity(params (string Name, bool IsIdentity, bool IsComputed)[] columns)
        => MakeEntity("User", columns);

    private static EntityInfo MakeEntity(string entityName, params (string Name, bool IsIdentity, bool IsComputed)[] columns)
    {
        var cols = columns.Select(c =>
        {
            var mods = new ColumnModifiers(isIdentity: c.IsIdentity, isComputed: c.IsComputed);
            return new ColumnInfo(c.Name, c.Name.ToLowerInvariant(), "string", "string", false, ColumnKind.Standard, null, mods);
        }).ToArray();

        return new EntityInfo(
            entityName: entityName,
            schemaClassName: entityName + "Schema",
            schemaNamespace: "TestApp.Schema",
            tableName: entityName.ToLowerInvariant() + "s",
            namingStyle: NamingStyleKind.SnakeCase,
            columns: cols,
            navigations: System.Array.Empty<NavigationInfo>(),
            indexes: System.Array.Empty<IndexInfo>(),
            location: Location.None);
    }
}
