namespace Quarry;

/// <summary>
/// Specifies the type of database index.
/// </summary>
public enum IndexType
{
    /// <summary>B-Tree index (universal default).</summary>
    BTree,

    /// <summary>Hash index (PostgreSQL; limited MySQL/SQL Server support).</summary>
    Hash,

    /// <summary>GIN index (PostgreSQL only).</summary>
    Gin,

    /// <summary>GiST index (PostgreSQL only).</summary>
    Gist,

    /// <summary>SP-GiST index (PostgreSQL only).</summary>
    SpGist,

    /// <summary>BRIN index (PostgreSQL only).</summary>
    Brin,

    /// <summary>Clustered index (SQL Server, one per table).</summary>
    Clustered,

    /// <summary>Nonclustered index (SQL Server default).</summary>
    Nonclustered
}
