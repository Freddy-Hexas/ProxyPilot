using System.Text;

namespace ProcessProxyManager.Core;

public sealed class MihomoRuleGenerator
{
    public IReadOnlyList<string> GenerateRuleLines(
        IEnumerable<ProcessRule> rules,
        IEnumerable<string>? processDirectNames = null)
    {
        var directProcessNames = (processDirectNames ?? Enumerable.Empty<string>())
            .Where(static processName => !string.IsNullOrWhiteSpace(processName))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static processName => processName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var lines = directProcessNames
            .Select(static processName => $"PROCESS-NAME,{EscapeRuleValue(processName)},DIRECT")
            .ToList();

        lines.AddRange(rules
            .Where(static rule => rule.Action is ProxyAction.PROXY or ProxyAction.DIRECT or ProxyAction.REJECT)
            .Where(rule => !directProcessNames.Contains(rule.ProcessName, StringComparer.OrdinalIgnoreCase))
            .OrderBy(static rule => rule.ProcessName, StringComparer.OrdinalIgnoreCase)
            .Select(static rule => $"PROCESS-NAME,{EscapeRuleValue(rule.ProcessName)},{rule.Action}"));

        lines.Add("MATCH,DIRECT");
        return lines;
    }

    public string GenerateRulesYaml(
        IEnumerable<ProcessRule> rules,
        IEnumerable<string>? processDirectNames = null)
    {
        var builder = new StringBuilder();
        builder.AppendLine("rules:");

        foreach (var line in GenerateRuleLines(rules, processDirectNames))
        {
            builder.AppendLine($"  - {line}");
        }
        return builder.ToString();
    }

    public async Task WriteRulesYamlAsync(
        IEnumerable<ProcessRule> rules,
        string filePath,
        IEnumerable<string>? processDirectNames = null,
        CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(filePath, GenerateRulesYaml(rules, processDirectNames), cancellationToken);
    }

    private static string EscapeRuleValue(string value)
    {
        return value.Replace(",", "\\,", StringComparison.Ordinal);
    }
}
