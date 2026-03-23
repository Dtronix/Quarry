using Quarry.Tests.Samples;

namespace Quarry.Tests.Integration;


[TestFixture]
internal class UpdateIntegrationTests : SqliteIntegrationTestBase
{
    #region Set(Action<T>) — Expression Lambda (Single Column)

    [Test]
    public async Task Update_SetAction_UpdatesSingleColumn()
    {
        var rows = await Db.Users().Update()
            .Set(u => u.UserName = "Updated")
            .Where(u => u.UserId == 1)
            .ExecuteNonQueryAsync();

        Assert.That(rows, Is.EqualTo(1));

        var user = await Db.Users()
            .Where(u => u.UserId == 1)
            .Select(u => u.UserName)
            .ExecuteFetchFirstAsync();

        Assert.That(user, Is.EqualTo("Updated"));
    }

    [Test]
    public async Task Update_SetAction_MultipleColumns()
    {
        var rows = await Db.Users().Update()
            .Set(u => { u.UserName = "Multi"; u.IsActive = false; })
            .Where(u => u.UserId == 2)
            .ExecuteNonQueryAsync();

        Assert.That(rows, Is.EqualTo(1));

        var result = await Db.Users()
            .Where(u => u.UserId == 2)
            .Select(u => (u.UserName, u.IsActive))
            .ExecuteFetchFirstAsync();

        Assert.That(result.UserName, Is.EqualTo("Multi"));
        Assert.That(result.IsActive, Is.False);
    }

    #endregion

    #region Set(T entity) — POCO Form

    [Test]
    public async Task Update_SetPoco_UpdatesMultipleColumns()
    {
        var rows = await Db.Users().Update()
            .Set(new User { UserName = "Poco", IsActive = false })
            .Where(u => u.UserId == 3)
            .ExecuteNonQueryAsync();

        Assert.That(rows, Is.EqualTo(1));

        var result = await Db.Users()
            .Where(u => u.UserId == 3)
            .Select(u => (u.UserName, u.IsActive))
            .ExecuteFetchFirstAsync();

        Assert.That(result.UserName, Is.EqualTo("Poco"));
        Assert.That(result.IsActive, Is.False);
    }

    #endregion

    #region Set(Action<T>) — Single Assignment (Expression Lambda)

    [Test]
    public async Task Update_SetAction_SingleAssignment_Literal()
    {
        var rows = await Db.Users().Update()
            .Set(u => u.UserName = "ActionLiteral")
            .Where(u => u.UserId == 1)
            .ExecuteNonQueryAsync();

        Assert.That(rows, Is.EqualTo(1));

        var user = await Db.Users()
            .Where(u => u.UserId == 1)
            .Select(u => u.UserName)
            .ExecuteFetchFirstAsync();

        Assert.That(user, Is.EqualTo("ActionLiteral"));
    }

    [Test]
    public async Task Update_SetAction_SingleAssignment_Boolean()
    {
        var rows = await Db.Users().Update()
            .Set(u => u.IsActive = false)
            .Where(u => u.UserId == 1)
            .ExecuteNonQueryAsync();

        Assert.That(rows, Is.EqualTo(1));

        var active = await Db.Users()
            .Where(u => u.UserId == 1)
            .Select(u => u.IsActive)
            .ExecuteFetchFirstAsync();

        Assert.That(active, Is.False);
    }

    [Test]
    public async Task Update_SetAction_SingleAssignment_CapturedVariable()
    {
        var name = "FromLocal";
        var rows = await Db.Users().Update()
            .Set(u => u.UserName = name)
            .Where(u => u.UserId == 2)
            .ExecuteNonQueryAsync();

        Assert.That(rows, Is.EqualTo(1));

        var user = await Db.Users()
            .Where(u => u.UserId == 2)
            .Select(u => u.UserName)
            .ExecuteFetchFirstAsync();

        Assert.That(user, Is.EqualTo("FromLocal"));
    }

    #endregion

    #region Set(Action<T>) — Multi-Assignment (Statement Lambda)

    [Test]
    public async Task Update_SetAction_MultiAssignment_Literals()
    {
        var rows = await Db.Users().Update()
            .Set(u => { u.UserName = "BlockLiteral"; u.IsActive = false; })
            .Where(u => u.UserId == 3)
            .ExecuteNonQueryAsync();

        Assert.That(rows, Is.EqualTo(1));

        var result = await Db.Users()
            .Where(u => u.UserId == 3)
            .Select(u => (u.UserName, u.IsActive))
            .ExecuteFetchFirstAsync();

        Assert.That(result.UserName, Is.EqualTo("BlockLiteral"));
        Assert.That(result.IsActive, Is.False);
    }

    [Test]
    public async Task Update_SetAction_MultiAssignment_WithCapturedVariable()
    {
        var name = "BlockCaptured";
        var rows = await Db.Users().Update()
            .Set(u => { u.UserName = name; u.IsActive = true; })
            .Where(u => u.UserId == 1)
            .ExecuteNonQueryAsync();

        Assert.That(rows, Is.EqualTo(1));

        var result = await Db.Users()
            .Where(u => u.UserId == 1)
            .Select(u => (u.UserName, u.IsActive))
            .ExecuteFetchFirstAsync();

        Assert.That(result.UserName, Is.EqualTo("BlockCaptured"));
        Assert.That(result.IsActive, Is.True);
    }

    #endregion

    #region Set(Action<T>) — Chained SetAction Calls

    [Test]
    public async Task Update_SetAction_ChainedCalls()
    {
        var rows = await Db.Users().Update()
            .Set(u => u.UserName = "Chained")
            .Set(u => u.IsActive = false)
            .Where(u => u.UserId == 2)
            .ExecuteNonQueryAsync();

        Assert.That(rows, Is.EqualTo(1));

        var result = await Db.Users()
            .Where(u => u.UserId == 2)
            .Select(u => (u.UserName, u.IsActive))
            .ExecuteFetchFirstAsync();

        Assert.That(result.UserName, Is.EqualTo("Chained"));
        Assert.That(result.IsActive, Is.False);
    }

    #endregion

    #region Update with All()

    [Test]
    public async Task Update_SetAction_All_UpdatesAllRows()
    {
        var rows = await Db.Users().Update()
            .Set(u => u.IsActive = true)
            .All()
            .ExecuteNonQueryAsync();

        Assert.That(rows, Is.EqualTo(3));

        var activeCount = await Db.Users()
            .Where(u => u.IsActive)
            .Select(u => u.UserId)
            .ExecuteFetchAllAsync();

        Assert.That(activeCount, Has.Count.EqualTo(3));
    }

    #endregion
}
