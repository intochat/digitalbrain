export const KERNEL = {
  label: 'Kernel',
  section: '#_2-the-kernel',
  role: 'DigitalBrain.Kernel.Neuron mechanics only — receive and dispatch synapses, journal both directions, enforce owner and delivery invariants, mint the one opaque CapabilityDelegation. No AI, provider, integration, or memory concepts live here.',
  owns: ['Neuron', 'Synapse', 'CapabilityDelegation'],
  synapses: ['CapabilityRequested', 'CapabilityCompleted'],
}

export const MODULES = [
  {
    id: 'ai', label: 'AI', status: 'built', section: '#_4-1-ai',
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
    id: 'tasks', label: 'Tasks', status: 'built', section: '#_4-2-tasks',
    role: 'Durable desired-outcome identity. Exactly one Attempt is active at a time; a MAF workflow runs each attempt. Workers report typed facts.',
    neurons: ['ITask', 'IWorker'],
    synapses: ['AttemptSucceeded', 'AttemptFailed', 'AttemptWaiting', 'AttemptProgressed', 'AttemptOutcomeUncertain'],
    mcp: false, ui: false, aspire: [],
  },
  {
    id: 'google', label: 'Google', status: 'built', section: '#_4-3-google',
    role: 'Gmail as a semantic capability root. Exact hosted-MCP admission and mapping stay module-private; raw MCP types never cross IGmail.',
    neurons: ['IGmail'], synapses: [], mcp: true, ui: false,
    aspire: [{ res: 'Google OAuth', sub: 'loopback callback', model: '', params: ['google-client-id', 'google-client-secret', 'google-redirect-uri'] }],
  },
  {
    id: 'salesforce', label: 'Salesforce', status: 'built', section: '#_4-4-salesforce',
    role: 'Approved, reconciled external mutations bound to a CommandId. Never claims exactly-once effects.',
    neurons: ['ISalesforce'], synapses: [], mcp: true, ui: false,
    aspire: [{ res: 'Salesforce OAuth', sub: 'external client app', model: '', params: ['salesforce-client-id', 'salesforce-redirect-uri'] }],
  },
  {
    id: 'time', label: 'Time', status: 'designed', section: '#_4-5-time',
    role: 'Built: durable one-shot ICountdown. Designed/unbuilt: IReminder and recurring calendar schedules. Separate from kernel-private outbox timers; reuses the shared kernel reminder provider.',
    neurons: ['ICountdown', 'IReminder'],
    synapses: ['CountdownElapsed', 'ReminderElapsed', 'ReminderOverdue'],
    mcp: false, ui: false, aspire: [],
  },
  {
    id: 'flutter', label: 'Flutter', status: 'designed', section: '#_4-6-flutter',
    role: 'Flutter neurons and a contract drift guard. Outside the first executable proof.',
    neurons: ['IFlutter'], synapses: [], mcp: false, ui: true, aspire: [],
  },
  {
    id: 'memory', label: 'Memory', status: 'scope', section: '#_4-7-memory',
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
    uses: ['ReminderElapsed', 'IReminder', 'IGmail', 'ILlama32'],
    role: 'When the daily reminder elapses, read a message and summarise it with a local model. Composes Time, Google, and AI vocabulary — no new contract.',
    script: `public sealed class MorningDigest(
    IReminder daily, IGmail gmail, ILlama32 llama)
    : Behavior, IHandle<ReminderElapsed>
{
    public async Task HandleAsync(ReminderElapsed e, ...)
    {
        var message = await gmail.ReadMessageAsync(...);
        await llama.RespondAsync(Summarise(message));
    }
}`,
  },
  {
    id: 'lead', label: 'Lead follow-up', status: 'designed', trigger: 'on AttemptWaiting',
    uses: ['AttemptWaiting', 'ISalesforce', 'ICountdown'],
    role: 'When an attempt reports it is waiting on approval, propose a Salesforce update and set a follow-up countdown. Composes Tasks, Salesforce, and Time — the mutation stays behind the module\'s approval rail.',
    script: `public sealed class LeadFollowUp(
    ISalesforce crm, ICountdown followUp)
    : Behavior, IHandle<AttemptWaiting>
{
    public async Task HandleAsync(AttemptWaiting a, ...)
    {
        if (a.Blocker is not ApprovalRequired) return;
        await crm.ProposeAccountDescriptionAsync(...);
        await followUp.StartAsync(TimeSpan.FromDays(2));
    }
}`,
  },
]
