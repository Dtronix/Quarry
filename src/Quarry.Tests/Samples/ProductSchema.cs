using System.Data.Common;
using Quarry;

namespace Quarry.Tests.Samples;

/// <summary>
/// Partial extension of the generated Product entity.
/// Adds DisplayLabel — a non-column property set by the custom reader to prove it ran.
/// </summary>
public partial class Product
{
    /// <summary>
    /// Computed display label set by the custom EntityReader.
    /// Not a schema column — the generated reader would never set this.
    /// </summary>
    public string DisplayLabel { get; set; } = "";
}

/// <summary>
/// Custom entity reader that materializes Product from DbDataReader
/// and sets DisplayLabel to prove it was used instead of the generated reader.
/// </summary>
public class ProductReader : EntityReader<Product>
{
    public override Product Read(DbDataReader reader) => new Product
    {
        ProductId = reader.GetInt32(reader.GetOrdinal("ProductId")),
        ProductName = reader.GetString(reader.GetOrdinal("ProductName")),
        Price = reader.GetDecimal(reader.GetOrdinal("Price")),
        Description = reader.IsDBNull(reader.GetOrdinal("Description"))
            ? null
            : reader.GetString(reader.GetOrdinal("Description")),
        DisplayLabel = $"[{reader.GetString(reader.GetOrdinal("ProductName"))}] ${reader.GetDecimal(reader.GetOrdinal("Price")):F2}",
    };
}

/// <summary>
/// Schema definition for the products table.
/// Uses [EntityReader] to delegate materialization to ProductReader.
/// Includes a Computed column (DiscountedPrice) for testing computed column exclusion.
/// </summary>
[EntityReader(typeof(ProductReader))]
public class ProductSchema : Schema
{
    public static string Table => "products";

    public Key<int> ProductId => Identity();
    public Col<string> ProductName => Length(200);
    public Col<decimal> Price => Precision(18, 2);
    public Col<string?> Description { get; }
    public Col<decimal> DiscountedPrice => Computed<decimal>();
}
