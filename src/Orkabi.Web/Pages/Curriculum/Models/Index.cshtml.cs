using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Orkabi.Web.Modules.Curriculum;
using Orkabi.Web.Modules.Identity;
using CurriculumModel = Orkabi.Web.Modules.Curriculum.Model;

namespace Orkabi.Web.Pages.Curriculum.Models;

[Authorize(Roles = AppRoles.CsOrAdmin)]
public class IndexModel : PageModel
{
    private readonly CurriculumService _curriculum;
    public IndexModel(CurriculumService curriculum) => _curriculum = curriculum;

    public List<CurriculumModel> Models { get; private set; } = new();

    public async Task OnGetAsync()
    {
        Models = await _curriculum.ListModelsAsync();
    }
}
