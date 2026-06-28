using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Orkabi.Web.Modules.Identity;
using Orkabi.Web.Modules.People;

namespace Orkabi.Web.Pages.People.Schools;

[Authorize(Roles = AppRoles.CsOrAdmin)]
public class EditModel : PageModel
{
    private readonly SchoolService _schools;

    public EditModel(SchoolService schools) => _schools = schools;

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public int SchoolId { get; private set; }

    public class InputModel
    {
        [Required(ErrorMessage = "יש להזין שם בית ספר")]
        [MaxLength(200)]
        public string Name { get; set; } = "";

        [Required(ErrorMessage = "יש להזין עיר")]
        [MaxLength(100)]
        public string City { get; set; } = "";

        [Url(ErrorMessage = "כתובת אתר אינה תקינה")]
        public string? ExternalWebsiteUrl { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var school = await _schools.GetAsync(id);
        if (school is null)
            return NotFound();

        SchoolId = id;
        Input = new InputModel
        {
            Name = school.Name,
            City = school.City,
            ExternalWebsiteUrl = school.ExternalWebsiteUrl
        };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        if (!ModelState.IsValid)
        {
            SchoolId = id;
            return Page();
        }

        var school = await _schools.GetAsync(id);
        if (school is null)
            return NotFound();

        school.Name = Input.Name;
        school.City = Input.City;
        school.ExternalWebsiteUrl = string.IsNullOrWhiteSpace(Input.ExternalWebsiteUrl)
            ? null
            : Input.ExternalWebsiteUrl;

        await _schools.UpdateAsync(school);

        return RedirectToPage("Index");
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        try
        {
            await _schools.DeleteAsync(id);
            return RedirectToPage("Index");
        }
        catch (InvalidOperationException ex)
        {
            var school = await _schools.GetAsync(id);
            if (school is null) return NotFound();
            SchoolId = id;
            Input = new InputModel
            {
                Name = school.Name,
                City = school.City,
                ExternalWebsiteUrl = school.ExternalWebsiteUrl
            };
            ModelState.AddModelError(string.Empty, ex.Message);
            return Page();
        }
    }
}
