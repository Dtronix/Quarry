using Quarry.Shared.Migration;
using Quarry.Shared.Scaffold;

namespace Quarry.Tests.Scaffold;

[TestFixture]
public class ScaffoldCodeGeneratorTests
{
    [TestCase("users", false, "UserSchema")]
    [TestCase("order_items", false, "OrderItemSchema")]
    [TestCase("categories", false, "CategorySchema")]
    [TestCase("products", false, "ProductSchema")]
    [TestCase("UserProfile", false, "UserProfileSchema")]
    [TestCase("addresses", false, "AddressSchema")]
    public void ToClassName_GeneratesCorrectNames(string tableName, bool noSingularize, string expected)
    {
        Assert.That(ScaffoldCodeGenerator.ToClassName(tableName, noSingularize), Is.EqualTo(expected));
    }

    [Test]
    public void ToClassName_NoSingularize_KeepsPlural()
    {
        Assert.That(ScaffoldCodeGenerator.ToClassName("users", true), Is.EqualTo("UsersSchema"));
    }

    [TestCase("customer_first_name", "SnakeCase", "CustomerFirstName")]
    [TestCase("customerId", "Exact", "customerId")]
    [TestCase("order_date", "SnakeCase", "OrderDate")]
    public void ToPascalCase_ConvertsCorrectly(string input, string styleStr, string expected)
    {
        var style = Enum.Parse<NamingStyleKind>(styleStr);
        Assert.That(ScaffoldCodeGenerator.ToPascalCase(input, style), Is.EqualTo(expected));
    }

    [Test]
    public void GenerateSchemaFile_SimpleTable_ContainsExpectedElements()
    {
        var table = CreateSimpleTable();
        var tableClassMap = new Dictionary<string, string> { ["customers"] = "CustomerSchema" };

        var code = ScaffoldCodeGenerator.GenerateSchemaFile(
            table, "MyApp.Data", NamingStyleKind.SnakeCase, false, false, "mydb", tableClassMap);

        Assert.Multiple(() =>
        {
            Assert.That(code, Does.Contain("namespace MyApp.Data;"));
            Assert.That(code, Does.Contain("public class CustomerSchema : Schema"));
            Assert.That(code, Does.Contain("public static string Table => \"customers\";"));
            Assert.That(code, Does.Contain("NamingStyle.SnakeCase"));
            Assert.That(code, Does.Contain("Key<int> Id => Identity();"));
            Assert.That(code, Does.Contain("Col<string> FirstName => Length(100);"));
            Assert.That(code, Does.Contain("Col<string?> Email"));
            Assert.That(code, Does.Contain("Col<bool> IsActive => Default(true);"));
            Assert.That(code, Does.Contain("Auto-scaffolded by quarry"));
        });
    }

    [Test]
    public void GenerateSchemaFile_WithForeignKey_EmitsRef()
    {
        var columns = new List<ColumnMetadata>
        {
            new("id", "INTEGER", false, isIdentity: true),
            new("customer_id", "INTEGER", false)
        };
        var pk = new PrimaryKeyMetadata(null, new[] { "id" });
        var fks = new List<ForeignKeyMetadata>
        {
            new("FK_order_customer", "customer_id", "customers", "id", onDelete: "CASCADE")
        };
        var typeResults = new List<ReverseTypeResult>
        {
            new("int", false),
            new("int", false)
        };

        var table = new ScaffoldCodeGenerator.ScaffoldedTable(
            "orders", null, "OrderSchema",
            columns, pk, fks, new List<ForeignKeyMetadata>(),
            new List<IndexMetadata>(), typeResults, null,
            new List<ScaffoldCodeGenerator.IncomingRelationship>());

        var tableClassMap = new Dictionary<string, string>
        {
            ["orders"] = "OrderSchema",
            ["customers"] = "CustomerSchema"
        };

        var code = ScaffoldCodeGenerator.GenerateSchemaFile(
            table, "MyApp", NamingStyleKind.SnakeCase, false, false, "mydb", tableClassMap);

        Assert.That(code, Does.Contain("Ref<CustomerSchema, int> CustomerId => ForeignKey<CustomerSchema, int>();"));
        Assert.That(code, Does.Contain("// ON DELETE CASCADE"));
    }

    [Test]
    public void GenerateSchemaFile_WithNavigations_EmitsMany()
    {
        var table = CreateSimpleTable();
        table = new ScaffoldCodeGenerator.ScaffoldedTable(
            table.TableName, table.Schema, table.ClassName,
            table.Columns, table.PrimaryKey, table.ForeignKeys, table.ImplicitForeignKeys,
            table.Indexes, table.TypeResults, null,
            new List<ScaffoldCodeGenerator.IncomingRelationship>
            {
                new("orders", "OrderSchema", "customer_id", false)
            });

        var tableClassMap = new Dictionary<string, string> { ["customers"] = "CustomerSchema" };

        var code = ScaffoldCodeGenerator.GenerateSchemaFile(
            table, "MyApp", NamingStyleKind.SnakeCase, false, false, "mydb", tableClassMap);

        Assert.That(code, Does.Contain("Many<OrderSchema> Orders => HasMany<OrderSchema>"));
    }

    [Test]
    public void GenerateSchemaFile_CompositePk_EmitsCompositeKey()
    {
        var columns = new List<ColumnMetadata>
        {
            new("student_id", "INTEGER", false),
            new("course_id", "INTEGER", false)
        };
        var pk = new PrimaryKeyMetadata(null, new[] { "student_id", "course_id" });
        var typeResults = new List<ReverseTypeResult>
        {
            new("int", false),
            new("int", false)
        };

        var table = new ScaffoldCodeGenerator.ScaffoldedTable(
            "student_courses", null, "StudentCourseSchema",
            columns, pk, new List<ForeignKeyMetadata>(), new List<ForeignKeyMetadata>(),
            new List<IndexMetadata>(), typeResults, null,
            new List<ScaffoldCodeGenerator.IncomingRelationship>());

        var code = ScaffoldCodeGenerator.GenerateSchemaFile(
            table, null, NamingStyleKind.SnakeCase, false, false, "mydb", new Dictionary<string, string>());

        Assert.That(code, Does.Contain("public CompositeKey PK => PrimaryKey(StudentId, CourseId);"));
    }

    [Test]
    public void GenerateSchemaFile_NoNavigationsFlag_SkipsMany()
    {
        var table = CreateSimpleTable();
        table = new ScaffoldCodeGenerator.ScaffoldedTable(
            table.TableName, table.Schema, table.ClassName,
            table.Columns, table.PrimaryKey, table.ForeignKeys, table.ImplicitForeignKeys,
            table.Indexes, table.TypeResults, null,
            new List<ScaffoldCodeGenerator.IncomingRelationship>
            {
                new("orders", "OrderSchema", "customer_id", false)
            });

        var code = ScaffoldCodeGenerator.GenerateSchemaFile(
            table, "MyApp", NamingStyleKind.SnakeCase, false, true, "mydb", new Dictionary<string, string>());

        Assert.That(code, Does.Not.Contain("Many<"));
    }

    [Test]
    public void GenerateSchemaFile_JunctionTable_EmitsComment()
    {
        var columns = new List<ColumnMetadata>
        {
            new("student_id", "INTEGER", false),
            new("course_id", "INTEGER", false)
        };
        var pk = new PrimaryKeyMetadata(null, new[] { "student_id", "course_id" });
        var fks = new List<ForeignKeyMetadata>
        {
            new("FK1", "student_id", "students", "id"),
            new("FK2", "course_id", "courses", "id")
        };
        var typeResults = new List<ReverseTypeResult>
        {
            new("int", false),
            new("int", false)
        };
        var junction = new JunctionTableDetector.JunctionTableResult(
            "student_courses", null, fks[0], fks[1], new List<ColumnMetadata>());

        var table = new ScaffoldCodeGenerator.ScaffoldedTable(
            "student_courses", null, "StudentCourseSchema",
            columns, pk, fks, new List<ForeignKeyMetadata>(),
            new List<IndexMetadata>(), typeResults, junction,
            new List<ScaffoldCodeGenerator.IncomingRelationship>());

        var tableClassMap = new Dictionary<string, string>
        {
            ["students"] = "StudentSchema",
            ["courses"] = "CourseSchema"
        };

        var code = ScaffoldCodeGenerator.GenerateSchemaFile(
            table, null, NamingStyleKind.SnakeCase, false, false, "mydb", tableClassMap);

        Assert.That(code, Does.Contain("Junction table: Many-to-many between Student and Course"));
    }

    [Test]
    public void GenerateSchemaFile_WarningType_EmitsWarningComment()
    {
        var columns = new List<ColumnMetadata>
        {
            new("id", "INTEGER", false, isIdentity: true),
            new("metadata", "JSONB", true)
        };
        var pk = new PrimaryKeyMetadata(null, new[] { "id" });
        var typeResults = new List<ReverseTypeResult>
        {
            new("int", false),
            new("string", true, warning: "Unmapped PostgreSQL type 'jsonb' -- mapped as string")
        };

        var table = new ScaffoldCodeGenerator.ScaffoldedTable(
            "items", null, "ItemSchema",
            columns, pk, new List<ForeignKeyMetadata>(), new List<ForeignKeyMetadata>(),
            new List<IndexMetadata>(), typeResults, null,
            new List<ScaffoldCodeGenerator.IncomingRelationship>());

        var code = ScaffoldCodeGenerator.GenerateSchemaFile(
            table, null, NamingStyleKind.Exact, false, false, "mydb", new Dictionary<string, string>());

        Assert.That(code, Does.Contain("WARNING: Column 'metadata' has type 'JSONB'"));
    }

    // --- Gap 1: Context file generation ---

    [Test]
    public void GenerateContextFile_EmitsCorrectStructure()
    {
        var tableClassMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["customers"] = "CustomerSchema",
            ["orders"] = "OrderSchema"
        };

        var code = ScaffoldCodeGenerator.GenerateContextFile(
            "MyDbContext", "sqlite", "MyApp.Data", "mydb", tableClassMap);

        Assert.Multiple(() =>
        {
            Assert.That(code, Does.Contain("using Quarry;"));
            Assert.That(code, Does.Contain("namespace MyApp.Data;"));
            Assert.That(code, Does.Contain("[QuarryContext(Dialect = SqlDialect.SQLite)]"));
            Assert.That(code, Does.Contain("public partial class MyDbContext : QuarryContext"));
            Assert.That(code, Does.Contain("public partial IEntityAccessor<Customer> Customers();"));
            Assert.That(code, Does.Contain("public partial IEntityAccessor<Order> Orders();"));
            Assert.That(code, Does.Contain("Auto-scaffolded by quarry"));
        });
    }

    [Test]
    public void GenerateContextFile_NoNamespace_OmitsNamespaceLine()
    {
        var tableClassMap = new Dictionary<string, string> { ["items"] = "ItemSchema" };

        var code = ScaffoldCodeGenerator.GenerateContextFile(
            "AppDbContext", "sqlite", null, "mydb", tableClassMap);

        Assert.That(code, Does.Not.Contain("namespace"));
        Assert.That(code, Does.Contain("public partial class AppDbContext : QuarryContext"));
    }

    [TestCase("sqlite", "SQLite")]
    [TestCase("postgresql", "PostgreSQL")]
    [TestCase("postgres", "PostgreSQL")]
    [TestCase("pg", "PostgreSQL")]
    [TestCase("sqlserver", "SqlServer")]
    [TestCase("mssql", "SqlServer")]
    [TestCase("mysql", "MySQL")]
    public void MapDialectToEnum_MapsCorrectly(string input, string expected)
    {
        Assert.That(ScaffoldCodeGenerator.MapDialectToEnum(input), Is.EqualTo(expected));
    }

    [TestCase("school", "SchoolDbContext")]
    [TestCase("my_app", "MyAppDbContext")]
    [TestCase("NorthwindDbContext", "NorthwindDbContext")]
    public void ToContextClassName_GeneratesCorrectNames(string dbName, string expected)
    {
        Assert.That(ScaffoldCodeGenerator.ToContextClassName(dbName), Is.EqualTo(expected));
    }

    // --- Gap 2: Entity names in Ref<>/Many<> ---

    [TestCase("CustomerSchema", "Customer")]
    [TestCase("OrderItemSchema", "OrderItem")]
    [TestCase("Product", "Product")]
    public void ToEntityName_StripsSchemaCorrectly(string className, string expected)
    {
        Assert.That(ScaffoldCodeGenerator.ToEntityName(className), Is.EqualTo(expected));
    }

    [Test]
    public void GenerateSchemaFile_ForeignKey_UsesSchemaClassName()
    {
        var columns = new List<ColumnMetadata>
        {
            new("id", "INTEGER", false, isIdentity: true),
            new("department_id", "INTEGER", false)
        };
        var pk = new PrimaryKeyMetadata(null, new[] { "id" });
        var fks = new List<ForeignKeyMetadata>
        {
            new("FK_instr_dept", "department_id", "departments", "id")
        };
        var typeResults = new List<ReverseTypeResult>
        {
            new("int", false),
            new("int", false)
        };

        var table = new ScaffoldCodeGenerator.ScaffoldedTable(
            "instructors", null, "InstructorSchema",
            columns, pk, fks, new List<ForeignKeyMetadata>(),
            new List<IndexMetadata>(), typeResults, null,
            new List<ScaffoldCodeGenerator.IncomingRelationship>());

        var tableClassMap = new Dictionary<string, string>
        {
            ["instructors"] = "InstructorSchema",
            ["departments"] = "DepartmentSchema"
        };

        var code = ScaffoldCodeGenerator.GenerateSchemaFile(
            table, "MyApp", NamingStyleKind.SnakeCase, false, false, "mydb", tableClassMap);

        Assert.Multiple(() =>
        {
            Assert.That(code, Does.Contain("Ref<DepartmentSchema, int>"));
            Assert.That(code, Does.Contain("ForeignKey<DepartmentSchema, int>()"));
        });
    }

    // --- Gap 3: Duplicate navigation property disambiguation ---

    [Test]
    public void GenerateSchemaFile_DuplicateNavProps_DisambiguatesByFkColumn()
    {
        var columns = new List<ColumnMetadata>
        {
            new("id", "INTEGER", false, isIdentity: true),
            new("name", "TEXT", false)
        };
        var pk = new PrimaryKeyMetadata(null, new[] { "id" });
        var typeResults = new List<ReverseTypeResult>
        {
            new("int", false),
            new("string", false)
        };

        // Two incoming relationships from the same entity (course_prerequisites)
        var incoming = new List<ScaffoldCodeGenerator.IncomingRelationship>
        {
            new("course_prerequisites", "CoursePrerequisiteSchema", "course_id", false),
            new("course_prerequisites", "CoursePrerequisiteSchema", "prerequisite_id", false)
        };

        var table = new ScaffoldCodeGenerator.ScaffoldedTable(
            "courses", null, "CourseSchema",
            columns, pk, new List<ForeignKeyMetadata>(), new List<ForeignKeyMetadata>(),
            new List<IndexMetadata>(), typeResults, null, incoming);

        var code = ScaffoldCodeGenerator.GenerateSchemaFile(
            table, "MyApp", NamingStyleKind.SnakeCase, false, false, "mydb", new Dictionary<string, string>());

        Assert.Multiple(() =>
        {
            Assert.That(code, Does.Contain("CoursePrerequisitesByCourseId"));
            Assert.That(code, Does.Contain("CoursePrerequisitesByPrerequisiteId"));
            // Should not contain the bare duplicate name
            Assert.That(code, Does.Not.Match(@"Many<.*> CoursePrerequisites =>"));
        });
    }

    [Test]
    public void GenerateSchemaFile_SingleNavProp_NotDisambiguated()
    {
        var columns = new List<ColumnMetadata>
        {
            new("id", "INTEGER", false, isIdentity: true)
        };
        var pk = new PrimaryKeyMetadata(null, new[] { "id" });
        var typeResults = new List<ReverseTypeResult>
        {
            new("int", false)
        };

        var incoming = new List<ScaffoldCodeGenerator.IncomingRelationship>
        {
            new("orders", "OrderSchema", "customer_id", false)
        };

        var table = new ScaffoldCodeGenerator.ScaffoldedTable(
            "customers", null, "CustomerSchema",
            columns, pk, new List<ForeignKeyMetadata>(), new List<ForeignKeyMetadata>(),
            new List<IndexMetadata>(), typeResults, null, incoming);

        var code = ScaffoldCodeGenerator.GenerateSchemaFile(
            table, null, NamingStyleKind.SnakeCase, false, false, "mydb", new Dictionary<string, string>());

        // Single relationship should use simple name, not disambiguated
        Assert.That(code, Does.Contain("Many<OrderSchema> Orders => HasMany<OrderSchema>"));
        Assert.That(code, Does.Not.Contain("OrdersByCustomerId"));
    }

    [Test]
    public void GenerateSchemaFile_WithIndexes_EmitsIndexProperties()
    {
        var columns = new List<ColumnMetadata>
        {
            new("id", "INTEGER", false, isIdentity: true),
            new("email", "TEXT", false, maxLength: 255),
            new("first_name", "TEXT", false, maxLength: 100),
            new("last_name", "TEXT", false, maxLength: 100)
        };
        var pk = new PrimaryKeyMetadata(null, new[] { "id" });
        var indexes = new List<IndexMetadata>
        {
            new("IX_users_email", new[] { "email" }, isUnique: true),
            new("IX_users_name", new[] { "first_name", "last_name" }, isUnique: false),
            new("PK_users", new[] { "id" }, isUnique: true, isPrimaryKey: true)
        };
        var typeResults = new List<ReverseTypeResult>
        {
            new("int", false),
            new("string", false, maxLength: 255),
            new("string", false, maxLength: 100),
            new("string", false, maxLength: 100)
        };

        var table = new ScaffoldCodeGenerator.ScaffoldedTable(
            "users", null, "UserSchema",
            columns, pk, new List<ForeignKeyMetadata>(), new List<ForeignKeyMetadata>(),
            indexes, typeResults, null,
            new List<ScaffoldCodeGenerator.IncomingRelationship>());

        var code = ScaffoldCodeGenerator.GenerateSchemaFile(
            table, null, NamingStyleKind.SnakeCase, false, false, "mydb", new Dictionary<string, string>());

        Assert.Multiple(() =>
        {
            // Non-PK indexes should be emitted
            Assert.That(code, Does.Contain("// Indexes"));
            Assert.That(code, Does.Contain("Index IXUsersEmail => Index(Email).Unique();"));
            Assert.That(code, Does.Contain("Index IXUsersName => Index(FirstName, LastName);"));
            // PK index should NOT be emitted
            Assert.That(code, Does.Not.Contain("PkUsers"));
        });
    }

    [Test]
    public void GenerateSchemaFile_IndexWithoutPrefix_AddIdxPrefix()
    {
        var columns = new List<ColumnMetadata>
        {
            new("id", "INTEGER", false, isIdentity: true),
            new("code", "TEXT", false)
        };
        var pk = new PrimaryKeyMetadata(null, new[] { "id" });
        var indexes = new List<IndexMetadata>
        {
            new("unique_code", new[] { "code" }, isUnique: true)
        };
        var typeResults = new List<ReverseTypeResult>
        {
            new("int", false),
            new("string", false)
        };

        var table = new ScaffoldCodeGenerator.ScaffoldedTable(
            "items", null, "ItemSchema",
            columns, pk, new List<ForeignKeyMetadata>(), new List<ForeignKeyMetadata>(),
            indexes, typeResults, null,
            new List<ScaffoldCodeGenerator.IncomingRelationship>());

        var code = ScaffoldCodeGenerator.GenerateSchemaFile(
            table, null, NamingStyleKind.SnakeCase, false, false, "mydb", new Dictionary<string, string>());

        // Index names without Ix/Idx/Index prefix get "Idx" prepended
        Assert.That(code, Does.Contain("Index IdxUniqueCode => Index(Code).Unique();"));
    }

    private static ScaffoldCodeGenerator.ScaffoldedTable CreateSimpleTable()
    {
        var columns = new List<ColumnMetadata>
        {
            new("id", "INTEGER", false, isIdentity: true),
            new("first_name", "TEXT", false, maxLength: 100),
            new("last_name", "TEXT", false, maxLength: 100),
            new("email", "TEXT", true, maxLength: 255),
            new("is_active", "INTEGER", false, defaultExpression: "1")
        };
        var pk = new PrimaryKeyMetadata(null, new[] { "id" });
        var typeResults = new List<ReverseTypeResult>
        {
            new("int", false),
            new("string", false, maxLength: 100),
            new("string", false, maxLength: 100),
            new("string", true, maxLength: 255),
            new("bool", false)
        };

        return new ScaffoldCodeGenerator.ScaffoldedTable(
            "customers", null, "CustomerSchema",
            columns, pk, new List<ForeignKeyMetadata>(), new List<ForeignKeyMetadata>(),
            new List<IndexMetadata>(), typeResults, null,
            new List<ScaffoldCodeGenerator.IncomingRelationship>());
    }
}
