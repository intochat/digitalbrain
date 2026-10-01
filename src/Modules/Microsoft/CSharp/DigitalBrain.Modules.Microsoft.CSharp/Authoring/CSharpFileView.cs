using System.ComponentModel;
using System.Reflection;
using DigitalBrain.Microsoft.CSharp;
using ModelContextProtocol.Server;

namespace DigitalBrain.Microsoft.CSharp;

public sealed record CSharpFileView(CSharpDescription Description, CSharpFileSnapshot File, string? Logs = null);
