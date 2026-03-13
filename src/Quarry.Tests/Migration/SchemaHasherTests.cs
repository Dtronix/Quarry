using Quarry.Shared.Migration;

namespace Quarry.Tests.Migration;

public class SchemaHasherTests
{
    [Test]
    public void ComputeHash_SameSchema_ReturnsSameHash()
    {
        var tables = new[]
        {
            new TableDef("users", null, NamingStyleKind.Exact,
                new[] { new ColumnDef("id", "int", false, ColumnKind.PrimaryKey) },
                Array.Empty<ForeignKeyDef>(), Array.Empty<IndexDef>())
        };

        var hash1 = SchemaHasher.ComputeHash(tables);
        var hash2 = SchemaHasher.ComputeHash(tables);

        Assert.That(hash1, Is.EqualTo(hash2));
    }

    [Test]
    public void ComputeHash_DifferentSchema_ReturnsDifferentHash()
    {
        var tables1 = new[]
        {
            new TableDef("users", null, NamingStyleKind.Exact,
                new[] { new ColumnDef("id", "int", false, ColumnKind.PrimaryKey) },
                Array.Empty<ForeignKeyDef>(), Array.Empty<IndexDef>())
        };
        var tables2 = new[]
        {
            new TableDef("users", null, NamingStyleKind.Exact,
                new[]
                {
                    new ColumnDef("id", "int", false, ColumnKind.PrimaryKey),
                    new ColumnDef("name", "string", true, ColumnKind.Standard)
                },
                Array.Empty<ForeignKeyDef>(), Array.Empty<IndexDef>())
        };

        var hash1 = SchemaHasher.ComputeHash(tables1);
        var hash2 = SchemaHasher.ComputeHash(tables2);

        Assert.That(hash1, Is.Not.EqualTo(hash2));
    }

    [Test]
    public void ComputeHash_OrderIndependent()
    {
        var tables1 = new[]
        {
            new TableDef("users", null, NamingStyleKind.Exact,
                new[] { new ColumnDef("id", "int", false, ColumnKind.PrimaryKey) },
                Array.Empty<ForeignKeyDef>(), Array.Empty<IndexDef>()),
            new TableDef("posts", null, NamingStyleKind.Exact,
                new[] { new ColumnDef("id", "int", false, ColumnKind.PrimaryKey) },
                Array.Empty<ForeignKeyDef>(), Array.Empty<IndexDef>())
        };
        var tables2 = new[]
        {
            new TableDef("posts", null, NamingStyleKind.Exact,
                new[] { new ColumnDef("id", "int", false, ColumnKind.PrimaryKey) },
                Array.Empty<ForeignKeyDef>(), Array.Empty<IndexDef>()),
            new TableDef("users", null, NamingStyleKind.Exact,
                new[] { new ColumnDef("id", "int", false, ColumnKind.PrimaryKey) },
                Array.Empty<ForeignKeyDef>(), Array.Empty<IndexDef>())
        };

        Assert.That(SchemaHasher.ComputeHash(tables1), Is.EqualTo(SchemaHasher.ComputeHash(tables2)));
    }

    [Test]
    public void ComputeHashFromEntities_MatchesComputeHash()
    {
        var tables = new[]
        {
            new TableDef("users", null, NamingStyleKind.Exact,
                new[]
                {
                    new ColumnDef("id", "int", false, ColumnKind.PrimaryKey),
                    new ColumnDef("name", "string", true, ColumnKind.Standard)
                },
                Array.Empty<ForeignKeyDef>(), Array.Empty<IndexDef>())
        };

        var tableNames = new List<string> { "users" };
        // Format: name\0clrType\0isNullable\0kind\0isIdentity\0isComputed\0maxLength\0precision\0scale\0hasDefault\0mappedName
        var colSigs = new List<IReadOnlyList<string>>
        {
            new List<string>
            {
                "id\0int\00\01\00\00\0\0\0\00\0",
                "name\0string\01\00\00\00\0\0\0\00\0"
            }
        };

        var hash1 = SchemaHasher.ComputeHash(tables);
        var hash2 = SchemaHasher.ComputeHashFromEntities(tableNames, colSigs);

        Assert.That(hash1, Is.EqualTo(hash2));
    }

    [Test]
    public void ComputeHash_ColumnAdded_HashChanges()
    {
        var tables1 = new[]
        {
            new TableDef("users", null, NamingStyleKind.Exact,
                new[] { new ColumnDef("id", "int", false, ColumnKind.PrimaryKey) },
                Array.Empty<ForeignKeyDef>(), Array.Empty<IndexDef>())
        };
        var tables2 = new[]
        {
            new TableDef("users", null, NamingStyleKind.Exact,
                new[]
                {
                    new ColumnDef("id", "int", false, ColumnKind.PrimaryKey),
                    new ColumnDef("email", "string", true, ColumnKind.Standard)
                },
                Array.Empty<ForeignKeyDef>(), Array.Empty<IndexDef>())
        };

        Assert.That(SchemaHasher.ComputeHash(tables1), Is.Not.EqualTo(SchemaHasher.ComputeHash(tables2)));
    }

    [Test]
    public void ComputeHash_NullabilityChanged_HashChanges()
    {
        var tables1 = new[]
        {
            new TableDef("users", null, NamingStyleKind.Exact,
                new[] { new ColumnDef("id", "int", false, ColumnKind.PrimaryKey), new ColumnDef("name", "string", false, ColumnKind.Standard) },
                Array.Empty<ForeignKeyDef>(), Array.Empty<IndexDef>())
        };
        var tables2 = new[]
        {
            new TableDef("users", null, NamingStyleKind.Exact,
                new[] { new ColumnDef("id", "int", false, ColumnKind.PrimaryKey), new ColumnDef("name", "string", true, ColumnKind.Standard) },
                Array.Empty<ForeignKeyDef>(), Array.Empty<IndexDef>())
        };

        Assert.That(SchemaHasher.ComputeHash(tables1), Is.Not.EqualTo(SchemaHasher.ComputeHash(tables2)));
    }

    [Test]
    public void ComputeHash_TypeChanged_HashChanges()
    {
        var tables1 = new[]
        {
            new TableDef("users", null, NamingStyleKind.Exact,
                new[] { new ColumnDef("id", "int", false, ColumnKind.PrimaryKey), new ColumnDef("age", "string", false, ColumnKind.Standard) },
                Array.Empty<ForeignKeyDef>(), Array.Empty<IndexDef>())
        };
        var tables2 = new[]
        {
            new TableDef("users", null, NamingStyleKind.Exact,
                new[] { new ColumnDef("id", "int", false, ColumnKind.PrimaryKey), new ColumnDef("age", "int", false, ColumnKind.Standard) },
                Array.Empty<ForeignKeyDef>(), Array.Empty<IndexDef>())
        };

        Assert.That(SchemaHasher.ComputeHash(tables1), Is.Not.EqualTo(SchemaHasher.ComputeHash(tables2)));
    }

    [Test]
    public void ComputeHash_EmptyTables_ConsistentHash()
    {
        var hash1 = SchemaHasher.ComputeHash(Array.Empty<TableDef>());
        var hash2 = SchemaHasher.ComputeHash(Array.Empty<TableDef>());

        Assert.That(hash1, Is.Not.Null.And.Not.Empty);
        Assert.That(hash1, Is.EqualTo(hash2));
    }
}
