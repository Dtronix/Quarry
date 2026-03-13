// Auto-scaffolded by quarry from database 'school' on 2026-03-11
// Review and adjust before using with quarry migrate

using Quarry;
using Index = Quarry.Index;

namespace Scaffolding;

public class InstructorSchema : Schema
{
    public static string Table => "instructors";
    protected override NamingStyle NamingStyle => NamingStyle.SnakeCase;

    public Key<int> Id => Identity();
    public Col<string> FirstName { get; }
    public Col<string> LastName { get; }
    public Col<string> Email { get; }
    public Ref<DepartmentSchema, int> DepartmentId => ForeignKey<DepartmentSchema, int>(); // ON DELETE CASCADE
    public Col<string> HireDate { get; }
    public Col<string?> Bio { get; }

    // Indexes
    public Index IdxInstructorsName => Index(LastName, FirstName);
    public Index IdxInstructorsEmail => Index(Email).Unique();
}
