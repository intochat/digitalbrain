#pragma warning disable ASPIRECERTIFICATES001

using DigitalBrain.Hosting.DigitalBrain;

namespace DigitalBrain.Hosting;

public static class InoTopologyParser
{
    public static void LoadDynamicTopology(IDistributedApplicationBuilder builder, DigitalBrainResource digitalbrain, string inoFilePath)
    {
        string path = inoFilePath;
        if (!File.Exists(path))
        {
            var candidates = new[]
            {
                Path.Combine(AppContext.BaseDirectory, inoFilePath),
                Path.Combine(Directory.GetCurrentDirectory(), inoFilePath),
                Path.GetFullPath(Path.Combine(builder.AppHostDirectory, inoFilePath)),
                Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "../../", inoFilePath)),
                Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "../", inoFilePath)),
                Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "../..", inoFilePath))
            };
            foreach (var cand in candidates)
            {
                if (File.Exists(cand))
                {
                    path = cand;
                    break;
                }
            }
        }

        if (!File.Exists(path))
        {
            Console.WriteLine($"[InoTopologyParser] Warning: digitalbrain.ino not found at {inoFilePath}");
            return;
        }

        var lines = File.ReadAllLines(path);
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.Contains("register-resource", StringComparison.OrdinalIgnoreCase))
            {
                // Find prompt inside quotes
                string prompt = trimmed;
                int quoteStart = trimmed.IndexOf('"');
                int quoteEnd = trimmed.LastIndexOf('"');
                if (quoteStart != -1 && quoteEnd > quoteStart)
                {
                    prompt = trimmed[(quoteStart + 1)..quoteEnd];
                }

                // Parse
                var parsed = ParseRegisterResource(prompt);
                if (string.IsNullOrEmpty(parsed.Name)) continue;

                // Check if already registered
                bool isRegistered = builder.Resources.Any(r => string.Equals(r.Name, parsed.Name, StringComparison.OrdinalIgnoreCase));
                if (isRegistered) continue;

                // Dynamically register
                RegisterResource(builder, digitalbrain, parsed.Name, parsed.Type, parsed.Config);
            }
        }
    }

    private static (string Name, string Type, Dictionary<string, string> Config) ParseRegisterResource(string prompt)
    {
        var clean = prompt.Trim().Trim('"');
        if (clean.StartsWith("register-resource ", StringComparison.OrdinalIgnoreCase))
        {
            clean = clean["register-resource ".Length..];
        }
        
        var spaceIndex = clean.IndexOf(' ');
        if (spaceIndex == -1)
        {
            return (clean, string.Empty, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        }
        
        var name = clean[..spaceIndex].Trim();
        var remainder = clean[spaceIndex..].Trim();
        
        var config = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var type = string.Empty;
        
        var keys = new[] { "type:", "port:", "path:", "args:", "autostart:" };
        var indices = new List<(string Key, int Index)>();
        foreach (var key in keys)
        {
            int idx = remainder.IndexOf(key, StringComparison.OrdinalIgnoreCase);
            if (idx != -1)
            {
                indices.Add((key, idx));
            }
        }
        
        indices = indices.OrderBy(x => x.Index).ToList();
        
        for (int i = 0; i < indices.Count; i++)
        {
            var current = indices[i];
            int start = current.Index + current.Key.Length;
            int end = (i + 1 < indices.Count) ? indices[i + 1].Index : remainder.Length;
            
            var val = remainder[start..end].Trim();
            var keyName = current.Key.TrimEnd(':');
            
            if (keyName.Equals("type", StringComparison.OrdinalIgnoreCase))
            {
                type = val;
            }
            else
            {
                config[keyName] = val;
            }
        }
        
        return (name, type, config);
    }

    private static void RegisterResource(
        IDistributedApplicationBuilder builder, 
        DigitalBrainResource digitalbrain, 
        string name, 
        string type, 
        Dictionary<string, string> config)
    {
        config.TryGetValue("path", out var configPath);
        config.TryGetValue("args", out var configArgs);
        config.TryGetValue("port", out var configPort);
        config.TryGetValue("autostart", out var configAutostart);

        if (string.Equals(name, "orleans-redis", StringComparison.OrdinalIgnoreCase))
        {
            builder.AddRedis(name)
                .WithoutHttpsCertificate();
        }
        else if (string.Equals(name, "flutter-web", StringComparison.OrdinalIgnoreCase))
        {
            var workingDir = GetWorkingDir(builder.AppHostDirectory, configPath ?? "../../UI/flutter");
            var parsedArgs = (configArgs ?? "run -d web-server --release").Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
            if (!parsedArgs.Any(a => a.StartsWith("--web-hostname")))
            {
                parsedArgs.Add("--web-hostname=localhost");
            }
            if (!parsedArgs.Any(a => a.StartsWith("--web-port")) && configPort != null)
            {
                parsedArgs.Add($"--web-port={configPort}");
            }

            var executable = builder.AddExecutable(name, "flutter", workingDir, parsedArgs.ToArray());
            if (int.TryParse(configPort, out var portNum))
            {
                executable.WithHttpEndpoint(port: portNum, targetPort: portNum, name: "http", isProxied: false);
            }

            var kernel = digitalbrain.Kernel!;
            var kernelHttp = kernel.GetEndpoint("kernel-http");
            executable
                .WithArgs(context => context.Args.Add(
                    ReferenceExpression.Create($"--dart-define=KERNEL_ENDPOINT={kernelHttp}")))
                .WithReference(kernel)
                .WithOtlpExporter()
                .WaitFor(kernel);
        }
        else if (string.Equals(name, "flutter-windows", StringComparison.OrdinalIgnoreCase))
        {
            var workingDir = GetWorkingDir(builder.AppHostDirectory, configPath ?? "../../UI/flutter");
            var parsedArgs = (configArgs ?? "run -d windows --print-dtd").Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
            if (!parsedArgs.Any(a => a.StartsWith("--vm-service-port")) && configPort != null)
            {
                parsedArgs.Add($"--vm-service-port={configPort}");
            }

            var executable = builder.AddExecutable(name, "flutter", workingDir, parsedArgs.ToArray());

            var kernel = digitalbrain.Kernel!;
            var kernelHttps = kernel.GetEndpoint("kernel-https");
            executable
                .WithArgs(context => context.Args.Add(
                    ReferenceExpression.Create($"--dart-define=KERNEL_ENDPOINT={kernelHttps}")))
                .WithReference(kernel)
                .WithOtlpExporter();

            bool autostart = !string.Equals(configAutostart, "false", StringComparison.OrdinalIgnoreCase);
            if (!autostart)
            {
                executable.WithExplicitStart();
            }
        }
        else if (string.Equals(name, "digitalbrain-mcp", StringComparison.OrdinalIgnoreCase))
        {
            var project = builder.AddProject<Projects.DigitalBrain_SDK>(name)
                .WithReference(digitalbrain.Kernel!)
                .WaitFor(digitalbrain.Kernel!)
                .WithEnvironment("KERNEL_ENDPOINT", digitalbrain.Kernel!.GetEndpoint("kernel-https"));

            if (int.TryParse(configPort, out var portNum))
            {
                project.WithHttpEndpoint(port: portNum, targetPort: portNum, name: "http", isProxied: false);
            }
        }
    }

    private static string GetWorkingDir(string appHostDir, string relativePath)
    {
        // Find repository root robustly
        var dir = appHostDir;
        while (dir is not null && !File.Exists(Path.Combine(dir, "DigitalBrain.slnx")))
        {
            dir = Path.GetDirectoryName(dir);
        }

        if (dir is null)
        {
            dir = Directory.GetCurrentDirectory();
            while (dir is not null && !File.Exists(Path.Combine(dir, "DigitalBrain.slnx")))
            {
                dir = Path.GetDirectoryName(dir);
            }
        }

        if (dir is not null)
        {
            var cleanPath = relativePath;
            while (cleanPath.StartsWith("../") || cleanPath.StartsWith("..\\"))
            {
                cleanPath = cleanPath[3..];
            }
            var rootCombined = Path.GetFullPath(Path.Combine(dir, cleanPath));
            if (Directory.Exists(rootCombined))
            {
                return rootCombined;
            }
        }

        var pathsToTry = new[]
        {
            Path.GetFullPath(Path.Combine(appHostDir, relativePath)),
            Path.GetFullPath(Path.Combine(appHostDir, "../..", relativePath)),
            Path.GetFullPath(Path.Combine(appHostDir, "..", relativePath)),
            relativePath
        };

        foreach (var path in pathsToTry)
        {
            if (Directory.Exists(path))
            {
                return path;
            }
        }
        return relativePath;
    }
}
