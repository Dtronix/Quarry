using Microsoft.Data.Sqlite;

namespace Quarry.Benchmarks.Infrastructure;

public static class DatabaseSetup
{
    private static readonly string[] Statuses = ["pending", "shipped", "delivered", "cancelled"];

    public static void CreateAndSeed(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();

        // Create tables
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS users (
                UserId INTEGER PRIMARY KEY AUTOINCREMENT,
                UserName TEXT NOT NULL,
                Email TEXT,
                IsActive INTEGER NOT NULL DEFAULT 1,
                CreatedAt TEXT NOT NULL DEFAULT (datetime('now')),
                LastLogin TEXT
            );

            CREATE TABLE IF NOT EXISTS orders (
                OrderId INTEGER PRIMARY KEY AUTOINCREMENT,
                UserId INTEGER NOT NULL,
                Total REAL NOT NULL,
                Status TEXT NOT NULL,
                OrderDate TEXT NOT NULL DEFAULT (datetime('now')),
                Notes TEXT,
                FOREIGN KEY (UserId) REFERENCES users(UserId)
            );

            CREATE TABLE IF NOT EXISTS order_items (
                OrderItemId INTEGER PRIMARY KEY AUTOINCREMENT,
                OrderId INTEGER NOT NULL,
                ProductName TEXT NOT NULL,
                Quantity INTEGER NOT NULL,
                UnitPrice REAL NOT NULL,
                LineTotal REAL NOT NULL,
                FOREIGN KEY (OrderId) REFERENCES orders(OrderId)
            );

            -- Views mapping entity class names to actual table names.
            -- The Quarry join interceptor uses entity type names as table identifiers.
            CREATE VIEW IF NOT EXISTS "Order" AS SELECT * FROM orders;
            CREATE VIEW IF NOT EXISTS "OrderItem" AS SELECT * FROM order_items;
            """;
        cmd.ExecuteNonQuery();

        // Seed 100 users
        for (int i = 1; i <= 100; i++)
        {
            cmd.CommandText = "INSERT INTO users (UserName, Email, IsActive, CreatedAt) VALUES (@name, @email, @active, @created)";
            cmd.Parameters.Clear();
            cmd.Parameters.AddWithValue("@name", $"User{i:D3}");
            cmd.Parameters.AddWithValue("@email", i % 5 == 0 ? (object)DBNull.Value : $"user{i:D3}@example.com");
            cmd.Parameters.AddWithValue("@active", i % 10 != 0 ? 1 : 0); // 90% active
            cmd.Parameters.AddWithValue("@created", DateTime.UtcNow.AddDays(-i).ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.ExecuteNonQuery();
        }

        // Seed 100 orders (one per user)
        for (int i = 1; i <= 100; i++)
        {
            cmd.CommandText = "INSERT INTO orders (UserId, Total, Status, OrderDate) VALUES (@userId, @total, @status, @date)";
            cmd.Parameters.Clear();
            cmd.Parameters.AddWithValue("@userId", i);
            cmd.Parameters.AddWithValue("@total", Math.Round(10.0m + (i * 1.5m), 2));
            cmd.Parameters.AddWithValue("@status", Statuses[i % Statuses.Length]);
            cmd.Parameters.AddWithValue("@date", DateTime.UtcNow.AddDays(-i).ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.ExecuteNonQuery();
        }

        // Seed ~250 order items (2-3 per order)
        int itemId = 0;
        for (int orderId = 1; orderId <= 100; orderId++)
        {
            int itemCount = 2 + (orderId % 2); // alternates between 2 and 3
            for (int j = 1; j <= itemCount; j++)
            {
                itemId++;
                decimal unitPrice = Math.Round(5.0m + (itemId % 20) * 2.5m, 2);
                int qty = 1 + (itemId % 5);
                cmd.CommandText = "INSERT INTO order_items (OrderId, ProductName, Quantity, UnitPrice, LineTotal) VALUES (@orderId, @name, @qty, @price, @total)";
                cmd.Parameters.Clear();
                cmd.Parameters.AddWithValue("@orderId", orderId);
                cmd.Parameters.AddWithValue("@name", $"Product{itemId:D3}");
                cmd.Parameters.AddWithValue("@qty", qty);
                cmd.Parameters.AddWithValue("@price", unitPrice);
                cmd.Parameters.AddWithValue("@total", unitPrice * qty);
                cmd.ExecuteNonQuery();
            }
        }
    }
}
