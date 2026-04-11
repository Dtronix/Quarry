using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Quarry.Migration;

namespace Quarry.Migration.Tests;

[TestFixture]
public class EfCoreDetectorTests
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
        public static Task<int> SumAsync<TSource>(this IQueryable<TSource> source, Expression<Func<TSource, int>> selector, CancellationToken ct = default)
            => Task.FromResult(0);
        public static IQueryable<TSource> AsNoTracking<TSource>(this IQueryable<TSource> source) where TSource : class
            => source;
        public static IQueryable<TEntity> Include<TEntity, TProperty>(this IQueryable<TEntity> source, Expression<Func<TEntity, TProperty>> path) where TEntity : class
            => source;
    }
}
";

    private static (SemanticModel model, SyntaxNode root) CreateCompilationForDetection(string userCode)
    {
        var userTree = CSharpSyntaxTree.ParseText(userCode);
        var efCoreTree = CSharpSyntaxTree.ParseText(EfCoreStub);

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
            new[] { userTree, efCoreTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var model = compilation.GetSemanticModel(userTree);
        return (model, userTree.GetRoot());
    }

    [Test]
    public void Detect_ToListAsync_DetectsCallSite()
    {
        var (model, root) = CreateCompilationForDetection(@"
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;

public class User { public int Id { get; set; } public string Name { get; set; } }

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

        var detector = new EfCoreDetector();
        var sites = detector.Detect(model, root);

        Assert.That(sites, Has.Count.EqualTo(1));
        Assert.That(sites[0].EntityTypeName, Is.EqualTo("User"));
        Assert.That(sites[0].TerminalMethod, Is.EqualTo("ToListAsync"));
        Assert.That(sites[0].Steps, Is.Empty);
    }

    [Test]
    public void Detect_WhereToListAsync_DetectsChain()
    {
        var (model, root) = CreateCompilationForDetection(@"
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;

public class User { public int Id { get; set; } public string Name { get; set; } }

public class AppDbContext : DbContext
{
    public DbSet<User> Users { get; set; }
}

public class Example
{
    public async Task Run(AppDbContext db)
    {
        var results = await db.Users.Where(u => u.Id > 5).ToListAsync();
    }
}
");

        var detector = new EfCoreDetector();
        var sites = detector.Detect(model, root);

        Assert.That(sites, Has.Count.EqualTo(1));
        Assert.That(sites[0].EntityTypeName, Is.EqualTo("User"));
        Assert.That(sites[0].TerminalMethod, Is.EqualTo("ToListAsync"));
        Assert.That(sites[0].Steps, Has.Count.EqualTo(1));
        Assert.That(sites[0].Steps[0].MethodName, Is.EqualTo("Where"));
    }

    [Test]
    public void Detect_MultiStepChain_DetectsAllSteps()
    {
        var (model, root) = CreateCompilationForDetection(@"
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;

public class User { public int Id { get; set; } public string Name { get; set; } }

public class AppDbContext : DbContext
{
    public DbSet<User> Users { get; set; }
}

public class Example
{
    public async Task Run(AppDbContext db)
    {
        var results = await db.Users
            .Where(u => u.Id > 5)
            .OrderBy(u => u.Name)
            .Take(10)
            .ToListAsync();
    }
}
");

        var detector = new EfCoreDetector();
        var sites = detector.Detect(model, root);

        Assert.That(sites, Has.Count.EqualTo(1));
        Assert.That(sites[0].Steps, Has.Count.EqualTo(3));
        Assert.That(sites[0].Steps[0].MethodName, Is.EqualTo("Where"));
        Assert.That(sites[0].Steps[1].MethodName, Is.EqualTo("OrderBy"));
        Assert.That(sites[0].Steps[2].MethodName, Is.EqualTo("Take"));
    }

    [Test]
    public void Detect_FirstAsync_DetectsTerminal()
    {
        var (model, root) = CreateCompilationForDetection(@"
using System.Threading.Tasks;
using System.Linq;
using Microsoft.EntityFrameworkCore;

public class User { public int Id { get; set; } }

public class AppDbContext : DbContext
{
    public DbSet<User> Users { get; set; }
}

public class Example
{
    public async Task Run(AppDbContext db)
    {
        var user = await db.Users.Where(u => u.Id == 1).FirstAsync();
    }
}
");

        var detector = new EfCoreDetector();
        var sites = detector.Detect(model, root);

        Assert.That(sites, Has.Count.EqualTo(1));
        Assert.That(sites[0].TerminalMethod, Is.EqualTo("FirstAsync"));
    }

    [Test]
    public void Detect_UnsupportedMethod_Flagged()
    {
        var (model, root) = CreateCompilationForDetection(@"
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;

public class User { public int Id { get; set; } public string Name { get; set; } }

public class AppDbContext : DbContext
{
    public DbSet<User> Users { get; set; }
}

public class Example
{
    public async Task Run(AppDbContext db)
    {
        var results = await db.Users.AsNoTracking().Where(u => u.Id > 5).ToListAsync();
    }
}
");

        var detector = new EfCoreDetector();
        var sites = detector.Detect(model, root);

        Assert.That(sites, Has.Count.EqualTo(1));
        Assert.That(sites[0].UnsupportedMethods, Contains.Item("AsNoTracking"));
    }

    [Test]
    public void Detect_DbContextSet_DetectsCallSite()
    {
        var (model, root) = CreateCompilationForDetection(@"
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;

public class User { public int Id { get; set; } }

public class AppDbContext : DbContext { }

public class Example
{
    public async Task Run(AppDbContext db)
    {
        var results = await db.Set<User>().ToListAsync();
    }
}
");

        var detector = new EfCoreDetector();
        var sites = detector.Detect(model, root);

        Assert.That(sites, Has.Count.EqualTo(1));
        Assert.That(sites[0].EntityTypeName, Is.EqualTo("User"));
    }

    [Test]
    public void Detect_NonEfCoreQueryable_NotDetected()
    {
        var (model, root) = CreateCompilationForDetection(@"
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;

public class User { public int Id { get; set; } }

public class Example
{
    public void Run()
    {
        var list = new List<User>();
        var results = list.Where(u => u.Id > 5).ToList();
    }
}
");

        var detector = new EfCoreDetector();
        var sites = detector.Detect(model, root);

        Assert.That(sites, Is.Empty);
    }

    [Test]
    public void Detect_SelectProjection_DetectsStep()
    {
        var (model, root) = CreateCompilationForDetection(@"
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;

public class User { public int Id { get; set; } public string Name { get; set; } }

public class AppDbContext : DbContext
{
    public DbSet<User> Users { get; set; }
}

public class Example
{
    public async Task Run(AppDbContext db)
    {
        var results = await db.Users.Select(u => u.Name).ToListAsync();
    }
}
");

        var detector = new EfCoreDetector();
        var sites = detector.Detect(model, root);

        Assert.That(sites, Has.Count.EqualTo(1));
        Assert.That(sites[0].Steps, Has.Count.EqualTo(1));
        Assert.That(sites[0].Steps[0].MethodName, Is.EqualTo("Select"));
    }

    [Test]
    public void Detect_OrderByDescending_DetectsStep()
    {
        var (model, root) = CreateCompilationForDetection(@"
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;

public class User { public int Id { get; set; } public string Name { get; set; } }

public class AppDbContext : DbContext
{
    public DbSet<User> Users { get; set; }
}

public class Example
{
    public async Task Run(AppDbContext db)
    {
        var results = await db.Users.OrderByDescending(u => u.Id).ToListAsync();
    }
}
");

        var detector = new EfCoreDetector();
        var sites = detector.Detect(model, root);

        Assert.That(sites, Has.Count.EqualTo(1));
        Assert.That(sites[0].Steps, Has.Count.EqualTo(1));
        Assert.That(sites[0].Steps[0].MethodName, Is.EqualTo("OrderByDescending"));
    }
}
