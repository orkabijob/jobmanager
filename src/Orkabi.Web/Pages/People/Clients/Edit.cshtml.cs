using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Orkabi.Web.Modules.Identity;
using Orkabi.Web.Modules.People;

namespace Orkabi.Web.Pages.People.Clients;

[Authorize(Roles = AppRoles.CsOrAdmin)]
public class EditModel : PageModel
{
    private readonly ClientService _clients;

    public EditModel(ClientService clients) => _clients = clients;

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public int ClientId { get; private set; }

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

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var client = await _clients.GetAsync(id);
        if (client is null)
            return NotFound();

        ClientId = id;
        Input = new InputModel
        {
            Name = client.Name,
            ParentPhone = client.ParentPhone,
            Age = client.Age,
            Birthday = client.Birthday,
            Address = client.Address,
            IsActive = client.IsActive
        };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        if (!ModelState.IsValid)
        {
            ClientId = id;
            return Page();
        }

        var client = await _clients.GetAsync(id);
        if (client is null)
            return NotFound();

        var priorIsActive = client.IsActive;
        var transitioningToInactive = priorIsActive && !Input.IsActive;

        client.Name = Input.Name;
        client.ParentPhone = string.IsNullOrWhiteSpace(Input.ParentPhone) ? null : Input.ParentPhone;
        client.Age = Input.Age;
        client.Birthday = Input.Birthday;
        client.Address = string.IsNullOrWhiteSpace(Input.Address) ? null : Input.Address;

        // When transitioning active→inactive, do NOT set IsActive=false here.
        // DeactivateAsync (called below) owns that transition and runs the mass-dropout check.
        // Setting IsActive=false before calling DeactivateAsync would cause the idempotent
        // guard in DeactivateAsync to see the client already inactive and skip the check.
        if (!transitioningToInactive)
            client.IsActive = Input.IsActive;

        await _clients.UpdateAsync(client);

        if (transitioningToInactive)
            await _clients.DeactivateAsync(client.Id);

        return RedirectToPage("Index");
    }
}
