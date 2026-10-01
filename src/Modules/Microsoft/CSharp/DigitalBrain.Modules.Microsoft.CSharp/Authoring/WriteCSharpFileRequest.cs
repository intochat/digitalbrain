using System.ComponentModel;
using System.Reflection;
using DigitalBrain.Microsoft.CSharp;
using ModelContextProtocol.Server;

namespace DigitalBrain.Microsoft.CSharp;

public sealed record WriteCSharpFileRequest(string Source, string? Name = null, string? Purpose = null);
