using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Orkabi.Web.Modules.Identity;
using Orkabi.Web.Modules.People;

namespace Orkabi.Web.Pages.People.Classes;

/// <summary>View model for the _RosterRow partial — one enrolled student row.</summary>
public record RosterRowVm(Enrollment Enrollment, int ClassId, bool Tryout);

[Authorize(Roles = AppRoles.CsOrAdmin)]
public class RosterModel : PageModel
{
    private readonly ClassService _classes;
    private readonly EnrollmentService _enrollments;

    public RosterModel(ClassService classes, EnrollmentService enrollments)
    {
        _classes = classes;
        _enrollments = enrollments;
    }

    public Class? TheClass { get; private set; }
    public List<Enrollment> Enrolled { get; private set; } = new();
    public List<Client> Available { get; private set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    private async Task<IActionResult?> LoadAsync(int classId)
    {
        TheClass = await _classes.GetAsync(classId);
        if (TheClass is null) return NotFound();
        Enrolled = await _enrollments.ListByClassAsync(classId);
        Available = await _enrollments.ListAvailableForClassAsync(classId, Q);
        return null;
    }

    public async Task<IActionResult> OnGetAsync(int classId)
    {
        var notFound = await LoadAsync(classId);
        return notFound ?? Page();
    }

    public async Task<IActionResult> OnPostAddAsync(int classId, int clientId)
    {
        try
        {
            await _enrollments.EnrollAsync(classId, clientId);
        }
        catch (InvalidOperationException ex)
        {
            ErrorMessage = ex.Message;
        }
        return RedirectToPage(new { classId });
    }

    public async Task<IActionResult> OnPostRemoveAsync(int classId, int enrollmentId)
    {
        await _enrollments.DropAsync(enrollmentId);
        return RedirectToPage(new { classId });
    }

    public async Task<IActionResult> OnPostToggleAsync(int classId, int enrollmentId, string field)
    {
        await _enrollments.ToggleAsync(enrollmentId, field);
        return RedirectToPage(new { classId });
    }
}
