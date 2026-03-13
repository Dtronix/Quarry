namespace Quarry.Tests.Samples;

/// <summary>
/// DTO for projecting user summary data.
/// Property names must match entity property names for generator to work correctly.
/// </summary>
public class UserSummaryDto
{
    public int UserId { get; set; }
    public string UserName { get; set; } = null!;
    public bool IsActive { get; set; }
}

/// <summary>
/// DTO for projecting user with email.
/// </summary>
public class UserWithEmailDto
{
    public int UserId { get; set; }
    public string UserName { get; set; } = null!;
    public string? Email { get; set; }
}

/// <summary>
/// DTO for projecting order summary data.
/// </summary>
public class OrderSummaryDto
{
    public int OrderId { get; set; }
    public decimal Total { get; set; }
    public string Status { get; set; } = null!;
}

/// <summary>
/// DTO for projecting data from User + Order joins.
/// </summary>
public class UserOrderDto
{
    public string UserName { get; set; } = null!;
    public decimal Total { get; set; }
}

/// <summary>
/// DTO for projecting data from User + Order + OrderItem joins.
/// </summary>
public class UserOrderItemDto
{
    public string UserName { get; set; } = null!;
    public decimal Total { get; set; }
    public string ProductName { get; set; } = null!;
}

/// <summary>
/// DTO for projecting data from User + Order + OrderItem + Product joins.
/// </summary>
public class UserOrderItemProductDto
{
    public string UserName { get; set; } = null!;
    public decimal Total { get; set; }
    public int Quantity { get; set; }
    public decimal Price { get; set; }
}
