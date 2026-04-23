using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Quarry.Generators;

namespace Quarry.Tests;

/// <summary>
/// Generator pipeline tests for RawSqlAsync and RawSqlScalarAsync interception.
/// Compiles real C# source through the generator and verifies interceptor output.
/// </summary>
[TestFixture]
public class RawSqlGeneratorPipelineTests
{
    private static readonly string QuarryCoreAssemblyPath = typeof(Schema).Assembly.Location;
    private static readonly string SystemRuntimeAssemblyPath = typeof(object).Assembly.Location;

    private static CSharpCompilation CreateCompilation(params string[] sources)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        var syntaxTrees = sources.Select(s => CSharpSyntaxTree.ParseText(s, parseOptions)).ToList();

        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(QuarryCoreAssemblyPath),
            MetadataReference.CreateFromFile(SystemRuntimeAssemblyPath),
            MetadataReference.CreateFromFile(typeof(System.Data.IDbConnection).Assembly.Location),
        };

        var runtimeDir = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
        references.Add(MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Runtime.dll")));
        references.Add(MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Collections.dll")));
        references.Add(MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Linq.dll")));
        references.Add(MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Linq.Expressions.dll")));
        references.Add(MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "netstandard.dll")));
        references.Add(MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Threading.dll")));
        references.Add(MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Threading.Tasks.dll")));

        return CSharpCompilation.Create(
            "TestAssembly",
            syntaxTrees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithNullableContextOptions(NullableContextOptions.Enable));
    }

    private static (ImmutableArray<Diagnostic> Diagnostics, GeneratorDriverRunResult Result) RunGeneratorWithDiagnostics(
        CSharpCompilation compilation)
    {
        var generator = new QuarryGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);
        return (diagnostics, driver.GetRunResult());
    }

    private static string? GetInterceptorsCode(GeneratorDriverRunResult result)
    {
        var interceptorsFile = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.Contains("Interceptors"));
        return interceptorsFile?.GetText().ToString();
    }

    #region RawSqlAsync Discovery Tests

    [Test]
    public void RawSqlAsync_CallSite_IsDiscovered_And_GeneratesInterceptor()
    {
        var source = @"
using Quarry;
using System.Threading.Tasks;

namespace TestApp;

public class UserDto
{
    public int UserId { get; set; }
    public string UserName { get; set; } = null!;
}

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId => Identity();
    public Col<string> UserName => Length(100);
}

[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<User> Users();
}

public class Service
{
    public async Task Test(TestDbContext db)
    {
        var results = await db.RawSqlAsync<UserDto>(""SELECT UserId, UserName FROM users"");
    }
}
";

        var compilation = CreateCompilation(source);
        var (diagnostics, result) = RunGeneratorWithDiagnostics(compilation);

        var code = GetInterceptorsCode(result);
        Assert.That(code, Is.Not.Null, "Should generate interceptors file");
        Assert.That(code, Does.Contain("RawSqlAsyncWithReader"),
            "Should generate RawSqlAsync interceptor calling RawSqlAsyncWithReader");
        Assert.That(code, Does.Contain("UserDto"),
            "Generated interceptor should reference the DTO type");
    }

    [Test]
    public void RawSqlScalarAsync_CallSite_IsDiscovered_And_GeneratesInterceptor()
    {
        var source = @"
using Quarry;
using System.Threading.Tasks;

namespace TestApp;

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId => Identity();
}

[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<User> Users();
}

public class Service
{
    public async Task Test(TestDbContext db)
    {
        var count = await db.RawSqlScalarAsync<int>(""SELECT COUNT(*) FROM users"");
    }
}
";

        var compilation = CreateCompilation(source);
        var (diagnostics, result) = RunGeneratorWithDiagnostics(compilation);

        var code = GetInterceptorsCode(result);
        Assert.That(code, Is.Not.Null, "Should generate interceptors file");
        Assert.That(code, Does.Contain("RawSqlScalarAsyncWithConverter"),
            "Should generate RawSqlScalarAsync interceptor calling RawSqlScalarAsyncWithConverter");
    }

    #endregion

    #region Non-Generic RawSql Ignored

    [Test]
    public void RawSqlNonQueryAsync_DoesNot_GenerateInterceptor()
    {
        var source = @"
using Quarry;
using System.Threading.Tasks;

namespace TestApp;

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId => Identity();
}

[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<User> Users();
}

public class Service
{
    public async Task Test(TestDbContext db)
    {
        await db.RawSqlNonQueryAsync(""DELETE FROM users WHERE UserId = @p0"", 1);
    }
}
";

        var compilation = CreateCompilation(source);
        var (diagnostics, result) = RunGeneratorWithDiagnostics(compilation);

        var code = GetInterceptorsCode(result);
        // Either no interceptors file at all, or it doesn't contain RawSqlNonQuery
        if (code != null)
        {
            Assert.That(code, Does.Not.Contain("RawSqlNonQuery"),
                "RawSqlNonQueryAsync should not produce an interceptor");
        }
    }

    #endregion

    #region Unresolvable Generic T

    [Test]
    public void RawSqlAsync_UnresolvableGenericT_EmitsQRY031_AndDoesNot_GenerateInterceptor()
    {
        var source = @"
using Quarry;
using System.Threading.Tasks;

namespace TestApp;

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId => Identity();
}

[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<User> Users();
}

public class Service
{
    public async Task<System.Collections.Generic.List<T>> QueryGeneric<T>(TestDbContext db, string sql)
    {
        // T is an open generic — generator cannot resolve concrete type
        return await db.RawSqlAsync<T>(sql);
    }
}
";

        var compilation = CreateCompilation(source);
        var (diagnostics, result) = RunGeneratorWithDiagnostics(compilation);

        // QRY031 should be emitted as an error for the unresolvable type parameter
        var qry031 = diagnostics.FirstOrDefault(d => d.Id == "QRY031");
        Assert.That(qry031, Is.Not.Null, "QRY031 diagnostic should be emitted for unresolvable generic T");
        Assert.That(qry031!.Severity, Is.EqualTo(DiagnosticSeverity.Error));
        Assert.That(qry031.GetMessage(), Does.Contain("T"));

        var code = GetInterceptorsCode(result);
        // Either no interceptors file, or no RawSqlAsync interceptor for the generic call
        if (code != null)
        {
            Assert.That(code, Does.Not.Contain("RawSqlAsyncWithReader"),
                "Should not generate interceptor for unresolvable generic T");
        }
    }

    #endregion

    #region Nested Row Types

    [Test]
    public void RawSqlAsync_NestedRowType_CompilesAndEmitsFullyQualifiedReferences()
    {
        var source = @"
using Quarry;
using System.Threading.Tasks;

namespace TestApp;

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId => Identity();
}

[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<User> Users();
}

public static class Host
{
    // Row type is nested inside Host. Without the Phase 2 fix the generator
    // would emit `using TestApp.Host;` which the compiler rejects (CS0138).
    public sealed class NestedRow
    {
        public int Id { get; set; }
        public string Name { get; set; } = """";
    }

    public static async Task Run(TestDbContext db)
    {
        var rows = await db.RawSqlAsync<NestedRow>(""SELECT Id, Name FROM users"");
        foreach (var r in rows) { _ = r; }
    }
}
";

        var compilation = CreateCompilation(source);
        var (diagnostics, result) = RunGeneratorWithDiagnostics(compilation);

        // No QRY043 — this is a valid shape; just nested.
        Assert.That(diagnostics.Any(d => d.Id == "QRY043"), Is.False,
            "Nested row type with valid shape should not raise QRY043");

        var code = GetInterceptorsCode(result);
        Assert.That(code, Is.Not.Null, "Interceptor file should be generated");

        // Generator must NOT emit a `using` whose target is actually a type. Before the fix
        // it emitted `using TestApp.Host;` (CS0138). The interceptor now lives inside
        // `namespace TestApp.Host { ... }`, so the bad import appears only as a top-level
        // using declaration — grep only the top-of-file block, ignoring the namespace line.
        var usingBlockEnd = code!.IndexOf("\nnamespace ", StringComparison.Ordinal);
        var usingBlock = usingBlockEnd > 0 ? code.Substring(0, usingBlockEnd) : code;
        Assert.That(usingBlock, Does.Not.Contain("using TestApp.Host;"),
            "Must not emit a using directive for an enclosing type (would be CS0138)");

        // Generated body must reference the row type via its FQN so it resolves without a using.
        Assert.That(code, Does.Contain("global::TestApp.Host.NestedRow"),
            "Generated body should reference the nested row type via its fully qualified name");
    }

    [Test]
    public void RawSqlAsync_NamespaceLevelRowType_StillUsesShortName()
    {
        // Regression: the non-nested path should keep emitting a short type reference
        // and a `using` for the row type's namespace.
        var source = @"
using Quarry;
using System.Threading.Tasks;

namespace TestApp.Rows
{
    public sealed class UserRow
    {
        public int Id { get; set; }
        public string Name { get; set; } = """";
    }
}

namespace TestApp
{
    public class UserSchema : Schema
    {
        public static string Table => ""users"";
        public Key<int> UserId => Identity();
    }

    [QuarryContext(Dialect = SqlDialect.SQLite)]
    public partial class TestDbContext : QuarryContext
    {
        public partial IEntityAccessor<User> Users();
    }

    public class Service
    {
        public async Task Test(TestDbContext db)
        {
            var rows = await db.RawSqlAsync<TestApp.Rows.UserRow>(""SELECT Id, Name FROM users"");
            foreach (var r in rows) { _ = r; }
        }
    }
}
";

        var compilation = CreateCompilation(source);
        var (diagnostics, result) = RunGeneratorWithDiagnostics(compilation);

        Assert.That(diagnostics.Any(d => d.Id == "QRY043"), Is.False);

        var code = GetInterceptorsCode(result);
        Assert.That(code, Is.Not.Null);

        // Non-nested row: body references the type via its short name — no `global::`
        // prefix, because that form is reserved for the nested-type fallback.
        Assert.That(code, Does.Contain("new UserRow()"),
            "Namespace-level row types should be referenced by their short name");
        Assert.That(code, Does.Not.Contain("global::TestApp.Rows.UserRow"),
            "Non-nested types should not use the FQN form");
        // The short name only resolves when the containing namespace is imported.
        Assert.That(code, Does.Contain("using TestApp.Rows;"),
            "Namespace-level row types must also emit a using directive for their namespace");
    }

    [Test]
    public void RawSqlAsync_NestedRowType_WithUnresolvableSql_EmitsSanitizedStructIdentifier()
    {
        // Exercises the struct-reader fallback path: the SQL expression is not a simple
        // column list, so the compile-time resolver bails and the generator falls back
        // to `file struct RawSqlReader_<sanitized>_<index>`. For a nested type the
        // ResultTypeName is a `global::`-prefixed FQN, which is not a valid identifier —
        // SanitizeForIdentifier must strip the prefix and replace dots/colons with _.
        var source = @"
using Quarry;
using System.Threading.Tasks;

namespace TestApp;

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId => Identity();
}

[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<User> Users();
}

public static class Host
{
    public sealed class NestedRow
    {
        public int Id { get; set; }
        public string Name { get; set; } = """";
    }

    public static async Task Run(TestDbContext db)
    {
        // Expression without AS alias — the compile-time column resolver can't match
        // this to NestedRow.Id, so the generator falls back to the struct reader.
        var rows = await db.RawSqlAsync<NestedRow>(""SELECT Id*2, Name FROM users"");
        foreach (var r in rows) { _ = r; }
    }
}
";

        var compilation = CreateCompilation(source);
        var (_, result) = RunGeneratorWithDiagnostics(compilation);
        var code = GetInterceptorsCode(result);
        Assert.That(code, Is.Not.Null);

        // Struct identifier must be valid C# — no `::` or `.` or angle brackets.
        Assert.That(code, Does.Match(@"file struct RawSqlReader_TestApp_Host_NestedRow_\d+\b"),
            "Sanitized struct identifier must use underscores in place of namespace separators");

        // And the struct's IRowReader<T> parameter uses the FQN so it resolves without a using.
        Assert.That(code, Does.Contain("IRowReader<global::TestApp.Host.NestedRow>"),
            "Struct reader interface should use the FQN for the nested row type");
    }

    #endregion

    #region Row Entity Materializability (QRY043)

    [Test]
    public void RawSqlAsync_PositionalRecordRowType_EmitsQRY043_AndSuppressesInterceptor()
    {
        var source = @"
using Quarry;
using System.Threading.Tasks;

namespace TestApp;

// Positional record — the generator can't call `new UserRow()` because the
// only accessible constructor takes positional parameters.
public sealed record UserRow(int Id, string Name);

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId => Identity();
}

[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<User> Users();
}

public class Service
{
    public async Task Test(TestDbContext db)
    {
        var rows = await db.RawSqlAsync<UserRow>(""SELECT Id, Name FROM users"");
        foreach (var r in rows) { _ = r; }
    }
}
";

        var compilation = CreateCompilation(source);
        var (diagnostics, result) = RunGeneratorWithDiagnostics(compilation);

        var qry043 = diagnostics.FirstOrDefault(d => d.Id == "QRY043");
        Assert.That(qry043, Is.Not.Null, "QRY043 should be emitted for a positional record row type");
        Assert.That(qry043!.Severity, Is.EqualTo(DiagnosticSeverity.Error));
        Assert.That(qry043.GetMessage(), Does.Contain("UserRow"));
        Assert.That(qry043.GetMessage(), Does.Contain("parameterless"));

        var code = GetInterceptorsCode(result);
        if (code != null)
        {
            Assert.That(code, Does.Not.Contain("RawSqlReader_UserRow"),
                "Should not generate an interceptor struct for an un-materializable row type");
        }
    }

    [Test]
    public void RawSqlAsync_InitOnlyProperties_EmitsQRY043()
    {
        var source = @"
using Quarry;
using System.Threading.Tasks;

namespace TestApp;

// Class with init-only properties — the generator would emit `item.Id = ...`
// assignments that don't compile outside an object initializer (CS8852).
public sealed class UserRow
{
    public int Id { get; init; }
    public string Name { get; init; } = """";
}

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId => Identity();
}

[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<User> Users();
}

public class Service
{
    public async Task Test(TestDbContext db)
    {
        var rows = await db.RawSqlAsync<UserRow>(""SELECT Id, Name FROM users"");
        foreach (var r in rows) { _ = r; }
    }
}
";

        var compilation = CreateCompilation(source);
        var (diagnostics, _) = RunGeneratorWithDiagnostics(compilation);

        var qry043 = diagnostics.FirstOrDefault(d => d.Id == "QRY043");
        Assert.That(qry043, Is.Not.Null, "QRY043 should be emitted for init-only row properties");
        Assert.That(qry043!.Severity, Is.EqualTo(DiagnosticSeverity.Error));
        Assert.That(qry043.GetMessage(), Does.Contain("init-only"));
    }

    [Test]
    public void RawSqlAsync_AbstractClassRowType_EmitsQRY043()
    {
        var source = @"
using Quarry;
using System.Threading.Tasks;

namespace TestApp;

public abstract class UserRow
{
    public int Id { get; set; }
    public string Name { get; set; } = """";
}

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId => Identity();
}

[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<User> Users();
}

public class Service
{
    public async Task Test(TestDbContext db)
    {
        var rows = await db.RawSqlAsync<UserRow>(""SELECT Id, Name FROM users"");
        foreach (var r in rows) { _ = r; }
    }
}
";

        var compilation = CreateCompilation(source);
        var (diagnostics, _) = RunGeneratorWithDiagnostics(compilation);

        var qry043 = diagnostics.FirstOrDefault(d => d.Id == "QRY043");
        Assert.That(qry043, Is.Not.Null, "QRY043 should fire for an abstract row type");
        Assert.That(qry043!.GetMessage(), Does.Contain("abstract"));
    }

    [Test]
    public void RawSqlAsync_InterfaceRowType_EmitsQRY043()
    {
        var source = @"
using Quarry;
using System.Threading.Tasks;

namespace TestApp;

public interface IUserRow
{
    int Id { get; set; }
    string Name { get; set; }
}

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId => Identity();
}

[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<User> Users();
}

public class Service
{
    public async Task Test(TestDbContext db)
    {
        var rows = await db.RawSqlAsync<IUserRow>(""SELECT Id, Name FROM users"");
        foreach (var r in rows) { _ = r; }
    }
}
";

        var compilation = CreateCompilation(source);
        var (diagnostics, _) = RunGeneratorWithDiagnostics(compilation);

        var qry043 = diagnostics.FirstOrDefault(d => d.Id == "QRY043");
        Assert.That(qry043, Is.Not.Null, "QRY043 should fire for an interface row type");
        Assert.That(qry043!.GetMessage(), Does.Contain("interface"));
    }

    [Test]
    public void RawSqlAsync_PlainClassRow_DoesNotEmitQRY043()
    {
        var source = @"
using Quarry;
using System.Threading.Tasks;

namespace TestApp;

// Plain class with parameterless ctor and public settable properties — valid.
public sealed class UserRow
{
    public int Id { get; set; }
    public string Name { get; set; } = """";
}

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId => Identity();
}

[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<User> Users();
}

public class Service
{
    public async Task Test(TestDbContext db)
    {
        var rows = await db.RawSqlAsync<UserRow>(""SELECT Id, Name FROM users"");
        foreach (var r in rows) { _ = r; }
    }
}
";

        var compilation = CreateCompilation(source);
        var (diagnostics, _) = RunGeneratorWithDiagnostics(compilation);

        var qry043 = diagnostics.FirstOrDefault(d => d.Id == "QRY043");
        Assert.That(qry043, Is.Null, "QRY043 should not fire for valid plain-class row types");
    }

    #endregion

    #region Both Overloads Detected

    [Test]
    public void RawSqlAsync_BothOverloads_WithAndWithoutCancellationToken_Detected()
    {
        var source = @"
using Quarry;
using System.Threading;
using System.Threading.Tasks;

namespace TestApp;

public class UserDto
{
    public int UserId { get; set; }
    public string UserName { get; set; } = null!;
}

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId => Identity();
    public Col<string> UserName => Length(100);
}

[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<User> Users();
}

public class Service
{
    public async Task Test(TestDbContext db, CancellationToken ct)
    {
        // Overload without CancellationToken
        var r1 = await db.RawSqlAsync<UserDto>(""SELECT UserId, UserName FROM users"");
        // Overload with CancellationToken
        var r2 = await db.RawSqlAsync<UserDto>(""SELECT UserId, UserName FROM users"", ct);
    }
}
";

        var compilation = CreateCompilation(source);
        var (diagnostics, result) = RunGeneratorWithDiagnostics(compilation);

        var code = GetInterceptorsCode(result);
        Assert.That(code, Is.Not.Null, "Should generate interceptors file");

        // Count the number of RawSqlAsyncWithReader interceptor methods
        var interceptorCount = CountOccurrences(code!, "RawSqlAsyncWithReader");
        Assert.That(interceptorCount, Is.GreaterThanOrEqualTo(2),
            "Should generate interceptors for both overloads");
    }

    #endregion

    #region Empty Properties — CS1522 Guard

    [Test]
    public void RawSqlAsync_DtoWithNoPublicSetters_DoesNotEmitEmptySwitch()
    {
        // When T is a DTO whose properties all lack public setters, the property list
        // is empty. The emitter must not produce an empty switch block (CS1522).
        var source = @"
using Quarry;
using System.Threading.Tasks;

namespace TestApp;

public class ReadOnlyDto
{
    public int Id { get; }
    public string Name { get; } = null!;
}

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId => Identity();
}

[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<User> Users();
}

public class Service
{
    public async Task Test(TestDbContext db)
    {
        var results = await db.RawSqlAsync<ReadOnlyDto>(""SELECT Id, Name FROM users"");
    }
}
";

        var compilation = CreateCompilation(source);
        var (diagnostics, result) = RunGeneratorWithDiagnostics(compilation);

        var code = GetInterceptorsCode(result);
        Assert.That(code, Is.Not.Null, "Should generate interceptors file");
        Assert.That(code, Does.Contain("static _ => new ReadOnlyDto()"),
            "Should emit a one-liner lambda discarding the reader when there are no settable properties");
        Assert.That(code, Does.Not.Contain("switch (r.GetName(i).ToLowerInvariant())"),
            "Should not emit a switch block when there are no settable properties");
    }

    #endregion

    #region Entity T Enrichment

    [Test]
    public void RawSqlAsync_EntityT_IsDiscovered_And_GeneratesInterceptor()
    {
        // When T matches a Pipeline 1 entity, the generator should still produce an
        // interceptor. Entity enrichment promotes DTO→Entity kind and maps schema metadata
        // onto the originally discovered properties. In a pipeline test the generated entity
        // class doesn't exist as concrete C# during discovery, so the initial property list
        // may be empty — but the interceptor itself must still be emitted.
        var source = @"
using Quarry;
using System.Threading.Tasks;

namespace TestApp;

public enum OrderPriority { Low = 0, Normal = 1, High = 2 }

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId => Identity();
    public Col<string> UserName => Length(100);
}

public class OrderSchema : Schema
{
    public static string Table => ""orders"";
    public Key<int> OrderId => Identity();
    public Ref<UserSchema, int> UserId => ForeignKey<UserSchema, int>();
    public Col<decimal> Total => Precision(18, 2);
    public Col<OrderPriority> Priority { get; }
}

[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<User> Users();
    public partial IEntityAccessor<Order> Orders();
}

public class Service
{
    public async Task Test(TestDbContext db)
    {
        var orders = await db.RawSqlAsync<Order>(""SELECT * FROM orders"");
    }
}
";

        var compilation = CreateCompilation(source);
        var (diagnostics, result) = RunGeneratorWithDiagnostics(compilation);

        var code = GetInterceptorsCode(result);
        Assert.That(code, Is.Not.Null, "Should generate interceptors file");

        // Interceptor should be generated with struct-based row reader
        Assert.That(code, Does.Contain("RawSqlAsyncWithReader<Order,"),
            "Should generate struct-based RawSqlAsync interceptor for entity T");
        Assert.That(code, Does.Contain("IAsyncEnumerable<Order>"),
            "Interceptor return type should use the entity type Order");
        Assert.That(code, Does.Contain("IRowReader<Order>"),
            "Should emit struct implementing IRowReader<Order>");
        Assert.That(code, Does.Contain("new Order()"),
            "Struct Read method should construct the entity");

        // Struct Resolve: ordinal discovery via switch
        Assert.That(code, Does.Contain("switch (r.GetName(i).ToLowerInvariant())"),
            "Should generate switch-based ordinal discovery in Resolve");
        Assert.That(code, Does.Contain("case \"orderid\""),
            "Should generate switch case for OrderId column");
        Assert.That(code, Does.Contain("case \"total\""),
            "Should generate switch case for Total column");
        Assert.That(code, Does.Contain("case \"priority\""),
            "Should generate switch case for Priority column");
        // Struct Read: typed reads with cached ordinals
        Assert.That(code, Does.Contain("(global::TestApp.OrderPriority)r.GetInt32(_ord"),
            "Should generate enum cast for Priority column with cached ordinal");
        Assert.That(code, Does.Contain("case \"userid\""),
            "Should generate switch case for FK UserId column");
        Assert.That(code, Does.Contain("EntityRef<User, int>"),
            "Should wrap FK column with EntityRef from ColumnInfo metadata");
        Assert.That(code, Does.Not.Contain("static _ => new Order()"),
            "Should NOT emit no-op reader delegate for enriched entity");
    }

    [Test]
    public void RawSqlAsync_EntityT_SimpleColumns_GeneratesPropertySwitch()
    {
        var source = @"
using Quarry;
using System.Threading.Tasks;

namespace TestApp;

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId => Identity();
    public Col<string> UserName => Length(100);
    public Col<string?> Email { get; }
    public Col<bool> IsActive => Default(true);
}

[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<User> Users();
}

public class Service
{
    public async Task Test(TestDbContext db)
    {
        var users = await db.RawSqlAsync<User>(""SELECT * FROM users"");
    }
}
";

        var compilation = CreateCompilation(source);
        var (diagnostics, result) = RunGeneratorWithDiagnostics(compilation);

        var code = GetInterceptorsCode(result);
        Assert.That(code, Is.Not.Null, "Should generate interceptors file");

        Assert.That(code, Does.Contain("switch (r.GetName(i).ToLowerInvariant())"),
            "Should generate switch-based reader for entity type");
        Assert.That(code, Does.Contain("case \"userid\""),
            "Should generate switch case for UserId");
        Assert.That(code, Does.Contain("case \"username\""),
            "Should generate switch case for UserName");
        Assert.That(code, Does.Contain("case \"email\""),
            "Should generate switch case for Email");
        Assert.That(code, Does.Contain("case \"isactive\""),
            "Should generate switch case for IsActive");
        Assert.That(code, Does.Not.Contain("static _ => new User()"),
            "Should NOT emit no-op reader delegate");
    }

    [Test]
    public void RawSqlAsync_EntityT_WithCustomTypeMapping_GeneratesFromDbReader()
    {
        // Custom type mappings are a schema-level concern stored in ColumnInfo, not in the
        // type symbol. After ResolveRawSqlTypeInfo discovers properties from ITypeSymbol,
        // PatchWithColumnMetadata enriches them with ColumnInfo metadata (CustomTypeMappingClass,
        // DbReaderMethodName) from the EntityRegistry. This produces FromDb calls in the reader.
        var source = @"
using Quarry;
using System.Threading.Tasks;

namespace TestApp;

public readonly struct Money
{
    public decimal Amount { get; }
    public Money(decimal amount) => Amount = amount;
}

public class MoneyMapping : TypeMapping<Money, decimal>
{
    public override decimal ToDb(Money value) => value.Amount;
    public override Money FromDb(decimal value) => new(value);
}

public class AccountSchema : Schema
{
    public static string Table => ""accounts"";
    public Key<int> AccountId => Identity();
    public Col<string> AccountName => Length(100);
    public Col<Money> Balance => Mapped<Money, MoneyMapping>();
}

[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<Account> Accounts();
}

public class Service
{
    public async Task Test(TestDbContext db)
    {
        var accounts = await db.RawSqlAsync<Account>(""SELECT * FROM accounts"");
    }
}
";

        var compilation = CreateCompilation(source);
        var (diagnostics, result) = RunGeneratorWithDiagnostics(compilation);

        var code = GetInterceptorsCode(result);
        Assert.That(code, Is.Not.Null, "Should generate interceptors file");

        Assert.That(code, Does.Contain("switch (r.GetName(i).ToLowerInvariant())"),
            "Should generate switch-based reader for enriched entity type");
        Assert.That(code, Does.Contain("case \"accountid\""),
            "Should generate switch case for AccountId");
        Assert.That(code, Does.Contain("case \"accountname\""),
            "Should generate switch case for AccountName");
        Assert.That(code, Does.Contain("case \"balance\""),
            "Should generate switch case for Balance");
        Assert.That(code, Does.Contain("MoneyMapping"),
            "Should reference MoneyMapping in the reader delegate");
        Assert.That(code, Does.Contain("FromDb"),
            "Should call FromDb for custom type mapping conversion");
        Assert.That(code, Does.Not.Contain("static _ => new Account()"),
            "Should NOT emit no-op reader delegate");
    }

    [Test]
    public void RawSqlAsync_ConcreteDto_WithLiteralSql_GeneratesStaticOrdinalReader()
    {
        // When T is a concrete DTO with a literal SQL string, the generator parses the SQL
        // at compile time and emits a static lambda with hardcoded ordinals.
        var source = @"
using Quarry;
using System.Threading.Tasks;

namespace TestApp;

public class UserDto
{
    public int UserId { get; set; }
    public string UserName { get; set; } = null!;
    public string? Email { get; set; }
}

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId => Identity();
    public Col<string> UserName => Length(100);
}

[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<User> Users();
}

public class Service
{
    public async Task Test(TestDbContext db)
    {
        var results = await db.RawSqlAsync<UserDto>(""SELECT UserId, UserName, Email FROM users"");
    }
}
";

        var compilation = CreateCompilation(source);
        var (diagnostics, result) = RunGeneratorWithDiagnostics(compilation);

        var code = GetInterceptorsCode(result);
        Assert.That(code, Is.Not.Null, "Should generate interceptors file");

        // Compile-time column resolution: static reader with hardcoded ordinals
        Assert.That(code, Does.Contain("r.GetInt32(0)"),
            "Should generate hardcoded ordinal 0 for UserId");
        Assert.That(code, Does.Contain("r.GetString(1)"),
            "Should generate hardcoded ordinal 1 for UserName");
        Assert.That(code, Does.Contain("r.GetString(2)"),
            "Should generate hardcoded ordinal 2 for Email");
        // Nullable Email should have IsDBNull check
        Assert.That(code, Does.Contain("!r.IsDBNull(2)"),
            "Should guard nullable Email with IsDBNull check");
        // Should NOT contain struct-based reader (no runtime ordinal discovery)
        Assert.That(code, Does.Not.Contain("IRowReader<UserDto>"),
            "Should not emit struct-based row reader for literal SQL");
        Assert.That(code, Does.Not.Contain("switch (r.GetName"),
            "Should not contain runtime column name discovery");
    }

    [Test]
    public void RawSqlAsync_UnresolvableExpression_EmitsQRY041AndFallsBack()
    {
        // SQL with arithmetic expression without alias → QRY041 warning + struct fallback
        var source = @"
using Quarry;
using System.Threading.Tasks;

namespace TestApp;

public class OrderDto
{
    public int OrderId { get; set; }
    public decimal Total { get; set; }
}

public class OrderSchema : Schema
{
    public static string Table => ""orders"";
    public Key<int> OrderId => Identity();
}

[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<Order> Orders();
}

public class Service
{
    public async Task Test(TestDbContext db)
    {
        var results = await db.RawSqlAsync<OrderDto>(""SELECT OrderId, price * qty FROM orders"");
    }
}
";

        var compilation = CreateCompilation(source);
        var (diagnostics, result) = RunGeneratorWithDiagnostics(compilation);

        var code = GetInterceptorsCode(result);
        Assert.That(code, Is.Not.Null, "Should generate interceptors file");

        // Should fall back to struct-based reader
        Assert.That(code, Does.Contain("IRowReader<OrderDto>"),
            "Should fall back to struct-based reader for unresolvable expression");
        Assert.That(code, Does.Contain("switch (r.GetName(i).ToLowerInvariant())"),
            "Should use runtime column discovery");

        // QRY041 diagnostic should be emitted
        Assert.That(diagnostics, Has.Some.Matches<Microsoft.CodeAnalysis.Diagnostic>(
            d => d.Id == "QRY041" && d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Warning),
            "Should emit QRY041 warning for unresolvable column expression");
    }

    [Test]
    public void RawSqlAsync_VariableSql_FallsBackToStructReader()
    {
        // SQL passed as a variable, not a literal → struct fallback
        var source = @"
using Quarry;
using System.Threading.Tasks;

namespace TestApp;

public class UserDto
{
    public int UserId { get; set; }
    public string UserName { get; set; } = null!;
}

public class UserSchema : Schema
{
    public static string Table => ""users"";
    public Key<int> UserId => Identity();
    public Col<string> UserName => Length(100);
}

[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class TestDbContext : QuarryContext
{
    public partial IEntityAccessor<User> Users();
}

public class Service
{
    public async Task Test(TestDbContext db)
    {
        var sql = ""SELECT UserId, UserName FROM users"";
        var results = await db.RawSqlAsync<UserDto>(sql);
    }
}
";

        var compilation = CreateCompilation(source);
        var (diagnostics, result) = RunGeneratorWithDiagnostics(compilation);

        var code = GetInterceptorsCode(result);
        Assert.That(code, Is.Not.Null, "Should generate interceptors file");

        // Variable SQL → struct-based reader (no compile-time resolution)
        Assert.That(code, Does.Contain("IRowReader<UserDto>"),
            "Should use struct-based reader for variable SQL");
    }

    #endregion

    #region Helpers

    private static int CountOccurrences(string source, string substring)
    {
        int count = 0;
        int index = 0;
        while ((index = source.IndexOf(substring, index, StringComparison.Ordinal)) != -1)
        {
            count++;
            index += substring.Length;
        }
        return count;
    }

    #endregion
}
