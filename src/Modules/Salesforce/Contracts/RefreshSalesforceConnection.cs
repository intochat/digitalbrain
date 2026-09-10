using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.Salesforce;

/// <summary>Requires a stored refresh token; a refused grant clears the connection.</summary>
[GenerateSerializer, Alias("db.salesforce.refresh")]
public sealed record RefreshSalesforceConnection(CommandId Id) : Command(Id);
