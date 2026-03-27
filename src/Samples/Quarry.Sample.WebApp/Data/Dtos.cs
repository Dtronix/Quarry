namespace Quarry.Sample.WebApp.Data;

public class UserListItem
{
    public int UserId { get; set; }
    public string UserName { get; set; } = "";
    public string Email { get; set; } = "";
    public UserRole Role { get; set; }
    public bool IsActive { get; set; }
    public DateTime? LastLoginAt { get; set; }
}

public class AuditLogEntry
{
    public int AuditLogId { get; set; }
    public string UserName { get; set; } = "";
    public AuditAction Action { get; set; }
    public string? Detail { get; set; }
    public string? IpAddress { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class AuditEntry
{
    public AuditAction Action { get; set; }
    public string? Detail { get; set; }
    public string? IpAddress { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class UserNameEmail
{
    public string UserName { get; set; } = "";
    public string Email { get; set; } = "";
}

public class RoleCount
{
    public UserRole Role { get; set; }
    public int Count { get; set; }
}

public class DailyLoginCount
{
    public string Day { get; set; } = "";
    public int Count { get; set; }
}

public class ProfileDto
{
    public string UserName { get; set; } = "";
    public string Email { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}
