// Auto-scaffolded by quarry from database 'school' on 2026-03-11
// Review and adjust before using with quarry migrate

using Quarry;
using Index = Quarry.Index;

namespace Scaffolding;

public class DepartmentSchema : Schema
{
    public static string Table => "departments";
    protected override NamingStyle NamingStyle => NamingStyle.SnakeCase;

    public Key<int> Id => Identity();
    public Col<string> Name { get; }
    public Col<string> Code { get; }
    public Col<double> Budget { get; }
    public Col<bool> IsActive => Default(true);

    // Navigations
    public Many<CourseSchema> Courses => HasMany<CourseSchema>(x => x.DepartmentId);
    public Many<InstructorSchema> Instructors => HasMany<InstructorSchema>(x => x.DepartmentId);

    // Indexes
    public Index IdxDepartmentsCode => Index(Code).Unique();
}
