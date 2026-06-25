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
    // to acquire a write lock while the first is still held, SQLite will retry for up to 30 s
    // instead of returning "database is locked" immediately.
    // Pooling=False is the ROOT fix for the parallel-only test flakes. With pooling ON, a
    // connection's native sqlite3 handle lingers in a PROCESS-WIDE pool after Dispose(), so the
    // file stays locked — Dispose() then had to call SqliteConnection.ClearAllPools() to free it.
    // But ClearAllPools() is GLOBAL: it tears down the pooled handles of EVERY connection string
    // in the process, including those that OTHER xUnit test classes (each with its own fixture +
    // DB file) are actively using on parallel threads. Yanking a live handle mid-request produced
    // intermittent ObjectDisposedException / "database is locked" / 500s in whichever test was in
    // flight (e.g. Creating_template_generates_instances) — passing in isolation, flaking only
    // under parallel load, green on retry. With pooling OFF, Close()/Dispose() releases the native
    // handle (and the file lock) immediately and locally, so each fixture can delete its own file
    // with NO process-global flush and NO cross-class interference.
    public string ConnectionString =>
        $"Data Source={_path};Foreign Keys=True;Default Timeout=30;Pooling=False";

    public void Dispose()
    {
        // No ClearAllPools() needed: Pooling=False means handles are already released on close,
        // and a global flush here would race other parallel test classes' live connections.
        if (File.Exists(_path)) File.Delete(_path);
    }
}
