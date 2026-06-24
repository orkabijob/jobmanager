using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Orkabi.Web.Modules.Identity;
using Orkabi.Web.Modules.People;
using Orkabi.Web.Modules.Scheduling;
using Orkabi.Web.Shared;

namespace Orkabi.Web.Pages.Scheduling.Templates;

[Authorize(Roles = AppRoles.CsOrAdmin)]
public class CreateModel : PageModel
{
    private readonly SchedulingService _scheduling;
    private readonly ClassService _classService;
    private readonly AcademicYearService _academicYearService;
    private readonly UserManager<AppUser> _userManager;

    public CreateModel(
        SchedulingService scheduling,
        ClassService classService,
        AcademicYearService academicYearService,
        UserManager<AppUser> userManager)
    {
        _scheduling = scheduling;
        _classService = classService;
        _academicYearService = academicYearService;
        _userManager = userManager;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public List<SelectListItem> ClassItems { get; private set; } = new();
    public List<SelectListItem> InstructorItems { get; private set; } = new();
    public List<SelectListItem> AcademicYearItems { get; private set; } = new();

    public class InputModel
    {
        [Required(ErrorMessage = "יש לבחור כיתה")]
        public int ClassId { get; set; }

        [Required(ErrorMessage = "יש לבחור מדריך")]
        public int DefaultInstructorId { get; set; }

        [Required(ErrorMessage = "יש לבחור יום בשבוע")]
        [Range(0, 6, ErrorMessage = "יום לא תקין")]
        public int DayOfWeek { get; set; }

        [Required(ErrorMessage = "יש להזין שעת התחלה")]
        public TimeOnly StartTime { get; set; }

        [Required(ErrorMessage = "יש להזין שעת סיום")]
        public TimeOnly EndTime { get; set; }

        [Required(ErrorMessage = "יש לבחור שנת לימודים")]
        public int AcademicYearId { get; set; }

        public string StatusValue { get; set; } = "Active";
    }

    public async Task OnGetAsync()
    {
        await LoadSelectListsAsync();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await LoadSelectListsAsync();

        if (!ModelState.IsValid)
            return Page();

        if (Input.EndTime <= Input.StartTime)
        {
            ModelState.AddModelError("Input.EndTime", "שעת הסיום חייבת להיות אחרי שעת ההתחלה");
            return Page();
        }

        var status = Input.StatusValue == "Archived" ? EntityStatus.Archived : EntityStatus.Active;

        var template = new ShiftTemplate
        {
            ClassId = Input.ClassId,
            DefaultInstructorId = Input.DefaultInstructorId,
            DayOfWeek = (DayOfWeek)Input.DayOfWeek,
            StartTime = Input.StartTime,
            EndTime = Input.EndTime,
            AcademicYearId = Input.AcademicYearId,
            Status = status
        };

        await _scheduling.CreateTemplateAsync(template);
        return RedirectToPage("/Scheduling/Templates/Index");
    }

    private async Task LoadSelectListsAsync()
    {
        var classes = await _classService.ListAsync(null, null, null);
        ClassItems = classes.Select(c => new SelectListItem(c.Name, c.Id.ToString())).ToList();

        var years = await _academicYearService.ListAsync();
        AcademicYearItems = years.Select(y => new SelectListItem(y.Label, y.Id.ToString())).ToList();

        var instructors = await _userManager.GetUsersInRoleAsync(AppRoles.Instructor);
        InstructorItems = instructors
            .Select(u => new SelectListItem(u.FullName ?? u.Email ?? u.UserName ?? u.Id.ToString(), u.Id.ToString()))
            .ToList();
    }
}
