using System.Data;
using BenchmarkDotNet.Attributes;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Quarry.Benchmarks.Infrastructure;
using SqlKata;
using SqlKata.Compilers;

namespace Quarry.Benchmarks.Benchmarks;

// NOTE — Reader floor for decimal columns on SQLite (canonical explanation; other
// decimal-reading benchmarks reference this file):
//
// Microsoft.Data.Sqlite.GetDecimal(int) is implemented as:
//     decimal.Parse(GetString(ordinal), NumberStyles.Number | AllowExponent, InvariantCulture)
// (see SqliteValueReader.cs in dotnet/efcore). That's a string allocation +
// culture-aware parse per cell — ~12 µs of the per-query floor in these benchmarks.
// Quarry's generated reader emits `r.GetDecimal(N)` and hits the same path, so
// Quarry tracks the hand-rolled Raw baseline within noise (~0.2%).
//
// Dapper's IL-emitted deserializer reads every column through the DbDataReader
// indexer (`get_Item(int)`), which delegates to GetValue → SQLite REAL column
// returns a boxed double → IL unbox + `(decimal)(double)box` conversion.
// Verified empirically by wrapping the reader in a logging proxy; for a
// (int, decimal) projection Dapper called only `get_Item(int)`, never
// GetDecimal/GetValue/GetFieldValue<T>/GetString. That skips the string-parse
// path (faster) but boxes per cell (more allocation, and quietly loses precision
// past ~15 significant digits). The benchmark numbers reflect that trade.
//
// Quarry intentionally does NOT do the (decimal)GetDouble(ordinal) trick: silent
// precision loss on Col<decimal> would corrupt currency/financial round-trips.
// The floor here is a Microsoft.Data.Sqlite driver characteristic, not a library
// overhead.
public class CteSimpleBenchmarks : BenchmarkBase
{
    private const string SimpleCteFilterSql = """
        WITH cte AS (
            SELECT OrderId, UserId, Total, Status, OrderDate, Notes
            FROM orders WHERE Total > 50
        )
        SELECT OrderId, Total FROM cte
        """;

    [Benchmark(Baseline = true)]
    public async Task<List<OrderIdTotalDto>> Raw_SimpleCte()
    {
        await using var cmd = Connection.CreateCommand();
        cmd.CommandText = SimpleCteFilterSql;
        await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SingleResult);
        var results = new List<OrderIdTotalDto>();
        while (await reader.ReadAsync())
        {
            results.Add(new OrderIdTotalDto
            {
                OrderId = reader.GetInt32(0),
                Total = reader.GetDouble(1)
            });
        }
        return results;
    }

    [Benchmark]
    public async Task<List<OrderIdTotalDto>> Dapper_SimpleCte()
    {
        return (await Connection.QueryAsync<OrderIdTotalDto>(SimpleCteFilterSql)).AsList();
    }

    [Benchmark]
    public async Task<List<OrderIdTotalDto>> EfCore_SimpleCte_RawFallback()
    {
        return await EfContext.Database
            .SqlQueryRaw<OrderIdTotalDto>(SimpleCteFilterSql)
            .ToListAsync();
    }

    [Benchmark]
    public async Task<List<OrderIdTotalDto>> Quarry_SimpleCte()
    {
        return await QuarryDb
            .With<Order>(orders => orders.Where(o => o.Total > 50))
            .FromCte<Order>()
            .Select(o => new OrderIdTotalDto
            {
                OrderId = o.OrderId,
                Total = o.Total
            })
            .ExecuteFetchAllAsync();
    }

    [Benchmark]
    public async Task<List<OrderIdTotalDto>> SqlKata_SimpleCte_RawFallback()
    {
        // SqlKata has no native CTE support; use raw SQL.
        await using var cmd = Connection.CreateCommand();
        cmd.CommandText = SimpleCteFilterSql;
        await using var reader = await cmd.ExecuteReaderAsync();
        var results = new List<OrderIdTotalDto>();
        while (await reader.ReadAsync())
        {
            results.Add(new OrderIdTotalDto
            {
                OrderId = reader.GetInt32(0),
                Total = reader.GetDouble(1)
            });
        }
        return results;
    }
}
