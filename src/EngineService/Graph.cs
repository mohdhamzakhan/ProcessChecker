using ProcessChecker.Contracts;

namespace EngineService;

/// A step starts when the step before it in its own path is done (a block counts as done only when all of its
/// paths are done). The first step of a path starts as soon as its block has run.
public sealed class Graph
{
    private readonly ProcessDefinitionDto _def;
    private readonly Dictionary<string, string?> _prev = new();
    private readonly Dictionary<string, List<string>> _children = new();

    public Graph(ProcessDefinitionDto def)
    {
        _def = def;
        var last = new Dictionary<string, string>();
        foreach (var s in def.Steps)
        {
            var list = s.Branch is null ? "" : s.Branch.Of + "\u0001" + s.Branch.Path;
            _prev[s.Key] = last.TryGetValue(list, out var p) ? p : null;
            last[list] = s.Key;
            if (s.Branch != null)
            {
                if (!_children.TryGetValue(s.Branch.Of, out var l)) _children[s.Branch.Of] = l = new List<string>();
                l.Add(s.Key);
            }
        }
    }

    public static bool Terminal(string status) => status is StepState.Completed or StepState.Skipped;

    private bool Done(string key, Dictionary<string, string> st) =>
        st.TryGetValue(key, out var s) && Terminal(s)
        && (!_children.TryGetValue(key, out var kids) || kids.All(k => Done(k, st)));

    public StepDefinition? NextReady(ProcessInstance inst)
    {
        var st = inst.Steps.ToDictionary(x => x.StepKey, x => x.Status);
        foreach (var d in _def.Steps)
        {
            if (!st.TryGetValue(d.Key, out var s) || s != StepState.Pending) continue;
            var ready = _prev[d.Key] is { } p
                ? Done(p, st)
                : d.Branch is null || (st.TryGetValue(d.Branch.Of, out var o) && Terminal(o));
            if (ready) return d;
        }
        return null;
    }
}