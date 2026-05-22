using System.Collections.Immutable;
using System.Threading;
using Microsoft.CodeAnalysis;
using NUnit.Framework;
using Quarry.Generators.IR;
using Quarry.Generators.Models;
using Quarry.Shared.Migration;
using GenSqlDialect = Quarry.Generators.Sql.SqlDialect;
using GenSqlDialectConfig = Quarry.Generators.Sql.SqlDialectConfig;

namespace Quarry.Tests.IR;

/// <summary>
/// Phase 4 tests for <see cref="CallSiteBinder"/>: ensures
/// <see cref="BoundCallSite.PatchInfo"/> is populated for
/// <see cref="InterceptorKind.UpdateSetPatch"/> and
/// <see cref="InterceptorKind.UpdateSetPatchAction"/> sites, and left null for
/// every other call-site kind. Column filtering is delegated to
/// <see cref="PatchInfo.FromEntityInfo"/> — those rules are covered by
/// <c>PatchInfoTests</c>; here we verify the wiring only.
/// </summary>
[TestFixture]
public class CallSiteBinderPatchTests
{
    [Test]
    public void Bind_UpdateSetPatch_PopulatesPatchInfoValueForm()
    {
        var registry = BuildRegistry();
        var raw = MakeRaw(InterceptorKind.UpdateSetPatch);

        var bound = CallSiteBinder.Bind(raw, registry, CancellationToken.None).Single();

        Assert.That(bound.PatchInfo, Is.Not.Null);
        Assert.That(bound.PatchInfo!.EntityTypeName, Is.EqualTo("User"));
        Assert.That(bound.PatchInfo.IsLambdaForm, Is.False);
        Assert.That(bound.PatchInfo.Columns.Select(c => c.PropertyName),
            Is.EquivalentTo(new[] { "Name", "Email" }));
    }

    [Test]
    public void Bind_UpdateSetPatchAction_PopulatesPatchInfoLambdaForm()
    {
        var registry = BuildRegistry();
        var raw = MakeRaw(InterceptorKind.UpdateSetPatchAction);

        var bound = CallSiteBinder.Bind(raw, registry, CancellationToken.None).Single();

        Assert.That(bound.PatchInfo, Is.Not.Null);
        Assert.That(bound.PatchInfo!.IsLambdaForm, Is.True);
        Assert.That(bound.PatchInfo.Columns.Select(c => c.PropertyName),
            Is.EquivalentTo(new[] { "Name", "Email" }));
    }

    [Test]
    public void Bind_UpdateSetPatch_ExcludesIdentityAndComputedColumns()
    {
        var registry = BuildRegistry();
        var raw = MakeRaw(InterceptorKind.UpdateSetPatch);

        var bound = CallSiteBinder.Bind(raw, registry, CancellationToken.None).Single();

        // Schema in BuildRegistry has Id (identity) + Name + Email + FullName (computed).
        // Patch columns must drop Id and FullName.
        Assert.That(bound.PatchInfo!.Columns.Select(c => c.PropertyName),
            Does.Not.Contain("Id"));
        Assert.That(bound.PatchInfo.Columns.Select(c => c.PropertyName),
            Does.Not.Contain("FullName"));
        Assert.That(bound.PatchInfo.Columns, Has.Count.EqualTo(2));
    }

    [Test]
    public void Bind_UpdateSetPoco_DoesNotPopulatePatchInfo()
    {
        var registry = BuildRegistry();
        var raw = MakeRaw(InterceptorKind.UpdateSetPoco);

        var bound = CallSiteBinder.Bind(raw, registry, CancellationToken.None).Single();

        Assert.That(bound.PatchInfo, Is.Null);
        Assert.That(bound.UpdateInfo, Is.Not.Null);
    }

    [Test]
    public void Bind_UpdateSetAction_DoesNotPopulatePatchInfo()
    {
        var registry = BuildRegistry();
        var raw = MakeRaw(InterceptorKind.UpdateSetAction);

        var bound = CallSiteBinder.Bind(raw, registry, CancellationToken.None).Single();

        Assert.That(bound.PatchInfo, Is.Null);
    }

    [Test]
    public void Bind_UpdateSetPatch_UnknownEntity_LeavesPatchInfoNull()
    {
        // No entity entry — binder cannot resolve, must not throw.
        var registry = EntityRegistry.Build(ImmutableArray<ContextInfo>.Empty, CancellationToken.None);
        var raw = MakeRaw(InterceptorKind.UpdateSetPatch);

        var bound = CallSiteBinder.Bind(raw, registry, CancellationToken.None).Single();

        Assert.That(bound.PatchInfo, Is.Null);
    }

    [Test]
    public void Bind_UpdateSetPatch_AppliesDialectQuotingFromContext()
    {
        var registry = BuildRegistry(GenSqlDialect.SqlServer);
        var raw = MakeRaw(InterceptorKind.UpdateSetPatch);

        var bound = CallSiteBinder.Bind(raw, registry, CancellationToken.None).Single();

        Assert.That(bound.PatchInfo!.Columns[0].QuotedColumnName, Is.EqualTo("[name]"));
    }

    // ── Harness ─────────────────────────────────────────────────────────

    private static EntityRegistry BuildRegistry(GenSqlDialect dialect = GenSqlDialect.SQLite)
    {
        var identityMods = new ColumnModifiers(isIdentity: true);
        var computedMods = new ColumnModifiers(isComputed: true);
        var standardMods = new ColumnModifiers();

        var userEntity = new EntityInfo(
            entityName: "User",
            schemaClassName: "UserSchema",
            schemaNamespace: "TestApp.Schema",
            tableName: "users",
            namingStyle: NamingStyleKind.SnakeCase,
            columns: new[]
            {
                new ColumnInfo("Id", "id", "int", "int", false, ColumnKind.PrimaryKey, null, identityMods, isValueType: true),
                new ColumnInfo("Name", "name", "string", "string", false, ColumnKind.Standard, null, standardMods),
                new ColumnInfo("Email", "email", "string", "string", false, ColumnKind.Standard, null, standardMods),
                new ColumnInfo("FullName", "full_name", "string", "string", false, ColumnKind.Standard, null, computedMods),
            },
            navigations: System.Array.Empty<NavigationInfo>(),
            indexes: System.Array.Empty<IndexInfo>(),
            location: Location.None);

        var context = new ContextInfo(
            className: "TestDb",
            @namespace: "TestApp",
            dialectConfig: new GenSqlDialectConfig(dialect),
            schema: null,
            entities: new[] { userEntity },
            entityMappings: System.Array.Empty<EntityMapping>(),
            location: Location.None);

        return EntityRegistry.Build(ImmutableArray.Create(context), CancellationToken.None);
    }

    private static RawCallSite MakeRaw(InterceptorKind kind)
    {
        return new RawCallSite(
            methodName: "Set",
            filePath: "Test.cs",
            line: 10,
            column: 10,
            uniqueId: "Set_0",
            kind: kind,
            builderKind: BuilderKind.Update,
            entityTypeName: "User",
            resultTypeName: null,
            isAnalyzable: true,
            nonAnalyzableReason: null,
            interceptableLocationData: "dGVzdGRhdGE=",
            interceptableLocationVersion: 1,
            location: default,
            contextClassName: "TestDb",
            contextNamespace: "TestApp");
    }
}
