namespace Orkabi.Web.Shared;

/// INVARIANT: `Archived` is set ONLY by the academic-year batch job (Slice 4+).
/// An entity's own `IsActive = false` (e.g. a dropped-out student) means inactive
/// WITHIN the current year and must NOT be Archived — it stays visible in current-year
/// views. Status (Active/Archived) and IsActive are orthogonal; never conflate them.
public interface IArchivable { EntityStatus Status { get; } }
