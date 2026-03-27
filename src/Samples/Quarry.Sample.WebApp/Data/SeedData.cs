using Quarry.Sample.WebApp.Services;

namespace Quarry.Sample.WebApp.Data;

public static class SeedData
{
    public static async Task InitializeAsync(AppDb db, PasswordHasher hasher)
    {
        // Check if already seeded
        var count = await db.Users()
            .Select(u => Sql.Count())
            .ExecuteScalarAsync<int>();

        if (count > 0)
            return;

        var users = new List<User>();

        var seeds = new (string email, string userName, UserRole role, string password)[]
        {
            ("admin@example.com", "admin", UserRole.Admin, "admin123"),
            ("alice@example.com", "alice", UserRole.User, "password1"),
            ("bob@example.com", "bob", UserRole.User, "password2"),
            ("carol@example.com", "carol", UserRole.User, "password3"),
            ("dave@example.com", "dave", UserRole.User, "password4"),
            ("eve@example.com", "eve", UserRole.User, "password5"),
        };

        foreach (var (email, userName, role, password) in seeds)
        {
            var (hash, salt) = hasher.Hash(password);
            users.Add(new User
            {
                Email = email,
                UserName = userName,
                PasswordHash = hash,
                Salt = salt,
                Role = role,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });
        }

        await db.Users()
            .InsertBatch(u => (u.Email, u.UserName, u.PasswordHash, u.Salt, u.Role, u.IsActive, u.CreatedAt))
            .Values(users)
            .ExecuteNonQueryAsync();
    }
}
