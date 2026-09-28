# Assistant

`AssistantApp` declares module requirements, app services, the main conversation surface and
startup through the Applications builder. `IAssistant` is the application boundary:

- `Run` executes a conversation turn, including model selection, tools, replay, cancellation,
  retained history, usage and receipts.
- `ReadConversation` reads existing workspace/thread history using its original persisted identity.
- `Transcribe` validates recordings and invokes the voice provider.

`AssistantPresentation` owns drafts, selected threads/models, messages, progress, result cards,
receipts and window creation as persisted neuron state. `AssistantApp` explicitly composes the
selectors, message/result/receipt layouts, draft field, attachment input, voice input and buttons.
Each input declares `.OnEvent<IAssistant>()`; its durable binding delivers primitive signals to
`HandleUiEvent`. `AssistantUi` projects application state into ordinary cards, text and controls.
`OpenWindow` creates an independent application instance and registers its surface with `IWorkspace`.
Existing shell conversation snapshots are imported once, preserving persisted thread identities.

There is no Assistant-specific Dart package or opaque conversation UI component. Flutter renders
only the declared primitives. A voice button exists because `AssistantApp` declares `VoiceInput`;
removing that node removes the button. IntoChat authenticates requests and
adapts application calls to HTTP.

Run from the repository root:

```powershell
dotnet test --project src/Apps/Assistant/Tests/Unit/DigitalBrain.Apps.Assistant.Tests.Unit.csproj -p:CodeGraphRefresh=false
```

Generic renderer tests live in `src/Modules/Google/Flutter/app/ui/test`; shell window tests live in
`src/Modules/Google/Flutter/app/shell/test`. The C# features exercise application composition,
neuron UI actions, independent windows, history, replay, cancellation, tools and receipts.
Provider execution is scripted for deterministic tests.
