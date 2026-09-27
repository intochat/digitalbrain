# Specs

Natural-language `.feature` specs that run against a real brain. A spec's steps bind only to phrasings that
host code registers as `StepLibrary` subclasses, so authors (people or agents) compose steps but never define
what a step checks. A green run therefore means the behavior the text states.

- `IFeature.Set(text)` parses the spec (Feature, Background, Scenario, tags, `"""` doc strings; no outlines or
  tables) and binds every step. Bound steps carry the matched pattern and the character ranges of their
  `{string}` / `{int}` parameters, which clients highlight. Syntax problems keep their line.
- `IFeature.Run(subject, skipTags)` runs each scenario (Background first), stops a scenario at its first failing
  step, and publishes `ScenarioFinished` and `FeatureVerified`. `{scenario}` in the subject becomes the
  scenario's line, so each scenario can act on its own subject.
- `IFeature.Vocabulary()` lists every phrasing the brain understands.

```csharp
internal sealed class ArithmeticSteps : StepLibrary
{
    public ArithmeticSteps()
    {
        Step("I add {int}", "Adds a number to the total.", (context, args) => { ... });
    }
}
// silo.Services.AddSingleton<StepLibrary, ArithmeticSteps>();
```

```powershell
dotnet test --project src/Modules/DigitalBrain/Specs/Tests/Unit/DigitalBrain.Modules.Specs.Tests.Unit.csproj -p:CodeGraphRefresh=false
```
