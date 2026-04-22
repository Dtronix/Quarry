using Quarry;

namespace DocsValidation;

// Schemas cover the queries used in:
//   docs/index.md, docs/articles/getting-started.md
//   docs/articles/querying.md (HasManyThrough, Many<T> aggregates, window functions, Sql.Raw projection)
//   docs/articles/scaffolding.md
//   llm.md, src/Quarry.Generator/README.md, docs/articles/releases/release-notes-v0.3.0.md

public class UserSchema : Schema
{
    public static string Table => "users";

    public Key<int> UserId => Identity();
    public Col<string> UserName => Length(100);
    public Col<string?> Email { get; }
    public Col<bool> IsActive => Default(true);
    public Col<DateTime> CreatedAt => Default(() => DateTime.UtcNow);

    public Many<OrderSchema> Orders => HasMany<OrderSchema>(o => o.UserId);
}

public class OrderSchema : Schema
{
    public static string Table => "orders";

    public Key<int> OrderId => Identity();
    public Ref<UserSchema, int> UserId => ForeignKey<UserSchema, int>();
    public Col<decimal> Total => Precision(18, 2);
    public Col<string> Status => Length(50);
    public Col<DateTime> OrderDate => Default(() => DateTime.UtcNow);

    // Reverse 1:1 nav — auto-detected from the single Ref<UserSchema, int>
    public One<UserSchema> User { get; }

    // Junction-based M:N — Tags through OrderTag
    public Many<OrderTagSchema> OrderTags => HasMany<OrderTagSchema>(ot => ot.OrderId);
    public Many<TagSchema> Tags
        => HasManyThrough<TagSchema, OrderTagSchema, OrderSchema>(
            self => self.OrderTags,
            through => through.Tag);
}

public class TagSchema : Schema
{
    public static string Table => "tags";

    public Key<int> TagId => Identity();
    public Col<string> Name => Length(50);
}

public class OrderTagSchema : Schema
{
    public static string Table => "order_tags";

    public Key<int> OrderTagId => Identity();
    public Ref<OrderSchema, int> OrderId => ForeignKey<OrderSchema, int>();
    public Ref<TagSchema, int> TagId => ForeignKey<TagSchema, int>();

    public One<OrderSchema> Order { get; }
    public One<TagSchema> Tag { get; }
}

public class SaleSchema : Schema
{
    public static string Table => "sales";

    public Key<int> SaleId => Identity();
    public Col<string> Region => Length(50);
    public Col<decimal> Amount => Precision(18, 2);
    public Col<DateTime> SaleDate { get; }
}

public class StudentSchema : Schema
{
    public static string Table => "students";

    public Key<int> StudentId => Identity();
    public Col<string> FirstName => Length(100);
    public Col<string> LastName => Length(100);
    public Col<string?> Email { get; }
    public Col<bool> IsActive => Default(true);
}
