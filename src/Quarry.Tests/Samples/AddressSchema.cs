using Quarry;

namespace Quarry.Tests.Samples;

public class AddressSchema : Schema
{
    public static string Table => "addresses";
    public Key<int> AddressId => Identity();
    public Col<string> City => Length(100);
    public Col<string> Street => Length(200);
    public Col<string?> ZipCode { get; }
}
