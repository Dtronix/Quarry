using System.Data;
using Dapper;

namespace DapperMigration;

// ============================================================================
// These Dapper calls demonstrate what Quarry.Migration detects and converts.
//
// With Quarry.Migration referenced as an analyzer, each method below will
// show a QRM001 lightbulb in the IDE offering to convert to Quarry chain API.
//
// Run 'quarry convert --from dapper -p .' to see all conversions at once.
// ============================================================================

public class DapperQueries
{
    private readonly IDbConnection _connection;

    public DapperQueries(IDbConnection connection) => _connection = connection;

    // ── Simple SELECT * ────────────────────────────────────
    // QRM001: Convert to → db.Users().Select(u => u).ExecuteFetchAllAsync()
    public async Task<IEnumerable<User>> GetAllUsers()
    {
        return await _connection.QueryAsync<User>(
            "SELECT * FROM users");
    }

    // ── SELECT with WHERE and parameters ───────────────────
    // QRM001: Convert to → db.Users().Where(u => u.IsActive == 1).Select(u => u).ExecuteFetchAllAsync()
    public async Task<IEnumerable<User>> GetActiveUsers()
    {
        return await _connection.QueryAsync<User>(
            "SELECT * FROM users WHERE is_active = 1");
    }

    // ── SELECT with parameterized WHERE ────────────────────
    // QRM001: Convert to → db.Users().Where(u => u.UserId == userId).Select(u => u).ExecuteFetchFirstAsync()
    public async Task<User> GetUserById(int userId)
    {
        return await _connection.QueryFirstAsync<User>(
            "SELECT * FROM users WHERE user_id = @userId",
            new { userId });
    }

    // ── Column projection ──────────────────────────────────
    // QRM001: Convert to → db.Users().Select(u => (u.UserId, u.UserName, u.Email)).ExecuteFetchAllAsync()
    public async Task<IEnumerable<dynamic>> GetUserNames()
    {
        return await _connection.QueryAsync<dynamic>(
            "SELECT user_id, user_name, email FROM users");
    }

    // ── JOIN query ─────────────────────────────────────────
    // QRM001: Convert to → db.Users().Join<OrderSchema>(...).Where(...).Select(...).ExecuteFetchAllAsync()
    public async Task<IEnumerable<dynamic>> GetUserOrders(int userId)
    {
        return await _connection.QueryAsync<dynamic>(
            @"SELECT u.user_name, o.total, o.status
              FROM users u
              INNER JOIN orders o ON u.user_id = o.user_id
              WHERE u.user_id = @userId",
            new { userId });
    }

    // ── LEFT JOIN ──────────────────────────────────────────
    // QRM001: Convert to → db.Users().LeftJoin<OrderSchema>(...).Select(...).ExecuteFetchAllAsync()
    public async Task<IEnumerable<dynamic>> GetUsersWithOrders()
    {
        return await _connection.QueryAsync<dynamic>(
            @"SELECT u.user_name, o.total
              FROM users u
              LEFT JOIN orders o ON u.user_id = o.user_id");
    }

    // ── Aggregation with GROUP BY ──────────────────────────
    // QRM001: Convert to → db.Orders().GroupBy(o => o.UserId).Select(...).ExecuteFetchAllAsync()
    public async Task<IEnumerable<dynamic>> GetOrderTotalsByUser()
    {
        return await _connection.QueryAsync<dynamic>(
            @"SELECT user_id, COUNT(*), SUM(total)
              FROM orders
              GROUP BY user_id");
    }

    // ── ORDER BY with LIMIT ────────────────────────────────
    // QRM001: Convert to → db.Orders().OrderBy(o => o.Total, Direction.Descending).Limit(10).Select(...)...
    public async Task<IEnumerable<dynamic>> GetTopOrders()
    {
        return await _connection.QueryAsync<dynamic>(
            "SELECT * FROM orders ORDER BY total DESC LIMIT 10");
    }

    // ── LIKE with parameter ────────────────────────────────
    // QRM001: Convert to → db.Users().Where(u => Sql.Like(u.UserName, pattern)).Select(...)...
    public async Task<IEnumerable<User>> SearchUsers(string pattern)
    {
        return await _connection.QueryAsync<User>(
            "SELECT * FROM users WHERE user_name LIKE @pattern",
            new { pattern });
    }

    // ── IN expression ──────────────────────────────────────
    // QRM001: Convert to → db.Users().Where(u => new[] { 1, 2, 3 }.Contains(u.UserId)).Select(...)...
    public async Task<IEnumerable<User>> GetSpecificUsers()
    {
        return await _connection.QueryAsync<User>(
            "SELECT * FROM users WHERE user_id IN (1, 2, 3)");
    }

    // ── IS NULL check ──────────────────────────────────────
    // QRM001: Convert to → db.Products().Where(p => p.Category == null).Select(...)...
    public async Task<IEnumerable<dynamic>> GetUncategorizedProducts()
    {
        return await _connection.QueryAsync<dynamic>(
            "SELECT * FROM products WHERE category IS NULL");
    }

    // ── BETWEEN expression ─────────────────────────────────
    // QRM001: Convert to → db.Products().Where(p => p.Price >= min && p.Price <= max).Select(...)...
    public async Task<IEnumerable<dynamic>> GetProductsByPriceRange(decimal min, decimal max)
    {
        return await _connection.QueryAsync<dynamic>(
            "SELECT * FROM products WHERE price BETWEEN @min AND @max",
            new { min, max });
    }

    // ── ExecuteAsync (non-query) ───────────────────────────
    // QRM003: Cannot convert — DELETE not supported by chain translator
    public async Task<int> DeactivateUser(int userId)
    {
        return await _connection.ExecuteAsync(
            "DELETE FROM users WHERE user_id = @userId",
            new { userId });
    }

    // ── Non-literal SQL (dynamic) ──────────────────────────
    // Not detected — DapperDetector skips non-literal SQL strings
    public async Task<IEnumerable<dynamic>> DynamicQuery(string sql)
    {
        return await _connection.QueryAsync<dynamic>(sql);
    }
}

// Simple DTOs matching the Dapper result shape
public class User
{
    public int UserId { get; set; }
    public string UserName { get; set; } = "";
    public string Email { get; set; } = "";
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class Order
{
    public int OrderId { get; set; }
    public int UserId { get; set; }
    public decimal Total { get; set; }
    public string Status { get; set; } = "";
    public DateTime OrderDate { get; set; }
}
