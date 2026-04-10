namespace Quarry.Benchmarks.Infrastructure;

/// <summary>
/// Plain POCO for Raw ADO.NET benchmarks. No navigation properties or ORM attributes —
/// represents the minimal type a developer would write for manual reader mapping.
/// </summary>
public class RawUser
{
    public int UserId { get; set; }
    public string UserName { get; set; } = "";
    public string? Email { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLogin { get; set; }
}

/// <summary>
/// Plain POCO for Dapper benchmarks. Dapper maps columns to properties by name convention;
/// no attributes or base classes needed.
/// </summary>
public class DapperUser
{
    public int UserId { get; set; }
    public string UserName { get; set; } = "";
    public string? Email { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLogin { get; set; }
}

/// <summary>
/// Plain POCO for SqlKata benchmarks. SqlKata builds SQL only; result mapping uses
/// manual reader code identical to Raw, so the type is a plain POCO.
/// </summary>
public class SqlKataUser
{
    public int UserId { get; set; }
    public string UserName { get; set; } = "";
    public string? Email { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLogin { get; set; }
}

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

public class OrderRowNumberDto
{
    public int OrderId { get; set; }
    public long RowNum { get; set; }
}

public class OrderRunningSumDto
{
    public int OrderId { get; set; }
    public decimal Total { get; set; }
    public decimal RunningSum { get; set; }
}

public class OrderRankDto
{
    public int OrderId { get; set; }
    public long Rank { get; set; }
}

public class OrderLagDto
{
    public int OrderId { get; set; }
    public decimal Total { get; set; }
    public decimal? PrevTotal { get; set; }
}

public class OrderIdTotalDto
{
    public int OrderId { get; set; }
    public decimal Total { get; set; }
}

public class UserIdNameDto
{
    public int UserId { get; set; }
    public string UserName { get; set; } = "";
}