// Auto-scaffolded by quarry from database 'school' on 2026-03-11
// Review and adjust before using with quarry migrate

using Quarry;
using Index = Quarry.Index;

namespace Scaffolding;

public class CourseSchema : Schema
{
    public static string Table => "courses";
    protected override NamingStyle NamingStyle => NamingStyle.SnakeCase;

    public Key<int> Id => Identity();
    public Col<string> CourseCode { get; }
    public Col<string> Title { get; }
    public Col<int> Credits => Default(3);
    public Ref<DepartmentSchema, int> DepartmentId => ForeignKey<DepartmentSchema, int>();
    public Col<string?> Description { get; }
    public Col<int> MaxEnrollment => Default(30);

    // Navigations
    public Many<AssignmentSchema> Assignments => HasMany<AssignmentSchema>(x => x.CourseId);
    public Many<CoursePrerequisiteSchema> CoursePrerequisitesByPrerequisiteId => HasMany<CoursePrerequisiteSchema>(x => x.PrerequisiteId);
    public Many<CoursePrerequisiteSchema> CoursePrerequisitesByCourseId => HasMany<CoursePrerequisiteSchema>(x => x.CourseId);
    public Many<EnrollmentSchema> Enrollments => HasMany<EnrollmentSchema>(x => x.CourseId);

    // Indexes
    public Index IdxCoursesDepartment => Index(DepartmentId);
    public Index IdxCoursesCourseCode => Index(CourseCode).Unique();
}
