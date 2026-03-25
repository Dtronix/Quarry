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

        // Assign to locals so the generator sees direct context-typed variables
        var Lite = t.Lite;
        var Pg = t.Pg;
        var My = t.My;
        var Ss = t.Ss;

        // Lite uses .Prepare() — enables both SQL verification and execution
        var lite = Lite.Users().Select(u => (u.UserId, u.UserName)).Prepare();

        // Pg/My/Ss use direct .ToDiagnostics() — only need SQL verification
        QueryTestHarness.AssertDialects(
            lite.ToDiagnostics(),
            Pg.Users().Select(u => (u.UserId, u.UserName)).ToDiagnostics(),
            My.Users().Select(u => (u.UserId, u.UserName)).ToDiagnostics(),
            Ss.Users().Select(u => (u.UserId, u.UserName)).ToDiagnostics(),
            sqlite: "SELECT \"UserId\", \"UserName\" FROM \"users\"",
            pg:     "SELECT \"UserId\", \"UserName\" FROM \"users\"",
            mysql:  "SELECT `UserId`, `UserName` FROM `users`",
            ss:     "SELECT [UserId], [UserName] FROM [users]");

        // Execute against real SQLite (same lite instance that was already used for SQL verification)
        var results = await lite.ExecuteFetchAllAsync();

        Assert.That(results, Has.Count.EqualTo(3));
        Assert.That(results[0], Is.EqualTo((1, "Alice")));
        Assert.That(results[1], Is.EqualTo((2, "Bob")));
        Assert.That(results[2], Is.EqualTo((3, "Charlie")));
    }
}
