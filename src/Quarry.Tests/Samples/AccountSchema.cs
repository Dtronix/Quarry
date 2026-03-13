using Quarry;

namespace Quarry.Tests.Samples;

/// <summary>
/// Schema definition for the accounts table. Uses Mapped&lt;&gt; for the Money type.
/// </summary>
public class AccountSchema : Schema
{
    public static string Table => "accounts";

    public Key<int> AccountId => Identity();
    public Ref<UserSchema, int> UserId => ForeignKey<UserSchema, int>();
    public Col<string> AccountName => Length(100);
    public Col<Money> Balance => Mapped<Money, MoneyMapping>();
    public Col<Money> CreditLimit => Mapped<Money, MoneyMapping>().MapTo("credit_limit");
    public Col<bool> IsActive => Default(true);
}
