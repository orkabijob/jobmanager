using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Orkabi.Web.Modules.Identity;
using Orkabi.Web.Modules.People;

namespace Orkabi.Web.Pages.People.Clients;

[Authorize(Roles = AppRoles.CsOrAdmin)]
public class IndexModel : PageModel
{
    private readonly ClientService _clients;

    public IndexModel(ClientService clients) => _clients = clients;

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    /// <summary>
    /// null (default) → true (פעילים); false → כולם
    /// </summary>
    [BindProperty(SupportsGet = true)]
    public bool? ActiveOnly { get; set; }

    public List<Client> Clients { get; private set; } = new();

    public async Task OnGetAsync()
    {
        // Default to activeOnly=true when no param supplied (פעילים pill is default active)
        var activeOnly = ActiveOnly ?? true;
        Clients = await _clients.ListAsync(Q, activeOnly);
    }
}
