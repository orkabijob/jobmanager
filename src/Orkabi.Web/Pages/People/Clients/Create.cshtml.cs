using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Orkabi.Web.Modules.Identity;
using Orkabi.Web.Modules.People;

namespace Orkabi.Web.Pages.People.Clients;

[Authorize(Roles = AppRoles.CsOrAdmin)]
public class CreateModel : PageModel
{
    private readonly ClientService _clients;

    public CreateModel(ClientService clients) => _clients = clients;

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public class InputModel
    {
        [Required(ErrorMessage = "יש להזין שם")]
        [MaxLength(200)]
        public string Name { get; set; } = "";

        [Phone(ErrorMessage = "מספר טלפון אינו תקין")]
        public string? ParentPhone { get; set; }

        [Range(3, 21, ErrorMessage = "גיל חייב להיות בין 3 ל-21")]
        public int? Age { get; set; }

        public DateOnly? Birthday { get; set; }

        public string? Address { get; set; }

        public bool IsActive { get; set; } = true;
    }

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
            return Page();

        await _clients.CreateAsync(new Client
        {
            Name = Input.Name,
            ParentPhone = string.IsNullOrWhiteSpace(Input.ParentPhone) ? null : Input.ParentPhone,
            Age = Input.Age,
            Birthday = Input.Birthday,
            Address = string.IsNullOrWhiteSpace(Input.Address) ? null : Input.Address,
            IsActive = Input.IsActive
        });

        return RedirectToPage("Index");
    }
}
