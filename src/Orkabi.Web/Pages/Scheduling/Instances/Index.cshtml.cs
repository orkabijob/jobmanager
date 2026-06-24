using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Orkabi.Web.Modules.Identity;
using Orkabi.Web.Modules.Scheduling;

namespace Orkabi.Web.Pages.Scheduling.Instances;

[Authorize(Roles = AppRoles.CsOrAdmin)]
public class IndexModel : PageModel
{
    private readonly SchedulingService _scheduling;
    private readonly IShiftInstanceGenerator _generator;

    public IndexModel(SchedulingService scheduling, IShiftInstanceGenerator generator)
    {
        _scheduling = scheduling;
        _generator = generator;
    }

    public Dictionary<DateOnly, List<ShiftInstance>> Grouped { get; private set; } = new();

    public async Task OnGetAsync()
    {
        await LoadInstancesAsync();
    }

    public async Task<IActionResult> OnPostGenerateAsync()
    {
        await _generator.GenerateAllActiveAsync();
        await LoadInstancesAsync();
        return Partial("_InstanceList", Grouped);
    }

    private async Task LoadInstancesAsync()
    {
        var from = DateOnly.FromDateTime(DateTime.UtcNow);
        var to = from.AddDays(30);
        var instances = await _scheduling.ListInstancesAsync(from, to);
        Grouped = instances
            .GroupBy(i => i.Date)
            .OrderBy(g => g.Key)
            .ToDictionary(g => g.Key, g => g.ToList());
    }
}
