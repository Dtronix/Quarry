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
        return await db.Sessions()
            .Join<User>((s, u) => s.UserId.Id == u.UserId)
            .Where((s, u) => s.Token == token && s.ExpiresAt > DateTime.UtcNow && u.IsActive)
            .Select((s, u) => u)
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
