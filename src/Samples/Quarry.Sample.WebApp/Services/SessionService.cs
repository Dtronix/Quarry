using System.Security.Cryptography;
using Quarry.Sample.WebApp.Data;

namespace Quarry.Sample.WebApp.Services;

public sealed class SessionService(AppDb db, AuditService audit)
{
    private static readonly TimeSpan SessionDuration = TimeSpan.FromHours(24);

    public async Task<string> CreateSessionAsync(int userId, string? ipAddress)
    {
        var tokenBytes = new byte[32];
        RandomNumberGenerator.Fill(tokenBytes);
        var token = Convert.ToBase64String(tokenBytes);

        await db.Sessions().Insert(new Session
        {
            UserId = userId,
            Token = token,
            ExpiresAt = DateTime.UtcNow.Add(SessionDuration),
            CreatedAt = DateTime.UtcNow
        }).ExecuteNonQueryAsync();

        // Opportunistic cleanup of expired sessions
        await db.Sessions().Delete()
            .Where(s => s.ExpiresAt < DateTime.UtcNow)
            .ExecuteNonQueryAsync();

        return token;
    }

    public async Task<User?> ValidateSessionAsync(string token)
    {
        // Find the session's user ID, then load the user separately.
        // Joined entity projection (.Select((s, u) => u)) is not yet supported;
        // a two-query approach avoids the limitation.
        var userId = await db.Sessions()
            .Where(s => s.Token == token && s.ExpiresAt > DateTime.UtcNow)
            .Select(s => s.UserId.Id)
            .ExecuteFetchFirstOrDefaultAsync();

        if (userId == 0) return null;

        return await db.Users()
            .Where(u => u.UserId == userId && u.IsActive)
            .Select(u => u)
            .ExecuteFetchFirstOrDefaultAsync();
    }

    public async Task InvalidateSessionAsync(string token)
    {
        await db.Sessions().Delete()
            .Where(s => s.Token == token)
            .ExecuteNonQueryAsync();
    }

    public async Task PurgeExpiredSessionsAsync()
    {
        await db.Sessions().Delete()
            .Where(s => s.ExpiresAt < DateTime.UtcNow)
            .ExecuteNonQueryAsync();
    }
}
