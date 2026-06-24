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
    // Default Timeout=30 sets the SQLite busy-timeout to 30 s: when a second connection tries
    // to acquire a write lock while the first is still held (e.g. a pooled connection from the
    // previous test's OrkabiAppFactory hasn't been released yet), SQLite will retry for up to
    // 30 s instead of returning "database is locked" immediately.  This is the root fix for the
    // intermittent Generator_is_idempotent_on_second_call flake observed under parallel
    // xUnit test-class execution where connection-pool handles can briefly outlive Dispose().
    public string ConnectionString => $"Data Source={_path};Foreign Keys=True;Default Timeout=30";

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();   // release file handles before delete
        if (File.Exists(_path)) File.Delete(_path);
    }
}
