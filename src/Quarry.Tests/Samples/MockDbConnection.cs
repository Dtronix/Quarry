using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;

namespace Quarry.Tests.Samples;

/// <summary>
/// Simple mock DbConnection for testing query building without a real database.
/// </summary>
internal class MockDbConnection : DbConnection
{
    private ConnectionState _state = ConnectionState.Closed;

    /// <summary>
    /// The last command created by this connection, for test inspection.
    /// </summary>
    public MockDbCommand? LastCommand { get; private set; }

    /// <summary>
    /// Value returned by <see cref="MockDbCommand.ExecuteNonQuery"/>.
    /// </summary>
    public int NonQueryResult { get; set; } = 1;

    /// <summary>
    /// Value returned by <see cref="MockDbCommand.ExecuteScalar"/>.
    /// </summary>
    public object? ScalarResult { get; set; } = 42;

    [AllowNull]
    public override string ConnectionString { get; set; } = "";
    public override string Database => "mock";
    public override string DataSource => "mock";
    public override string ServerVersion => "1.0";
    public override ConnectionState State => _state;

    public override void ChangeDatabase(string databaseName) { }
    public override void Close() => _state = ConnectionState.Closed;
    public override void Open() => _state = ConnectionState.Open;

    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
        => throw new NotImplementedException();

    protected override DbCommand CreateDbCommand()
    {
        var cmd = new MockDbCommand(this);
        LastCommand = cmd;
        return cmd;
    }
}

/// <summary>
/// Mock DbCommand that captures command text and parameters for test assertions.
/// </summary>
internal class MockDbCommand : DbCommand
{
    private readonly MockDbConnection _connection;
    private readonly MockDbParameterCollection _parameters = new();

    public MockDbCommand(MockDbConnection connection)
    {
        _connection = connection;
    }

    [AllowNull]
    public override string CommandText { get; set; } = "";
    public override int CommandTimeout { get; set; }
    public override CommandType CommandType { get; set; }
    public override bool DesignTimeVisible { get; set; }
    public override UpdateRowSource UpdatedRowSource { get; set; }
    protected override DbConnection? DbConnection { get; set; }
    protected override DbTransaction? DbTransaction { get; set; }
    protected override DbParameterCollection DbParameterCollection => _parameters;

    public override void Cancel() { }
    public override void Prepare() { }

    public override int ExecuteNonQuery() => _connection.NonQueryResult;
    public override object? ExecuteScalar() => _connection.ScalarResult;

    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
        => new MockDbDataReader();

    protected override DbParameter CreateDbParameter() => new MockDbParameter();
}

/// <summary>
/// Mock parameter for capturing parameter values.
/// </summary>
internal class MockDbParameter : DbParameter
{
    [AllowNull]
    public override string ParameterName { get; set; } = "";
    public override object? Value { get; set; }
    public override DbType DbType { get; set; }
    public override ParameterDirection Direction { get; set; }
    public override bool IsNullable { get; set; }
    public override int Size { get; set; }
    [AllowNull]
    public override string SourceColumn { get; set; } = "";
    public override bool SourceColumnNullMapping { get; set; }

    public override void ResetDbType() { }
}

/// <summary>
/// Mock parameter collection.
/// </summary>
internal class MockDbParameterCollection : DbParameterCollection
{
    private readonly List<DbParameter> _parameters = new();

    public override int Count => _parameters.Count;
    public override object SyncRoot => ((System.Collections.ICollection)_parameters).SyncRoot;

    public override int Add(object value)
    {
        _parameters.Add((DbParameter)value);
        return _parameters.Count - 1;
    }

    public override void AddRange(Array values)
    {
        foreach (DbParameter p in values)
            _parameters.Add(p);
    }

    public override void Clear() => _parameters.Clear();
    public override bool Contains(object value) => _parameters.Contains((DbParameter)value);
    public override bool Contains(string value) => _parameters.Any(p => p.ParameterName == value);
    public override void CopyTo(Array array, int index) => ((System.Collections.ICollection)_parameters).CopyTo(array, index);

    public override System.Collections.IEnumerator GetEnumerator() => _parameters.GetEnumerator();

    public override int IndexOf(object value) => _parameters.IndexOf((DbParameter)value);
    public override int IndexOf(string parameterName) => _parameters.FindIndex(p => p.ParameterName == parameterName);

    public override void Insert(int index, object value) => _parameters.Insert(index, (DbParameter)value);
    public override void Remove(object value) => _parameters.Remove((DbParameter)value);
    public override void RemoveAt(int index) => _parameters.RemoveAt(index);
    public override void RemoveAt(string parameterName) => _parameters.RemoveAll(p => p.ParameterName == parameterName);

    protected override DbParameter GetParameter(int index) => _parameters[index];
    protected override DbParameter GetParameter(string parameterName) => _parameters.First(p => p.ParameterName == parameterName);
    protected override void SetParameter(int index, DbParameter value) => _parameters[index] = value;
    protected override void SetParameter(string parameterName, DbParameter value)
    {
        var idx = IndexOf(parameterName);
        if (idx >= 0) _parameters[idx] = value;
    }
}

/// <summary>
/// Mock data reader that returns no rows.
/// </summary>
internal class MockDbDataReader : DbDataReader
{
    public override object this[int ordinal] => throw new InvalidOperationException();
    public override object this[string name] => throw new InvalidOperationException();

    public override int FieldCount => 0;
    public override int RecordsAffected => 0;
    public override bool HasRows => false;
    public override bool IsClosed => true;
    public override int Depth => 0;

    public override bool Read() => false;
    public override bool NextResult() => false;

    public override bool GetBoolean(int ordinal) => throw new InvalidOperationException();
    public override byte GetByte(int ordinal) => throw new InvalidOperationException();
    public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length) => 0;
    public override char GetChar(int ordinal) => throw new InvalidOperationException();
    public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length) => 0;
    public override string GetDataTypeName(int ordinal) => "mock";
    public override DateTime GetDateTime(int ordinal) => throw new InvalidOperationException();
    public override decimal GetDecimal(int ordinal) => throw new InvalidOperationException();
    public override double GetDouble(int ordinal) => throw new InvalidOperationException();
    public override Type GetFieldType(int ordinal) => typeof(object);
    public override float GetFloat(int ordinal) => throw new InvalidOperationException();
    public override Guid GetGuid(int ordinal) => throw new InvalidOperationException();
    public override short GetInt16(int ordinal) => throw new InvalidOperationException();
    public override int GetInt32(int ordinal) => throw new InvalidOperationException();
    public override long GetInt64(int ordinal) => throw new InvalidOperationException();
    public override string GetName(int ordinal) => "";
    public override int GetOrdinal(string name) => -1;
    public override string GetString(int ordinal) => throw new InvalidOperationException();
    public override object GetValue(int ordinal) => throw new InvalidOperationException();
    public override int GetValues(object[] values) => 0;
    public override bool IsDBNull(int ordinal) => true;

    public override System.Collections.IEnumerator GetEnumerator() => Array.Empty<object>().GetEnumerator();
}
