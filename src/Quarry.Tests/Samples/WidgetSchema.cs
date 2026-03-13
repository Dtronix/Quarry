using Quarry;

namespace Quarry.Tests.Samples;

/// <summary>
/// Schema with a ClientGenerated GUID key for testing that no RETURNING/OUTPUT clause is emitted.
/// </summary>
public class WidgetSchema : Schema
{
    public static string Table => "widgets";

    public Key<Guid> WidgetId => ClientGenerated();
    public Col<string> WidgetName => Length(100);
    public Col<string> Secret => Length(200).Sensitive();
}
