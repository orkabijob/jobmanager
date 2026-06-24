using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Orkabi.Web.Modules.Identity;
using Orkabi.Web.Modules.People;
using Orkabi.Web.Shared;

namespace Orkabi.Web.Pages.People.Classes;

[Authorize(Roles = AppRoles.CsOrAdmin)]
public class IndexModel : PageModel
{
    private readonly ClassService _classes;
    private readonly SchoolService _schools;
    private readonly AcademicYearService _years;

    public IndexModel(ClassService classes, SchoolService schools, AcademicYearService years)
    {
        _classes = classes;
        _schools = schools;
        _years = years;
    }

    [BindProperty(SupportsGet = true)]
    public int? SchoolId { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? YearId { get; set; }

    // "Active" | "Archived" | "All" — default is Active
    [BindProperty(SupportsGet = true)]
    public string? Status { get; set; }

    public List<Class> Classes { get; private set; } = new();
    public List<School> Schools { get; private set; } = new();
    public List<AcademicYear> Years { get; private set; } = new();

    public async Task OnGetAsync()
    {
        Schools = await _schools.ListAsync();
        Years = await _years.ListAsync();

        EntityStatus? statusFilter = Status switch
        {
            "Archived" => EntityStatus.Archived,
            "All" => null,
            _ => EntityStatus.Active  // default: Active only
        };

        // When "All" is requested we need to call ListAsync with no status filter,
        // but ListAsync with null uses the global filter (Active only). We handle
        // "All" by calling both and merging, or by using the Archived overload pattern.
        // The service doesn't expose an IgnoreQueryFilters-with-null-status path,
        // so we replicate: ListAsync(null status) = global filter = Active. For "All",
        // call Archived explicitly and merge with Active results.
        if (Status == "All")
        {
            var active = await _classes.ListAsync(SchoolId, YearId, EntityStatus.Active);
            var archived = await _classes.ListAsync(SchoolId, YearId, EntityStatus.Archived);
            // Merge and sort by name (both lists are already sorted individually)
            Classes = active.Concat(archived).OrderBy(c => c.Name).ToList();
        }
        else
        {
            Classes = await _classes.ListAsync(SchoolId, YearId, statusFilter);
        }
    }
}
