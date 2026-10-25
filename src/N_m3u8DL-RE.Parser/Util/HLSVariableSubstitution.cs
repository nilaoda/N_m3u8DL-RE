using System.Text;
using System.Text.RegularExpressions;
using N_m3u8DL_RE.Common.Resource;
using N_m3u8DL_RE.Parser.Constants;

namespace N_m3u8DL_RE.Parser.Util;

internal static partial class HLSVariableSubstitution
{
    [GeneratedRegex(@"\{\$([a-zA-Z0-9_-]+)\}")]
    private static partial Regex ReferenceRegex();
    [GeneratedRegex("(?<=[,:])([A-Z0-9-]+)=(\"[^\"]*\"|[^,]*)")]
    private static partial Regex AttributeRegex();
    [GeneratedRegex(@"\A[a-zA-Z0-9_-]+\z")]
    private static partial Regex NameRegex();

    internal static string Process(string content, string url, bool isMaster,
        IReadOnlyDictionary<string, string> masterVariables, out Dictionary<string, string> variables)
    {
        // 每次加载都从新作用域开始；媒体清单只能通过 IMPORT 使用主清单中的定义。
        var current = new Dictionary<string, string>(StringComparer.Ordinal);
        variables = current;
        if (!content.Contains(HLSTags.ext_x_define) && !ReferenceRegex().IsMatch(content))
            return content;

        FormatException Invalid(string name) => new($"{ResString.hlsInvalidVariable}: {name} ({url})");
        string Replace(string value) => ReferenceRegex().Replace(value, match =>
            current.TryGetValue(match.Groups[1].Value, out var replacement) ? replacement
                : throw new FormatException($"{ResString.hlsUndefinedVariable}: {match.Groups[1].Value} ({url})"));

        var result = new StringBuilder();
        using var reader = new StringReader(content);
        while (reader.ReadLine() is { } line)
        {
            if (line.StartsWith(HLSTags.ext_x_define + ":", StringComparison.Ordinal))
            {
                var attributes = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (Match attribute in AttributeRegex().Matches(line))
                {
                    var value = attribute.Groups[2].Value;
                    if (value.Length < 2 || value[0] != '"' || value[^1] != '"' ||
                        !attributes.TryAdd(attribute.Groups[1].Value, value[1..^1]))
                        throw Invalid(attribute.Groups[1].Value);
                }
                var kinds = new[] { "NAME", "IMPORT", "QUERYPARAM" }.Where(attributes.ContainsKey).ToArray();
                if (kinds.Length != 1)
                    throw Invalid("EXT-X-DEFINE");
                var kind = kinds[0];
                var name = attributes[kind];
                if (!NameRegex().IsMatch(name) || current.ContainsKey(name) ||
                    (kind != "NAME" && attributes.ContainsKey("VALUE")))
                    throw Invalid(name);

                string? valueText;
                if (kind == "NAME")
                {
                    if (!attributes.TryGetValue("VALUE", out valueText))
                        throw Invalid(name);
                    valueText = Replace(valueText);
                }
                else if (kind == "IMPORT")
                {
                    if (isMaster || !masterVariables.TryGetValue(name, out valueText))
                        throw Invalid(name);
                }
                else
                {
                    valueText = null;
                    if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
                    {
                        foreach (var parameter in uri.Query.TrimStart('?').Split('&'))
                        {
                            var pair = parameter.Split('=', 2);
                            if (pair.Length != 2 || Uri.UnescapeDataString(pair[0]) != name)
                                continue;
                            valueText = Uri.UnescapeDataString(pair[1]);
                            break;
                        }
                    }
                    if (valueText == null)
                        throw Invalid(name);
                }
                if (valueText.IndexOfAny(['\r', '\n', '"']) >= 0)
                    throw Invalid(name);
                current.Add(name, valueText);
                continue;
            }

            if (!line.StartsWith('#'))
                line = Replace(line);
            else if (line.StartsWith("#EXT-X-", StringComparison.Ordinal))
            {
                // 只展开带引号的属性和十六进制属性，普通注释、标题等文本保持原样。
                line = AttributeRegex().Replace(line, match =>
                {
                    var value = match.Groups[2].Value;
                    return value.StartsWith('"') || value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ||
                        value.StartsWith("{$", StringComparison.Ordinal)
                        ? match.Groups[1].Value + "=" + Replace(value) : match.Value;
                });
            }
            result.AppendLine(line);
        }
        return result.ToString();
    }
}
