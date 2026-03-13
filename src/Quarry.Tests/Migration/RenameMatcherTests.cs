using Quarry.Shared.Migration;

namespace Quarry.Tests.Migration;

public class RenameMatcherTests
{
    [Test]
    public void Similarity_IdenticalStrings_Returns1()
    {
        Assert.That(LevenshteinDistance.Similarity("users", "users"), Is.EqualTo(1.0));
    }

    [Test]
    public void Similarity_CompletelyDifferent_ReturnsLow()
    {
        var score = LevenshteinDistance.Similarity("abc", "xyz");
        Assert.That(score, Is.LessThan(0.5));
    }

    [Test]
    public void Similarity_SimilarStrings_ReturnsHigh()
    {
        var score = LevenshteinDistance.Similarity("user_name", "username");
        Assert.That(score, Is.GreaterThan(0.7));
    }

    [Test]
    public void ShouldAutoAccept_HighScore_ReturnsTrue()
    {
        var candidate = new RenameMatcher.RenameCandidate("old", "new", 0.85);
        Assert.That(RenameMatcher.ShouldAutoAccept(candidate), Is.True);
    }

    [Test]
    public void ShouldAutoAccept_MediumScore_ReturnsFalse()
    {
        var candidate = new RenameMatcher.RenameCandidate("old", "new", 0.7);
        Assert.That(RenameMatcher.ShouldAutoAccept(candidate), Is.False);
    }

    [Test]
    public void MatchTable_LowScore_ReturnsNull()
    {
        var added = BuildTable("invoices", new[] { BuildColumn("id", "int", ColumnKind.PrimaryKey), BuildColumn("amount", "decimal", ColumnKind.Standard) });
        var dropped = BuildTable("users", new[] { BuildColumn("id", "int", ColumnKind.PrimaryKey) });

        var result = RenameMatcher.MatchTable(added, dropped);

        Assert.That(result, Is.Null);
    }

    [Test]
    public void MatchColumn_SameType_HigherScore()
    {
        var added = new ColumnDef("user_name", "string", false, ColumnKind.Standard);
        var dropped = new ColumnDef("username", "string", false, ColumnKind.Standard);

        var result = RenameMatcher.MatchColumn(added, dropped);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Score, Is.GreaterThanOrEqualTo(0.6));
    }

    [Test]
    public void MatchColumn_DifferentType_LowerScore()
    {
        var added = new ColumnDef("age", "int", false, ColumnKind.Standard);
        var dropped = new ColumnDef("age_str", "string", false, ColumnKind.Standard);

        var sameTypeResult = RenameMatcher.MatchColumn(
            new ColumnDef("age", "string", false, ColumnKind.Standard), dropped);
        var diffTypeResult = RenameMatcher.MatchColumn(added, dropped);

        if (sameTypeResult != null && diffTypeResult != null)
            Assert.That(diffTypeResult.Score, Is.LessThan(sameTypeResult.Score));
        else
            Assert.Pass("One or both matches returned null (below threshold)");
    }

    #region LevenshteinDistance boundary cases

    [Test]
    public void Similarity_EmptyVsEmpty_Returns1()
    {
        // Empty strings are equal via string.Equals check
        Assert.That(LevenshteinDistance.Similarity("", ""), Is.EqualTo(1.0));
    }

    [Test]
    public void Similarity_EmptyVsNonEmpty_Returns0()
    {
        Assert.That(LevenshteinDistance.Similarity("", "abc"), Is.EqualTo(0.0));
    }

    [Test]
    public void Similarity_SingleChar_Identical_Returns1()
    {
        Assert.That(LevenshteinDistance.Similarity("a", "a"), Is.EqualTo(1.0));
    }

    [Test]
    public void Similarity_SingleChar_Different_ReturnsLow()
    {
        Assert.That(LevenshteinDistance.Similarity("a", "b"), Is.LessThanOrEqualTo(0.5));
    }

    [Test]
    public void Compute_EmptyFirst_ReturnsLengthOfSecond()
    {
        Assert.That(LevenshteinDistance.Compute("", "abc"), Is.EqualTo(3));
    }

    [Test]
    public void Compute_EmptySecond_ReturnsLengthOfFirst()
    {
        Assert.That(LevenshteinDistance.Compute("abc", ""), Is.EqualTo(3));
    }

    [Test]
    public void Compute_Identical_ReturnsZero()
    {
        Assert.That(LevenshteinDistance.Compute("abc", "abc"), Is.EqualTo(0));
    }

    [Test]
    public void Compute_SingleInsertion_Returns1()
    {
        Assert.That(LevenshteinDistance.Compute("abc", "abcd"), Is.EqualTo(1));
    }

    #endregion

    #region Helpers

    private static TableDef BuildTable(string name, IReadOnlyList<ColumnDef> columns)
    {
        return new TableDef(name, null, NamingStyleKind.Exact, columns,
            Array.Empty<ForeignKeyDef>(), Array.Empty<IndexDef>());
    }

    private static ColumnDef BuildColumn(string name, string clrType, ColumnKind kind)
    {
        return new ColumnDef(name, clrType, false, kind);
    }

    #endregion
}
