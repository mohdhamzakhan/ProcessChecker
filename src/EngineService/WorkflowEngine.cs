using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ProcessChecker.Contracts;

namespace EngineService;

public class WorkflowEngine
{
    private static readonly JsonSerializerOptions J = new(JsonSerializerDefaults.Web);

    private readonly EngineDb _db;
    private readonly DefinitionClient _defs;
    private readonly Dictionary<string, IStepHandler> _handlers;
    private readonly ILogger<WorkflowEngine> _log;

    public WorkflowEngine(EngineDb db, DefinitionClient defs,
        IEnumerable<IStepHandler> handlers, ILogger<WorkflowEngine> log)
    {
        _db = db; _defs = defs; _log = log;
        _handlers = handlers.ToDictionary(h => h.Type);
    }

    // ---------------------------------------------------------------- start
    public async Task<ProcessInstance> StartAsync(StartRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.ProcessKey))
            throw new EngineException(400, "processKey is required.");

        var info = await _defs.GetLatestAsync(req.ProcessKey, ct)
                   ?? throw new EngineException(404, $"Process '{req.ProcessKey}' was not found.");

        return await StartAsync(info, DataBag.FromDictionary(req.Data), req.StartedBy, req.Title, null, ct);
    }

    public async Task<ProcessInstance> StartAsync(
        DefinitionInfo info, DataBag data, string? startedBy, string? title,
        string? sourceKey, CancellationToken ct)
    {
        if (!info.IsActive)
            throw new EngineException(409, $"Process '{info.Key}' is disabled.");

        var def = info.Definition;
        var finalTitle = string.IsNullOrWhiteSpace(title)
            ? $"{info.Name} {DateTime.UtcNow:yyyy-MM-dd HH:mm}"
            : title.Trim();
        if (finalTitle.Length > 300) finalTitle = finalTitle[..300];

        var inst = new ProcessInstance
        {
            ProcessKey = info.Key,
            ProcessName = info.Name,
            ProcessVersion = info.Version,
            Title = finalTitle,
            StartedBy = startedBy,
            SourceKey = sourceKey,
            DefinitionJson = JsonSerializer.Serialize(def, J),
            DataJson = data.ToJson()
        };

        var order = 0;
        foreach (var s in def.Steps)
            inst.Steps.Add(new StepInstance { StepKey = s.Key, StepName = s.Name, StepType = s.Type, OrderNo = order++ });

        _db.Instances.Add(inst);
        if (sourceKey != null)
            _db.TriggerSeenRows.Add(new TriggerSeen { ProcessKey = info.Key, SourceKey = sourceKey, InstanceId = inst.Id });
        await _db.SaveChangesAsync(ct);

        await AdvanceAsync(inst, def, data, ct);
        return inst;
    }

    // ---------------------------------------------------------------- act
    public async Task ActAsync(Guid id, string stepKey, StepAction action, CancellationToken ct)
    {
        var inst = await LoadAsync(id, ct);
        if (inst.Status != InstanceStatus.Running)
            throw new EngineException(409, $"This request is {inst.Status} and cannot be changed.");

        var step = inst.Steps.FirstOrDefault(s => s.StepKey == stepKey)
                   ?? throw new EngineException(404, $"Step '{stepKey}' was not found.");
        if (step.Status != StepState.InProgress)
            throw new EngineException(409, "This step is not waiting for input.");

        var def = ReadDefinition(inst);
        var sd = def.Steps.First(s => s.Key == stepKey);
        var data = DataBag.FromJson(inst.DataJson);

        var result = await Handler(sd.Type).ActAsync(NewContext(inst, step, sd, data), action, ct);
        Apply(inst, step, result);
        await AdvanceAsync(inst, def, data, ct);
    }

    public async Task RetryAsync(Guid id, string stepKey, CancellationToken ct)
    {
        var inst = await LoadAsync(id, ct);
        var step = inst.Steps.FirstOrDefault(s => s.StepKey == stepKey)
                   ?? throw new EngineException(404, $"Step '{stepKey}' was not found.");
        if (step.Status != StepState.Failed)
            throw new EngineException(409, "Only failed steps can be retried.");

        step.Status = StepState.Pending;
        step.Error = null;
        inst.Status = InstanceStatus.Running;

        await AdvanceAsync(inst, ReadDefinition(inst), DataBag.FromJson(inst.DataJson), ct);
    }

    public async Task CancelAsync(Guid id, CancellationToken ct)
    {
        var inst = await LoadAsync(id, ct);
        if (inst.Status is not (InstanceStatus.Running or InstanceStatus.Failed))
            throw new EngineException(409, $"A {inst.Status} request cannot be cancelled.");

        inst.Status = InstanceStatus.Cancelled;
        inst.CompletedAt = DateTime.UtcNow;
        await SaveAsync(inst, ct);
    }

    // ---------------------------------------------------------------- core loop
    private async Task AdvanceAsync(ProcessInstance inst, ProcessDefinitionDto def, DataBag data, CancellationToken ct)
    {
        while (inst.Status == InstanceStatus.Running)
        {
            var step = inst.Steps.OrderBy(s => s.OrderNo).FirstOrDefault(s => s.Status != StepState.Completed);

            if (step == null)
            {
                inst.Status = InstanceStatus.Completed;
                inst.CompletedAt = DateTime.UtcNow;
                break;
            }
            if (step.Status != StepState.Pending) break;   // waiting for a person, or failed/rejected

            var sd = def.Steps.First(s => s.Key == step.StepKey);
            step.Status = StepState.InProgress;
            step.StartedAt = DateTime.UtcNow;

            StepResult result;
            try
            {
                result = await Handler(sd.Type).StartAsync(NewContext(inst, step, sd, data), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogError(ex, "Step {Step} of {Id} crashed", step.StepKey, inst.Id);
                result = StepResult.Fail(ex.Message);
            }

            Apply(inst, step, result);
            if (result.Outcome != StepOutcome.Completed) break;
        }

        inst.DataJson = data.ToJson();
        await SaveAsync(inst, ct);
    }

    private static void Apply(ProcessInstance inst, StepInstance step, StepResult r)
    {
        var now = DateTime.UtcNow;
        if (r.Output != null) step.OutputJson = JsonSerializer.Serialize(r.Output, J);

        switch (r.Outcome)
        {
            case StepOutcome.Completed:
                step.Status = StepState.Completed;
                step.Error = null;
                step.CompletedAt = now;
                break;

            case StepOutcome.Rejected:
                step.Status = StepState.Rejected;
                step.CompletedAt = now;
                inst.Status = InstanceStatus.Rejected;
                inst.CompletedAt = now;
                break;

            case StepOutcome.Failed:
                step.Status = StepState.Failed;
                step.Error = r.Error is { Length: > 1000 } ? r.Error[..1000] : r.Error;
                inst.Status = InstanceStatus.Failed;
                break;
        }
    }

    // ---------------------------------------------------------------- helpers
    private IStepHandler Handler(string type) =>
        _handlers.TryGetValue(type, out var h)
            ? h
            : throw new EngineException(500, $"No handler is registered for step type '{type}'.");

    private static StepContext NewContext(ProcessInstance i, StepInstance s, StepDefinition d, DataBag data) =>
        new() { Instance = i, Step = s, Def = d, Data = data };

    private static ProcessDefinitionDto ReadDefinition(ProcessInstance i) =>
        JsonSerializer.Deserialize<ProcessDefinitionDto>(i.DefinitionJson, J)!;

    private async Task<ProcessInstance> LoadAsync(Guid id, CancellationToken ct) =>
        await _db.Instances.Include(i => i.Steps).ThenInclude(s => s.Approvals)
            .FirstOrDefaultAsync(i => i.Id == id, ct)
        ?? throw new EngineException(404, "Request not found.");

    private async Task SaveAsync(ProcessInstance inst, CancellationToken ct)
    {
        inst.RowVer++;
        try { await _db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException)
        {
            throw new EngineException(409, "Someone else just changed this request. Reload and try again.");
        }
    }
}