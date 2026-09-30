using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace DigitalBrain.Platform.Integrations;

internal sealed record IntegrationCatalogEntry(string Id, string DisplayName, string Status, string[] MissingFields);
