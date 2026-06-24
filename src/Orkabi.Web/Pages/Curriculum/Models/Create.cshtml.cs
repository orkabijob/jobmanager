using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Orkabi.Web.Modules.Curriculum;
using Orkabi.Web.Modules.Identity;
using CurriculumModel = Orkabi.Web.Modules.Curriculum.Model;

namespace Orkabi.Web.Pages.Curriculum.Models;

[Authorize(Roles = AppRoles.CsOrAdmin)]
public class CreateModel : PageModel
{
    private readonly CurriculumService _curriculum;
    public CreateModel(CurriculumService curriculum) => _curriculum = curriculum;

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

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
            return Page();

        await _curriculum.CreateModelAsync(new CurriculumModel
        {
            Name = Input.Name,
            ExpectedLessonsToComplete = Input.ExpectedLessonsToComplete,
            MaterialLink = string.IsNullOrWhiteSpace(Input.MaterialLink) ? null : Input.MaterialLink,
            VideoLink = string.IsNullOrWhiteSpace(Input.VideoLink) ? null : Input.VideoLink
        });

        return RedirectToPage("Index");
    }
}
