using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Orkabi.Web.Modules.Identity;

namespace Orkabi.Web.Pages.Admin.Users;

[Authorize(Roles = AppRoles.Admin)]
public class IndexModel : PageModel
{
    private readonly UserAdminService _users;

    public IndexModel(UserAdminService users) => _users = users;

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    public List<UserRow> Users { get; private set; } = new();

    public string Greeting => User.Identity?.Name?.Split('@')[0] ?? "";

    public async Task OnGetAsync() => Users = await _users.ListAsync(Q);
}
