export const KERNEL = {
  label: 'Kernel',
  section: '#2-the-kernel',
  role: 'DigitalBrain.Kernel.Neuron mechanics only — receive and dispatch synapses, journal both directions, enforce owner and delivery invariants, mint the one opaque CapabilityDelegation. No AI, provider, integration, or memory concepts live here.',
  owns: ['Neuron', 'Synapse', 'CapabilityDelegation'],
  synapses: ['CapabilityRequested', 'CapabilityCompleted'],
}

export const MODULES = [
  {
    id: 'ai', label: 'AI', status: 'built', section: '#41-ai',
    role: 'MAF-backed agents and orchestration over typed models. The public wire is Microsoft.Extensions.AI; MAF types stay internal.',
    neurons: ['ILLM', 'IAgent', 'IGroupChat', 'ILlama32', 'IGpt56'], synapses: [],
    mcp: false, ui: false,
    aspire: [
      { res: 'Ollama', sub: 'data volume', model: 'llama3.2', params: [] },
      { res: 'OpenAI', sub: '', model: 'gpt-5.6', params: ['openai-api-key'] },
    ],
    example: true,
  },
  {
    id: 'tasks', label: 'Tasks', status: 'built', section: '#42-tasks',
    role: 'Durable desired-outcome identity. Exactly one Attempt is active at a time; a MAF workflow runs each attempt. Workers report typed facts.',
    neurons: ['ITask', 'IWorker'],
    synapses: ['AttemptSucceeded', 'AttemptFailed', 'AttemptWaiting', 'ApprovalRequired', 'AttemptOutcomeUncertain'],
    mcp: false, ui: false, aspire: [],
  },
  {
    id: 'google', label: 'Google', status: 'built', section: '#43-google',
    role: 'Gmail as a semantic capability root. The pinned MCP catalog stays module-private; the model sees only selected exact tools.',
    neurons: ['IGmail'], synapses: [], mcp: true, ui: false,
    aspire: [{ res: 'Google OAuth', sub: 'loopback callback', model: '', params: ['google-client-id', 'google-client-secret', 'google-redirect-uri'] }],
  },
  {
    id: 'salesforce', label: 'Salesforce', status: 'built', section: '#44-salesforce',
    role: 'Approved, reconciled external mutations bound to a CommandId. Never claims exactly-once effects.',
    neurons: ['ISalesforce'], synapses: [], mcp: true, ui: false,
    aspire: [{ res: 'Salesforce OAuth', sub: 'external client app', model: '', params: ['salesforce-client-id', 'salesforce-client-secret', 'salesforce-redirect-uri'] }],
  },
  {
    id: 'time', label: 'Time', status: 'designed', section: '#45-time',
    role: 'Durable one-shot and recurring schedules, separate from the kernel-private outbox timers. Reuses the shared kernel reminder provider — it adds no store of its own.',
    neurons: ['ICountdown', 'IReminder'],
    synapses: ['CountdownElapsed', 'ReminderElapsed', 'ReminderOverdue'],
    mcp: false, ui: false, aspire: [],
  },
  {
    id: 'flutter', label: 'Flutter', status: 'designed', section: '#46-flutter',
    role: 'Flutter neurons and a contract drift guard. Outside the first executable proof.',
    neurons: ['IFlutter'], synapses: [], mcp: false, ui: true, aspire: [],
  },
  {
    id: 'memory', label: 'Memory', status: 'scope', section: '#47-memory',
    role: 'Deliberately out of scope. Designed independently around its own vocabulary, later.',
    neurons: [], synapses: [], mcp: false, ui: false, aspire: [],
  },
]

export const ACTORS = [
  {
    id: 'people', label: 'People', status: 'built',
    role: 'Operate the brain through the owner-bound client — DigitalBrainClient.Connect(grains, "acme"). They send to and observe neurons, and they are the approval authority for every behaviour install.',
  },
  {
    id: 'agents', label: 'Agents', status: 'designed',
    role: 'LLM-powered neurons that also act inside the workspace. An agent can propose a behaviour or operate a neuron, but a mutating action still passes through the same human approval rail — an agent advises, it never owns authority.',
  },
]

export const BEHAVIORS = [
  {
    id: 'digest', label: 'Morning digest', status: 'designed', trigger: 'on ReminderElapsed',
    uses: ['ReminderElapsed', 'IReminder', 'IGmail', 'ILLM'],
    role: 'When the daily reminder elapses, read unread mail and summarise it with a local model. Composes Time, Google, and AI vocabulary — no new contract.',
    script: `public sealed class MorningDigest(
    IReminder daily, IGmail gmail, ILlama32 llama)
    : Behavior, IHandle<ReminderElapsed>
{
    public async Task HandleAsync(ReminderElapsed e, ...)
    {
        var mail = await gmail.SearchAsync("is:unread");
        await llama.RespondAsync(Digest(mail));
    }
}`,
  },
  {
    id: 'lead', label: 'Lead follow-up', status: 'designed', trigger: 'on ApprovalRequired',
    uses: ['ApprovalRequired', 'ISalesforce', 'ICountdown'],
    role: 'When a Task asks for approval, update the Salesforce record and set a follow-up countdown. Composes Tasks, Salesforce, and Time — the mutation stays behind the module\'s approval rail.',
    script: `public sealed class LeadFollowUp(
    ISalesforce crm, ICountdown followUp)
    : Behavior, IHandle<ApprovalRequired>
{
    public async Task HandleAsync(ApprovalRequired a, ...)
    {
        await crm.UpdateAsync(a.Account, ...);
        await followUp.StartAsync(TimeSpan.FromDays(2));
    }
}`,
  },
]
