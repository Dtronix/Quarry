using BenchmarkDotNet.Attributes;
using Microsoft.Data.Sqlite;
using Quarry.Benchmarks.Context;
using Quarry.Benchmarks.Infrastructure;
using SqlKata.Compilers;

namespace Quarry.Benchmarks.Benchmarks;

public abstract class BenchmarkBase
{
    protected SqliteConnection Connection { get; private set; } = null!;
    protected BenchDb QuarryDb { get; private set; } = null!;
    protected EfBenchContext EfContext { get; private set; } = null!;
    protected SqliteCompiler SqlKataCompiler { get; private set; } = null!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        Connection = new SqliteConnection("Data Source=BenchDb;Mode=Memory;Cache=Shared");
        Connection.Open();

        DatabaseSetup.CreateAndSeed(Connection);

        QuarryDb = new BenchDb(Connection);
        EfContext = CreateEfContext();
        SqlKataCompiler = new SqliteCompiler();
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        QuarryDb?.Dispose();
        EfContext?.Dispose();
        Connection?.Close();
        Connection?.Dispose();
    }

    protected EfBenchContext CreateEfContext() => new(Connection);
}
