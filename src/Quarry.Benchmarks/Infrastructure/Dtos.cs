namespace Quarry.Benchmarks.Infrastructure;

public class UserSummaryDto
{
    public int UserId { get; set; }
    public string UserName { get; set; } = "";
    public bool IsActive { get; set; }
}

public class UserWithEmailDto
{
    public int UserId { get; set; }
    public string UserName { get; set; } = "";
    public string? Email { get; set; }
}

public class OrderSummaryDto
{
    public int OrderId { get; set; }
    public decimal Total { get; set; }
    public string Status { get; set; } = "";
}

public class UserOrderDto
{
    public string UserName { get; set; } = "";
    public decimal Total { get; set; }
}

public class UserOrderItemDto
{
    public string UserName { get; set; } = "";
    public decimal Total { get; set; }
    public string ProductName { get; set; } = "";
}
