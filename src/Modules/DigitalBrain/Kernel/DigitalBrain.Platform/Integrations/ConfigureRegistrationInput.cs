using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace DigitalBrain.Platform.Integrations;

internal sealed record ConfigureRegistrationInput(Dictionary<string, string>? Values);
