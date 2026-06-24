using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Orkabi.Web.Modules.Curriculum;
using Orkabi.Web.Modules.Identity;
using Orkabi.Web.Shared;

namespace Orkabi.Web.Pages.Curriculum.Syllabi;

[Authorize(Roles = AppRoles.CsOrAdmin)]
public class CreateModel : PageModel
{
    private readonly CurriculumService _curriculum;
    public CreateModel(CurriculumService curriculum) => _curriculum = curriculum;

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public class InputModel
    {
        [Required(ErrorMessage = "יש להזין שם סילבוס")]
        [MaxLength(200)]
        public string Name { get; set; } = "";

        [Required(ErrorMessage = "יש לבחור תאריך התחלה")]
        public DateOnly StartDate { get; set; }

        [Required(ErrorMessage = "יש לבחור תאריך סיום")]
        public DateOnly EndDate { get; set; }

        public string StatusValue { get; set; } = "Active";
    }

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
            return Page();

        var status = Input.StatusValue == "Archived" ? EntityStatus.Archived : EntityStatus.Active;

        var syllabus = await _curriculum.CreateSyllabusAsync(
            new Syllabus
            {
                Name = Input.Name,
                StartDate = Input.StartDate,
                EndDate = Input.EndDate,
                Status = status
            },
            Array.Empty<(int, int)>()
        );

        return RedirectToPage("Edit", new { id = syllabus.Id });
    }
}
