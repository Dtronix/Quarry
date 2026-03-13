using Quarry.Shared.Scaffold;

namespace Quarry.Tests.Scaffold;

[TestFixture]
public class ImplicitForeignKeyDetectorTests
{
    [Test]
    public void Detect_ColumnNameMatchesTable_id_ReturnsCandidateAboveThreshold()
    {
        var sourceColumns = new List<ColumnMetadata>
        {
            new("id", "INTEGER", false, isIdentity: true),
            new("customer_id", "INTEGER", false)
        };
        var existingFks = new List<ForeignKeyMetadata>();
        var allTables = new Dictionary<string, (PrimaryKeyMetadata? Pk, List<ColumnMetadata> Columns)>
        {
            ["orders"] = (new PrimaryKeyMetadata(null, new[] { "id" }), sourceColumns),
            ["customers"] = (new PrimaryKeyMetadata(null, new[] { "id" }), new List<ColumnMetadata>
            {
                new("id", "INTEGER", false, isIdentity: true),
                new("name", "TEXT", false)
            })
        };

        var candidates = ImplicitForeignKeyDetector.Detect(
            "orders", sourceColumns, existingFks, allTables, new List<IndexMetadata>());

        Assert.That(candidates, Has.Count.EqualTo(1));
        Assert.That(candidates[0].SourceColumn, Is.EqualTo("customer_id"));
        Assert.That(candidates[0].TargetTable, Is.EqualTo("customers"));
        Assert.That(candidates[0].Score, Is.GreaterThanOrEqualTo(50));
    }

    [Test]
    public void Detect_SkipsColumnsWithExistingFks()
    {
        var sourceColumns = new List<ColumnMetadata>
        {
            new("id", "INTEGER", false, isIdentity: true),
            new("customer_id", "INTEGER", false)
        };
        var existingFks = new List<ForeignKeyMetadata>
        {
            new("FK_existing", "customer_id", "customers", "id")
        };
        var allTables = new Dictionary<string, (PrimaryKeyMetadata? Pk, List<ColumnMetadata> Columns)>
        {
            ["orders"] = (new PrimaryKeyMetadata(null, new[] { "id" }), sourceColumns),
            ["customers"] = (new PrimaryKeyMetadata(null, new[] { "id" }), new List<ColumnMetadata>
            {
                new("id", "INTEGER", false, isIdentity: true)
            })
        };

        var candidates = ImplicitForeignKeyDetector.Detect(
            "orders", sourceColumns, existingFks, allTables, new List<IndexMetadata>());

        Assert.That(candidates, Is.Empty);
    }

    [Test]
    public void Detect_ColumnWithUniqueIndex_PenalizesScoreBelowThreshold()
    {
        var sourceColumns = new List<ColumnMetadata>
        {
            new("id", "INTEGER", false, isIdentity: true),
            new("customer_id", "INTEGER", false)
        };
        var allTables = new Dictionary<string, (PrimaryKeyMetadata? Pk, List<ColumnMetadata> Columns)>
        {
            ["orders"] = (new PrimaryKeyMetadata(null, new[] { "id" }), sourceColumns),
            ["customers"] = (new PrimaryKeyMetadata(null, new[] { "id" }), new List<ColumnMetadata>
            {
                new("id", "INTEGER", false, isIdentity: true)
            })
        };
        var indexes = new List<IndexMetadata>
        {
            new("UQ_customer", new[] { "customer_id" }, isUnique: true)
        };

        var candidates = ImplicitForeignKeyDetector.Detect(
            "orders", sourceColumns, new List<ForeignKeyMetadata>(), allTables, indexes);

        // Unique index penalty (-30) drops score below the >=50 threshold, so no candidates returned
        Assert.That(candidates, Is.Empty);
    }

    [Test]
    public void Detect_AllCapsColumnName_MatchesCaseInsensitively()
    {
        var sourceColumns = new List<ColumnMetadata>
        {
            new("ID", "INTEGER", false, isIdentity: true),
            new("CUSTOMER_ID", "INTEGER", false)
        };
        var existingFks = new List<ForeignKeyMetadata>();
        var allTables = new Dictionary<string, (PrimaryKeyMetadata? Pk, List<ColumnMetadata> Columns)>
        {
            ["orders"] = (new PrimaryKeyMetadata(null, new[] { "ID" }), sourceColumns),
            ["CUSTOMERS"] = (new PrimaryKeyMetadata(null, new[] { "ID" }), new List<ColumnMetadata>
            {
                new("ID", "INTEGER", false, isIdentity: true),
                new("NAME", "TEXT", false)
            })
        };

        var candidates = ImplicitForeignKeyDetector.Detect(
            "orders", sourceColumns, existingFks, allTables, new List<IndexMetadata>());

        Assert.That(candidates, Has.Count.EqualTo(1));
        Assert.That(candidates[0].SourceColumn, Is.EqualTo("CUSTOMER_ID"));
        Assert.That(candidates[0].TargetTable, Is.EqualTo("CUSTOMERS"));
        Assert.That(candidates[0].Score, Is.GreaterThanOrEqualTo(50));
    }

    [Test]
    public void Detect_CamelCaseId_MatchesCaseInsensitively()
    {
        var sourceColumns = new List<ColumnMetadata>
        {
            new("id", "INTEGER", false, isIdentity: true),
            new("customerId", "INTEGER", false)
        };
        var existingFks = new List<ForeignKeyMetadata>();
        var allTables = new Dictionary<string, (PrimaryKeyMetadata? Pk, List<ColumnMetadata> Columns)>
        {
            ["orders"] = (new PrimaryKeyMetadata(null, new[] { "id" }), sourceColumns),
            ["customer"] = (new PrimaryKeyMetadata(null, new[] { "id" }), new List<ColumnMetadata>
            {
                new("id", "INTEGER", false, isIdentity: true),
                new("name", "TEXT", false)
            })
        };

        var candidates = ImplicitForeignKeyDetector.Detect(
            "orders", sourceColumns, existingFks, allTables, new List<IndexMetadata>());

        Assert.That(candidates, Has.Count.EqualTo(1));
        Assert.That(candidates[0].SourceColumn, Is.EqualTo("customerId"));
        Assert.That(candidates[0].TargetTable, Is.EqualTo("customer"));
    }

    [Test]
    public void Detect_AmbiguousMatches_PenalizesScoreBelowThreshold()
    {
        // Both "item" and "items" tables match "item_id" (exact and singular respectively)
        var sourceColumns = new List<ColumnMetadata>
        {
            new("id", "INTEGER", false, isIdentity: true),
            new("item_id", "INTEGER", false)
        };
        var existingFks = new List<ForeignKeyMetadata>();
        var allTables = new Dictionary<string, (PrimaryKeyMetadata? Pk, List<ColumnMetadata> Columns)>
        {
            ["source"] = (new PrimaryKeyMetadata(null, new[] { "id" }), sourceColumns),
            ["item"] = (new PrimaryKeyMetadata(null, new[] { "id" }), new List<ColumnMetadata>
            {
                new("id", "INTEGER", false, isIdentity: true)
            }),
            ["items"] = (new PrimaryKeyMetadata(null, new[] { "id" }), new List<ColumnMetadata>
            {
                new("id", "INTEGER", false, isIdentity: true)
            })
        };

        var candidates = ImplicitForeignKeyDetector.Detect(
            "source", sourceColumns, existingFks, allTables, new List<IndexMetadata>());

        // Best match score is 60 (exact +40, type +20), but ambiguity penalty -20 drops it to 40,
        // which is below the >=50 threshold, so no candidates are returned
        Assert.That(candidates, Is.Empty);
    }

    [Test]
    public void Detect_NoMatchingPattern_ReturnsEmpty()
    {
        var sourceColumns = new List<ColumnMetadata>
        {
            new("id", "INTEGER", false, isIdentity: true),
            new("name", "TEXT", false),
            new("status", "INTEGER", false)
        };
        var allTables = new Dictionary<string, (PrimaryKeyMetadata? Pk, List<ColumnMetadata> Columns)>
        {
            ["orders"] = (new PrimaryKeyMetadata(null, new[] { "id" }), sourceColumns)
        };

        var candidates = ImplicitForeignKeyDetector.Detect(
            "orders", sourceColumns, new List<ForeignKeyMetadata>(), allTables, new List<IndexMetadata>());

        Assert.That(candidates, Is.Empty);
    }
}
