using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Quarry.Migration;

namespace Quarry.Migration.Tests;

[TestFixture]
public class EfCoreMigrationCodeFixTests
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

    private static async Task<(string? transformedSource, List<CodeAction> actions)> ApplyCodeFixAsync(string userCode)
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

        // Run analyzer to get real diagnostics
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { userTree, efCoreTree, quarryTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var analyzer = new EfCoreMigrationAnalyzer();
        var compilationWithAnalyzers = compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(analyzer));
        var diagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
        var migrationDiagnostics = diagnostics.Where(d => d.Id.StartsWith("QRM")).ToList();

        if (migrationDiagnostics.Count == 0)
            return (null, new List<CodeAction>());

        // Build workspace with the same source and references
        var workspace = new AdhocWorkspace();
        var projectInfo = ProjectInfo.Create(
            ProjectId.CreateNewId(),
            VersionStamp.Default,
            "TestProject",
            "TestProject",
            LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
            metadataReferences: references);
        var project = workspace.AddProject(projectInfo);
        project = project.AddDocument("EfCore.cs", SourceText.From(EfCoreStub)).Project;
        project = project.AddDocument("Quarry.cs", SourceText.From(QuarryStub)).Project;
        var document = project.AddDocument("Test.cs", SourceText.From(userCode));
        project = document.Project;

        // Apply workspace changes
        workspace.TryApplyChanges(project.Solution);
        document = workspace.CurrentSolution.GetDocument(document.Id)!;

        var fix = new EfCoreMigrationCodeFix();
        var fixableDiagnostic = migrationDiagnostics
            .FirstOrDefault(d => fix.FixableDiagnosticIds.Contains(d.Id));

        if (fixableDiagnostic == null)
            return (null, new List<CodeAction>());

        var actions = new List<CodeAction>();
        var context = new CodeFixContext(
            document,
            fixableDiagnostic,
            (action, _) => actions.Add(action),
            default);

        await fix.RegisterCodeFixesAsync(context);

        if (actions.Count == 0)
            return (null, actions);

        var operations = await actions[0].GetOperationsAsync(default);
        var changedSolution = operations.OfType<ApplyChangesOperation>().First().ChangedSolution;
        var changedDocument = changedSolution.GetDocument(document.Id)!;
        var newText = await changedDocument.GetTextAsync();

        return (newText.ToString(), actions);
    }

    // ── Registration tests ──

    [Test]
    public void FixableDiagnosticIds_ContainsExpectedIds()
    {
        var fix = new EfCoreMigrationCodeFix();
        Assert.That(fix.FixableDiagnosticIds, Does.Contain("QRM011"));
        Assert.That(fix.FixableDiagnosticIds, Does.Contain("QRM012"));
    }

    [Test]
    public void HasFixAllProvider()
    {
        var fix = new EfCoreMigrationCodeFix();
        Assert.That(fix.GetFixAllProvider(), Is.Not.Null);
    }

    // ── Functional tests ──

    [Test]
    public async Task SimpleToListAsync_ReplacesWithChainApi()
    {
        var (source, actions) = await ApplyCodeFixAsync(@"
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

        Assert.That(actions, Has.Count.EqualTo(1));
        Assert.That(source, Is.Not.Null);
        Assert.That(source, Does.Contain(".Users()"));
        Assert.That(source, Does.Contain(".ExecuteFetchAllAsync()"));
    }

    [Test]
    public async Task AwaitPreserved_WhenOriginalIsAwaited()
    {
        var (source, actions) = await ApplyCodeFixAsync(@"
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

        Assert.That(source, Is.Not.Null);
        Assert.That(source, Does.Contain("await "));
    }

    [Test]
    public async Task UsingDirectivesAdded()
    {
        var (source, _) = await ApplyCodeFixAsync(@"
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

        Assert.That(source, Is.Not.Null);
        Assert.That(source, Does.Contain("using Quarry;"));
        Assert.That(source, Does.Contain("using Quarry.Query;"));
    }

    [Test]
    public async Task NonFixableDiagnostic_QRM013_NoCodeAction()
    {
        // No schema match → analyzer reports QRM013 → code fix should not apply
        var (source, actions) = await ApplyCodeFixAsync(@"
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

        Assert.That(source, Is.Null);
        Assert.That(actions, Is.Empty);
    }
}
