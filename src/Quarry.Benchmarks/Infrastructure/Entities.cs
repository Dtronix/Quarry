namespace Quarry.Benchmarks.Infrastructure;

public class EfUser
{
    public int UserId { get; set; }
    public string UserName { get; set; } = "";
    public string? Email { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLogin { get; set; }

    public List<EfOrder> Orders { get; set; } = [];
}

public class EfOrder
{
    public int OrderId { get; set; }
    public int UserId { get; set; }
    public decimal Total { get; set; }
    public string Status { get; set; } = "";
    public DateTime OrderDate { get; set; }
    public string? Notes { get; set; }

    public EfUser User { get; set; } = null!;
    public List<EfOrderItem> Items { get; set; } = [];
}

public class EfOrderItem
{
    public int OrderItemId { get; set; }
    public int OrderId { get; set; }
    public string ProductName { get; set; } = "";
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineTotal { get; set; }

    public EfOrder Order { get; set; } = null!;
}
