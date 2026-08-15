namespace DigitalBrain.Core;

// A configured external MCP server, surfaced to discovery so intent like
// "sales from salesforce" leads to fire db.mcp.list-tools at mcp:<key>.
public sealed record ExternalServerCapability(string Key, string DisplayName);
