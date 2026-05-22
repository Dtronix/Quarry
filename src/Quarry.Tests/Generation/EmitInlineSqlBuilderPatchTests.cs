using System.Collections.Generic;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using Quarry.Generators.CodeGen;
using GenSqlDialect = Quarry.Generators.Sql.SqlDialect;

namespace Quarry.Tests.Generation;

/// <summary>
/// Phase 6 tests for <see cref="TerminalEmitHelpers.EmitInlineSqlBuilder"/> handling
/// <see cref="TerminalEmitHelpers.SqlSegmentKind.PatchSet"/>. The emitter must produce:
/// (1) an empty-mask guard that throws on <c>__c.PatchMask == 0UL</c>, (2) a runtime
/// loop over the per-chain fragment table that emits comma-separated <c>col = @pN</c>
/// fragments for active bits, and (3) downstream scalar parameters whose runtime
/// index is shifted by <c>__setShift</c> (count of active SET params) in addition to
/// the existing <c>__colShift</c>.
/// </summary>
[TestFixture]
public class EmitInlineSqlBuilderPatchTests
{
    [Test]
    public void Emit_PatchSetOnly_SQLite_ProducesGuardAndSetLoop()
    {
        var code = Emit(GenSqlDialect.SQLite,
            Literal("UPDATE \"users\""),
            PatchSet(),
            Literal(" WHERE \"UserId\" = "),
            Scalar(0));

        Assert.That(code, Does.Contain("int __setShift = 0;"));
        Assert.That(code, Does.Contain("if (__c.PatchMask == 0UL)"));
        Assert.That(code, Does.Contain("throw new System.InvalidOperationException"));
        Assert.That(code, Does.Contain("__sb.Append(\" SET \");"));
        Assert.That(code, Does.Contain("if ((__c.PatchMask & __frag.Bit) == 0UL) continue;"));
        Assert.That(code, Does.Contain("__sb.Append(__frag.Prefix);"));
        Assert.That(code, Does.Contain("__setShift++;"));
    }

    [Test]
    public void Emit_PatchSet_SQLite_PlaceholderUsesSetShiftAndColShift()
    {
        var code = Emit(GenSqlDialect.SQLite, PatchSet());

        // Per-fragment placeholder must be "@p" + (__setShift + __colShift)
        Assert.That(code, Does.Contain("__sb.Append(\"@p\");"));
        Assert.That(code, Does.Contain("__sb.Append(__setShift + __colShift);"));
    }

    [Test]
    public void Emit_PatchSet_PostgreSQL_PlaceholderUsesDollarSign()
    {
        var code = Emit(GenSqlDialect.PostgreSQL, PatchSet());

        Assert.That(code, Does.Contain("__sb.Append('$');"));
        Assert.That(code, Does.Contain("__sb.Append(__setShift + 1 + __colShift);"));
        // PostgreSQL must NOT emit the @p form
        Assert.That(code, Does.Not.Contain("__sb.Append(\"@p\");"));
    }

    [Test]
    public void Emit_PatchSet_MySQL_PlaceholderUsesQuestionMark()
    {
        var code = Emit(GenSqlDialect.MySQL, PatchSet());

        Assert.That(code, Does.Contain("__sb.Append('?');"));
        // Positional ? — no numeric shift expression in placeholder
        Assert.That(code, Does.Not.Contain("__sb.Append(__setShift"));
    }

    [Test]
    public void Emit_PatchSet_SqlServer_PlaceholderUsesAtP()
    {
        var code = Emit(GenSqlDialect.SqlServer, PatchSet());

        Assert.That(code, Does.Contain("__sb.Append(\"@p\");"));
        Assert.That(code, Does.Contain("__sb.Append(__setShift + __colShift);"));
    }

    [Test]
    public void Emit_PatchSet_ScalarAfterPatch_AddsSetShiftToIndex()
    {
        // WHERE @p0 placed AFTER the SET clause should expand to
        // (0 + __colShift + __setShift) — i.e. SET shift dominates as the leading term.
        var code = Emit(GenSqlDialect.SQLite,
            Literal("UPDATE \"users\""),
            PatchSet(),
            Literal(" WHERE \"UserId\" = "),
            Scalar(0));

        Assert.That(code, Does.Contain("__sb.Append(0 + __colShift + __setShift);"));
    }

    [Test]
    public void Emit_PatchSet_PostgreSQLScalarAfterPatch_AddsSetShiftWithOneOffset()
    {
        var code = Emit(GenSqlDialect.PostgreSQL,
            Literal("UPDATE \"users\""),
            PatchSet(),
            Literal(" WHERE \"UserId\" = "),
            Scalar(0));

        // PG uses (idx + 1 + __colShift + __setShift) — 1-based dollar placeholders
        Assert.That(code, Does.Contain("__sb.Append(0 + 1 + __colShift + __setShift);"));
    }

    [Test]
    public void Emit_NoPatchSet_DoesNotDeclareSetShift()
    {
        // Regression guard: when no Patch is present, behaviour is unchanged.
        var code = Emit(GenSqlDialect.SQLite,
            Literal("SELECT * FROM \"users\" WHERE \"Id\" = "),
            Scalar(0));

        Assert.That(code, Does.Not.Contain("__setShift"));
        Assert.That(code, Does.Contain("__sb.Append(0 + __colShift);"));
    }

    [Test]
    public void Emit_PatchSet_UsesProvidedFragmentTableReference()
    {
        var code = Emit(GenSqlDialect.SQLite, "Carrier_5._PatchFragments_0",
            PatchSet());

        Assert.That(code, Does.Contain("Carrier_5._PatchFragments_0.Length"));
        Assert.That(code, Does.Contain("Carrier_5._PatchFragments_0[__pi]"));
    }

    [Test]
    public void Emit_PatchSet_DefaultFragmentTableReferenceIsPatchFragments()
    {
        // Default reference works when Phase 7 sets up a local of that name.
        var code = Emit(GenSqlDialect.SQLite, PatchSet());

        Assert.That(code, Does.Contain("__patchFragments.Length"));
    }

    // ── Reflection harness ─────────────────────────────────────────

    private static string Emit(GenSqlDialect dialect, params object[] segments)
        => Emit(dialect, "__patchFragments", segments);

    private static string Emit(GenSqlDialect dialect, string patchFragmentsRef, params object[] segments)
    {
        var method = typeof(TerminalEmitHelpers).GetMethod(
            "EmitInlineSqlBuilder", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new System.InvalidOperationException("EmitInlineSqlBuilder not found");

        var segListType = typeof(TerminalEmitHelpers)
            .GetNestedType("SqlSegment", BindingFlags.NonPublic)!;
        var listType = typeof(List<>).MakeGenericType(segListType);
        var segList = (System.Collections.IList)System.Activator.CreateInstance(listType)!;
        foreach (var s in segments) segList.Add(s);

        var collectionsType = typeof(IReadOnlyList<>)
            .MakeGenericType(typeof(System.ValueTuple<int, int>));
        var collections = System.Array.Empty<(int, int)>();

        var sb = new StringBuilder();
        method.Invoke(null, new object[] { sb, "        ", segList, dialect, collections, patchFragmentsRef });
        return sb.ToString();
    }

    private static object Literal(string text)
    {
        var factory = typeof(TerminalEmitHelpers)
            .GetNestedType("SqlSegment", BindingFlags.NonPublic)!
            .GetMethod("Literal", BindingFlags.Public | BindingFlags.Static)!;
        return factory.Invoke(null, new object[] { text })!;
    }

    private static object Scalar(int globalIndex)
    {
        var factory = typeof(TerminalEmitHelpers)
            .GetNestedType("SqlSegment", BindingFlags.NonPublic)!
            .GetMethod("Scalar", BindingFlags.Public | BindingFlags.Static)!;
        return factory.Invoke(null, new object[] { globalIndex })!;
    }

    private static object PatchSet()
    {
        var factory = typeof(TerminalEmitHelpers)
            .GetNestedType("SqlSegment", BindingFlags.NonPublic)!
            .GetMethod("PatchSet", BindingFlags.Public | BindingFlags.Static)!;
        return factory.Invoke(null, System.Array.Empty<object>())!;
    }
}
