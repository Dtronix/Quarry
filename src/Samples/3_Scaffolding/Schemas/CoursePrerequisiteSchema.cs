// Auto-scaffolded by quarry from database 'school' on 2026-03-11
// Review and adjust before using with quarry migrate

using Quarry;
using Index = Quarry.Index;

namespace Scaffolding;

// Junction table: Many-to-many between Course and Course
public class CoursePrerequisiteSchema : Schema
{
    public static string Table => "course_prerequisites";
    protected override NamingStyle NamingStyle => NamingStyle.SnakeCase;

    public Ref<CourseSchema, int> CourseId => ForeignKey<CourseSchema, int>(); // ON DELETE CASCADE
    public Ref<CourseSchema, int> PrerequisiteId => ForeignKey<CourseSchema, int>(); // ON DELETE CASCADE

    public CompositeKey PK => PrimaryKey(CourseId, PrerequisiteId);
}
