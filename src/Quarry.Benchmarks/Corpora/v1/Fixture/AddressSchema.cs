using Quarry;

namespace BenchHarness;

public class AddressSchema : Schema
{
    public static string Table => "addresses";

    public Key<int> AddressId => Identity();
    public Col<string> City => Length(100);
    public Col<string> Street => Length(200);
    public Col<string?> ZipCode { get; }
}
