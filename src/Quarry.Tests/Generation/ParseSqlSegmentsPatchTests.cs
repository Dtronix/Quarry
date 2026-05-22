using System.Reflection;
using NUnit.Framework;
using Quarry.Generators.CodeGen;
using GenSqlDialect = Quarry.Generators.Sql.SqlDialect;

namespace Quarry.Tests.Generation;

/// <summary>
/// Phase 5 tests for <see cref="TerminalEmitHelpers.ParseSqlSegments"/> recognising
/// the new <c>{__PATCH_SET__}</c> token. The parser already handled scalar params
/// (<c>@pN</c> / <c>$N</c>) and collection-expand tokens (<c>{__COL_PN__}</c>); the
/// Patch token must be split into a dedicated <see cref="TerminalEmitHelpers.SqlSegmentKind.PatchSet"/>
/// segment alongside the surrounding literals.
/// </summary>
[TestFixture]
public class ParseSqlSegmentsPatchTests
{
    [Test]
    public void ParseSqlSegments_PatchTokenOnly_ProducesSinglePatchSegment()
    {
        var segs = Parse("{__PATCH_SET__}", GenSqlDialect.SQLite);

        Assert.That(segs, Has.Count.EqualTo(1));
        Assert.That(SegmentKind(segs[0]), Is.EqualTo("PatchSet"));
    }

    [Test]
    public void ParseSqlSegments_UpdateWithPatchAndWhere_SQLite_SplitsAroundToken()
    {
        // Mirrors the Phase 5 assembler output: UPDATE "users" {__PATCH_SET__} WHERE "UserId" = @p0
        var segs = Parse("UPDATE \"users\" {__PATCH_SET__} WHERE \"UserId\" = @p0", GenSqlDialect.SQLite);

        Assert.That(SegmentKind(segs[0]), Is.EqualTo("Literal"));
        Assert.That(SegmentText(segs[0]), Is.EqualTo("UPDATE \"users\" "));

        Assert.That(SegmentKind(segs[1]), Is.EqualTo("PatchSet"));

        Assert.That(SegmentKind(segs[2]), Is.EqualTo("Literal"));
        Assert.That(SegmentText(segs[2]), Is.EqualTo(" WHERE \"UserId\" = "));

        Assert.That(SegmentKind(segs[3]), Is.EqualTo("ScalarParam"));
        Assert.That(SegmentParamIndex(segs[3]), Is.EqualTo(0));
    }

    [Test]
    public void ParseSqlSegments_UpdateWithPatchAndWhere_PostgreSQL_SplitsAroundToken()
    {
        var segs = Parse("UPDATE \"users\" {__PATCH_SET__} WHERE \"UserId\" = $1", GenSqlDialect.PostgreSQL);

        Assert.That(SegmentKind(segs[1]), Is.EqualTo("PatchSet"));
        Assert.That(SegmentKind(segs[3]), Is.EqualTo("ScalarParam"));
        Assert.That(SegmentParamIndex(segs[3]), Is.EqualTo(0)); // $1 → GlobalIndex 0
    }

    [Test]
    public void ParseSqlSegments_UpdateWithPatchAndWhere_MySQL_SplitsAroundToken()
    {
        // MySQL has positional ? markers — no scalar segments are split out.
        var segs = Parse("UPDATE `users` {__PATCH_SET__} WHERE `UserId` = ?", GenSqlDialect.MySQL);

        Assert.That(SegmentKind(segs[0]), Is.EqualTo("Literal"));
        Assert.That(SegmentKind(segs[1]), Is.EqualTo("PatchSet"));
        Assert.That(SegmentKind(segs[2]), Is.EqualTo("Literal"));
        Assert.That(SegmentText(segs[2]), Is.EqualTo(" WHERE `UserId` = ?"));
    }

    [Test]
    public void ParseSqlSegments_PatchAndCollectionToken_CoexistInSameSql()
    {
        // Combined Patch + collection-expansion (Where(ids.Contains(...))). Both tokens
        // must be recognised independently; the collection token's index parses correctly.
        var segs = Parse("UPDATE \"users\" {__PATCH_SET__} WHERE \"Id\" IN ({__COL_P5__})", GenSqlDialect.SQLite);

        var kinds = string.Join(",", segs.ConvertAll(SegmentKind));
        Assert.That(kinds, Is.EqualTo("Literal,PatchSet,Literal,CollectionExpand,Literal"));
        // CollectionExpand segment carries the param's GlobalIndex
        Assert.That(SegmentParamIndex(segs[3]), Is.EqualTo(5));
    }

    [Test]
    public void ParseSqlSegments_NoPatchToken_BehavesAsBefore()
    {
        // Regression guard: the new branch must not break ordinary scalar/collection parsing.
        var segs = Parse("SELECT * FROM \"users\" WHERE \"Id\" = @p0", GenSqlDialect.SQLite);

        Assert.That(SegmentKind(segs[0]), Is.EqualTo("Literal"));
        Assert.That(SegmentKind(segs[1]), Is.EqualTo("ScalarParam"));
        Assert.That(SegmentParamIndex(segs[1]), Is.EqualTo(0));
    }

    // ── Reflection harness ─────────────────────────────────────────

    private static System.Collections.Generic.List<object> Parse(string sql, GenSqlDialect dialect)
    {
        var method = typeof(TerminalEmitHelpers).GetMethod(
            "ParseSqlSegments", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new System.InvalidOperationException("ParseSqlSegments not found");
        var list = (System.Collections.IList)method.Invoke(null, new object[] { sql, dialect })!;
        var result = new System.Collections.Generic.List<object>(list.Count);
        foreach (var item in list) result.Add(item!);
        return result;
    }

    private static string SegmentKind(object seg)
    {
        var kindField = seg.GetType().GetField("Kind")!;
        return kindField.GetValue(seg)!.ToString()!;
    }

    private static string? SegmentText(object seg)
    {
        var textField = seg.GetType().GetField("Text")!;
        return (string?)textField.GetValue(seg);
    }

    private static int SegmentParamIndex(object seg)
    {
        var idxField = seg.GetType().GetField("ParamIndex")!;
        return (int)idxField.GetValue(seg)!;
    }
}
