using NUnit.Framework;
using Quarry.Generators.CodeGen;
using Quarry.Generators.Models;

namespace Quarry.Tests.IR;

[TestFixture]
public class InterceptorRouterTests
{
    [Test]
    public void Categorize_WhereClauses()
    {
        Assert.That(InterceptorRouter.Categorize(InterceptorKind.Where), Is.EqualTo(EmitterCategory.Clause));
        Assert.That(InterceptorRouter.Categorize(InterceptorKind.DeleteWhere), Is.EqualTo(EmitterCategory.Clause));
        Assert.That(InterceptorRouter.Categorize(InterceptorKind.UpdateWhere), Is.EqualTo(EmitterCategory.Clause));
    }

    [Test]
    public void Categorize_OrderByClauses()
    {
        Assert.That(InterceptorRouter.Categorize(InterceptorKind.OrderBy), Is.EqualTo(EmitterCategory.Clause));
        Assert.That(InterceptorRouter.Categorize(InterceptorKind.ThenBy), Is.EqualTo(EmitterCategory.Clause));
    }

    [Test]
    public void Categorize_OtherClauses()
    {
        Assert.That(InterceptorRouter.Categorize(InterceptorKind.GroupBy), Is.EqualTo(EmitterCategory.Clause));
        Assert.That(InterceptorRouter.Categorize(InterceptorKind.Having), Is.EqualTo(EmitterCategory.Clause));
        Assert.That(InterceptorRouter.Categorize(InterceptorKind.Set), Is.EqualTo(EmitterCategory.Clause));
        Assert.That(InterceptorRouter.Categorize(InterceptorKind.UpdateSet), Is.EqualTo(EmitterCategory.Clause));
        Assert.That(InterceptorRouter.Categorize(InterceptorKind.Select), Is.EqualTo(EmitterCategory.Clause));
        Assert.That(InterceptorRouter.Categorize(InterceptorKind.Distinct), Is.EqualTo(EmitterCategory.Clause));
        Assert.That(InterceptorRouter.Categorize(InterceptorKind.Limit), Is.EqualTo(EmitterCategory.Clause));
        Assert.That(InterceptorRouter.Categorize(InterceptorKind.Offset), Is.EqualTo(EmitterCategory.Clause));
        Assert.That(InterceptorRouter.Categorize(InterceptorKind.WithTimeout), Is.EqualTo(EmitterCategory.Clause));
    }

    [Test]
    public void Categorize_ExecutionTerminals()
    {
        Assert.That(InterceptorRouter.Categorize(InterceptorKind.ExecuteFetchAll), Is.EqualTo(EmitterCategory.Terminal));
        Assert.That(InterceptorRouter.Categorize(InterceptorKind.ExecuteFetchFirst), Is.EqualTo(EmitterCategory.Terminal));
        Assert.That(InterceptorRouter.Categorize(InterceptorKind.ExecuteFetchFirstOrDefault), Is.EqualTo(EmitterCategory.Terminal));
        Assert.That(InterceptorRouter.Categorize(InterceptorKind.ExecuteFetchSingle), Is.EqualTo(EmitterCategory.Terminal));
        Assert.That(InterceptorRouter.Categorize(InterceptorKind.ExecuteScalar), Is.EqualTo(EmitterCategory.Terminal));
        Assert.That(InterceptorRouter.Categorize(InterceptorKind.ExecuteNonQuery), Is.EqualTo(EmitterCategory.Terminal));
        Assert.That(InterceptorRouter.Categorize(InterceptorKind.ToAsyncEnumerable), Is.EqualTo(EmitterCategory.Terminal));
        Assert.That(InterceptorRouter.Categorize(InterceptorKind.InsertExecuteNonQuery), Is.EqualTo(EmitterCategory.Terminal));
        Assert.That(InterceptorRouter.Categorize(InterceptorKind.InsertExecuteScalar), Is.EqualTo(EmitterCategory.Terminal));
        Assert.That(InterceptorRouter.Categorize(InterceptorKind.ToDiagnostics), Is.EqualTo(EmitterCategory.Terminal));
    }

    [Test]
    public void Categorize_Joins()
    {
        Assert.That(InterceptorRouter.Categorize(InterceptorKind.Join), Is.EqualTo(EmitterCategory.Join));
        Assert.That(InterceptorRouter.Categorize(InterceptorKind.LeftJoin), Is.EqualTo(EmitterCategory.Join));
        Assert.That(InterceptorRouter.Categorize(InterceptorKind.RightJoin), Is.EqualTo(EmitterCategory.Join));
        Assert.That(InterceptorRouter.Categorize(InterceptorKind.CrossJoin), Is.EqualTo(EmitterCategory.Join));
        Assert.That(InterceptorRouter.Categorize(InterceptorKind.FullOuterJoin), Is.EqualTo(EmitterCategory.Join));
    }

    [Test]
    public void Categorize_Transitions()
    {
        Assert.That(InterceptorRouter.Categorize(InterceptorKind.DeleteTransition), Is.EqualTo(EmitterCategory.Transition));
        Assert.That(InterceptorRouter.Categorize(InterceptorKind.UpdateTransition), Is.EqualTo(EmitterCategory.Transition));
        Assert.That(InterceptorRouter.Categorize(InterceptorKind.AllTransition), Is.EqualTo(EmitterCategory.Transition));
        Assert.That(InterceptorRouter.Categorize(InterceptorKind.InsertTransition), Is.EqualTo(EmitterCategory.Transition));
    }

    [Test]
    public void Categorize_RawSql()
    {
        Assert.That(InterceptorRouter.Categorize(InterceptorKind.RawSqlAsync), Is.EqualTo(EmitterCategory.RawSql));
        Assert.That(InterceptorRouter.Categorize(InterceptorKind.RawSqlScalarAsync), Is.EqualTo(EmitterCategory.RawSql));
    }

    [Test]
    public void Categorize_ChainRoot()
    {
        Assert.That(InterceptorRouter.Categorize(InterceptorKind.ChainRoot), Is.EqualTo(EmitterCategory.ChainRoot));
    }

    [Test]
    public void Categorize_Unknown()
    {
        Assert.That(InterceptorRouter.Categorize(InterceptorKind.Unknown), Is.EqualTo(EmitterCategory.Unknown));
    }
}
