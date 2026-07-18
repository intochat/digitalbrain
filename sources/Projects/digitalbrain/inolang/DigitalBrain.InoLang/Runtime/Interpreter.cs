using DigitalBrain.InoLang.Ast;
using DigitalBrain.InoLang.Planning;

namespace DigitalBrain.InoLang.Runtime;

public sealed class Interpreter(ExecutionPlan plan)
{
    static readonly IReadOnlyDictionary<string, string> EmptyVariables =
        new Dictionary<string, string>(StringComparer.Ordinal);

    public Action<string, string, string, double, string, Guid, Guid?>? OnTrace { get; set; }

    private sealed class SpeculativeSandbox(string branchName, SpeculativeSandbox? parentSandbox, Dictionary<string, string> parentVars)
    {
        public string BranchName { get; } = branchName;
        public SpeculativeSandbox? ParentSandbox { get; } = parentSandbox;
        public Dictionary<string, string> ParentVars { get; } = parentVars;
        public Dictionary<string, string> SavedResources { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, long> Counters { get; } = new(StringComparer.Ordinal);
        public List<EmittedSynapse> EmittedSynapses { get; } = [];
        public Dictionary<string, string> Vars { get; } = new(StringComparer.Ordinal);
        public bool Committed { get; set; }
        public bool RolledBack { get; set; }
        public Guid SpeculateStartStepId { get; } = Guid.NewGuid();
    }

    private sealed class SpeculationFailedException(string branchName) : Exception
    {
        public string BranchName { get; } = branchName;
    }

    public async Task<ActivationResult> RunAsync(
        TriggerKey trigger,
        IReadOnlyDictionary<string, string> inbound,
        INeuronHost neurons,
        CancellationToken ct)
        => await RunAsync(trigger, inbound, neurons, null, ct);

    public async Task<ActivationResult> RunAsync(
        TriggerKey trigger,
        IReadOnlyDictionary<string, string> inbound,
        INeuronHost neurons,
        IReadOnlyDictionary<string, string>? memory,
        CancellationToken ct)
    {
        var result = new ActivationResult();
        var vars = new Dictionary<string, string>(StringComparer.Ordinal);
        var activeSandboxes = new Dictionary<string, SpeculativeSandbox>(StringComparer.Ordinal);
        var allSandboxes = new Dictionary<string, SpeculativeSandbox>(StringComparer.Ordinal);

        foreach (var handler in plan.HandlersFor(trigger))
        {
            if (handler.Where is { } w &&
                !await PredicateHoldsAsync(w, inbound, neurons, memory, ct))
                continue;

            foreach (var stmt in handler.Body)
                await ExecAsync(stmt, inbound, vars, neurons, memory, result, activeSandboxes, allSandboxes, null, ct);
        }
        return result;
    }

    async Task<bool> PredicateHoldsAsync(
        Predicate w, IReadOnlyDictionary<string, string> inbound,
        INeuronHost neurons, IReadOnlyDictionary<string, string>? memory, CancellationToken ct)
    {
        var subject = w.Subject.Arg is null ? "" : EvalToString(w.Subject.Arg, inbound, EmptyVariables, memory);
        return await neurons.EvaluatePredicateAsync(w.Subject.Builtin, subject, w.Expected, ct);
    }

    async Task ExecAsync(
        Stmt stmt, IReadOnlyDictionary<string, string> inbound,
        Dictionary<string, string> vars, INeuronHost neurons,
        IReadOnlyDictionary<string, string>? memory,
        ActivationResult result,
        Dictionary<string, SpeculativeSandbox> activeSandboxes,
        Dictionary<string, SpeculativeSandbox> allSandboxes,
        SpeculativeSandbox? activeSandbox,
        CancellationToken ct)
    {
        var targetVars = GetEffectiveVars(vars, activeSandbox);
        switch (stmt)
        {
            case LetAskStmt l:
                var promptVal = EvalToString(l.Prompt, inbound, targetVars, memory);
                OnTrace?.Invoke(
                    activeSandbox?.BranchName ?? "",
                    "AskCall",
                    $"Ask port '{l.Port}' with prompt: {promptVal}",
                    1.0,
                    "",
                    Guid.NewGuid(),
                    activeSandbox?.SpeculateStartStepId);

                targetVars[l.Var] = await neurons.AskAsync(l.Port, promptVal, ct);
                break;
            case LetExprStmt le:
                targetVars[le.Var] = EvalToString(le.Value, inbound, targetVars, memory);
                break;
            case EmitStmt e:
                var args = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var a in e.Args)
                    args[a.Name] = EvalToString(a.Value, inbound, targetVars, memory);
                var emitted = new EmittedSynapse(e.Port, args);
                var emitSandbox = GetActiveSandbox(activeSandbox);
                if (emitSandbox != null)
                    emitSandbox.EmittedSynapses.Add(emitted);
                else
                    result.EmittedSynapses.Add(emitted);
                break;
            case SaveStmt s:
                var val = EvalToString(s.Value, inbound, targetVars, memory);
                var saveSandbox = GetActiveSandbox(activeSandbox);
                if (saveSandbox != null)
                    saveSandbox.SavedResources[s.Port] = val;
                else
                    result.SavedResources[s.Port] = val;
                break;

            case RememberStmt r:
                var rKey = EvalToString(r.Text, inbound, targetVars, memory);
                var rVal = r.Value is null ? "" : EvalToString(r.Value, inbound, targetVars, memory);
                var remSandbox = GetActiveSandbox(activeSandbox);
                if (remSandbox != null)
                    remSandbox.SavedResources[rKey] = rVal;
                else
                    result.SavedResources[rKey] = rVal;
                break;
            case CountStmt c:
                var countSandbox = GetActiveSandbox(activeSandbox);
                if (countSandbox != null)
                {
                    countSandbox.Counters[c.Counter] = GetCounterValue(c.Counter, result, countSandbox) + 1;
                }
                else
                {
                    result.Counters[c.Counter] = result.Counters.GetValueOrDefault(c.Counter, 0L) + 1;
                }
                break;
            case LogStmt g:
                result.Logs.Add(EvalToString(g.Message, inbound, targetVars, memory));
                break;
            case IfStmt i:
                var condVal = EvalToString(i.Cond, inbound, targetVars, memory);
                var condBool = !string.IsNullOrEmpty(condVal) && !string.Equals(condVal, "false", StringComparison.OrdinalIgnoreCase);
                if (condBool)
                {
                    foreach (var s in i.ThenBody)
                        await ExecAsync(s, inbound, targetVars, neurons, memory, result, activeSandboxes, allSandboxes, activeSandbox, ct);
                }
                else
                {
                    foreach (var s in i.ElseBody)
                        await ExecAsync(s, inbound, targetVars, neurons, memory, result, activeSandboxes, allSandboxes, activeSandbox, ct);
                }
                break;
            case ForEachStmt f:
                var listVal = EvalToString(f.SourceList, inbound, targetVars, memory);
                var itemOffset = 0;
                while (TryReadNextListItem(listVal, ref itemOffset, out var item))
                {
                    targetVars[f.VarName] = item;
                    foreach (var s in f.Body)
                        await ExecAsync(s, inbound, targetVars, neurons, memory, result, activeSandboxes, allSandboxes, activeSandbox, ct);
                }
                break;
            case SpeculateStmt spec:
                var sandbox = new SpeculativeSandbox(spec.Branch, activeSandbox, targetVars);
                // COW variables initialization
                foreach (var kvp in targetVars)
                {
                    sandbox.Vars[kvp.Key] = kvp.Value;
                }
                activeSandboxes[spec.Branch] = sandbox;
                allSandboxes[spec.Branch] = sandbox;

                OnTrace?.Invoke(
                    spec.Branch,
                    "SpeculateStart",
                    $"Speculation branch '{spec.Branch}' started.",
                    1.0,
                    "",
                    sandbox.SpeculateStartStepId,
                    activeSandbox?.SpeculateStartStepId);

                try
                {
                    foreach (var s in spec.Body)
                    {
                        await ExecAsync(s, inbound, sandbox.Vars, neurons, memory, result, activeSandboxes, allSandboxes, sandbox, ct);
                    }
                }
                catch (SpeculationFailedException ex) when (ex.BranchName == spec.Branch)
                {
                    bool alreadyRolledBack = sandbox.RolledBack;
                    sandbox.RolledBack = true;
                    activeSandboxes.Remove(spec.Branch);

                    if (!alreadyRolledBack)
                    {
                        OnTrace?.Invoke(
                            spec.Branch,
                            "Rollback",
                            $"Speculation branch '{spec.Branch}' rolled back.",
                            0.0,
                            "",
                            Guid.NewGuid(),
                            sandbox.SpeculateStartStepId);
                    }

                    // Route control flow to the failure handlers!
                    await ExecuteFailureHandlersAsync(spec.Branch, inbound, targetVars, neurons, memory, result, activeSandboxes, allSandboxes, activeSandbox, ct);
                }
                break;
            case CommitStmt commit:
                if (activeSandboxes.TryGetValue(commit.Branch, out var sbToCommit))
                {
                    MergeSandbox(sbToCommit, result, vars);
                    sbToCommit.Committed = true;
                    activeSandboxes.Remove(commit.Branch);

                    OnTrace?.Invoke(
                        commit.Branch,
                        "Commit",
                        $"Speculation branch '{commit.Branch}' committed.",
                        1.0,
                        "",
                        Guid.NewGuid(),
                        sbToCommit.SpeculateStartStepId);
                }
                break;
            case RollbackStmt rollback:
                if (activeSandboxes.TryGetValue(rollback.Branch, out var sbToRollback))
                {
                    sbToRollback.RolledBack = true;
                    activeSandboxes.Remove(rollback.Branch);

                    OnTrace?.Invoke(
                        rollback.Branch,
                        "Rollback",
                        $"Speculation branch '{rollback.Branch}' rolled back.",
                        0.0,
                        "",
                        Guid.NewGuid(),
                        sbToRollback.SpeculateStartStepId);

                    if (activeSandbox != null && activeSandbox.BranchName == rollback.Branch)
                    {
                        throw new SpeculationFailedException(rollback.Branch);
                    }
                }
                else if (allSandboxes.TryGetValue(rollback.Branch, out var historicalSb))
                {
                    OnTrace?.Invoke(
                        rollback.Branch,
                        "Rollback",
                        $"Speculation branch '{rollback.Branch}' rolled back.",
                        0.0,
                        "",
                        Guid.NewGuid(),
                        historicalSb.SpeculateStartStepId);
                }
                break;
            case VerifyStmt v:
                var verifyVal = EvalToString(v.Cond, inbound, targetVars, memory);
                var verifyBool = !string.IsNullOrEmpty(verifyVal) && !string.Equals(verifyVal, "false", StringComparison.OrdinalIgnoreCase);
                if (verifyBool)
                {
                    OnTrace?.Invoke(
                        activeSandbox?.BranchName ?? "",
                        "VerifyPass",
                        $"Verification passed for condition '{v.Cond}'.",
                        1.0,
                        "",
                        Guid.NewGuid(),
                        activeSandbox?.SpeculateStartStepId);
                }
                else
                {
                    var failSandbox = GetActiveSandbox(activeSandbox);
                    if (failSandbox == null)
                        throw new InvalidOperationException("Verify statement executed outside of a speculative sandbox.");

                    OnTrace?.Invoke(
                        failSandbox.BranchName,
                        "VerifyFail",
                        $"Verification failed for condition '{v.Cond}'.",
                        0.0,
                        "",
                        Guid.NewGuid(),
                        failSandbox.SpeculateStartStepId);

                    throw new SpeculationFailedException(failSandbox.BranchName);
                }
                break;

            case FlowMappingStmt fm:
                {
                    var sourcePort = fm.Source switch {
                        PortRefExpr p => p.Name,
                        FieldAccessExpr f => $"{f.PortName}.{f.Field}",
                        _ => EvalToString(fm.Source, inbound, targetVars, memory)
                    };

                    string fmScope = inbound.GetValueOrDefault("Scope", inbound.GetValueOrDefault("scope", ""));
                    string fmKey = inbound.GetValueOrDefault("Key", inbound.GetValueOrDefault("key", ""));
                    
                    var actualPort = sourcePort;
                    string method = "";
                    if (sourcePort.EndsWith(".Get", StringComparison.OrdinalIgnoreCase))
                    {
                        actualPort = sourcePort[..^4];
                        method = "Get";
                    }

                    var prompt = (method == "Get" || actualPort.Contains("SettingsStore"))
                        ? $"get {fmScope}:{fmKey}"
                        : string.Join(" ", inbound.Select(kv => $"{kv.Key}={kv.Value}"));

                    var fmVal = await neurons.AskAsync(sourcePort, prompt, ct);
                    if (string.IsNullOrEmpty(fmVal) && sourcePort.Contains('.'))
                    {
                        var lastDot = sourcePort.LastIndexOf('.');
                        var stripped = sourcePort[..lastDot];
                        fmVal = await neurons.AskAsync(stripped, prompt, ct);
                    }

                    var targetPort = fm.Target switch {
                        PortRefExpr p => p.Name,
                        FieldAccessExpr f => $"{f.PortName}.{f.Field}",
                        _ => EvalToString(fm.Target, inbound, targetVars, memory)
                    };

                    var targetArgs = new Dictionary<string, string>(StringComparer.Ordinal);
                    foreach (var kv in inbound)
                    {
                        targetArgs[kv.Key] = kv.Value;
                    }
                    targetArgs["Value"] = fmVal;
                    targetArgs["value"] = fmVal;

                    var fmEmitted = new EmittedSynapse(targetPort, targetArgs);
                    var fmSandbox = GetActiveSandbox(activeSandbox);
                    if (fmSandbox != null)
                        fmSandbox.EmittedSynapses.Add(fmEmitted);
                    else
                        result.EmittedSynapses.Add(fmEmitted);
                }
                break;
            case WriteStmt ws:
                {
                    var targetPortName = "";
                    string writeScope = "";
                    string writeKey = "";
                    
                    if (ws.Target is CallExpr callTarget)
                    {
                        targetPortName = callTarget.Builtin;
                        if (callTarget.Arg is ArgsExpr argsExpr)
                        {
                            foreach (var arg in argsExpr.Args)
                            {
                                if (arg.Name.Equals("Scope", StringComparison.OrdinalIgnoreCase))
                                    writeScope = EvalToString(arg.Value, inbound, targetVars, memory);
                                else if (arg.Name.Equals("Key", StringComparison.OrdinalIgnoreCase))
                                    writeKey = EvalToString(arg.Value, inbound, targetVars, memory);
                            }
                        }
                        else if (callTarget.Arg != null)
                        {
                            writeKey = EvalToString(callTarget.Arg, inbound, targetVars, memory);
                        }
                    }
                    else if (ws.Target is PortRefExpr prTarget)
                    {
                        targetPortName = prTarget.Name;
                    }
                    else if (ws.Target is FieldAccessExpr faTarget)
                    {
                        targetPortName = $"{faTarget.PortName}.{faTarget.Field}";
                    }
                    else
                    {
                        targetPortName = EvalToString(ws.Target, inbound, targetVars, memory);
                    }
                    
                    var writeVal = EvalToString(ws.Value, inbound, targetVars, memory);

                    if (string.IsNullOrEmpty(writeScope))
                        writeScope = inbound.GetValueOrDefault("Scope", inbound.GetValueOrDefault("scope", ""));
                    if (string.IsNullOrEmpty(writeKey))
                        writeKey = inbound.GetValueOrDefault("Key", inbound.GetValueOrDefault("key", ""));

                    var writePrompt = (targetPortName.Contains("SettingsStore") || targetPortName.Contains("Settings"))
                        ? $"set {writeScope}:{writeKey}={writeVal}"
                        : (!string.IsNullOrEmpty(writeKey))
                            ? $"write {writeKey} {writeVal}"
                            : $"set {writeVal}";

                    await neurons.AskAsync(targetPortName, writePrompt, ct);
                    
                    var saveWriteSandbox = GetActiveSandbox(activeSandbox);
                    if (saveWriteSandbox != null)
                        saveWriteSandbox.SavedResources[targetPortName] = writeVal;
                    else
                        result.SavedResources[targetPortName] = writeVal;
                }
                break;
        }
    }

    static bool TryReadNextListItem(string value, ref int offset, out string item)
    {
        while (offset <= value.Length)
        {
            ReadOnlySpan<char> remaining = value.AsSpan(offset);
            var comma = remaining.IndexOf(',');
            var segmentLength = comma < 0 ? remaining.Length : comma;
            var segment = remaining[..segmentLength].Trim();
            offset += comma < 0 ? remaining.Length + 1 : comma + 1;

            if (!segment.IsEmpty)
            {
                item = segment.ToString();
                return true;
            }

            if (comma < 0)
                break;
        }

        item = string.Empty;
        return false;
    }

    static SpeculativeSandbox? GetActiveSandbox(SpeculativeSandbox? sandbox)
    {
        if (sandbox is null) return null;
        if (!sandbox.Committed && !sandbox.RolledBack) return sandbox;
        var current = sandbox.ParentSandbox;
        while (current != null)
        {
            if (!current.Committed && !current.RolledBack)
                return current;
            current = current.ParentSandbox;
        }
        return null;
    }

    static Dictionary<string, string> GetEffectiveVars(Dictionary<string, string> currentVars, SpeculativeSandbox? sandbox)
    {
        if (sandbox is null) return currentVars;
        var current = sandbox;
        while (current != null)
        {
            if (current.Committed || current.RolledBack)
            {
                return current.ParentVars;
            }
            current = current.ParentSandbox;
        }
        return currentVars;
    }

    static long GetCounterValue(string counter, ActivationResult result, SpeculativeSandbox? sandbox)
    {
        var current = sandbox;
        while (current != null)
        {
            if (current.Counters.TryGetValue(counter, out var count))
                return count;
            current = current.ParentSandbox;
        }
        return result.Counters.GetValueOrDefault(counter, 0L);
    }

    void MergeSandbox(SpeculativeSandbox sb, ActivationResult result, Dictionary<string, string> vars)
    {
        if (sb.Committed || sb.RolledBack) return;

        if (sb.ParentSandbox is { } parent)
        {
            foreach (var kvp in sb.SavedResources)
                parent.SavedResources[kvp.Key] = kvp.Value;
            

            
            foreach (var kvp in sb.Counters)
                parent.Counters[kvp.Key] = kvp.Value;
            
            parent.EmittedSynapses.AddRange(sb.EmittedSynapses);

            foreach (var kvp in sb.Vars)
                parent.Vars[kvp.Key] = kvp.Value;
        }
        else
        {
            foreach (var kvp in sb.SavedResources)
                result.SavedResources[kvp.Key] = kvp.Value;
            

            
            foreach (var kvp in sb.Counters)
                result.Counters[kvp.Key] = kvp.Value;
            
            result.EmittedSynapses.AddRange(sb.EmittedSynapses);

            foreach (var kvp in sb.Vars)
                sb.ParentVars[kvp.Key] = kvp.Value;
        }
    }

    async Task ExecuteFailureHandlersAsync(
        string branchName,
        IReadOnlyDictionary<string, string> inbound,
        Dictionary<string, string> vars,
        INeuronHost neurons,
        IReadOnlyDictionary<string, string>? memory,
        ActivationResult result,
        Dictionary<string, SpeculativeSandbox> activeSandboxes,
        Dictionary<string, SpeculativeSandbox> allSandboxes,
        SpeculativeSandbox? activeSandbox,
        CancellationToken ct)
    {
        var triggerKey = TriggerKey.Failure(branchName);
        foreach (var handler in plan.HandlersFor(triggerKey))
        {
            if (handler.Where is { } w &&
                !await PredicateHoldsAsync(w, inbound, neurons, memory, ct))
                continue;

            foreach (var stmt in handler.Body)
            {
                await ExecAsync(stmt, inbound, vars, neurons, memory, result, activeSandboxes, allSandboxes, activeSandbox, ct);
            }
        }
    }

    static string EvalToString(
        Expr e, IReadOnlyDictionary<string, string> inbound,
        IReadOnlyDictionary<string, string> vars,
        IReadOnlyDictionary<string, string>? memory)
    {
        switch (e)
        {
            case StringExpr s:
                return s.Value;
            case NumberExpr n:
                return n.Value.ToString();
            case PortRefExpr p:
                return vars.GetValueOrDefault(p.Name, "");
            case FieldAccessExpr f:
                return inbound.GetValueOrDefault(f.Field, "");
            case CallExpr c:
                var argVal = EvalToString(c.Arg, inbound, vars, memory);
                switch (c.Builtin)
                {
                    case "is-successful-spawn":
                        return argVal.StartsWith("success:", StringComparison.Ordinal) ? "true" : "false";
                    case "get-token-from-spawn":
                        return argVal.StartsWith("success:", StringComparison.Ordinal)
                            ? argVal["success:".Length..]
                            : "";
                    case "is-azure":
                        return string.Equals(argVal, "azure", StringComparison.OrdinalIgnoreCase) ? "true" : "false";
                    case "is-consent-required":
                        return string.Equals(argVal, "OAuthConsentRequired", StringComparison.Ordinal) ? "true" : "false";
                    case "extract-path":
                    case "get-folder-path":
                        var pathMatch = System.Text.RegularExpressions.Regex.Match(argVal, @"([a-zA-Z]:[/\\][^""\s]+|""(?<path>[^""]+)""|(?<path>[a-zA-Z]:[/\\]\S+))");
                        if (pathMatch.Success)
                        {
                            return pathMatch.Value.Trim('"').Replace("\\", "/");
                        }
                        if (argVal.Contains("D:/"))
                        {
                            return "D:/" + argVal.Split("D:/")[1].Split(' ')[0].Trim('"');
                        }
                        return argVal;
                    default:
                        return string.IsNullOrEmpty(argVal) ? c.Builtin : $"{c.Builtin} {argVal}";
                }
            case ArgsExpr a:
                return string.Join(",", a.Args.Select(arg => $"{arg.Name}:{EvalToString(arg.Value, inbound, vars, memory)}"));
            case InterpExpr i:
                return string.Concat(i.Parts.Select(p => EvalToString(p, inbound, vars, memory)));

            default:
                return "";
        }
    }
}
