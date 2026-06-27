using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Orkabi.Web.Modules.Identity;

namespace Orkabi.Web.Pages.Admin.Users;

[Authorize(Roles = AppRoles.Admin)]
public class CreateModel : PageModel
{
    private readonly UserAdminService _users;

    public CreateModel(UserAdminService users) => _users = users;

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string Greeting => User.Identity?.Name?.Split('@')[0] ?? "";

    public class InputModel
    {
        [Required(ErrorMessage = "יש להזין אימייל")]
        [EmailAddress(ErrorMessage = "כתובת אימייל אינה תקינה")]
        public string Email { get; set; } = "";

        [MaxLength(200)]
        public string? FullName { get; set; }

        [Required(ErrorMessage = "יש להזין סיסמה")]
        [MinLength(8, ErrorMessage = "הסיסמה חייבת לכלול לפחות 8 תווים")]
        public string Password { get; set; } = "";

        [Required(ErrorMessage = "יש לבחור תפקיד")]
        public string Role { get; set; } = "";
    }

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();

        var result = await _users.CreateAsync(Input.Email, Input.FullName, Input.Password, Input.Role);
        if (result.Succeeded) return RedirectToPage("Index");

        foreach (var e in result.Errors)
            ModelState.AddModelError(string.Empty, e.Description);
        return Page();
    }
}
