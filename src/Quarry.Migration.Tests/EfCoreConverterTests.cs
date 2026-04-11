using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Quarry.Migration;

namespace Quarry.Migration.Tests;

[TestFixture]
public class EfCoreConverterTests
{
    private static readonly string EfCoreStub = @"
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.EntityFrameworkCore
{
    public class DbContext
    {
        public DbSet<T> Set<T>() where T : class => null!;
    }

    public abstract class DbSet<TEntity> : IQueryable<TEntity> where TEntity : class
    {
        public Type ElementType => typeof(TEntity);
        public Expression Expression => null!;
        public IQueryProvider Provider => null!;
        public IEnumerator<TEntity> GetEnumerator() => null!;
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => null!;
    }

    public static class EntityFrameworkQueryableExtensions
    {
        public static Task<List<TSource>> ToListAsync<TSource>(this IQueryable<TSource> source, CancellationToken ct = default)
            => Task.FromResult(new List<TSource>());
        public static Task<TSource> FirstAsync<TSource>(this IQueryable<TSource> source, CancellationToken ct = default)
            => Task.FromResult<TSource>(default!);
        public static Task<TSource> FirstOrDefaultAsync<TSource>(this IQueryable<TSource> source, CancellationToken ct = default)
            => Task.FromResult<TSource>(default!);
        public static Task<TSource> SingleAsync<TSource>(this IQueryable<TSource> source, CancellationToken ct = default)
            => Task.FromResult<TSource>(default!);
        public static Task<TSource> SingleOrDefaultAsync<TSource>(this IQueryable<TSource> source, CancellationToken ct = default)
            => Task.FromResult<TSource>(default!);
        public static Task<int> CountAsync<TSource>(this IQueryable<TSource> source, CancellationToken ct = default)
            => Task.FromResult(0);
        public static IQueryable<TSource> AsNoTracking<TSource>(this IQueryable<TSource> source) where TSource : class
            => source;
        public static IQueryable<TEntity> Include<TEntity, TProperty>(this IQueryable<TEntity> source, Expression<Func<TEntity, TProperty>> path) where TEntity : class
            => source;
    }
}
";

    private static readonly string QuarryStub = @"
namespace Quarry
{
    public abstract class Schema
    {
        protected virtual NamingStyle NamingStyle => NamingStyle.Exact;
        protected static ColumnBuilder<T> Identity<T>() => default;
        protected static ColumnBuilder<T> Length<T>(int maxLength) => default;
    }
    public enum NamingStyle { Exact = 0, SnakeCase = 1 }
    public readonly struct Col<T>
    {
        public static implicit operator Col<T>(ColumnBuilder<T> builder) => default;
    }
    public readonly struct Key<T>
    {
        public static implicit operator Key<T>(ColumnBuilder<T> builder) => default;
    }
    public readonly struct ColumnBuilder<T>
    {
        public ColumnBuilder<T> Identity() => default;
        public ColumnBuilder<T> Length(int maxLength) => default;
    }
}
";

    private static IReadOnlyList<EfCoreConversionEntry> Convert(string userCode)
    {
        var userTree = CSharpSyntaxTree.ParseText(userCode);
        var efCoreTree = CSharpSyntaxTree.ParseText(EfCoreStub);
        var quarryTree = CSharpSyntaxTree.ParseText(QuarryStub);

        var runtimeDir = System.IO.Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var references = new MetadataReference[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Linq.IQueryable).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Linq.Enumerable).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Linq.Expressions.Expression).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Threading.Tasks.Task).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Collections.Generic.IEnumerable<>).Assembly.Location),
            MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "System.Runtime.dll")),
        };

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { userTree, efCoreTree, quarryTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var converter = new EfCoreConverter();
        return converter.ConvertAll(compilation);
    }

    [Test]
    public void SimpleToListAsync_Converts()
    {
        var entries = Convert(@"
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Quarry;

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId => Identity<int>();
    public Col<string> UserName => Length<string>(100);
}

public class User { public int UserId { get; set; } public string UserName { get; set; } }

public class AppDbContext : DbContext
{
    public DbSet<User> Users { get; set; }
}

public class Example
{
    public async Task Run(AppDbContext db)
    {
        var results = await db.Users.ToListAsync();
    }
}
");

        Assert.That(entries, Has.Count.EqualTo(1));
        Assert.That(entries[0].IsConvertible, Is.True);
        Assert.That(entries[0].ChainCode, Does.Contain(".Users()"));
        Assert.That(entries[0].ChainCode, Does.Contain(".ExecuteFetchAllAsync()"));
    }

    [Test]
    public void WhereOrderByTake_Converts()
    {
        var entries = Convert(@"
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Quarry;

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId => Identity<int>();
    public Col<string> UserName => Length<string>(100);
}

public class User { public int UserId { get; set; } public string UserName { get; set; } }

public class AppDbContext : DbContext
{
    public DbSet<User> Users { get; set; }
}

public class Example
{
    public async Task Run(AppDbContext db)
    {
        var results = await db.Users
            .Where(u => u.UserId > 5)
            .OrderBy(u => u.UserName)
            .Take(10)
            .ToListAsync();
    }
}
");

        Assert.That(entries, Has.Count.EqualTo(1));
        Assert.That(entries[0].IsConvertible, Is.True);
        Assert.That(entries[0].ChainCode, Does.Contain(".Where("));
        Assert.That(entries[0].ChainCode, Does.Contain(".OrderBy("));
        Assert.That(entries[0].ChainCode, Does.Contain(".Limit(10)"));
        Assert.That(entries[0].ChainCode, Does.Contain(".ExecuteFetchAllAsync()"));
    }

    [Test]
    public void OrderByDescending_MapsToDirectionDescending()
    {
        var entries = Convert(@"
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Quarry;

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId => Identity<int>();
}

public class User { public int UserId { get; set; } }

public class AppDbContext : DbContext
{
    public DbSet<User> Users { get; set; }
}

public class Example
{
    public async Task Run(AppDbContext db)
    {
        var results = await db.Users.OrderByDescending(u => u.UserId).ToListAsync();
    }
}
");

        Assert.That(entries, Has.Count.EqualTo(1));
        Assert.That(entries[0].ChainCode, Does.Contain("Direction.Descending"));
    }

    [Test]
    public void FirstAsync_MapsToExecuteFetchFirstAsync()
    {
        var entries = Convert(@"
using System.Threading.Tasks;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Quarry;

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId => Identity<int>();
}

public class User { public int UserId { get; set; } }

public class AppDbContext : DbContext
{
    public DbSet<User> Users { get; set; }
}

public class Example
{
    public async Task Run(AppDbContext db)
    {
        var user = await db.Users.Where(u => u.UserId == 1).FirstAsync();
    }
}
");

        Assert.That(entries, Has.Count.EqualTo(1));
        Assert.That(entries[0].ChainCode, Does.Contain(".ExecuteFetchFirstAsync()"));
    }

    [Test]
    public void UnsupportedAsNoTracking_ProducesWarning()
    {
        var entries = Convert(@"
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Quarry;

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId => Identity<int>();
}

public class User { public int UserId { get; set; } }

public class AppDbContext : DbContext
{
    public DbSet<User> Users { get; set; }
}

public class Example
{
    public async Task Run(AppDbContext db)
    {
        var results = await db.Users.AsNoTracking().Where(u => u.UserId > 5).ToListAsync();
    }
}
");

        Assert.That(entries, Has.Count.EqualTo(1));
        Assert.That(entries[0].IsConvertible, Is.True);
        Assert.That(entries[0].HasWarnings, Is.True);
        Assert.That(entries[0].Diagnostics.Any(d => d.Message.Contains("AsNoTracking")), Is.True);
    }

    [Test]
    public void NoSchemaMatch_ReturnsNotConvertible()
    {
        var entries = Convert(@"
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Quarry;

public class Product { public int Id { get; set; } }

public class AppDbContext : DbContext
{
    public DbSet<Product> Products { get; set; }
}

public class Example
{
    public async Task Run(AppDbContext db)
    {
        var results = await db.Products.ToListAsync();
    }
}
");

        Assert.That(entries, Has.Count.EqualTo(1));
        Assert.That(entries[0].IsConvertible, Is.False);
    }

    [Test]
    public void SkipAndTake_MapToOffsetAndLimit()
    {
        var entries = Convert(@"
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Quarry;

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId => Identity<int>();
}

public class User { public int UserId { get; set; } }

public class AppDbContext : DbContext
{
    public DbSet<User> Users { get; set; }
}

public class Example
{
    public async Task Run(AppDbContext db)
    {
        var results = await db.Users.Skip(20).Take(10).ToListAsync();
    }
}
");

        Assert.That(entries, Has.Count.EqualTo(1));
        Assert.That(entries[0].ChainCode, Does.Contain(".Offset(20)"));
        Assert.That(entries[0].ChainCode, Does.Contain(".Limit(10)"));
    }

    [Test]
    public void CountAsync_MapsToScalar()
    {
        var entries = Convert(@"
using System.Threading.Tasks;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Quarry;

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId => Identity<int>();
}

public class User { public int UserId { get; set; } }

public class AppDbContext : DbContext
{
    public DbSet<User> Users { get; set; }
}

public class Example
{
    public async Task Run(AppDbContext db)
    {
        var count = await db.Users.CountAsync();
    }
}
");

        Assert.That(entries, Has.Count.EqualTo(1));
        Assert.That(entries[0].ChainCode, Does.Contain(".ExecuteScalarAsync<int>()"));
    }

    [Test]
    public void Distinct_MapsToDistinct()
    {
        var entries = Convert(@"
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Quarry;

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId => Identity<int>();
    public Col<string> UserName => Length<string>(100);
}

public class User { public int UserId { get; set; } public string UserName { get; set; } }

public class AppDbContext : DbContext
{
    public DbSet<User> Users { get; set; }
}

public class Example
{
    public async Task Run(AppDbContext db)
    {
        var results = await db.Users.Distinct().ToListAsync();
    }
}
");

        Assert.That(entries, Has.Count.EqualTo(1));
        Assert.That(entries[0].ChainCode, Does.Contain(".Distinct()"));
    }
}
