using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Orkabi.Web.Modules.ActionHub;
using Orkabi.Web.Modules.Identity;

namespace Orkabi.Web.Pages.Operations.ActionItems;

[Authorize(Roles = AppRoles.Admin)]
public class IndexModel : PageModel
{
    private readonly ActionItemService _actionItemService;

    public IndexModel(ActionItemService actionItemService)
    {
        _actionItemService = actionItemService;
    }

    public List<ActionItem> Items { get; private set; } = new();

    public async Task OnGetAsync()
    {
        Items = await _actionItemService.ListOpenForRoleAsync(AppRoles.Admin);
    }
}
