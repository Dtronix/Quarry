using Quarry.Tests.Samples;
using Pg = Quarry.Tests.Samples.Pg;
using My = Quarry.Tests.Samples.My;
using Ss = Quarry.Tests.Samples.Ss;

namespace Quarry.Tests.SqlOutput;


/// <summary>
/// Proof-of-concept: validates that QueryTestHarness works with .Prepare()
/// for both SQL verification (all 4 dialects) and execution (real SQLite).
/// </summary>
[TestFixture]
internal class HarnessProofOfConceptTests
{
    [Test]
    public async Task Select_Tuple_TwoColumns_SqlAndExecution()
    {
        await using var t = await QueryTestHarness.CreateAsync();
        var (Lite, Pg, My, Ss) = t;

        var lite = Lite.Users().Select(u => (u.UserId, u.UserName)).Prepare();
        var pg   = Pg.Users().Select(u => (u.UserId, u.UserName)).Prepare();
        var my   = My.Users().Select(u => (u.UserId, u.UserName)).Prepare();
        var ss   = Ss.Users().Select(u => (u.UserId, u.UserName)).Prepare();

        QueryTestHarness.AssertDialects(
            lite.ToDiagnostics(), pg.ToDiagnostics(),
            my.ToDiagnostics(), ss.ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\"",
            pg:     "SELECT \"UserId\", \"UserName\" FROM \"users\"",
            mysql:  "SELECT `UserId`, `UserName` FROM `users`",
            ss:     "SELECT [UserId], [UserName] FROM [users]");

        var results = await lite.ExecuteFetchAllAsync();
        Assert.That(results, Has.Count.EqualTo(3));
        Assert.That(results[0], Is.EqualTo((1, "Alice")));
        Assert.That(results[1], Is.EqualTo((2, "Bob")));
        Assert.That(results[2], Is.EqualTo((3, "Charlie")));
    }
}
