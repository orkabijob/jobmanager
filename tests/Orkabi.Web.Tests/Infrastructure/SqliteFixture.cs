using Microsoft.Data.Sqlite;

namespace Orkabi.Web.Tests.Infrastructure;

/// <summary>
/// Gives each test class its own pristine SQLite database. File-backed (NOT :memory:) so the
/// schema survives across the several connections WebApplicationFactory + a test open and close;
/// an in-memory DB is destroyed when its last connection closes, which would drop the schema
/// mid-test. A unique temp file per fixture also isolates classes from each other's rows.
/// </summary>
public sealed class SqliteFixture : IDisposable
{
    private readonly string _path =
        Path.Combine(Path.GetTempPath(), $"orkabi_{Guid.NewGuid():N}.db");

    // Foreign Keys=True turns on FK enforcement (off by default in SQLite) so the inner loop
    // actually exercises relational constraints.
    public string ConnectionString => $"Data Source={_path};Foreign Keys=True";

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();   // release file handles before delete
        if (File.Exists(_path)) File.Delete(_path);
    }
}
