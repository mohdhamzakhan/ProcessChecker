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
    private readonly TaskMailer _tasks;
    private readonly ILogger<WorkflowEngine> _log;

    public WorkflowEngine(EngineDb db, DefinitionClient defs,
        IEnumerable<IStepHandler> handlers, TaskMailer tasks, ILogger<WorkflowEngine> log)
    {
        _db = db; _defs = defs; _tasks = tasks; _log = log;
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

        var observers = TaskMailer.Parse(def.Observers.Select(o => data.Render(o)));
        inst.Observers = TaskMailer.Join(observers);

        var order = 0;
        foreach (var s in def.Steps)
            inst.Steps.Add(new StepInstance { StepKey = s.Key, StepName = s.Name, StepType = s.Type, OrderNo = order++ });

        _db.Instances.Add(inst);
        if (sourceKey != null)
            _db.TriggerSeenRows.Add(new TriggerSeen { ProcessKey = info.Key, SourceKey = sourceKey, InstanceId = inst.Id });
        await SyncParticipantsAsync(inst, ct);
        await _db.SaveChangesAsync(ct);

        await AdvanceAsync(inst, def, data, ct);
        await _tasks.NotifyObserversAsync(inst, observers, startedBy, ct);
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

        if (step.StepType is StepTypes.Form or StepTypes.Upload)
        {
            var assigned = TaskMailer.Split(step.Assignees);
            if (assigned.Count > 0 && !action.IsAdmin &&
                !assigned.Contains((action.By ?? "").Trim().ToLowerInvariant()))
                throw new EngineException(403, "This task is assigned to other people.");
        }

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

    public async Task ReassignAsync(Guid id, string stepKey, IEnumerable<string> raw, string? by, CancellationToken ct)
    {
        var inst = await LoadAsync(id, ct);
        if (inst.Status != InstanceStatus.Running)
            throw new EngineException(409, $"This request is {inst.Status} and cannot be changed.");

        var step = inst.Steps.FirstOrDefault(s => s.StepKey == stepKey)
                   ?? throw new EngineException(404, $"Step '{stepKey}' was not found.");
        if (step.Status != StepState.InProgress)
            throw new EngineException(409, "Only a step that is waiting can be assigned.");

        var list = TaskMailer.Parse(raw);
        if (list.Count == 0)
            throw new EngineException(400, "Choose at least one person (a valid email address).");

        switch (step.StepType)
        {
            case StepTypes.Approval:
                // while the step waits nobody has decided yet (one approval completes it, one rejection ends it)
                foreach (var old in step.Approvals.Where(a => a.Decision == null).ToList())
                    _db.Approvals.Remove(old);
                foreach (var a in list)
                    step.Approvals.Add(new StepApproval { StepInstanceId = step.Id, Approver = a });
                break;

            case StepTypes.Form:
            case StepTypes.Upload:
                step.Assignees = TaskMailer.Join(list);
                break;

            default:
                throw new EngineException(400, "This kind of step has nobody to assign.");
        }

        await SaveAsync(inst, ct);
        await _tasks.NotifyAsync(inst, step, list, by, ct);     // after the change is saved
    }

    // ---------------------------------------------------------------- core loop
    private async Task AdvanceAsync(ProcessInstance inst, ProcessDefinitionDto def, DataBag data, CancellationToken ct)
    {
        while (inst.Status == InstanceStatus.Running)
        {
            var step = inst.Steps.OrderBy(s => s.OrderNo)
               .FirstOrDefault(s => s.Status != StepState.Completed && s.Status != StepState.Skipped);

            if (step == null)
            {
                inst.Status = InstanceStatus.Completed;
                inst.CompletedAt = DateTime.UtcNow;
                break;
            }
            if (step.Status != StepState.Pending) break;   // waiting for a person, or failed/rejected

            var sd = def.Steps.First(s => s.Key == step.StepKey);
            var why = BranchBlocked(sd, def, data);
            if (why == null)
            {
                var check = Conditions.Evaluate(sd.RunIf, data);
                if (!check.Pass) why = check.Why;
            }
            if (why != null)
            {
                step.Status = StepState.Skipped;
                step.StartedAt = step.CompletedAt = DateTime.UtcNow;
                step.OutputJson = JsonSerializer.Serialize(new { skipped = true, reason = why }, J);
                continue;
            }
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
        inst.Requester = Requesters.Resolve(def, data) ?? inst.Requester;
        await SaveAsync(inst, ct);
    }
    /// null = the step may run. Otherwise the reason it is skipped: a lane that was not taken.
    private static string? BranchBlocked(StepDefinition sd, ProcessDefinitionDto def, DataBag data)
    {
        var cur = sd;
        for (var guard = 0; cur.Branch != null && guard < 20; guard++)
        {
            var dec = def.Steps.FirstOrDefault(s => s.Key == cur.Branch.Of);
            if (dec == null) return $"its If / Else step '{cur.Branch.Of}' no longer exists";

            var result = data.GetString($"{dec.Key}.result");
            if (string.IsNullOrEmpty(result)) return $"the decision '{dec.Name}' was not reached";
            if (!string.Equals(result, cur.Branch.Path, StringComparison.OrdinalIgnoreCase))
                return $"the decision '{dec.Name}' went the {(result == "then" ? "If true" : "Else")} way";

            cur = dec;     // a nested decision is only reached through its own lane
        }
        return null;
    }
    /// Works out the requester again for every request of a process, using the latest saved settings.
    public async Task<int> ResolveRequestersAsync(ProcessDefinitionDto def, string processKey, CancellationToken ct)
    {
        var list = await _db.Instances.Include(i => i.Steps).ThenInclude(s => s.Approvals)
            .Where(i => i.ProcessKey == processKey).ToListAsync(ct);
        var n = 0;
        foreach (var inst in list)
        {
            var req = Requesters.Resolve(def, DataBag.FromJson(inst.DataJson));
            if (req == null || req == inst.Requester) continue;
            inst.Requester = req;
            n++;
            await SyncParticipantsAsync(inst, ct);
        }
        await _db.SaveChangesAsync(ct);
        return n;
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

            case StepOutcome.Ended:
                {
                    step.Status = StepState.Completed;
                    step.Error = null;
                    step.CompletedAt = now;
                    inst.Status = r.EndStatus is InstanceStatus.Rejected or InstanceStatus.Cancelled
                        ? r.EndStatus : InstanceStatus.Completed;
                    inst.CompletedAt = now;

                    // everything still waiting will never run
                    var why = $"the request ended at \"{step.StepName}\"";
                    foreach (var rest in inst.Steps.Where(x => x.Status == StepState.Pending))
                    {
                        rest.Status = StepState.Skipped;
                        rest.StartedAt = rest.CompletedAt = now;
                        rest.OutputJson = JsonSerializer.Serialize(new { skipped = true, reason = why }, J);
                    }
                    break;
                }
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
        try
        {
            await SyncParticipantsAsync(inst, ct);
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new EngineException(409, "Someone else just changed this request. Reload and try again.");
        }
    }

    /// Adds a row for everyone who now takes part in the request (never removes: past assignees keep access).
    private async Task SyncParticipantsAsync(ProcessInstance inst, CancellationToken ct)
    {
        var rows = await _db.Participants.Where(p => p.InstanceId == inst.Id)
            .Select(p => new { p.Email, p.Role }).ToListAsync(ct);
        var have = rows.Select(r => r.Email + "|" + r.Role).ToHashSet();

        foreach (var (email, role) in ParticipantRules.Wanted(inst).Distinct())
            if (have.Add(email + "|" + role))
                _db.Participants.Add(new Participant { InstanceId = inst.Id, Email = email, Role = role });
    }

    public async Task SetObserversAsync(Guid id, IEnumerable<string> raw, string? by, CancellationToken ct)
    {
        var inst = await LoadAsync(id, ct);
        var list = TaskMailer.Parse(raw);
        var added = list.Except(TaskMailer.Split(inst.Observers)).ToList();
        inst.Observers = TaskMailer.Join(list);

        var current = await _db.Participants
            .Where(p => p.InstanceId == id && p.Role == PartRole.Observer).ToListAsync(ct);
        _db.Participants.RemoveRange(current.Where(p => !list.Contains(p.Email)));

        await SaveAsync(inst, ct);
        await _tasks.NotifyObserversAsync(inst, added, by, ct);
    }
}