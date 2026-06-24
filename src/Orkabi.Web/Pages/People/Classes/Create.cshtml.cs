using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Orkabi.Web.Modules.Identity;
using Orkabi.Web.Modules.People;
using Orkabi.Web.Shared;

namespace Orkabi.Web.Pages.People.Classes;

[Authorize(Roles = AppRoles.CsOrAdmin)]
public class CreateModel : PageModel
{
    private readonly ClassService _classes;
    private readonly SchoolService _schools;
    private readonly AcademicYearService _years;

    public CreateModel(ClassService classes, SchoolService schools, AcademicYearService years)
    {
        _classes = classes;
        _schools = schools;
        _years = years;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public List<School> Schools { get; private set; } = new();
    public List<AcademicYear> Years { get; private set; } = new();

    public class InputModel
    {
        [Required(ErrorMessage = "יש להזין שם כיתה")]
        [MaxLength(200)]
        public string Name { get; set; } = "";

        [Required(ErrorMessage = "יש לבחור בית ספר")]
        [Range(1, int.MaxValue, ErrorMessage = "יש לבחור בית ספר")]
        public int SchoolId { get; set; }

        [Required(ErrorMessage = "יש לבחור שנת לימודים")]
        [Range(1, int.MaxValue, ErrorMessage = "יש לבחור שנת לימודים")]
        public int AcademicYearId { get; set; }

        public EntityStatus Status { get; set; } = EntityStatus.Active;
    }

    public async Task OnGetAsync()
    {
        Schools = await _schools.ListAsync();
        Years = await _years.ListAsync();

        var currentYear = await _years.GetCurrentAsync();
        if (currentYear is not null)
            Input.AcademicYearId = currentYear.Id;
    }

    public async Task<IActionResult> OnPostAsync()
    {
        Schools = await _schools.ListAsync();
        Years = await _years.ListAsync();

        if (!ModelState.IsValid)
            return Page();

        await _classes.CreateAsync(new Class
        {
            Name = Input.Name,
            SchoolId = Input.SchoolId,
            AcademicYearId = Input.AcademicYearId,
            Status = Input.Status
        });

        return RedirectToPage("Index");
    }
}
