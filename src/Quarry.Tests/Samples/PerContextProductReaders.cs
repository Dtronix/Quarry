using System.Data.Common;
using Quarry;

namespace Quarry.Tests.Samples.Pg
{
    public partial class Product
    {
        public string DisplayLabel { get; set; } = "";
    }

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
}

namespace Quarry.Tests.Samples.My
{
    public partial class Product
    {
        public string DisplayLabel { get; set; } = "";
    }

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
}

namespace Quarry.Tests.Samples.Ss
{
    public partial class Product
    {
        public string DisplayLabel { get; set; } = "";
    }

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
}
