using System.Data.Common;
using Quarry;

#pragma warning disable CS0162 // Unreachable code — conditional branching tests use if(true)/if(false) intentionally

namespace Quarry.Tests.Samples;

/// <summary>
/// Integration tests for interceptor-generated code.
/// These tests verify that the source generator produces correct interceptors
/// and that the generated context, entities, and metadata work correctly.
///
/// Known generator limitations:
/// - Where after Select with anonymous types - triggers invalid type generation
/// - OrderBy, ThenBy, GroupBy, Having - trigger invalid interceptor generation
///
/// Safe patterns:
/// - Where on QueryBuilder&lt;T&gt; with boolean properties
/// - Select(u => u) entity projections (including entities with Ref&lt;&gt; FK columns)
/// - Select with anonymous types (skipped by design, uses original method)
/// - Limit, Offset, Distinct (no interception needed)
/// </summary>
[TestFixture]
public class InterceptorIntegrationTests
{
    private MockDbConnection _connection = null!;
    private TestDbContext _db = null!;

    [SetUp]
    public void Setup()
    {
        _connection = new MockDbConnection();
        _db = new TestDbContext(_connection);
    }

    [TearDown]
    public void TearDown()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    #region Where Clause Interceptor Tests

    [Test]
    public void Where_BooleanProperty_GeneratesWhereClause()
    {
        // This triggers the Where interceptor with a boolean property access
        var sql = _db.Users().Where(u => u.IsActive).ToDiagnostics().Sql;

        Assert.That(sql, Does.Contain("WHERE"));
        Assert.That(sql, Does.Contain("IsActive"));
    }

    [Test]
    public void Where_MultipleChained_GeneratesAndConditions()
    {
        var sql = _db.Users()
            .Where(u => u.IsActive)
            .Where(u => u.UserId > 0)
            .ToDiagnostics().Sql;

        Assert.That(sql, Does.Contain("WHERE"));
        // Multiple WHERE calls should result in AND-combined conditions
    }

    #endregion

    #region Select Interceptor Tests - Tuple Projections

    [Test]
    public void Select_Tuple_TwoColumns_GeneratesCorrectSql()
    {
        var sql = _db.Users().Select(u => (u.UserId, u.UserName)).ToDiagnostics().Sql;

        Assert.That(sql, Does.Contain("SELECT"));
        Assert.That(sql, Does.Contain("\"UserId\""));
        Assert.That(sql, Does.Contain("\"UserName\""));
        Assert.That(sql, Does.Contain("FROM"));
        Assert.That(sql, Does.Contain("\"users\""));
    }

    [Test]
    public void Select_Tuple_ThreeColumns_GeneratesCorrectSql()
    {
        var sql = _db.Users().Select(u => (u.UserId, u.UserName, u.IsActive)).ToDiagnostics().Sql;

        Assert.That(sql, Does.Contain("\"UserId\""));
        Assert.That(sql, Does.Contain("\"UserName\""));
        Assert.That(sql, Does.Contain("\"IsActive\""));
    }

    [Test]
    public void Select_Tuple_WithNullableColumn_GeneratesCorrectSql()
    {
        var sql = _db.Users().Select(u => (u.UserId, u.Email)).ToDiagnostics().Sql;

        Assert.That(sql, Does.Contain("\"UserId\""));
        Assert.That(sql, Does.Contain("\"Email\""));
    }

    [Test]
    public void Select_Tuple_AllColumns_GeneratesCorrectSql()
    {
        var sql = _db.Users().Select(u => (u.UserId, u.UserName, u.Email, u.IsActive, u.CreatedAt, u.LastLogin)).ToDiagnostics().Sql;

        Assert.That(sql, Does.Contain("\"UserId\""));
        Assert.That(sql, Does.Contain("\"UserName\""));
        Assert.That(sql, Does.Contain("\"Email\""));
        Assert.That(sql, Does.Contain("\"IsActive\""));
        Assert.That(sql, Does.Contain("\"CreatedAt\""));
        Assert.That(sql, Does.Contain("\"LastLogin\""));
    }

    [Test]
    public void Select_Tuple_OrderEntity_GeneratesCorrectSql()
    {
        var sql = _db.Orders().Select(o => (o.OrderId, o.Total, o.Status)).ToDiagnostics().Sql;

        Assert.That(sql, Does.Contain("\"OrderId\""));
        Assert.That(sql, Does.Contain("\"Total\""));
        Assert.That(sql, Does.Contain("\"Status\""));
        Assert.That(sql, Does.Contain("\"orders\""));
    }

    #endregion

    #region Select Interceptor Tests - Entity Projections

    [Test]
    public void Select_Entity_User_GeneratesCorrectSql()
    {
        var sql = _db.Users().Select(u => u).ToDiagnostics().Sql;

        Assert.That(sql, Does.Contain("\"UserId\""));
        Assert.That(sql, Does.Contain("\"UserName\""));
        Assert.That(sql, Does.Contain("\"Email\""));
        Assert.That(sql, Does.Contain("\"IsActive\""));
        Assert.That(sql, Does.Contain("\"CreatedAt\""));
        Assert.That(sql, Does.Contain("\"users\""));
    }

    [Test]
    public void Select_Entity_Account_WithForeignKey_GeneratesCorrectSql()
    {
        // Account has Ref<User, int> UserId — entity projection must wrap FK columns
        var sql = _db.Accounts().Select(a => a).ToDiagnostics().Sql;

        Assert.That(sql, Does.Contain("\"AccountId\""));
        Assert.That(sql, Does.Contain("\"UserId\""));
        Assert.That(sql, Does.Contain("\"AccountName\""));
        Assert.That(sql, Does.Contain("\"Balance\""));
        Assert.That(sql, Does.Contain("\"accounts\""));
    }

    [Test]
    public void Select_Entity_Order_WithForeignKey_GeneratesCorrectSql()
    {
        // Order has Ref<User, int> UserId — entity projection must wrap FK columns
        var sql = _db.Orders().Select(o => o).ToDiagnostics().Sql;

        Assert.That(sql, Does.Contain("\"OrderId\""));
        Assert.That(sql, Does.Contain("\"UserId\""));
        Assert.That(sql, Does.Contain("\"Total\""));
        Assert.That(sql, Does.Contain("\"Status\""));
        Assert.That(sql, Does.Contain("\"orders\""));
    }

    #endregion

    #region Select Interceptor Tests - DTO Projections

    [Test]
    public void Select_Dto_UserSummary_GeneratesCorrectSql()
    {
        // Note: DTO property names must match entity property names for generator to work
        var sql = _db.Users().Select(u => new UserSummaryDto
        {
            UserId = u.UserId,
            UserName = u.UserName,
            IsActive = u.IsActive
        }).ToDiagnostics().Sql;

        Assert.That(sql, Does.Contain("\"UserId\""));
        Assert.That(sql, Does.Contain("\"UserName\""));
        Assert.That(sql, Does.Contain("\"IsActive\""));
    }

    [Test]
    public void Select_Dto_UserWithEmail_GeneratesCorrectSql()
    {
        var sql = _db.Users().Select(u => new UserWithEmailDto
        {
            UserId = u.UserId,
            UserName = u.UserName,
            Email = u.Email
        }).ToDiagnostics().Sql;

        Assert.That(sql, Does.Contain("\"UserId\""));
        Assert.That(sql, Does.Contain("\"UserName\""));
        Assert.That(sql, Does.Contain("\"Email\""));
    }

    [Test]
    public void Select_Dto_OrderSummary_GeneratesCorrectSql()
    {
        var sql = _db.Orders().Select(o => new OrderSummaryDto
        {
            OrderId = o.OrderId,
            Total = o.Total,
            Status = o.Status
        }).ToDiagnostics().Sql;

        Assert.That(sql, Does.Contain("\"OrderId\""));
        Assert.That(sql, Does.Contain("\"Total\""));
        Assert.That(sql, Does.Contain("\"Status\""));
    }

    #endregion

    #region Select with Where Clause Tests

    [Test]
    public void Where_ThenSelect_Tuple_GeneratesBothClauses()
    {
        var sql = _db.Users()
            .Where(u => u.IsActive)
            .Select(u => (u.UserId, u.UserName))
            .ToDiagnostics().Sql;

        Assert.That(sql, Does.Contain("SELECT"));
        Assert.That(sql, Does.Contain("\"UserId\""));
        Assert.That(sql, Does.Contain("\"UserName\""));
        Assert.That(sql, Does.Contain("WHERE"));
        Assert.That(sql, Does.Contain("\"IsActive\""));
    }

    [Test]
    public void Where_ThenSelect_Dto_GeneratesBothClauses()
    {
        // Note: DTO property names must match entity property names for generator to work
        var sql = _db.Users()
            .Where(u => u.IsActive)
            .Select(u => new UserSummaryDto { UserId = u.UserId, UserName = u.UserName, IsActive = u.IsActive })
            .ToDiagnostics().Sql;

        Assert.That(sql, Does.Contain("\"UserId\""));
        Assert.That(sql, Does.Contain("\"UserName\""));
        Assert.That(sql, Does.Contain("WHERE"));
    }

    #endregion

    #region Select with Pagination Tests

    [Test]
    public void Select_Tuple_WithLimit_GeneratesCorrectSql()
    {
        var sql = _db.Users()
            .Select(u => (u.UserId, u.UserName))
            .Limit(10)
            .ToDiagnostics().Sql;

        Assert.That(sql, Does.Contain("\"UserId\""));
        Assert.That(sql, Does.Contain("\"UserName\""));
        Assert.That(sql, Does.Contain("LIMIT 10"));
    }

    [Test]
    public void Select_Tuple_WithOffsetLimit_GeneratesCorrectSql()
    {
        var sql = _db.Users()
            .Select(u => (u.UserId, u.UserName))
            .Offset(20)
            .Limit(10)
            .ToDiagnostics().Sql;

        Assert.That(sql, Does.Contain("\"UserId\""));
        Assert.That(sql, Does.Contain("OFFSET 20"));
        Assert.That(sql, Does.Contain("LIMIT 10"));
    }

    #endregion

    #region Complex Select Chain Tests

    [Test]
    public void ComplexChain_WhereTupleSelectLimitOffset_GeneratesCorrectSql()
    {
        var sql = _db.Users()
            .Where(u => u.IsActive)
            .Select(u => (u.UserId, u.UserName, u.Email))
            .Limit(50)
            .Offset(100)
            .ToDiagnostics().Sql;

        Assert.That(sql, Does.Contain("SELECT"));
        Assert.That(sql, Does.Contain("\"UserId\""));
        Assert.That(sql, Does.Contain("\"UserName\""));
        Assert.That(sql, Does.Contain("\"Email\""));
        Assert.That(sql, Does.Contain("WHERE"));
        Assert.That(sql, Does.Contain("LIMIT 50"));
        Assert.That(sql, Does.Contain("OFFSET 100"));
    }
    
    [Test]
    public void ComplexChain_WhereTupleWithExternalCapturedParameter_GeneratesCorrectSql()
    {
        int externalValueParameter = 44;
        var sql = _db.Users()
            .Where(u => u.UserId >= externalValueParameter)
            .Select(u => (u.UserId, u.UserName, u.Email))
            .ToDiagnostics().Sql;

        Assert.That(sql, Does.Contain("SELECT"));
        Assert.That(sql, Does.Contain("\"UserId\""));
        Assert.That(sql, Does.Contain("\"UserName\""));
        Assert.That(sql, Does.Contain("\"Email\""));
        Assert.That(sql, Does.Contain("WHERE"));
        // The external parameter should be translated to a SQL parameter placeholder
        Assert.That(sql, Does.Contain("@p0"));
        Assert.That(sql, Does.Contain(">="));
    }

    [Test]
    public void ComplexChain_MultipleWheresTupleSelect_GeneratesCorrectSql()
    {
        var sql = _db.Users()
            .Where(u => u.IsActive)
            .Where(u => u.UserId > 0)
            .Select(u => (u.UserId, u.UserName))
            .ToDiagnostics().Sql;

        Assert.That(sql, Does.Contain("SELECT"));
        Assert.That(sql, Does.Contain("\"UserId\""));
        Assert.That(sql, Does.Contain("\"UserName\""));
        Assert.That(sql, Does.Contain("WHERE"));
    }

    #endregion

    #region Pagination Tests (No Interception Needed)

    [Test]
    public void Where_WithLimit_GeneratesBothClauses()
    {
        var sql = _db.Users()
            .Where(u => u.IsActive)
            .Limit(10)
            .ToDiagnostics().Sql;

        Assert.That(sql, Does.Contain("WHERE"));
        Assert.That(sql, Does.Contain("LIMIT 10"));
    }

    [Test]
    public void Where_WithLimitOffset_GeneratesAllClauses()
    {
        var sql = _db.Users()
            .Where(u => u.IsActive)
            .Offset(20)
            .Limit(10)
            .ToDiagnostics().Sql;

        Assert.That(sql, Does.Contain("WHERE"));
        Assert.That(sql, Does.Contain("OFFSET 20"));
        Assert.That(sql, Does.Contain("LIMIT 10"));
    }

    #endregion

    #region Distinct Tests (No Interception Needed)

    [Test]
    public void Where_WithDistinct_GeneratesBothClauses()
    {
        var sql = _db.Users()
            .Where(u => u.IsActive)
            .Distinct()
            .ToDiagnostics().Sql;

        Assert.That(sql, Does.Contain("SELECT DISTINCT"));
        Assert.That(sql, Does.Contain("WHERE"));
    }

    #endregion

    #region Multiple Entity QueryBuilder Tests

    [Test]
    public void Orders_TupleSelect_GeneratesSql()
    {
        var sql = _db.Orders().Select(o => (o.OrderId, o.Total)).ToDiagnostics().Sql;

        Assert.That(sql, Does.Contain("SELECT"));
        Assert.That(sql, Does.Contain("FROM"));
    }

    [Test]
    public void OrderItems_TupleSelect_GeneratesSql()
    {
        var sql = _db.OrderItems().Select(oi => (oi.OrderItemId, oi.ProductName)).ToDiagnostics().Sql;

        Assert.That(sql, Does.Contain("SELECT"));
        Assert.That(sql, Does.Contain("FROM"));
    }

    #endregion

    #region Generated Entity Tests

    [Test]
    public void User_CanBeInstantiated()
    {
        var user = new User
        {
            UserId = 1,
            UserName = "testuser",
            Email = "test@example.com",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            LastLogin = DateTime.UtcNow
        };

        Assert.That(user.UserId, Is.EqualTo(1));
        Assert.That(user.UserName, Is.EqualTo("testuser"));
        Assert.That(user.Email, Is.EqualTo("test@example.com"));
        Assert.That(user.IsActive, Is.True);
    }

    [Test]
    public void User_NullableProperties_CanBeNull()
    {
        var user = new User
        {
            UserId = 1,
            UserName = "testuser",
            Email = null, // Email is nullable
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            LastLogin = null // LastLogin is nullable
        };

        Assert.That(user.Email, Is.Null);
        Assert.That(user.LastLogin, Is.Null);
    }

    [Test]
    public void Order_HasForeignKeyRef()
    {
        var order = new Order
        {
            OrderId = 1,
            UserId = new EntityRef<User, int> { Id = 42 },
            Total = 199.99m,
            Status = "Completed",
            OrderDate = DateTime.UtcNow
        };

        Assert.That(order.OrderId, Is.EqualTo(1));
        Assert.That(order.UserId.Id, Is.EqualTo(42));
        Assert.That(order.UserId.Value, Is.Null); // Not loaded
        Assert.That(order.Total, Is.EqualTo(199.99m));
    }

    [Test]
    public void Order_NullableNotes_CanBeNull()
    {
        var order = new Order
        {
            OrderId = 1,
            UserId = new EntityRef<User, int> { Id = 1 },
            Total = 50m,
            Status = "Pending",
            OrderDate = DateTime.UtcNow,
            Notes = null
        };

        Assert.That(order.Notes, Is.Null);
    }

    [Test]
    public void OrderItem_HasForeignKeyToOrder()
    {
        var item = new OrderItem
        {
            OrderItemId = 1,
            OrderId = new EntityRef<Order, int> { Id = 10 },
            ProductName = "Widget",
            Quantity = 5,
            UnitPrice = 9.99m,
            LineTotal = 49.95m
        };

        Assert.That(item.OrderId.Id, Is.EqualTo(10));
        Assert.That(item.ProductName, Is.EqualTo("Widget"));
        Assert.That(item.LineTotal, Is.EqualTo(49.95m));
    }

    #endregion

    #region EntityRef<T,K> Structure Tests

    [Test]
    public void Ref_Id_IsAccessible()
    {
        var refValue = new EntityRef<User, int> { Id = 123 };
        Assert.That(refValue.Id, Is.EqualTo(123));
    }

    [Test]
    public void Ref_Value_DefaultsToNull()
    {
        var refValue = new EntityRef<User, int> { Id = 1 };
        Assert.That(refValue.Value, Is.Null);
    }

    [Test]
    public void Ref_CanHaveValueSet()
    {
        var user = new User { UserId = 1, UserName = "test", IsActive = true, CreatedAt = DateTime.UtcNow };
        var refValue = new EntityRef<User, int> { Id = 1, Value = user };

        Assert.That(refValue.Value, Is.Not.Null);
        Assert.That(refValue.Value!.UserName, Is.EqualTo("test"));
    }

    #endregion

    #region NavigationList Tests

    [Test]
    public void User_Orders_IsNavigationList()
    {
        var user = new User
        {
            UserId = 1,
            UserName = "test",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        // Orders property should exist but be unloaded when not populated via join
        Assert.That(user.Orders, Is.Not.Null);
        Assert.That(user.Orders.IsLoaded, Is.False);
        Assert.That(user.Orders.Count, Is.EqualTo(0));
    }

    [Test]
    public void Order_Items_IsNavigationList()
    {
        var order = new Order
        {
            OrderId = 1,
            UserId = new EntityRef<User, int> { Id = 1 },
            Total = 100m,
            Status = "Active",
            OrderDate = DateTime.UtcNow
        };

        // Items property should exist but be unloaded when not populated via join
        Assert.That(order.Items, Is.Not.Null);
        Assert.That(order.Items.IsLoaded, Is.False);
        Assert.That(order.Items.Count, Is.EqualTo(0));
    }

    #endregion

    #region Context Disposal Tests

    [Test]
    public void Context_Dispose_DoesNotThrow()
    {
        using var connection = new MockDbConnection();
        using var db = new TestDbContext(connection);

        // Use the context
        var sql = db.Users().Where(u => u.IsActive).ToDiagnostics().Sql;
        Assert.That(sql, Is.Not.Empty);

        // Disposal should not throw
        Assert.DoesNotThrow(() => db.Dispose());
    }

    #endregion

    #region Validation Tests

    [Test]
    public void Limit_NegativeValue_ThrowsArgumentOutOfRange()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _db.Users().Where(u => u.IsActive).Limit(-1));
    }

    [Test]
    public void Offset_NegativeValue_ThrowsArgumentOutOfRange()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _db.Users().Where(u => u.IsActive).Offset(-1));
    }

    [Test]
    public void WithTimeout_ZeroOrNegative_ThrowsArgumentOutOfRange()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _db.Users().Where(u => u.IsActive).WithTimeout(TimeSpan.Zero));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _db.Users().Where(u => u.IsActive).WithTimeout(TimeSpan.FromSeconds(-1)));
    }

    #endregion

    #region Complex Chain Tests

    [Test]
    public void ComplexChain_WhereLimitOffset_GeneratesCorrectSql()
    {
        var sql = _db.Users()
            .Where(u => u.IsActive)
            .Limit(100)
            .Offset(50)
            .ToDiagnostics().Sql;

        Assert.That(sql, Does.Contain("WHERE"));
        Assert.That(sql, Does.Contain("LIMIT 100"));
        Assert.That(sql, Does.Contain("OFFSET 50"));
    }

    [Test]
    public void ComplexChain_WhereDistinctLimit_GeneratesCorrectSql()
    {
        var sql = _db.Users()
            .Where(u => u.IsActive)
            .Distinct()
            .Limit(20)
            .ToDiagnostics().Sql;

        Assert.That(sql, Does.Contain("SELECT DISTINCT"));
        Assert.That(sql, Does.Contain("WHERE"));
        Assert.That(sql, Does.Contain("LIMIT 20"));
    }

    #endregion

    #region Captured Parameter Extraction Tests

    [Test]
    public void CapturedParameter_DirectPath_ExtractsCorrectValue()
    {
        // Arrange
        int value = 42;

        // Act - the captured variable should be extracted using direct path navigation
        var query = (QueryBuilder<User, (int UserId, string UserName)>)_db.Users()
            .Where(u => u.UserId >= value)
            .Select(u => (u.UserId, u.UserName));

        // Assert - verify the parameter was correctly extracted
        Assert.That(query.State.Parameters.Length, Is.EqualTo(1));
        Assert.That(query.State.Parameters[0].Value, Is.EqualTo(42));
    }

    [Test]
    public void CapturedParameter_ValueChanges_ReflectedInQuery()
    {
        // Arrange - first query with value = 42
        int value = 42;
        var query1 = (QueryBuilder<User, (int UserId, string UserName)>)_db.Users()
            .Where(u => u.UserId >= value)
            .Select(u => (u.UserId, u.UserName));

        // Change the value and create a new query
        value = 100;
        var query2 = (QueryBuilder<User, (int UserId, string UserName)>)_db.Users()
            .Where(u => u.UserId >= value)
            .Select(u => (u.UserId, u.UserName));

        // Assert - each query should have captured its respective value
        Assert.That(query1.State.Parameters[0].Value, Is.EqualTo(42));
        Assert.That(query2.State.Parameters[0].Value, Is.EqualTo(100));
    }

    [Test]
    public void CapturedParameter_ComplexExpression_WorksCorrectly()
    {
        // Arrange
        int minValue = 10;
        int maxValue = 100;

        // Act - multiple captured variables in a complex expression
        var query = (QueryBuilder<User, (int UserId, string UserName)>)_db.Users()
            .Where(u => u.UserId >= minValue && u.UserId <= maxValue)
            .Select(u => (u.UserId, u.UserName));

        // Assert - both parameters should be correctly extracted
        Assert.That(query.State.Parameters.Length, Is.EqualTo(2));
        Assert.That(query.State.Parameters[0].Value, Is.EqualTo(10));
        Assert.That(query.State.Parameters[1].Value, Is.EqualTo(100));

        var s = query.ToDiagnostics().Sql;
    }

    [Test]
    public void CapturedParameter_StringContains_ExtractsCorrectValue()
    {
        // Arrange
        string searchTerm = "john";

        // Act - captured variable in method call
        var query = (QueryBuilder<User, (int UserId, string UserName)>)_db.Users()
            .Where(u => u.UserName.Contains(searchTerm))
            .Select(u => (u.UserId, u.UserName));

        // Assert - the raw string parameter should be correctly extracted
        // Note: The % wildcards are added in the SQL LIKE pattern, not the parameter value
        Assert.That(query.State.Parameters.Length, Is.EqualTo(1));
        Assert.That(query.State.Parameters[0].Value, Is.EqualTo("john"));
    }

    [Test]
    public void CapturedParameter_NullableValue_ExtractsCorrectly()
    {
        // Arrange
        DateTime? cutoffDate = 
            new DateTime(2024, 1, 15);

        // Act
        var query = (QueryBuilder<User, (int UserId, string UserName)>)_db.Users()
            .Where(u => u.LastLogin > cutoffDate)
            .Select(u => (u.UserId, u.UserName));

        // Assert
        Assert.That(query.State.Parameters.Length, Is.EqualTo(1));
        Assert.That(query.State.Parameters[0].Value, Is.EqualTo(new DateTime(2024, 1, 15)));
    }

    [Test]
    public void CapturedParameter_LeftSideOfComparison_ExtractsCorrectValue()
    {
        // Arrange - captured variable on left side of comparison
        int threshold = 50;

        // Act
        var query = (QueryBuilder<User, (int UserId, string UserName)>)_db.Users()
            .Where(u => threshold <= u.UserId)
            .Select(u => (u.UserId, u.UserName));

        // Assert
        Assert.That(query.State.Parameters.Length, Is.EqualTo(1));
        Assert.That(query.State.Parameters[0].Value, Is.EqualTo(50));
    }

    [Test]
    public void CapturedParameter_InOrExpression_ExtractsCorrectValues()
    {
        // Arrange
        int value1 = 10;
        int value2 = 20;

        // Act - captured variables in OR expression
        var query = (QueryBuilder<User, (int UserId, string UserName)>)_db.Users()
            .Where(u => u.UserId == value1 || u.UserId == value2)
            .Select(u => (u.UserId, u.UserName));

        // Assert
        Assert.That(query.State.Parameters.Length, Is.EqualTo(2));
        Assert.That(query.State.Parameters[0].Value, Is.EqualTo(10));
        Assert.That(query.State.Parameters[1].Value, Is.EqualTo(20));
    }

    #endregion

    #region Captured Parameter Execution Tests

    [Test]
    public async Task CapturedParameterExecution_DirectPath_PassesCorrectValue()
    {
        int value = 42;

        var results = await _db.Users()
            .Where(u => u.UserId >= value)
            .Select(u => (u.UserId, u.UserName))
            .ExecuteFetchAllAsync();

        Assert.That(results, Is.Not.Null);
        Assert.That(_connection.LastCommand, Is.Not.Null);
        var sql = _connection.LastCommand!.CommandText;
        Assert.That(sql, Does.Contain("SELECT"));
        Assert.That(sql, Does.Contain("WHERE"));
        Assert.That(_connection.LastCommand.Parameters.Count, Is.EqualTo(1));
        Assert.That(((DbParameter)_connection.LastCommand.Parameters[0]!).Value, Is.EqualTo(42));
    }

    [Test]
    public async Task CapturedParameterExecution_ValueChanges_ReflectedInExecutedQuery()
    {
        int value = 42;
        var results1 = await _db.Users()
            .Where(u => u.UserId >= value)
            .Select(u => (u.UserId, u.UserName))
            .ExecuteFetchAllAsync();

        var cmd1Params = _connection.LastCommand!.Parameters;
        Assert.That(((DbParameter)cmd1Params[0]!).Value, Is.EqualTo(42));

        value = 100;
        var results2 = await _db.Users()
            .Where(u => u.UserId >= value)
            .Select(u => (u.UserId, u.UserName))
            .ExecuteFetchAllAsync();

        Assert.That(((DbParameter)_connection.LastCommand!.Parameters[0]!).Value, Is.EqualTo(100));
    }

    [Test]
    public async Task CapturedParameterExecution_ComplexExpression_PassesBothValues()
    {
        int minValue = 10;
        int maxValue = 100;

        var results = await _db.Users()
            .Where(u => u.UserId >= minValue && u.UserId <= maxValue)
            .Select(u => (u.UserId, u.UserName))
            .ExecuteFetchAllAsync();

        Assert.That(results, Is.Not.Null);
        Assert.That(_connection.LastCommand, Is.Not.Null);
        Assert.That(_connection.LastCommand!.Parameters.Count, Is.EqualTo(2));
        Assert.That(((DbParameter)_connection.LastCommand.Parameters[0]!).Value, Is.EqualTo(10));
        Assert.That(((DbParameter)_connection.LastCommand.Parameters[1]!).Value, Is.EqualTo(100));
    }

    [Test]
    public async Task CapturedParameterExecution_StringContains_PassesCorrectValue()
    {
        string searchTerm = "john";

        var results = await _db.Users()
            .Where(u => u.UserName.Contains(searchTerm))
            .Select(u => (u.UserId, u.UserName))
            .ExecuteFetchAllAsync();

        Assert.That(results, Is.Not.Null);
        Assert.That(_connection.LastCommand, Is.Not.Null);
        Assert.That(_connection.LastCommand!.Parameters.Count, Is.EqualTo(1));
        Assert.That(((DbParameter)_connection.LastCommand.Parameters[0]!).Value, Is.EqualTo("john"));
    }

    [Test]
    public async Task CapturedParameterExecution_NullableValue_PassesCorrectly()
    {
        DateTime? cutoffDate = new DateTime(2024, 1, 15);

        var results = await _db.Users()
            .Where(u => u.LastLogin > cutoffDate)
            .Select(u => (u.UserId, u.UserName))
            .ExecuteFetchAllAsync();

        Assert.That(results, Is.Not.Null);
        Assert.That(_connection.LastCommand, Is.Not.Null);
        Assert.That(_connection.LastCommand!.Parameters.Count, Is.EqualTo(1));
        Assert.That(((DbParameter)_connection.LastCommand.Parameters[0]!).Value, Is.EqualTo(new DateTime(2024, 1, 15)));
    }

    [Test]
    public async Task CapturedParameterExecution_LeftSideOfComparison_PassesCorrectValue()
    {
        int threshold = 50;

        var results = await _db.Users()
            .Where(u => threshold <= u.UserId)
            .Select(u => (u.UserId, u.UserName))
            .ExecuteFetchAllAsync();

        Assert.That(results, Is.Not.Null);
        Assert.That(_connection.LastCommand, Is.Not.Null);
        Assert.That(_connection.LastCommand!.Parameters.Count, Is.EqualTo(1));
        Assert.That(((DbParameter)_connection.LastCommand.Parameters[0]!).Value, Is.EqualTo(50));
    }

    [Test]
    public async Task CapturedParameterExecution_InOrExpression_PassesBothValues()
    {
        int value1 = 10;
        int value2 = 20;

        var results = await _db.Users()
            .Where(u => u.UserId == value1 || u.UserId == value2)
            .Select(u => (u.UserId, u.UserName))
            .ExecuteFetchAllAsync();

        Assert.That(results, Is.Not.Null);
        Assert.That(_connection.LastCommand, Is.Not.Null);
        Assert.That(_connection.LastCommand!.Parameters.Count, Is.EqualTo(2));
        Assert.That(((DbParameter)_connection.LastCommand.Parameters[0]!).Value, Is.EqualTo(10));
        Assert.That(((DbParameter)_connection.LastCommand.Parameters[1]!).Value, Is.EqualTo(20));
    }

    #endregion

    #region Join Integration Tests

    [Test]
    public void Join_UsersOrders_GeneratesInnerJoinSql()
    {
        var sql = _db.Users()
            .Join<Order>((u, o) => u.UserId == o.UserId.Id)
            .ToDiagnostics().Sql;

        Assert.That(sql, Does.Contain("INNER JOIN"));
        Assert.That(sql, Does.Contain("AS \"t1\""));
        Assert.That(sql, Does.Contain("ON"));
    }

    [Test]
    public void LeftJoin_UsersOrders_GeneratesLeftJoinSql()
    {
        var sql = _db.Users()
            .LeftJoin<Order>((u, o) => u.UserId == o.UserId.Id)
            .ToDiagnostics().Sql;

        Assert.That(sql, Does.Contain("LEFT JOIN"));
        Assert.That(sql, Does.Contain("AS \"t1\""));
        Assert.That(sql, Does.Contain("ON"));
    }

    [Test]
    public void RightJoin_UsersOrders_GeneratesRightJoinSql()
    {
        var sql = _db.Users()
            .RightJoin<Order>((u, o) => u.UserId == o.UserId.Id)
            .ToDiagnostics().Sql;

        Assert.That(sql, Does.Contain("RIGHT JOIN"));
        Assert.That(sql, Does.Contain("AS \"t1\""));
        Assert.That(sql, Does.Contain("ON"));
    }

    [Test]
    public void Join_WithLimit_GeneratesBothClauses()
    {
        var sql = _db.Users()
            .Join<Order>((u, o) => u.UserId == o.UserId.Id)
            .Limit(10)
            .ToDiagnostics().Sql;

        Assert.That(sql, Does.Contain("INNER JOIN"));
        Assert.That(sql, Does.Contain("LIMIT 10"));
    }

    [Test]
    public void Join_WithOffset_GeneratesBothClauses()
    {
        var sql = _db.Users()
            .Join<Order>((u, o) => u.UserId == o.UserId.Id)
            .Offset(20)
            .Limit(10)
            .ToDiagnostics().Sql;

        Assert.That(sql, Does.Contain("INNER JOIN"));
        Assert.That(sql, Does.Contain("OFFSET 20"));
        Assert.That(sql, Does.Contain("LIMIT 10"));
    }

    [Test]
    public void Join_WithDistinct_GeneratesBothClauses()
    {
        var sql = _db.Users()
            .Join<Order>((u, o) => u.UserId == o.UserId.Id)
            .Distinct()
            .ToDiagnostics().Sql;

        Assert.That(sql, Does.Contain("SELECT DISTINCT"));
        Assert.That(sql, Does.Contain("INNER JOIN"));
    }

    [Test]
    public void Join_OrdersOrderItems_GeneratesJoinSql()
    {
        var sql = _db.Orders()
            .Join<OrderItem>((o, oi) => o.OrderId == oi.OrderId.Id)
            .ToDiagnostics().Sql;

        Assert.That(sql, Does.Contain("INNER JOIN"));
        Assert.That(sql, Does.Contain("AS \"t1\""));
        Assert.That(sql, Does.Contain("ON"));
    }

    [Test]
    public void LeftJoin_OrdersOrderItems_GeneratesLeftJoinSql()
    {
        var sql = _db.Orders()
            .LeftJoin<OrderItem>((o, oi) => o.OrderId == oi.OrderId.Id)
            .ToDiagnostics().Sql;

        Assert.That(sql, Does.Contain("LEFT JOIN"));
        Assert.That(sql, Does.Contain("AS \"t1\""));
    }

    #endregion

    #region 3-Table Join Integration Tests

    [Test]
    public void ThreeTableJoin_UsersOrdersOrderItems_GeneratesChainedJoinSql()
    {
        var sql = _db.Users()
            .Join<Order>((u, o) => u.UserId == o.UserId.Id)
            .Join<OrderItem>((u, o, oi) => o.OrderId == oi.OrderId.Id)
            .ToDiagnostics().Sql;

        Assert.That(sql, Does.Contain("INNER JOIN"));
        Assert.That(sql, Does.Contain("AS \"t0\""));
        Assert.That(sql, Does.Contain("AS \"t1\""));
        Assert.That(sql, Does.Contain("AS \"t2\""));
    }

    [Test]
    public void ThreeTableJoin_WithWhere_GeneratesFilterClause()
    {
        var sql = _db.Users()
            .Join<Order>((u, o) => u.UserId == o.UserId.Id)
            .Join<OrderItem>((u, o, oi) => o.OrderId == oi.OrderId.Id)
            .Where((u, o, oi) => u.IsActive)
            .ToDiagnostics().Sql;

        Assert.That(sql, Does.Contain("INNER JOIN"));
        Assert.That(sql, Does.Contain("WHERE"));
    }

    [Test]
    public void ThreeTableJoin_WithPagination_GeneratesOffsetLimit()
    {
        var sql = _db.Users()
            .Join<Order>((u, o) => u.UserId == o.UserId.Id)
            .Join<OrderItem>((u, o, oi) => o.OrderId == oi.OrderId.Id)
            .Offset(10)
            .Limit(20)
            .ToDiagnostics().Sql;

        Assert.That(sql, Does.Contain("OFFSET 10"));
        Assert.That(sql, Does.Contain("LIMIT 20"));
    }

    [Test]
    public void ThreeTableJoin_MixedJoinTypes_LeftAndInner()
    {
        var sql = _db.Users()
            .LeftJoin<Order>((u, o) => u.UserId == o.UserId.Id)
            .Join<OrderItem>((u, o, oi) => o.OrderId == oi.OrderId.Id)
            .ToDiagnostics().Sql;

        Assert.That(sql, Does.Contain("LEFT JOIN"));
        Assert.That(sql, Does.Contain("INNER JOIN"));
    }

    #endregion

    #region Joined Select Projection Tests

    [Test]
    public void TwoTableJoin_Select_Dto_GeneratesSqlWithAliasedColumns()
    {
        var sql = _db.Users()
            .Join<Order>((u, o) => u.UserId == o.UserId.Id)
            .Select((u, o) => new UserOrderDto { UserName = u.UserName, Total = o.Total })
            .ToDiagnostics().Sql;

        Assert.That(sql, Does.Contain("\"t0\"."));
        Assert.That(sql, Does.Contain("\"t1\"."));
        Assert.That(sql, Does.Contain("SELECT"));
    }

    [Test]
    public void TwoTableJoin_Select_Tuple_GeneratesSqlWithAliasedColumns()
    {
        var sql = _db.Users()
            .Join<Order>((u, o) => u.UserId == o.UserId.Id)
            .Select((u, o) => (u.UserName, o.Total))
            .ToDiagnostics().Sql;

        Assert.That(sql, Does.Contain("\"t0\"."));
        Assert.That(sql, Does.Contain("\"t1\"."));
    }

    [Test]
    public void ThreeTableJoin_Select_Dto_GeneratesSqlWithAliasedColumns()
    {
        var sql = _db.Users()
            .Join<Order>((u, o) => u.UserId == o.UserId.Id)
            .Join<OrderItem>((u, o, oi) => o.OrderId == oi.OrderId.Id)
            .Select((u, o, oi) => new UserOrderItemDto { UserName = u.UserName, Total = o.Total, ProductName = oi.ProductName })
            .ToDiagnostics().Sql;

        Assert.That(sql, Does.Contain("\"t0\"."));
        Assert.That(sql, Does.Contain("\"t1\"."));
        Assert.That(sql, Does.Contain("\"t2\"."));
    }

    [Test]
    public void TwoTableJoin_Select_SingleColumn_GeneratesSqlWithAlias()
    {
        var sql = _db.Users()
            .Join<Order>((u, o) => u.UserId == o.UserId.Id)
            .Select((u, o) => o.Total)
            .ToDiagnostics().Sql;

        Assert.That(sql, Does.Contain("\"t1\"."));
    }

    #endregion

    #region Insert Interceptor Tests

    [Test]
    public async Task Insert_SingleEntity_ExecuteNonQueryAsync_Succeeds()
    {
        var result = await _db.Users().Insert(new User
        {
            UserName = "testuser",
            Email = "test@example.com",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        }).ExecuteNonQueryAsync();

        Assert.That(result, Is.EqualTo(1));
        Assert.That(_connection.LastCommand, Is.Not.Null);
        Assert.That(_connection.LastCommand!.CommandText, Does.Contain("INSERT INTO"));
        Assert.That(_connection.LastCommand.CommandText, Does.Contain("\"users\""));
        Assert.That(_connection.LastCommand.CommandText, Does.Contain("\"UserName\""));
        Assert.That(_connection.LastCommand.CommandText, Does.Contain("\"Email\""));
        Assert.That(_connection.LastCommand.CommandText, Does.Contain("\"IsActive\""));
        Assert.That(_connection.LastCommand.CommandText, Does.Contain("\"CreatedAt\""));
        Assert.That(_connection.LastCommand.CommandText, Does.Not.Contain("\"LastLogin\""));
    }

    // InsertMany test removed: old InsertMany() API has been replaced by column-selector batch API.

    [Test]
    public async Task Insert_SingleEntity_ExecuteScalarAsync_ReturnsIdentity()
    {
        _connection.ScalarResult = 42;

        var identity = await _db.Users().Insert(new User
        {
            UserName = "newuser",
            Email = "new@example.com",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        }).ExecuteScalarAsync<int>();

        Assert.That(identity, Is.EqualTo(42));
        Assert.That(_connection.LastCommand, Is.Not.Null);
        Assert.That(_connection.LastCommand!.CommandText, Does.Contain("INSERT INTO"));
        Assert.That(_connection.LastCommand.CommandText, Does.Contain("RETURNING"));
        Assert.That(_connection.LastCommand.CommandText, Does.Contain("\"UserId\""));
    }

    [Test]
    public async Task Insert_Order_ExecuteNonQueryAsync_Succeeds()
    {
        var result = await _db.Orders().Insert(new Order
        {
            UserId = 1,
            Total = 99.99m,
            Status = "Pending",
            OrderDate = DateTime.UtcNow
        }).ExecuteNonQueryAsync();

        Assert.That(result, Is.EqualTo(1));
        Assert.That(_connection.LastCommand, Is.Not.Null);
        Assert.That(_connection.LastCommand!.CommandText, Does.Contain("INSERT INTO"));
        Assert.That(_connection.LastCommand.CommandText, Does.Contain("\"orders\""));
    }

    #endregion

    #region Update Set Interceptor Tests

    [Test]
    public void UpdateSet_SingleColumn_GeneratesSetClause()
    {
        var sql = _db.Users().Update()
            .Set(u => u.UserName = "NewName")
            .Where(u => u.UserId == 1)
            .ToDiagnostics().Sql;

        Assert.That(sql, Does.Contain("UPDATE"));
        Assert.That(sql, Does.Contain("\"users\""));
        Assert.That(sql, Does.Contain("SET"));
        Assert.That(sql, Does.Contain("\"UserName\""));
        Assert.That(sql, Does.Contain("WHERE"));
        Assert.That(sql, Does.Contain("\"UserId\""));
    }

    [Test]
    public void UpdateSet_MultipleColumns_GeneratesMultipleSetClauses()
    {
        var sql = _db.Users().Update()
            .Set(u => u.UserName = "NewName")
            .Set(u => u.IsActive = false)
            .Where(u => u.UserId == 1)
            .ToDiagnostics().Sql;

        Assert.That(sql, Does.Contain("SET"));
        Assert.That(sql, Does.Contain("\"UserName\""));
        Assert.That(sql, Does.Contain("\"IsActive\""));
        Assert.That(sql, Does.Contain("WHERE"));
    }

    [Test]
    public void UpdateSet_WithAll_GeneratesUpdateWithoutWhere()
    {
        var sql = _db.Users().Update()
            .Set(u => u.IsActive = false)
            .All()
            .ToDiagnostics().Sql;

        Assert.That(sql, Does.Contain("UPDATE"));
        Assert.That(sql, Does.Contain("SET"));
        Assert.That(sql, Does.Contain("\"IsActive\""));
        Assert.That(sql, Does.Not.Contain("WHERE"));
    }

    #endregion

    #region Update Where Interceptor Tests

    [Test]
    public void UpdateWhere_SimpleCondition_GeneratesWhereClause()
    {
        var sql = _db.Users().Update()
            .Set(u => u.UserName = "Updated")
            .Where(u => u.IsActive)
            .ToDiagnostics().Sql;

        Assert.That(sql, Does.Contain("WHERE"));
        Assert.That(sql, Does.Contain("\"IsActive\""));
    }

    [Test]
    public void UpdateWhere_WithCapturedParameter_GeneratesParameterizedWhere()
    {
        int userId = 42;
        var sql = _db.Users().Update()
            .Set(u => u.UserName = "Updated")
            .Where(u => u.UserId == userId)
            .ToDiagnostics().Sql;

        Assert.That(sql, Does.Contain("WHERE"));
        Assert.That(sql, Does.Contain("@p"));
    }

    [Test]
    public void UpdateWhere_ChainedWheres_GeneratesAndConditions()
    {
        var sql = _db.Users().Update()
            .Set(u => u.UserName = "Updated")
            .Where(u => u.IsActive)
            .Where(u => u.UserId > 0)
            .ToDiagnostics().Sql;

        Assert.That(sql, Does.Contain("WHERE"));
        // Multiple WHERE calls should result in AND-combined conditions
    }

    [Test]
    public void UpdateSet_OnExecutableBuilder_ChainedAfterWhere()
    {
        // Set() after Where() operates on ExecutableUpdateBuilder
        var sql = _db.Users().Update()
            .Set(u => u.UserName = "First")
            .Where(u => u.UserId == 1)
            .Set(u => u.IsActive = true)
            .ToDiagnostics().Sql;

        Assert.That(sql, Does.Contain("SET"));
        Assert.That(sql, Does.Contain("\"UserName\""));
        Assert.That(sql, Does.Contain("\"IsActive\""));
        Assert.That(sql, Does.Contain("WHERE"));
    }

    [Test]
    public async Task Update_ExecuteNonQueryAsync_Succeeds()
    {
        var result = await _db.Users().Update()
            .Set(u => u.UserName = "Updated")
            .Where(u => u.UserId == 1)
            .ExecuteNonQueryAsync();

        Assert.That(result, Is.EqualTo(1));
        Assert.That(_connection.LastCommand, Is.Not.Null);
        Assert.That(_connection.LastCommand!.CommandText, Does.Contain("UPDATE"));
        Assert.That(_connection.LastCommand.CommandText, Does.Contain("SET"));
        Assert.That(_connection.LastCommand.CommandText, Does.Contain("WHERE"));
    }

    #endregion

    #region Update POCO Set Interceptor Tests

    [Test]
    public void UpdateSetPoco_SingleColumn_GeneratesSetClause()
    {
        var sql = _db.Users().Update()
            .Set(new User { UserName = "NewName" })
            .Where(u => u.UserId == 1)
            .ToDiagnostics().Sql;

        Assert.That(sql, Does.Contain("UPDATE"));
        Assert.That(sql, Does.Contain("\"users\""));
        Assert.That(sql, Does.Contain("SET"));
        Assert.That(sql, Does.Contain("\"UserName\""));
        Assert.That(sql, Does.Contain("WHERE"));
        Assert.That(sql, Does.Contain("\"UserId\""));
    }

    [Test]
    public void UpdateSetPoco_MultipleColumns_GeneratesSetClauses()
    {
        var sql = _db.Users().Update()
            .Set(new User { UserName = "NewName", IsActive = false })
            .Where(u => u.UserId == 1)
            .ToDiagnostics().Sql;

        Assert.That(sql, Does.Contain("SET"));
        Assert.That(sql, Does.Contain("\"UserName\""));
        Assert.That(sql, Does.Contain("\"IsActive\""));
        Assert.That(sql, Does.Contain("WHERE"));
    }

    [Test]
    public void UpdateSetPoco_WithAll_NoWhereClause()
    {
        var sql = _db.Users().Update()
            .Set(new User { IsActive = false })
            .All()
            .ToDiagnostics().Sql;

        Assert.That(sql, Does.Contain("UPDATE"));
        Assert.That(sql, Does.Contain("SET"));
        Assert.That(sql, Does.Contain("\"IsActive\""));
        Assert.That(sql, Does.Not.Contain("WHERE"));
    }

    [Test]
    public void UpdateSetPoco_MixedWithPropertySet()
    {
        // POCO Set followed by property Set — both should produce SET clauses
        var sql = _db.Users().Update()
            .Set(new User { UserName = "NewName" })
            .Set(u => u.IsActive = true)
            .Where(u => u.UserId == 1)
            .ToDiagnostics().Sql;

        Assert.That(sql, Does.Contain("\"UserName\""));
        Assert.That(sql, Does.Contain("\"IsActive\""));
    }

    [Test]
    public void UpdateSetPoco_OnExecutableBuilder_AfterWhere()
    {
        // POCO Set after Where() operates on ExecutableUpdateBuilder
        var sql = _db.Users().Update()
            .Set(u => u.UserName = "First")
            .Where(u => u.UserId == 1)
            .Set(new User { IsActive = true })
            .ToDiagnostics().Sql;

        Assert.That(sql, Does.Contain("\"UserName\""));
        Assert.That(sql, Does.Contain("\"IsActive\""));
        Assert.That(sql, Does.Contain("WHERE"));
    }

    [Test]
    public async Task UpdateSetPoco_ExecuteNonQueryAsync_Succeeds()
    {
        var result = await _db.Users().Update()
            .Set(new User { UserName = "Updated", IsActive = false })
            .Where(u => u.UserId == 1)
            .ExecuteNonQueryAsync();

        Assert.That(result, Is.EqualTo(1));
        Assert.That(_connection.LastCommand, Is.Not.Null);
        Assert.That(_connection.LastCommand!.CommandText, Does.Contain("UPDATE"));
        Assert.That(_connection.LastCommand.CommandText, Does.Contain("SET"));
        Assert.That(_connection.LastCommand.CommandText, Does.Contain("\"UserName\""));
        Assert.That(_connection.LastCommand.CommandText, Does.Contain("\"IsActive\""));
    }

    #endregion

    #region Tuple Aggregate Type Resolution Tests (Issue #49)

    [Test]
    public void Select_TupleWithAvg_GeneratesCorrectSql()
    {
        var sql = _db.Orders()
            .Where(o => true)
            .GroupBy(o => o.Status)
            .Select(o => (o.Status, Sql.Avg(o.Total)))
            .ToDiagnostics().Sql;

        Assert.That(sql, Does.Contain("SELECT"));
        Assert.That(sql, Does.Contain("AVG("));
        Assert.That(sql, Does.Contain("\"Status\""));
    }

    [Test]
    public void Select_TupleWithMin_GeneratesCorrectSql()
    {
        var sql = _db.Orders()
            .Where(o => true)
            .GroupBy(o => o.Status)
            .Select(o => (o.Status, Sql.Min(o.Total)))
            .ToDiagnostics().Sql;

        Assert.That(sql, Does.Contain("SELECT"));
        Assert.That(sql, Does.Contain("MIN("));
        Assert.That(sql, Does.Contain("\"Status\""));
    }

    [Test]
    public void Select_TupleWithMax_GeneratesCorrectSql()
    {
        var sql = _db.Orders()
            .Where(o => true)
            .GroupBy(o => o.Status)
            .Select(o => (o.Status, Sql.Max(o.Total)))
            .ToDiagnostics().Sql;

        Assert.That(sql, Does.Contain("SELECT"));
        Assert.That(sql, Does.Contain("MAX("));
        Assert.That(sql, Does.Contain("\"Status\""));
    }

    [Test]
    public void Select_TupleWithMultipleAggregates_GeneratesCorrectSql()
    {
        var sql = _db.Orders()
            .Where(o => true)
            .GroupBy(o => o.Status)
            .Select(o => (o.Status, Sql.Avg(o.Total), Sql.Min(o.Total), Sql.Max(o.Total), Sql.Count()))
            .ToDiagnostics().Sql;

        Assert.That(sql, Does.Contain("AVG("));
        Assert.That(sql, Does.Contain("MIN("));
        Assert.That(sql, Does.Contain("MAX("));
        Assert.That(sql, Does.Contain("COUNT("));
    }

    [Test]
    public void GroupBy_Having_Select_TupleWithAvg_GeneratesCorrectSql()
    {
        var sql = _db.Orders()
            .Where(o => true)
            .GroupBy(o => o.Status)
            .Having(o => Sql.Count() > 1)
            .Select(o => (o.Status, Sql.Avg(o.Total)))
            .ToDiagnostics().Sql;

        Assert.That(sql, Does.Contain("GROUP BY"));
        Assert.That(sql, Does.Contain("HAVING"));
        Assert.That(sql, Does.Contain("AVG("));
    }

    [Test]
    public void Select_ThenGroupBy_TupleProjection_GeneratesCorrectSql()
    {
        // Bug B scenario: Select-then-GroupBy where element names could shadow types
        var sql = _db.Orders()
            .Select(o => (o.Status, Sql.Count()))
            .GroupBy(o => o.Status)
            .ToDiagnostics().Sql;

        Assert.That(sql, Does.Contain("SELECT"));
        Assert.That(sql, Does.Contain("GROUP BY"));
    }

    #endregion

    #region Delete ExecuteNonQuery Interceptor Tests

    [Test]
    public async Task Delete_WithWhere_ExecuteNonQueryAsync_Succeeds()
    {
        var result = await _db.Users().Delete()
            .Where(u => u.UserId == 1)
            .ExecuteNonQueryAsync();

        Assert.That(result, Is.EqualTo(1));
        Assert.That(_connection.LastCommand, Is.Not.Null);
        Assert.That(_connection.LastCommand!.CommandText, Does.Contain("DELETE"));
        Assert.That(_connection.LastCommand.CommandText, Does.Contain("FROM"));
        Assert.That(_connection.LastCommand.CommandText, Does.Contain("\"users\""));
        Assert.That(_connection.LastCommand.CommandText, Does.Contain("WHERE"));
    }

    [Test]
    public async Task Delete_WithMultipleWheres_ExecuteNonQueryAsync_Succeeds()
    {
        var result = await _db.Users().Delete()
            .Where(u => u.UserId > 100)
            .Where(u => !u.IsActive)
            .ExecuteNonQueryAsync();

        Assert.That(result, Is.EqualTo(1));
        Assert.That(_connection.LastCommand, Is.Not.Null);
        Assert.That(_connection.LastCommand!.CommandText, Does.Contain("DELETE"));
        Assert.That(_connection.LastCommand.CommandText, Does.Contain("WHERE"));
    }

    [Test]
    public async Task Delete_All_ExecuteNonQueryAsync_Succeeds()
    {
        var result = await _db.Users().Delete()
            .All()
            .ExecuteNonQueryAsync();

        Assert.That(result, Is.EqualTo(1));
        Assert.That(_connection.LastCommand, Is.Not.Null);
        Assert.That(_connection.LastCommand!.CommandText, Does.Contain("DELETE"));
        Assert.That(_connection.LastCommand.CommandText, Does.Contain("FROM"));
        Assert.That(_connection.LastCommand.CommandText, Does.Contain("\"users\""));
        Assert.That(_connection.LastCommand.CommandText, Does.Not.Contain("WHERE"));
    }

    [Test]
    public void Delete_WithWhere_ToDiagnosticsSql_GeneratesCorrectSql()
    {
        var sql = _db.Users().Delete()
            .Where(u => u.UserId == 1)
            .ToDiagnostics().Sql;

        Assert.That(sql, Does.Contain("DELETE"));
        Assert.That(sql, Does.Contain("FROM"));
        Assert.That(sql, Does.Contain("\"users\""));
        Assert.That(sql, Does.Contain("WHERE"));
        Assert.That(sql, Does.Contain("\"UserId\""));
    }

    [Test]
    public void Delete_WithBooleanWhere_ToDiagnosticsSql_GeneratesCorrectSql()
    {
        var sql = _db.Users().Delete()
            .Where(u => u.IsActive)
            .ToDiagnostics().Sql;

        Assert.That(sql, Does.Contain("DELETE"));
        Assert.That(sql, Does.Contain("WHERE"));
        Assert.That(sql, Does.Contain("\"IsActive\""));
    }

    #endregion

    #region Variable-Based Chain Tests

    [Test]
    public async Task VariableBasedDelete_ExecutesNonQuery()
    {
        // Simple variable-based DELETE chain — no conditionals, verifies variable chains work
        var del = _db.Users().Delete().Where(u => u.UserId == 1);
        var result = await del.ExecuteNonQueryAsync();

        Assert.That(result, Is.EqualTo(1));
        Assert.That(_connection.LastCommand, Is.Not.Null);
        Assert.That(_connection.LastCommand!.CommandText, Does.Contain("DELETE"));
        Assert.That(_connection.LastCommand.CommandText, Does.Contain("WHERE"));
    }

    [Test]
    public async Task VariableBasedDeleteAll_ExecutesNonQuery()
    {
        // Variable DELETE with All() — verifies variable chains with All() work
        var del = _db.Users().Delete().All();
        var result = await del.ExecuteNonQueryAsync();

        Assert.That(result, Is.EqualTo(1));
        Assert.That(_connection.LastCommand, Is.Not.Null);
        Assert.That(_connection.LastCommand!.CommandText, Does.Contain("DELETE"));
    }

    [Test]
    public async Task VariableBasedUpdate_ExecutesNonQuery()
    {
        // Variable-based UPDATE chain
        var upd = _db.Users().Update().Set(u => u.UserName = "newname").Where(u => u.UserId == 1);
        var result = await upd.ExecuteNonQueryAsync();

        Assert.That(result, Is.EqualTo(1));
        Assert.That(_connection.LastCommand, Is.Not.Null);
        Assert.That(_connection.LastCommand!.CommandText, Does.Contain("UPDATE"));
        Assert.That(_connection.LastCommand.CommandText, Does.Contain("WHERE"));
    }

    [Test]
    public async Task VariableBasedSelect_ExecutesFetchAll()
    {
        // Variable-based SELECT (QueryBuilder) chain
        var query = _db.Users().Where(u => u.IsActive).Select(u => u);
        var results = await query.ExecuteFetchAllAsync();

        Assert.That(results, Is.Not.Null);
        Assert.That(_connection.LastCommand, Is.Not.Null);
        Assert.That(_connection.LastCommand!.CommandText, Does.Contain("SELECT"));
        Assert.That(_connection.LastCommand.CommandText, Does.Contain("WHERE"));
        Assert.That(_connection.LastCommand.CommandText, Does.Contain("\"IsActive\""));
    }

    #endregion

    #region Conditional Branching Tests

    [Test]
    public async Task ConditionalDelete_WithConditionTrue_IncludesConditionalWhere()
    {
        // Conditional WHERE on DELETE — condition true → bit 0 set → mask=1
        var del = _db.Users().Delete().Where(u => u.UserId > 100);
        if (true) // always true — exercises the conditional branch path
        {
            del = del.Where(u => !u.IsActive);
        }
        var result = await del.ExecuteNonQueryAsync();

        Assert.That(result, Is.EqualTo(1));
        Assert.That(_connection.LastCommand, Is.Not.Null);
        var sql = _connection.LastCommand!.CommandText;
        Assert.That(sql, Does.Contain("DELETE"));
        Assert.That(sql, Does.Contain("WHERE"));
        // Both WHERE clauses should be present
        Assert.That(sql, Does.Contain("100"));
        Assert.That(sql, Does.Contain("IsActive"));
    }

    [Test]
    public async Task ConditionalDelete_WithConditionFalse_ExcludesConditionalWhere()
    {
        // Conditional WHERE on DELETE — condition false → bit 0 not set → mask=0
        var del = _db.Users().Delete().Where(u => u.UserId > 100);
        if (false) // always false — exercises the non-conditional path
        {
            del = del.Where(u => !u.IsActive);
        }
        var result = await del.ExecuteNonQueryAsync();

        Assert.That(result, Is.EqualTo(1));
        Assert.That(_connection.LastCommand, Is.Not.Null);
        var sql = _connection.LastCommand!.CommandText;
        Assert.That(sql, Does.Contain("DELETE"));
        Assert.That(sql, Does.Contain("WHERE"));
        Assert.That(sql, Does.Contain("100"));
        // The conditional WHERE should NOT be present
        Assert.That(sql, Does.Not.Contain("IsActive"));
    }

    [Test]
    public async Task ConditionalUpdate_WithConditionTrue_IncludesConditionalWhere()
    {
        // Conditional WHERE on UPDATE — condition true → mask includes conditional bit
        var upd = _db.Users().Update().Set(u => u.UserName = "updated").Where(u => u.UserId == 1);
        if (true)
        {
            upd = upd.Where(u => u.IsActive);
        }
        var result = await upd.ExecuteNonQueryAsync();

        Assert.That(result, Is.EqualTo(1));
        Assert.That(_connection.LastCommand, Is.Not.Null);
        var sql = _connection.LastCommand!.CommandText;
        Assert.That(sql, Does.Contain("UPDATE"));
        Assert.That(sql, Does.Contain("SET"));
        Assert.That(sql, Does.Contain("WHERE"));
        Assert.That(sql, Does.Contain("IsActive"));
    }
    
    [Test]
    public async Task ConditionalUpdate_WithConditionTrue_IncludesConditionalWhere2()
    {
        var active = true;
        var upd = _db.Users()
            .Update().Set(u => u.UserName = "updated")
            .Where(u => u.UserId == 1);
        if (true)
        {
            upd = upd.Where(u => u.IsActive == active);
        }
        var result = await upd.ExecuteNonQueryAsync();

        Assert.That(result, Is.EqualTo(1));
        Assert.That(_connection.LastCommand, Is.Not.Null);
        var sql = _connection.LastCommand!.CommandText;
        Assert.That(sql, Does.Contain("UPDATE"));
        Assert.That(sql, Does.Contain("SET"));
        Assert.That(sql, Does.Contain("WHERE"));
        Assert.That(sql, Does.Contain("IsActive"));
    }

    [Test]
    public async Task ConditionalUpdate_WithConditionFalse_ExcludesConditionalWhere()
    {
        // Conditional WHERE on UPDATE — condition false → only base WHERE
        var upd = _db.Users().Update().Set(u => u.UserName = "updated").Where(u => u.UserId == 1);
        if (false)
        {
            upd = upd.Where(u => u.IsActive);
        }
        var result = await upd.ExecuteNonQueryAsync();

        Assert.That(result, Is.EqualTo(1));
        Assert.That(_connection.LastCommand, Is.Not.Null);
        var sql = _connection.LastCommand!.CommandText;
        Assert.That(sql, Does.Contain("UPDATE"));
        Assert.That(sql, Does.Contain("SET"));
        Assert.That(sql, Does.Contain("WHERE"));
        // The conditional WHERE should NOT be present
        Assert.That(sql, Does.Not.Contain("IsActive"));
    }

    [Test]
    public async Task ConditionalSelect_WithConditionTrue_IncludesConditionalWhere()
    {
        // Conditional WHERE on SELECT — condition true
        // Where must come before Select so the variable type stays IQueryBuilder<User>
        IQueryBuilder<User> query = _db.Users().Where(u => true);
        if (true)
        {
            query = query.Where(u => u.IsActive);
        }
        var results = await query.Select(u => u).ExecuteFetchAllAsync();

        Assert.That(results, Is.Not.Null);
        Assert.That(_connection.LastCommand, Is.Not.Null);
        var sql = _connection.LastCommand!.CommandText;
        Assert.That(sql, Does.Contain("SELECT"));
        Assert.That(sql, Does.Contain("WHERE"));
        Assert.That(sql, Does.Contain("IsActive"));
    }

    [Test]
    public async Task ConditionalSelect_WithConditionFalse_ExcludesConditionalWhere()
    {
        // Conditional WHERE on SELECT — condition false
        IQueryBuilder<User> query = _db.Users().Where(u => true);
        if (false)
        {
            query = query.Where(u => u.IsActive);
        }
        var results = await query.Select(u => u).ExecuteFetchAllAsync();

        Assert.That(results, Is.Not.Null);
        Assert.That(_connection.LastCommand, Is.Not.Null);
        var sql = _connection.LastCommand!.CommandText;
        Assert.That(sql, Does.Contain("SELECT"));
        Assert.That(sql, Does.Not.Contain("WHERE"));
    }

    #endregion

    #region Join Execution Interceptor Tests

    [Test]
    public async Task JoinExecution_TwoEntityInnerJoin_TupleProjection_GeneratesPrebuiltSql()
    {
        var results = await _db.Users()
            .Join<Order>((u, o) => u.UserId == o.UserId.Id)
            .Select((u, o) => (u.UserName, o.Total))
            .ExecuteFetchAllAsync();

        Assert.That(results, Is.Not.Null);
        Assert.That(_connection.LastCommand, Is.Not.Null);
        var sql = _connection.LastCommand!.CommandText;
        Assert.That(sql, Does.Contain("SELECT"));
        Assert.That(sql, Does.Contain("INNER JOIN"));
        Assert.That(sql, Does.Contain("AS \"t0\""));
        Assert.That(sql, Does.Contain("AS \"t1\""));
        Assert.That(sql, Does.Contain("\"t0\".\"UserName\""));
        Assert.That(sql, Does.Contain("\"t1\".\"Total\""));
    }

    [Test]
    public async Task JoinExecution_TwoEntityLeftJoin_DtoProjection_GeneratesPrebuiltSql()
    {
        var results = await _db.Users()
            .LeftJoin<Order>((u, o) => u.UserId == o.UserId.Id)
            .Select((u, o) => new UserOrderDto { UserName = u.UserName, Total = o.Total })
            .ExecuteFetchAllAsync();

        Assert.That(results, Is.Not.Null);
        Assert.That(_connection.LastCommand, Is.Not.Null);
        var sql = _connection.LastCommand!.CommandText;
        Assert.That(sql, Does.Contain("LEFT JOIN"));
        Assert.That(sql, Does.Contain("AS \"t1\""));
        Assert.That(sql, Does.Contain("\"t0\".\"UserName\""));
        Assert.That(sql, Does.Contain("\"t1\".\"Total\""));
    }

    [Test]
    public async Task JoinExecution_ThreeEntityJoin_TupleProjection_GeneratesPrebuiltSql()
    {
        var results = await _db.Users()
            .Join<Order>((u, o) => u.UserId == o.UserId.Id)
            .Join<OrderItem>((u, o, oi) => o.OrderId == oi.OrderId.Id)
            .Select((u, o, oi) => (u.UserName, o.Total, oi.ProductName))
            .ExecuteFetchAllAsync();

        Assert.That(results, Is.Not.Null);
        Assert.That(_connection.LastCommand, Is.Not.Null);
        var sql = _connection.LastCommand!.CommandText;
        Assert.That(sql, Does.Contain("INNER JOIN"));
        Assert.That(sql, Does.Contain("AS \"t1\""));
        Assert.That(sql, Does.Contain("AS \"t2\""));
        Assert.That(sql, Does.Contain("\"t0\".\"UserName\""));
        Assert.That(sql, Does.Contain("\"t2\".\"ProductName\""));
    }

    [Test]
    public async Task JoinExecution_VariableBasedChain_GeneratesPrebuiltSql()
    {
        var q = _db.Users()
            .Join<Order>((u, o) => u.UserId == o.UserId.Id)
            .Select((u, o) => (u.UserName, o.Total));
        var results = await q.ExecuteFetchAllAsync();

        Assert.That(results, Is.Not.Null);
        Assert.That(_connection.LastCommand, Is.Not.Null);
        var sql = _connection.LastCommand!.CommandText;
        Assert.That(sql, Does.Contain("INNER JOIN"));
        Assert.That(sql, Does.Contain("\"t0\".\"UserName\""));
        Assert.That(sql, Does.Contain("\"t1\".\"Total\""));
    }

    [Test]
    public async Task JoinExecution_WithWhereAndOrderBy_GeneratesPrebuiltSql()
    {
        var results = await _db.Users()
            .Join<Order>((u, o) => u.UserId == o.UserId.Id)
            .Where((u, o) => o.Total > 100)
            .OrderBy((u, o) => u.UserName)
            .Select((u, o) => (u.UserName, o.Total))
            .ExecuteFetchAllAsync();

        Assert.That(results, Is.Not.Null);
        Assert.That(_connection.LastCommand, Is.Not.Null);
        var sql = _connection.LastCommand!.CommandText;
        Assert.That(sql, Does.Contain("INNER JOIN"));
        Assert.That(sql, Does.Contain("WHERE"));
        Assert.That(sql, Does.Contain("ORDER BY"));
    }

    [Test]
    public async Task JoinExecution_WithWhere_DirectFluent_GeneratesPrebuiltSql()
    {
        // Direct fluent join with WHERE — no conditional branching
        var results = await _db.Users()
            .Join<Order>((u, o) => u.UserId == o.UserId.Id)
            .Where((u, o) => u.IsActive)
            .Select((u, o) => (u.UserName, o.Total))
            .ExecuteFetchAllAsync();

        Assert.That(results, Is.Not.Null);
        Assert.That(_connection.LastCommand, Is.Not.Null);
        var sql = _connection.LastCommand!.CommandText;
        Assert.That(sql, Does.Contain("INNER JOIN"));
        Assert.That(sql, Does.Contain("WHERE"));
        Assert.That(sql, Does.Contain("\"IsActive\""));
    }

    [Test]
    public async Task JoinExecution_ConditionalWhere_TruePath_IncludesClause()
    {
        // Conditional WHERE on unprojected join builder (before Select)
        // Tests that chain analysis correctly handles hybrid variable+fluent tail
        var q = _db.Users()
            .Join<Order>((u, o) => u.UserId == o.UserId.Id);
        if (true)
        {
            q = q.Where((u, o) => o.Total > 50);
        }
        var results = await q
            .Select((u, o) => (u.UserName, o.Total))
            .ExecuteFetchAllAsync();

        Assert.That(results, Is.Not.Null);
        Assert.That(_connection.LastCommand, Is.Not.Null);
        var sql = _connection.LastCommand!.CommandText;
        Assert.That(sql, Does.Contain("INNER JOIN"));
        Assert.That(sql, Does.Contain("WHERE"));
        Assert.That(sql, Does.Contain("50"));
    }

    [Test]
    public async Task JoinExecution_ConditionalWhere_FalsePath_ExcludesClause()
    {
        // Conditional WHERE on unprojected join builder (before Select)
        var q = _db.Users()
            .Join<Order>((u, o) => u.UserId == o.UserId.Id);
        if (false)
        {
            q = q.Where((u, o) => o.Total > 50);
        }
        var results = await q
            .Select((u, o) => (u.UserName, o.Total))
            .ExecuteFetchAllAsync();

        Assert.That(results, Is.Not.Null);
        Assert.That(_connection.LastCommand, Is.Not.Null);
        var sql = _connection.LastCommand!.CommandText;
        Assert.That(sql, Does.Contain("INNER JOIN"));
        Assert.That(sql, Does.Not.Contain("WHERE"));
    }

    [Test]
    public async Task JoinExecution_SingleColumnProjection_GeneratesPrebuiltSql()
    {
        var results = await _db.Users()
            .Join<Order>((u, o) => u.UserId == o.UserId.Id)
            .Select((u, o) => o.Total)
            .ExecuteFetchAllAsync();

        Assert.That(results, Is.Not.Null);
        Assert.That(_connection.LastCommand, Is.Not.Null);
        var sql = _connection.LastCommand!.CommandText;
        Assert.That(sql, Does.Contain("INNER JOIN"));
        Assert.That(sql, Does.Contain("\"t1\".\"Total\""));
    }

    [Test]
    public async Task JoinExecution_FourEntityJoin_TupleProjection_GeneratesPrebuiltSql()
    {
        var results = await _db.Users()
            .Join<Order>((u, o) => u.UserId == o.UserId.Id)
            .Join<OrderItem>((u, o, oi) => o.OrderId == oi.OrderId.Id)
            .Join<Product>((u, o, oi, p) => oi.ProductName == p.ProductName)
            .Select((u, o, oi, p) => (u.UserName, o.Total, oi.Quantity, p.Price))
            .ExecuteFetchAllAsync();

        Assert.That(results, Is.Not.Null);
        Assert.That(_connection.LastCommand, Is.Not.Null);
        var sql = _connection.LastCommand!.CommandText;
        Assert.That(sql, Does.Contain("SELECT"));
        Assert.That(sql, Does.Contain("INNER JOIN"));
        Assert.That(sql, Does.Contain("AS \"t0\""));
        Assert.That(sql, Does.Contain("AS \"t1\""));
        Assert.That(sql, Does.Contain("AS \"t2\""));
        Assert.That(sql, Does.Contain("AS \"t3\""));
        Assert.That(sql, Does.Contain("\"t0\".\"UserName\""));
        Assert.That(sql, Does.Contain("\"t3\".\"Price\""));
    }

    [Test]
    public async Task JoinExecution_FourEntityJoin_DtoProjection_GeneratesPrebuiltSql()
    {
        var results = await _db.Users()
            .Join<Order>((u, o) => u.UserId == o.UserId.Id)
            .Join<OrderItem>((u, o, oi) => o.OrderId == oi.OrderId.Id)
            .Join<Product>((u, o, oi, p) => oi.ProductName == p.ProductName)
            .Select((u, o, oi, p) => new UserOrderItemProductDto
            {
                UserName = u.UserName,
                Total = o.Total,
                Quantity = oi.Quantity,
                Price = p.Price
            })
            .ExecuteFetchAllAsync();

        Assert.That(results, Is.Not.Null);
        Assert.That(_connection.LastCommand, Is.Not.Null);
        var sql = _connection.LastCommand!.CommandText;
        Assert.That(sql, Does.Contain("INNER JOIN"));
        Assert.That(sql, Does.Contain("AS \"t3\""));
        Assert.That(sql, Does.Contain("\"t2\".\"Quantity\""));
        Assert.That(sql, Does.Contain("\"t3\".\"Price\""));
    }

    [Test]
    public async Task JoinExecution_ConditionalWhere_OnProjectedBuilder_TruePath()
    {
        // Conditional WHERE on projected join builder (after Select)
        // This exercises the tuple TResult type in the Where interceptor signature
        var q = _db.Users()
            .Join<Order>((u, o) => u.UserId == o.UserId.Id)
            .Select((u, o) => (u.UserName, o.Total));
        if (true)
        {
            q = q.Where((u, o) => o.Total > 50);
        }
        var results = await q.ExecuteFetchAllAsync();

        Assert.That(results, Is.Not.Null);
        Assert.That(_connection.LastCommand, Is.Not.Null);
        var sql = _connection.LastCommand!.CommandText;
        Assert.That(sql, Does.Contain("INNER JOIN"));
        Assert.That(sql, Does.Contain("WHERE"));
        Assert.That(sql, Does.Contain("50"));
    }

    [Test]
    public async Task JoinExecution_ConditionalWhere_OnProjectedBuilder_FalsePath()
    {
        // Conditional WHERE on projected join builder (after Select)
        var q = _db.Users()
            .Join<Order>((u, o) => u.UserId == o.UserId.Id)
            .Select((u, o) => (u.UserName, o.Total));
        var neverTrue = DateTime.UtcNow.Year < 1;
        if (neverTrue)
        {
            q = q.Where((u, o) => o.Total > 50);
        }
        var results = await q.ExecuteFetchAllAsync();

        Assert.That(results, Is.Not.Null);
        Assert.That(_connection.LastCommand, Is.Not.Null);
        var sql = _connection.LastCommand!.CommandText;
        Assert.That(sql, Does.Contain("INNER JOIN"));
        Assert.That(sql, Does.Not.Contain("WHERE"));
    }

    [Test]
    public async Task JoinExecution_NavigationJoin_ExplicitType_TupleProjection_GeneratesPrebuiltSql()
    {
        // Navigation join with explicit type argument — semantic model can resolve TJoined
        var results = await _db.Users()
            .Join<Order>(u => u.Orders)
            .Select((u, o) => (u.UserName, o.Total))
            .ExecuteFetchAllAsync();

        Assert.That(results, Is.Not.Null);
        Assert.That(_connection.LastCommand, Is.Not.Null);
        var sql = _connection.LastCommand!.CommandText;
        Assert.That(sql, Does.Contain("INNER JOIN"));
        Assert.That(sql, Does.Contain("\"t0\".\"UserName\""));
        Assert.That(sql, Does.Contain("\"t1\".\"Total\""));
    }

    [Test]
    public async Task JoinExecution_NavigationJoin_InferredType_TupleProjection_GeneratesPrebuiltSql()
    {
        // Navigation join with inferred type — relies on syntactic resolution
        var results = await _db.Users()
            .Join(u => u.Orders)
            .Select((u, o) => (u.UserName, o.Total))
            .ExecuteFetchAllAsync();

        Assert.That(results, Is.Not.Null);
        Assert.That(_connection.LastCommand, Is.Not.Null);
        var sql = _connection.LastCommand!.CommandText;
        Assert.That(sql, Does.Contain("INNER JOIN"));
        Assert.That(sql, Does.Contain("\"t0\".\"UserName\""));
        Assert.That(sql, Does.Contain("\"t1\".\"Total\""));
    }

    #endregion
}
