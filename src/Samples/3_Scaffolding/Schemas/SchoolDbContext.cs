// Auto-scaffolded by quarry from database 'school' on 2026-03-11
// Review and adjust before using with quarry migrate

using Quarry;

namespace Scaffolding;

[QuarryContext(Dialect = SqlDialect.SQLite)]
public partial class SchoolDbContext : QuarryContext
{
    public partial QueryBuilder<Assignment> Assignments { get; }
    public partial QueryBuilder<CoursePrerequisite> CoursePrerequisites { get; }
    public partial QueryBuilder<Course> Courses { get; }
    public partial QueryBuilder<Department> Departments { get; }
    public partial QueryBuilder<Enrollment> Enrollments { get; }
    public partial QueryBuilder<Instructor> Instructors { get; }
    public partial QueryBuilder<Student> Students { get; }
    public partial QueryBuilder<Submission> Submissions { get; }
}
