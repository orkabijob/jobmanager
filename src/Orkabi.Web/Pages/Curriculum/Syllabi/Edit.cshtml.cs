using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Orkabi.Web.Modules.Curriculum;
using Orkabi.Web.Modules.Identity;
using Orkabi.Web.Shared;
using CurriculumModel = Orkabi.Web.Modules.Curriculum.Model;

namespace Orkabi.Web.Pages.Curriculum.Syllabi;

[Authorize(Roles = AppRoles.CsOrAdmin)]
public class EditModel : PageModel
{
    private readonly CurriculumService _curriculum;
    public EditModel(CurriculumService curriculum) => _curriculum = curriculum;

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public Syllabus? Syllabus { get; private set; }
    public string SyllabusName { get; private set; } = "";
    public EntityStatus SyllabusStatus { get; private set; } = EntityStatus.Active;
    public List<CurriculumModel> AvailableModels { get; private set; } = new();

    public class InputModel
    {
        [Required(ErrorMessage = "יש להזין שם סילבוס")]
        [MaxLength(200)]
        public string Name { get; set; } = "";

        [Required(ErrorMessage = "יש לבחור תאריך התחלה")]
        public DateOnly StartDate { get; set; }

        [Required(ErrorMessage = "יש לבחור תאריך סיום")]
        public DateOnly EndDate { get; set; }

        public string StatusValue { get; set; } = "Active";
    }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        Syllabus = await _curriculum.GetSyllabusAsync(id);
        if (Syllabus is null)
            return NotFound();

        SyllabusName = Syllabus.Name;
        SyllabusStatus = Syllabus.Status;

        Input = new InputModel
        {
            Name = Syllabus.Name,
            StartDate = Syllabus.StartDate,
            EndDate = Syllabus.EndDate,
            StatusValue = Syllabus.Status == EntityStatus.Archived ? "Archived" : "Active"
        };

        await LoadAvailableModelsAsync(Syllabus);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        if (!ModelState.IsValid)
        {
            Syllabus = await _curriculum.GetSyllabusAsync(id);
            if (Syllabus is not null)
            {
                SyllabusName = Syllabus.Name;
                SyllabusStatus = Syllabus.Status;
                await LoadAvailableModelsAsync(Syllabus);
            }
            return Page();
        }

        var syllabus = await _curriculum.GetSyllabusAsync(id);
        if (syllabus is null)
            return NotFound();

        var status = Input.StatusValue == "Archived" ? EntityStatus.Archived : EntityStatus.Active;
        var existingModels = syllabus.SyllabusModels
            .OrderBy(sm => sm.OrderIndex)
            .Select(sm => (sm.ModelId, sm.OrderIndex));

        syllabus.Name = Input.Name;
        syllabus.StartDate = Input.StartDate;
        syllabus.EndDate = Input.EndDate;
        syllabus.Status = status;

        await _curriculum.UpdateSyllabusAsync(syllabus, existingModels);
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostMoveUpAsync(int id, int modelId)
    {
        await _curriculum.ReorderAsync(id, modelId, -1);
        var updated = await _curriculum.GetSyllabusAsync(id);
        return Partial("_SyllabusModelList", updated);
    }

    public async Task<IActionResult> OnPostMoveDownAsync(int id, int modelId)
    {
        await _curriculum.ReorderAsync(id, modelId, +1);
        var updated = await _curriculum.GetSyllabusAsync(id);
        return Partial("_SyllabusModelList", updated);
    }

    public async Task<IActionResult> OnPostRemoveAsync(int id, int modelId)
    {
        await _curriculum.RemoveModelFromSyllabusAsync(id, modelId);
        var updated = await _curriculum.GetSyllabusAsync(id);
        return Partial("_SyllabusModelList", updated);
    }

    public async Task<IActionResult> OnPostAddModelAsync(int id, int addModelId)
    {
        if (addModelId > 0)
            await _curriculum.AddModelToSyllabusAsync(id, addModelId);

        var updated = await _curriculum.GetSyllabusAsync(id);
        return Partial("_SyllabusModelList", updated);
    }

    private async Task LoadAvailableModelsAsync(Syllabus syllabus)
    {
        var allModels = await _curriculum.ListModelsAsync();
        var inSyllabus = syllabus.SyllabusModels.Select(sm => sm.ModelId).ToHashSet();
        AvailableModels = allModels.Where(m => !inSyllabus.Contains(m.Id)).ToList();
    }
}
