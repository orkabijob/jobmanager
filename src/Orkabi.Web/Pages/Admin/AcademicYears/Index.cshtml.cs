using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Orkabi.Web.Modules.Identity;
using Orkabi.Web.Modules.People;

namespace Orkabi.Web.Pages.Admin.AcademicYears;

[Authorize(Roles = AppRoles.Admin)]
public class IndexModel : PageModel
{
    private readonly AcademicYearService _years;
    public IndexModel(AcademicYearService years) => _years = years;

    public List<AcademicYear> Years { get; private set; } = new();
    public string Greeting => User.Identity?.Name?.Split('@')[0] ?? "";

    public async Task OnGetAsync() => Years = await _years.ListAsync();

    public async Task<IActionResult> OnPostSetCurrentAsync(int id)
    {
        try
        {
            await _years.SetCurrentAsync(id);
            TempData["SuccessMessage"] = "שנת הלימודים הנוכחית עודכנה";
        }
        catch (InvalidOperationException)
        {
            TempData["ErrorMessage"] = "שנת הלימודים לא נמצאה";
        }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRollOverAsync(int fromYearId, int toYearId)
    {
        try
        {
            var result = await _years.RollOverAsync(fromYearId, toYearId);
            TempData["SuccessMessage"] = $"הועתקו {result.ClassesCopied} כיתות ו-{result.TemplatesCopied} תבניות משמרת לשנת היעד.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }
        return RedirectToPage();
    }
}
