using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Orkabi.Web.Modules.Curriculum;
using Orkabi.Web.Modules.Identity;

namespace Orkabi.Web.Pages.Curriculum.Models;

[Authorize(Roles = AppRoles.CsOrAdmin)]
public class EditModel : PageModel
{
    private readonly CurriculumService _curriculum;
    public EditModel(CurriculumService curriculum) => _curriculum = curriculum;

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public class InputModel
    {
        [Required(ErrorMessage = "יש להזין שם דגם")]
        [MaxLength(200)]
        public string Name { get; set; } = "";

        [Required(ErrorMessage = "מספר שיעורים חייב להיות 1 ומעלה")]
        [Range(1, int.MaxValue, ErrorMessage = "מספר שיעורים חייב להיות 1 ומעלה")]
        public int ExpectedLessonsToComplete { get; set; } = 1;

        [Url(ErrorMessage = "כתובת קישור לחומר אינה תקינה")]
        public string? MaterialLink { get; set; }

        [Url(ErrorMessage = "כתובת קישור לוידאו אינה תקינה")]
        public string? VideoLink { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var model = await _curriculum.GetModelAsync(id);
        if (model is null)
            return NotFound();

        Input = new InputModel
        {
            Name = model.Name,
            ExpectedLessonsToComplete = model.ExpectedLessonsToComplete,
            MaterialLink = model.MaterialLink,
            VideoLink = model.VideoLink
        };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        if (!ModelState.IsValid)
            return Page();

        var model = await _curriculum.GetModelAsync(id);
        if (model is null)
            return NotFound();

        model.Name = Input.Name;
        model.ExpectedLessonsToComplete = Input.ExpectedLessonsToComplete;
        model.MaterialLink = string.IsNullOrWhiteSpace(Input.MaterialLink) ? null : Input.MaterialLink;
        model.VideoLink = string.IsNullOrWhiteSpace(Input.VideoLink) ? null : Input.VideoLink;

        await _curriculum.UpdateModelAsync(model);
        return RedirectToPage("Index");
    }
}
