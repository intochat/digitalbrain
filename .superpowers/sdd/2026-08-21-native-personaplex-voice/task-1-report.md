# Task 1: Native contracts and configuration report

## Changed files

- `src/Modules/AI/Contracts/PersonaPlex/IPersonaPlexSession.cs`
- `src/Modules/AI/Contracts/PersonaPlex/PersonaPlexAudioFrame.cs`
- `src/Modules/AI/Contracts/PersonaPlex/PersonaPlexReadiness.cs`
- `src/Modules/AI/Contracts/PersonaPlex/PersonaPlexSessionRequest.cs`
- `tests/DigitalBrain.AI.PersonaPlex.Tests/DigitalBrain.AI.PersonaPlex.Tests.csproj`
- `tests/DigitalBrain.AI.PersonaPlex.Tests/PersonaPlexAudioFrameTests.cs`

## RED

1. Initial command: `dotnet test tests/DigitalBrain.AI.PersonaPlex.Tests --filter FullyQualifiedName~PersonaPlexAudioFrameTests --no-restore`
   - Outcome: failed before compilation because the newly created test project had not yet restored its Microsoft Testing Platform runner assets. This was a test-project setup issue, not a contract result.
2. Definitive RED command: `dotnet test tests/DigitalBrain.AI.PersonaPlex.Tests --filter FullyQualifiedName~PersonaPlexAudioFrameTests`
   - Outcome: failed as expected with CS0103 because `PersonaPlexAudioFrame` did not exist.

## GREEN

1. Focused command: `dotnet test tests/DigitalBrain.AI.PersonaPlex.Tests --filter FullyQualifiedName~PersonaPlexAudioFrameTests`
   - Outcome: passed, 2 succeeded and 0 failed.
2. Final focused command: `dotnet test tests/DigitalBrain.AI.PersonaPlex.Tests --filter FullyQualifiedName~PersonaPlexAudioFrameTests`
   - Outcome: passed, 2 succeeded and 0 failed.
3. Contract build: `dotnet build src/Modules/AI/Contracts/DigitalBrain.Modules.AI.Contracts.csproj --no-restore`
   - Outcome: passed, 0 warnings and 0 errors.

## Implementation

- `PersonaPlexAudioFrame.Create` accepts only a 1,920-sample PCM16 frame and reports an argument error for every other length.
- Session and factory contracts use only native PersonaPlex frame/request types and cancellation; no Whisper/STT, `IChatClient`, `MapChatVoice`, or Orleans dependency is present.
- The readiness record exposes the required Disabled, Loading, Ready, and Failed states together with a non-sensitive message and model-configuration status.

## Self-review

- The valid-frame test catches a wrong frame-size gate; the invalid-frame test catches omission or weakening of the rejection branch.
- Frame creation has a private constructor, preventing callers from bypassing the fixed-size validation through the public contract.
- The changes introduce no logging or audio persistence, so raw PCM cannot be emitted by these contracts.

## Commit

Recorded in the Task 1 contracts commit created after this report.

## Concerns

`PersonaPlexSessionRequest` is deliberately minimal (`ConnectionId`) because the approved design specifies its use but no additional request fields. Later tasks may extend it only if their wire-protocol requirements need explicit metadata.
