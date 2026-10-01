using System.Text.RegularExpressions;

namespace DigitalBrain.Specs;

public sealed class StepFailedException(string message) : Exception(message);
