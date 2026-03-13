// Auto-scaffolded by quarry from database 'school' on 2026-03-11
// Review and adjust before using with quarry migrate

using Quarry;
using Index = Quarry.Index;

namespace Scaffolding;

// Junction table: Many-to-many between Course and Student
public class EnrollmentSchema : Schema
{
    public static string Table => "enrollments";
    protected override NamingStyle NamingStyle => NamingStyle.SnakeCase;

    public Ref<StudentSchema, int> StudentId => ForeignKey<StudentSchema, int>(); // ON DELETE CASCADE
    public Ref<CourseSchema, int> CourseId => ForeignKey<CourseSchema, int>(); // ON DELETE CASCADE
    public Col<string> EnrolledDate { get; }
    public Col<string?> Grade { get; }

    public CompositeKey PK => PrimaryKey(StudentId, CourseId);
}
