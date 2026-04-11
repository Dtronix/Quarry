using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Quarry.Migration;

namespace Quarry.Migration.Tests;

[TestFixture]
public class EfCoreMigrationAnalyzerTests
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
        public static IQueryable<TSource> AsNoTracking<TSource>(this IQueryable<TSource> source) where TSource : class
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

    private static async Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync(string userCode)
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

        var analyzer = new EfCoreMigrationAnalyzer();
        var compilationWithAnalyzers = compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(analyzer));
        var diagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();

        return diagnostics.Where(d => d.Id.StartsWith("QRM")).ToImmutableArray();
    }

    [Test]
    public async Task ConvertibleQuery_ReportsQRM011()
    {
        var diagnostics = await GetDiagnosticsAsync(@"
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
        var results = await db.Users.ToListAsync();
    }
}
");

        Assert.That(diagnostics.Any(d => d.Id == "QRM011"), Is.True,
            $"Expected QRM011. Got: {string.Join(", ", diagnostics.Select(d => d.Id + ": " + d.GetMessage()))}");
    }

    [Test]
    public async Task UnsupportedMethodChain_ReportsQRM012()
    {
        var diagnostics = await GetDiagnosticsAsync(@"
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
        var results = await db.Users.AsNoTracking().ToListAsync();
    }
}
");

        Assert.That(diagnostics.Any(d => d.Id == "QRM012"), Is.True,
            $"Expected QRM012. Got: {string.Join(", ", diagnostics.Select(d => d.Id + ": " + d.GetMessage()))}");
    }

    [Test]
    public async Task NoSchemaMatch_ReportsQRM013()
    {
        var diagnostics = await GetDiagnosticsAsync(@"
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

        Assert.That(diagnostics.Any(d => d.Id == "QRM013"), Is.True,
            $"Expected QRM013. Got: {string.Join(", ", diagnostics.Select(d => d.Id + ": " + d.GetMessage()))}");
    }

    [Test]
    public async Task NoEfCoreReference_NoDiagnostics()
    {
        var userTree = CSharpSyntaxTree.ParseText(@"
public class Example
{
    public void Run() { }
}
");

        var quarryTree = CSharpSyntaxTree.ParseText(QuarryStub);
        var runtimeDir = System.IO.Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var references = new MetadataReference[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "System.Runtime.dll")),
        };

        var compilation = CSharpCompilation.Create("TestAssembly",
            new[] { userTree, quarryTree }, references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var analyzer = new EfCoreMigrationAnalyzer();
        var compilationWithAnalyzers = compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(analyzer));
        var diagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();

        Assert.That(diagnostics.Where(d => d.Id.StartsWith("QRM")), Is.Empty);
    }
}
