using Quarry;

namespace Quarry.Tests.Samples;

public class UserAddressSchema : Schema
{
    public static string Table => "user_addresses";
    public Key<int> UserAddressId => Identity();
    public Ref<UserSchema, int> UserId => ForeignKey<UserSchema, int>();
    public Ref<AddressSchema, int> AddressId => ForeignKey<AddressSchema, int>();
    public One<AddressSchema> Address { get; }
}
