using System.Text.RegularExpressions;

namespace DigitalBrain.Specs;

// A Then step that does not hold. Its message is what the author sees next to the red step.
public sealed class StepFailedException(string message) : Exception(message);
