using Quarry;

namespace BenchHarness;

public static class BenchUsageSites
{
    public static async Task Q1(BenchDb db) =>
        await db.Users().Where(u => u.IsActive).Select(u => new { u.UserId, u.UserName }).ExecuteFetchAllAsync();
}
