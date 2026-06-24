namespace Orkabi.Web.Modules.Scheduling;

public interface IShiftInstanceGenerator
{
    Task GenerateForTemplateAsync(int templateId, int horizonDays = 30, CancellationToken ct = default);
    Task GenerateAllActiveAsync(int horizonDays = 30, CancellationToken ct = default);
}
