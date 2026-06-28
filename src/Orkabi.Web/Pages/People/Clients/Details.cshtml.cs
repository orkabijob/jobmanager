using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Orkabi.Web.Modules.Identity;
using Orkabi.Web.Modules.People;

namespace Orkabi.Web.Pages.People.Clients;

// F12 — read-only client profile: basic info + the client's enrollments across all classes.
[Authorize(Roles = AppRoles.CsOrAdmin)]
public class DetailsModel : PageModel
{
    private readonly ClientService _clients;
    private readonly EnrollmentService _enrollments;

    public DetailsModel(ClientService clients, EnrollmentService enrollments)
    {
        _clients = clients;
        _enrollments = enrollments;
    }

    public Client TheClient { get; private set; } = null!;
    public List<Enrollment> Enrollments { get; private set; } = new();

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var client = await _clients.GetAsync(id);
        if (client is null) return NotFound();
        TheClient = client;
        Enrollments = await _enrollments.ListByClientAsync(id);
        return Page();
    }
}
