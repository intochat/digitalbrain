using DigitalBrain.Abstractions;
using DigitalBrain.Behaviors.Artifacts;
using DigitalBrain.Behaviors.Manifest;
using Xunit;

namespace DigitalBrain.Behaviors.Tests;

public sealed class CanonicalArtifactCompatibility
{
    // Captured from CanonicalArtifactWriter.Write at 8f9d7f58, before BroadcastEmitAliases
    // existed on BehaviorEntryPoints. A signed artifact is only as trustworthy as the byte
    // stream its HMAC covers, so this frozen artifact is the guard on every later manifest
    // change: if the writer stops reproducing it, previously signed artifacts stop verifying.
    private const string FrozenPreEmitGrantArtifact = "UEsDBBQAAAAAAAAAIQDrvxzjMQAAADEAAAALAAAAQmVoYXZpb3IuY3NwdWJsaWMgc2VhbGVkIHJlY29yZCBGcm96ZW5UcmlnZ2VyKHN0cmluZyBMYWJlbCk7UEsDBBQAAAAAAAAAIQBcnFUUQQAAAEEAAAAQAAAAQmVoYXZpb3IuZmVhdHVyZUZlYXR1cmU6IGZyb3plbgogIFNjZW5hcmlvOiBmcm96ZW4gc2NlbmFyaW8KICAgIFRoZW4gaXQgaXMgZnJvemVuUEsDBBQAAAAAAAAAIQAyhjNEJAAAACQAAAAbAAAAYXJ0aWZhY3QvQmVoYXZpb3IuZGVwcy5qc29ueyJydW50aW1lVGFyZ2V0Ijp7Im5hbWUiOiJuZXQxMS4wIn19UEsDBBQAAAAAAAAAIQDN+zy2BAAAAAQAAAAVAAAAYXJ0aWZhY3QvQmVoYXZpb3IuZGxsAQIDBFBLAwQUAAAAAAAAACEAgB2wuhwAAAAcAAAAHwAAAGRlcGVuZGVuY2llcy9wYWNrYWdlcy5sb2NrLmpzb257ImxpYnJhcmllcyI6e30sInZlcnNpb24iOjF9UEsDBBQAAAAAAAAAIQDAGciVIgAAACIAAAAXAAAAZXZpZGVuY2UvYWRtaXNzaW9uLmpzb257InBvbGljeSI6InYxIiwicmVzdWx0IjoicGVuZGluZyJ9UEsDBBQAAAAAAAAAIQBe5TpGDwAAAA8AAAARAAAAZXZpZGVuY2UvYmRkLmpzb257InBhc3NlZCI6dHJ1ZX1QSwMEFAAAAAAAAAAhAGIBRxUdAAAAHQAAABYAAABldmlkZW5jZS9jb21waWxlci5qc29ueyJwb2xpY3kiOiJ2MSIsInJlc3VsdCI6Im9rIn1QSwMEFAAAAAAAAAAhADfx2VIsBAAALAQAAA0AAABtYW5pZmVzdC5qc29ueyJCZWhhdmlvciI6eyJWYWx1ZSI6ImNvbS5kaWdpdGFsYnJhaW4uZnJvemVuIn0sIkNhcGFiaWxpdHlHcmFudHMiOltdLCJDb21waWxlclBvbGljeSI6eyJMYW5ndWFnZVZlcnNpb24iOiJQcmV2aWV3IiwiUG9saWN5SWQiOiJjb250cmFjdC1vbmx5LXYxIiwiUm9zbHluVmVyc2lvbiI6IjUuNi4wIiwiU2RrVmVyc2lvbiI6IjExLjAuMTAwLXByZXZpZXcuNiJ9LCJEZXNjcmlwdGlvbiI6IkZyb3plbiBjb21wYXRpYmlsaXR5IGFydGlmYWN0IiwiRGlzcGxheU5hbWUiOiJGcm96ZW4iLCJFbnRyeVBvaW50cyI6eyJDb250cmFjdCI6eyJCZWhhdmlvckNvbnRyYWN0SWQiOiJjb20uZGlnaXRhbGJyYWluLmZyb3plbiIsIkNhc2VzIjpbeyJDYXNlSWQiOiJjYXNlLkZyb3plblRyaWdnZXIiLCJDYXNlTmFtZSI6IkZyb3plblRyaWdnZXIiLCJDYXNlU2NoZW1hVmVyc2lvbiI6MSwiUGF5bG9hZFNjaGVtYUpzb24iOiJ7XHUwMDIyYWRkaXRpb25hbFByb3BlcnRpZXNcdTAwMjI6ZmFsc2UsXHUwMDIycHJvcGVydGllc1x1MDAyMjp7XHUwMDIyTGFiZWxcdTAwMjI6e1x1MDAyMnR5cGVcdTAwMjI6XHUwMDIyc3RyaW5nXHUwMDIyfX0sXHUwMDIycmVxdWlyZWRcdTAwMjI6W1x1MDAyMkxhYmVsXHUwMDIyXSxcdTAwMjJ0eXBlXHUwMDIyOlx1MDAyMm9iamVjdFx1MDAyMn0ifV0sIkNvbnRyYWN0TWFqb3JWZXJzaW9uIjoxLCJPbmVPZlNjaGVtYUpzb24iOiJ7XHUwMDIyb25lT2ZcdTAwMjI6W3tcdTAwMjJ0eXBlXHUwMDIyOlx1MDAyMm9iamVjdFx1MDAyMn1dfSIsIlJlc3VsdFNjaGVtYUpzb24iOiJ7XHUwMDIydHlwZVx1MDAyMjpcdTAwMjJvYmplY3RcdTAwMjJ9In0sIkV2ZW50QWxpYXNlcyI6W119LCJPdmVydmlldyI6IkZyb3plbiBvdmVydmlldyIsIlJlc291cmNlTGltaXRzIjp7IkNwdU1pbGxpc2Vjb25kcyI6MTAwMCwiTWVtb3J5Qnl0ZXMiOjY3MTA4ODY0LCJXYWxsQ2xvY2tNaWxsaXNlY29uZHMiOjMwMDAwfSwiU2NlbmFyaW9zIjpbeyJCaW5kaW5nS2V5IjoiYmluZC5mcm96ZW4iLCJTY2VuYXJpb0lkIjoic2NlbmFyaW8uZnJvemVuIiwiVGl0bGUiOiJmcm96ZW4gc2NlbmFyaW8ifV19UEsBAhQAFAAAAAAAAAAhAOu/HOMxAAAAMQAAAAsAAAAAAAAAAAAAAAAAAAAAAEJlaGF2aW9yLmNzUEsBAhQAFAAAAAAAAAAhAFycVRRBAAAAQQAAABAAAAAAAAAAAAAAAAAAWgAAAEJlaGF2aW9yLmZlYXR1cmVQSwECFAAUAAAAAAAAACEAMoYzRCQAAAAkAAAAGwAAAAAAAAAAAAAAAADJAAAAYXJ0aWZhY3QvQmVoYXZpb3IuZGVwcy5qc29uUEsBAhQAFAAAAAAAAAAhAM37PLYEAAAABAAAABUAAAAAAAAAAAAAAAAAJgEAAGFydGlmYWN0L0JlaGF2aW9yLmRsbFBLAQIUABQAAAAAAAAAIQCAHbC6HAAAABwAAAAfAAAAAAAAAAAAAAAAAF0BAABkZXBlbmRlbmNpZXMvcGFja2FnZXMubG9jay5qc29uUEsBAhQAFAAAAAAAAAAhAMAZyJUiAAAAIgAAABcAAAAAAAAAAAAAAAAAtgEAAGV2aWRlbmNlL2FkbWlzc2lvbi5qc29uUEsBAhQAFAAAAAAAAAAhAF7lOkYPAAAADwAAABEAAAAAAAAAAAAAAAAADQIAAGV2aWRlbmNlL2JkZC5qc29uUEsBAhQAFAAAAAAAAAAhAGIBRxUdAAAAHQAAABYAAAAAAAAAAAAAAAAASwIAAGV2aWRlbmNlL2NvbXBpbGVyLmpzb25QSwECFAAUAAAAAAAAACEAN/HZUiwEAAAsBAAADQAAAAAAAAAAAAAAAACcAgAAbWFuaWZlc3QuanNvblBLBQYAAAAACQAJAFMCAADzBgAAAAA=";

    [Fact(DisplayName = "an artifact signed before broadcast emit grants existed still writes and verifies byte-identically")]
    public void PreEmitGrantArtifactStillRoundTripsByteIdentically()
    {
        var frozen = Convert.FromBase64String(FrozenPreEmitGrantArtifact);

        var rewritten = CanonicalArtifactWriter.Write(PreEmitGrantEnvelope());
        Assert.Equal(Convert.ToBase64String(frozen), Convert.ToBase64String(rewritten.Bytes));

        var read = CanonicalArtifactReader.Read(frozen);
        Assert.Empty(read.Manifest.EntryPoints.EventAliases);
        Assert.Equal(
            BehaviorArtifactDigest.Compute(frozen).Value,
            rewritten.Digest.Value);
    }

    internal static BehaviorArtifactEnvelope PreEmitGrantEnvelope()
        => new(
            new BehaviorDefinitionManifest(
                new BehaviorId("com.digitalbrain.frozen"),
                "Frozen",
                "Frozen compatibility artifact",
                new BehaviorEntryPoints(
                    [],
                    new BehaviorContractManifest(
                        "com.digitalbrain.frozen",
                        1,
                        """{"oneOf":[{"type":"object"}]}""",
                        [
                            new BehaviorContractCaseManifest(
                                "case.FrozenTrigger",
                                1,
                                "FrozenTrigger",
                                """{"type":"object","properties":{"Label":{"type":"string"}},"required":["Label"],"additionalProperties":false}"""),
                        ],
                        """{"type":"object"}""")),
                [
                    new BehaviorScenarioManifest(
                        "scenario.frozen",
                        "frozen scenario",
                        "bind.frozen"),
                ],
                "Frozen overview",
                new BehaviorCompilerPolicy("11.0.100-preview.6", "5.6.0", "Preview", "contract-only-v1"),
                [],
                new BehaviorResourceLimits(1_000, 64 * 1024 * 1024, 30_000)),
            "public sealed record FrozenTrigger(string Label);",
            "Feature: frozen\n  Scenario: frozen scenario\n    Then it is frozen",
            """{"libraries":{},"version":1}""",
            new byte[] { 1, 2, 3, 4 },
            """{"runtimeTarget":{"name":"net11.0"}}""",
            """{"policy":"v1","result":"ok"}""",
            """{"policy":"v1","result":"pending"}""",
            """{"passed":true}""");
}
