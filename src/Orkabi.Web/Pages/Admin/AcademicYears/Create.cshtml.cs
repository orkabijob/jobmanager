using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Orkabi.Web.Modules.Identity;
using Orkabi.Web.Modules.People;

namespace Orkabi.Web.Pages.Admin.AcademicYears;

[Authorize(Roles = AppRoles.Admin)]
public class CreateModel : PageModel
{
    private readonly AcademicYearService _years;
    public CreateModel(AcademicYearService years) => _years = years;

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string Greeting => User.Identity?.Name?.Split('@')[0] ?? "";

    public class InputModel
    {
        [Required(ErrorMessage = "יש להזין שם שנה")]
        [MaxLength(20, ErrorMessage = "שם השנה עד 20 תווים")]
        public string Label { get; set; } = "";

        // Nullable so [Required] actually fires: a non-nullable DateOnly can never be null, so an
        // empty/omitted date field would bypass the Hebrew message (and could even bind to MinValue).
        [Required(ErrorMessage = "יש לבחור תאריך התחלה")]
        public DateOnly? StartDate { get; set; }

        [Required(ErrorMessage = "יש לבחור תאריך סיום")]
        public DateOnly? EndDate { get; set; }
    }

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
            return Page();

        if (Input.EndDate!.Value <= Input.StartDate!.Value)
        {
            ModelState.AddModelError("Input.EndDate", "תאריך הסיום חייב להיות אחרי תאריך ההתחלה");
            return Page();
        }

        // New years are created non-current; promotion is an explicit "set current" on the list.
        await _years.CreateAsync(Input.Label, Input.StartDate.Value, Input.EndDate.Value);
        return RedirectToPage("Index");
    }
}
