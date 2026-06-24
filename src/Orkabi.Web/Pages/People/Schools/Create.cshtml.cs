using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Orkabi.Web.Modules.Identity;
using Orkabi.Web.Modules.People;

namespace Orkabi.Web.Pages.People.Schools;

[Authorize(Roles = AppRoles.CsOrAdmin)]
public class CreateModel : PageModel
{
    private readonly SchoolService _schools;

    public CreateModel(SchoolService schools) => _schools = schools;

    [BindProperty]
    public InputModel Input { get; set; } = new();

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

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
            return Page();

        await _schools.CreateAsync(new School
        {
            Name = Input.Name,
            City = Input.City,
            ExternalWebsiteUrl = string.IsNullOrWhiteSpace(Input.ExternalWebsiteUrl)
                ? null
                : Input.ExternalWebsiteUrl
        });

        return RedirectToPage("Index");
    }
}
