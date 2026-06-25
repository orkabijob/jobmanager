using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Orkabi.Web.Data;
using Orkabi.Web.Modules.Curriculum;
using Orkabi.Web.Modules.Identity;
using Orkabi.Web.Shared;

namespace Orkabi.Web.Pages.Curriculum.Syllabi;

[Authorize(Roles = AppRoles.CsOrAdmin)]
public class IndexModel : PageModel
{
    private readonly CurriculumService _curriculum;
    private readonly AppDbContext _db;

    public IndexModel(CurriculumService curriculum, AppDbContext db)
    {
        _curriculum = curriculum;
        _db = db;
    }

    [BindProperty(SupportsGet = true)]
    public string? Status { get; set; }

    public List<Syllabus> Syllabi { get; private set; } = new();
    public Dictionary<int, int> ModelCounts { get; private set; } = new();
    public Dictionary<int, int> ClassCounts { get; private set; } = new();

    public async Task OnGetAsync()
    {
        EntityStatus? statusFilter = Status == "Archived" ? EntityStatus.Archived : null;
        Syllabi = await _curriculum.ListSyllabiAsync(statusFilter);

        if (Syllabi.Any())
        {
            var ids = Syllabi.Select(s => s.Id).ToList();
            ModelCounts = await _db.SyllabusModels
                .Where(sm => ids.Contains(sm.SyllabusId))
                .GroupBy(sm => sm.SyllabusId)
                .ToDictionaryAsync(g => g.Key, g => g.Count());

            ClassCounts = await _db.Classes
                .IgnoreQueryFilters()
                .Where(c => c.SyllabusId.HasValue && ids.Contains(c.SyllabusId.Value)
                            && c.Status == EntityStatus.Active)
                .GroupBy(c => c.SyllabusId!.Value)
                .ToDictionaryAsync(g => g.Key, g => g.Count());
        }
    }
}
