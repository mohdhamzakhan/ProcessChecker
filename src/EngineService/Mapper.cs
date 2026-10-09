using System.Text.Json;
using ProcessChecker.Contracts;

namespace EngineService;

public static class Mapper
{
    private static readonly JsonSerializerOptions J = new(JsonSerializerDefaults.Web);

    public static InstanceSummary ToSummary(ProcessInstance i)
    {
        var steps = i.Steps.OrderBy(s => s.OrderNo).ToList();
        var current = steps.FirstOrDefault(s => s.Status is StepState.InProgress or StepState.Failed);

        return new InstanceSummary(
            i.Id, i.ProcessKey, i.ProcessName, i.ProcessVersion, i.Title, i.Status,
            i.StartedBy, i.CreatedAt, i.CompletedAt,
            steps.Count, steps.Count(s => s.Status == StepState.Completed), current?.StepName);
    }

    public static InstanceDetail ToDetail(ProcessInstance i)
    {
        var def = JsonSerializer.Deserialize<ProcessDefinitionDto>(i.DefinitionJson, J)!;

        var steps = i.Steps.OrderBy(s => s.OrderNo).Select(s =>
        {
            var sd = def.Steps.FirstOrDefault(x => x.Key == s.StepKey);
            var isForm = s.StepType == StepTypes.Form;

            return new StepView(
                s.Id, s.StepKey, s.StepName, s.StepType, s.OrderNo, s.Status,
                s.ActionBy, s.StartedAt, s.CompletedAt, s.Error,
                ParseElement(s.OutputJson),
                isForm ? sd?.Fields : null,
                isForm ? sd?.AllowUpload : null,
                s.Approvals.Count == 0
                    ? null
                    : s.Approvals.Select(a => new ApprovalView(a.Approver, a.Decision, a.Note, a.DecidedAt)).ToList());
        }).ToList();

        var data = DataBag.FromJson(i.DataJson).All.ToDictionary(k => k.Key, k => k.Value);
        return new InstanceDetail(ToSummary(i), data, steps);
    }

    private static JsonElement? ParseElement(string? json) =>
        string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<JsonElement>(json);
}