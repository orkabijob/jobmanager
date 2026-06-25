using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Orkabi.Web.Modules.Curriculum;
using Orkabi.Web.Modules.Identity;
using Orkabi.Web.Modules.People;
using Orkabi.Web.Shared;

namespace Orkabi.Web.Pages.People.Classes;

[Authorize(Roles = AppRoles.CsOrAdmin)]
public class EditModel : PageModel
{
    private readonly ClassService _classes;
    private readonly SchoolService _schools;
    private readonly AcademicYearService _years;
    private readonly CurriculumService _curriculum;

    public EditModel(ClassService classes, SchoolService schools, AcademicYearService years, CurriculumService curriculum)
    {
        _classes = classes;
        _schools = schools;
        _years = years;
        _curriculum = curriculum;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public int ClassId { get; private set; }
    public List<School> Schools { get; private set; } = new();
    public List<AcademicYear> Years { get; private set; } = new();
    public List<Syllabus> Syllabi { get; private set; } = new();

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

        public int? SyllabusId { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        Schools = await _schools.ListAsync();
        Years = await _years.ListAsync();
        Syllabi = await _curriculum.ListSyllabiAsync();

        var cls = await _classes.GetAsync(id);
        if (cls is null)
            return NotFound();

        ClassId = id;
        Input = new InputModel
        {
            Name = cls.Name,
            SchoolId = cls.SchoolId,
            AcademicYearId = cls.AcademicYearId,
            Status = cls.Status,
            SyllabusId = cls.SyllabusId
        };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        Schools = await _schools.ListAsync();
        Years = await _years.ListAsync();
        Syllabi = await _curriculum.ListSyllabiAsync();
        ClassId = id;

        if (!ModelState.IsValid)
            return Page();

        var cls = await _classes.GetAsync(id);
        if (cls is null)
            return NotFound();

        cls.Name = Input.Name;
        cls.SchoolId = Input.SchoolId;
        cls.AcademicYearId = Input.AcademicYearId;
        cls.Status = Input.Status;
        cls.SyllabusId = Input.SyllabusId;

        await _classes.UpdateAsync(cls);

        return RedirectToPage("Index");
    }
}
