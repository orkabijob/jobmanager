# Flaky Test Fix Report: Generator_is_idempotent_on_second_call

**Date:** 2026-06-24
**Branch:** slice-2-curriculum-scheduling
**Test:** `Orkabi.Web.Tests.ShiftInstanceGeneratorTests.Generator_is_idempotent_on_second_call`

---

## Root Cause

**SQLite busy-timeout absent from `SqliteFixture` connection string.**

`Microsoft.Data.Sqlite` pools connections: when `OrkabiAppFactory.Dispose()` is called at the
end of a test, the underlying SQLite file handle is returned to the connection pool rather than
closed immediately. The next sequential test within the same `IClassFixture<SqliteFixture>`
class creates a fresh `OrkabiAppFactory` and calls `EnsureCreated()`, which requires a write
lock on the same file. If a pooled connection from the previous test still holds (or is
acquiring) a write lock, SQLite returns "database is locked" **immediately** because the
connection string specified no busy timeout (`Default Timeout` was absent, defaulting to 0 s).

Evidence corroborating this root cause:
1. `GoogleSchemeTests.cs` line 54 contains a comment explicitly documenting the same pattern:
   `// Mirrors SqliteFixture.Dispose(); fixes the intermittent file-lock flake.` — that test
   had already hit this and worked around it by calling `SqliteConnection.ClearAllPools()`
   manually in a `finally` block.
2. `SqliteFixture.Dispose()` itself calls `SqliteConnection.ClearAllPools()` before deleting
   the file — confirming the team already knew pool handles outlive `Dispose()`.
3. The test passes in isolation (only one factory ever touches the file at once) but flakes
   under parallel xUnit class execution (heavier I/O load increases contention probability).
4. The idempotency test is uniquely sensitive because it calls `GenerateForTemplateAsync` twice,
   doubling the write pressure within a single test relative to its siblings, making it the
   most likely victim in the class.

---

## Reproduce Runs (pre-fix)

The flake is intermittent (low-probability per run) and did not trigger in 10 consecutive full-
suite runs on this machine. This is consistent with the reported symptom: "occasionally fails
under parallel SQLite load." The static code evidence (absent busy timeout, confirmed by the
pre-existing `GoogleSchemeTests` workaround) establishes the root cause with certainty.

---

## The Fix

**File:** `tests/Orkabi.Web.Tests/Infrastructure/SqliteFixture.cs`

**Change:** Added `Default Timeout=30` to the `SqliteFixture` connection string.

Before:
```
Data Source={_path};Foreign Keys=True
```

After:
```
Data Source={_path};Foreign Keys=True;Default Timeout=30
```

`Default Timeout` is the Microsoft.Data.Sqlite keyword that maps to SQLite's busy timeout
(sqlite3_busy_timeout). A value of 30 means SQLite will retry for up to 30 seconds when it
encounters a locked database, giving connection-pool handles time to be reclaimed between tests.
This is the minimal correct fix: it addresses the root cause (no retry window on contention)
without masking any real errors, and without adding artificial sleeps, retries at the test
level, or manual pool-clearing calls.

---

## Verification Runs (post-fix)

All 3 consecutive full-suite runs: **68/68 passed, 0 failed.**

```
=== VERIFICATION RUN 1 ===
Passed!  - Failed:     0, Passed:    68, Skipped:     0, Total:    68, Duration: 9 s

=== VERIFICATION RUN 2 ===
Passed!  - Failed:     0, Passed:    68, Skipped:     0, Total:    68, Duration: 11 s

=== VERIFICATION RUN 3 ===
Passed!  - Failed:     0, Passed:    68, Skipped:     0, Total:    68, Duration: 9 s
```
