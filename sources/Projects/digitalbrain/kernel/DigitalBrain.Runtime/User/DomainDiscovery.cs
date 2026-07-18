using System.Text.RegularExpressions;

namespace DigitalBrain.Runtime.User;

public sealed class DomainDiscovery : IDomainDiscovery
{
    private readonly string _repoRoot;

    public DomainDiscovery()
    {
        _repoRoot = FindRepositoryRoot();
    }

    public DomainDiscovery(string repoRoot)
    {
        _repoRoot = repoRoot;
    }

    private static string FindRepositoryRoot()
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (File.Exists(Path.Combine(current, "digitalbrain.slnx")) || 
                Directory.Exists(Path.Combine(current, ".git")))
            {
                return current;
            }
            var parent = Path.GetDirectoryName(current);
            if (parent == current) break;
            current = parent;
        }
        return Directory.GetCurrentDirectory();
    }

    public IReadOnlyList<SearchResult> Search(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return Array.Empty<SearchResult>();
        }

        var results = new List<SearchResult>();
        if (!Directory.Exists(_repoRoot))
        {
            return results;
        }

        var inoFiles = Directory.GetFiles(_repoRoot, "*.ino", SearchOption.AllDirectories);
        var searchWords = prompt.Split(new[] { ' ', ',', '.', ';', ':', '-', '_' }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var file in inoFiles)
        {
            if (file.Contains(@"\.gemini\") || file.Contains(@"\bin\") || file.Contains(@"\obj\"))
            {
                continue;
            }

            try
            {
                var content = File.ReadAllText(file);
                var parsed = ParseInoFile(file, content);

                double score = CalculateScore(parsed, prompt, searchWords);
                if (score > 0)
                {
                    results.Add(parsed with { Score = score });
                }
            }
            catch
            {
            }
        }

        return results.OrderByDescending(r => r.Score).ToList();
    }

    private static SearchResult ParseInoFile(string filePath, string content)
    {
        string domain = string.Empty;
        var neurons = new List<string>();
        var synapses = new List<string>();

        var lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("neuron ", StringComparison.OrdinalIgnoreCase))
            {
                domain = trimmed.Substring("neuron ".Length).Trim();
                var spaceIndex = domain.IndexOfAny(new[] { ' ', '\t', '\r', '\n' });
                if (spaceIndex >= 0)
                {
                    domain = domain.Substring(0, spaceIndex).Trim();
                }
            }
            else if (trimmed.Contains("synapse("))
            {
                var match = Regex.Match(trimmed, @"synapse\(([^)]+)\)");
                if (match.Success)
                {
                    synapses.Add(match.Groups[1].Value.Trim());
                }
            }
            else if (trimmed.Contains("neuron("))
            {
                var match = Regex.Match(trimmed, @"neuron\(([^)]+)\)");
                if (match.Success)
                {
                    var neuronVal = match.Groups[1].Value.Trim();
                    var bracketIndex = neuronVal.IndexOf('[');
                    if (bracketIndex >= 0)
                    {
                        neuronVal = neuronVal.Substring(0, bracketIndex).Trim();
                    }
                    neurons.Add(neuronVal);
                }
            }
        }

        if (string.IsNullOrEmpty(domain))
        {
            domain = Path.GetFileNameWithoutExtension(filePath);
        }

        return new SearchResult(filePath, domain, neurons.Distinct().ToList(), synapses.Distinct().ToList());
    }

    private static double CalculateScore(SearchResult parsed, string prompt, string[] searchWords)
    {
        double score = 0;

        if (parsed.Domain.Contains(prompt, StringComparison.OrdinalIgnoreCase))
        {
            score += 100;
        }

        foreach (var neuron in parsed.Neurons)
        {
            if (neuron.Contains(prompt, StringComparison.OrdinalIgnoreCase))
            {
                score += 50;
            }
        }
        foreach (var synapse in parsed.Synapses)
        {
            if (synapse.Contains(prompt, StringComparison.OrdinalIgnoreCase))
            {
                score += 50;
            }
        }

        foreach (var word in searchWords)
        {
            if (parsed.Domain.Contains(word, StringComparison.OrdinalIgnoreCase))
            {
                score += 10;
            }
            foreach (var neuron in parsed.Neurons)
            {
                if (neuron.Contains(word, StringComparison.OrdinalIgnoreCase))
                {
                    score += 5;
                }
            }
            foreach (var synapse in parsed.Synapses)
            {
                if (synapse.Contains(word, StringComparison.OrdinalIgnoreCase))
                {
                    score += 5;
                }
            }
        }

        return score;
    }
}
