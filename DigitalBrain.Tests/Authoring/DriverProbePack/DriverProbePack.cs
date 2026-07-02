namespace DigitalBrain.Tests.Authoring.DriverProbePack;

// The pack source PackSpecSteps reads by filename ("DriverProbePack.cs") instead of a
// hand-copied string duplicate, mirroring how MarketplaceSeeds keeps real pack source
// (e.g. PersonalAssistantNeuron.cs) as a single co-located string constant.
public static class Source
{
    public const string Code = """
        public sealed class DriverProbePack : DigitalBrain.Core.IPackBehavior
        {
            public string Respond(string input) => "driver-echo:" + (input ?? string.Empty);
        }
        """;
}
