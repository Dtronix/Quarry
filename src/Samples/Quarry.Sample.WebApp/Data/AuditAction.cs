namespace Quarry.Sample.WebApp.Data;

public enum AuditAction
{
    Login = 0,
    Logout = 1,
    PasswordChange = 2,
    RoleChange = 3,
    AccountCreated = 4,
    AccountDeleted = 5,
    AccountDeactivated = 6,
    AccountReactivated = 7
}
