using Quarry.Internal;

namespace Quarry.Tests.SqlOutput;

internal record SqlTestCase(string RuntimeSql, QueryState State);
internal record UpdateTestCase(string RuntimeSql, UpdateState State);
internal record DeleteTestCase(string RuntimeSql, DeleteState State);
internal record InsertTestCase(string RuntimeSql, InsertState State, int RowCount);
