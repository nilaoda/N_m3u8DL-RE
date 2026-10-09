using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using N_m3u8DL_RE.CommandLine;
using N_m3u8DL_RE.Common.Resource;
using N_m3u8DL_RE.Enum;
using Spectre.Console;

namespace N_m3u8DL_RE.Util;

internal sealed record DoctorTool(string Name, bool Required, bool Configured, string? Path,
    string Status, string? Version, int? ExitCode, string? Detail);
internal sealed record DoctorReport(string Application, string OS, string OSArchitecture,
    string ProcessArchitecture, string Runtime, bool DynamicCodeSupported, string Culture, string UILanguage,
    string? Executable, string WorkingDirectory, string? ConfigurationPath, bool ConfigurationEnabled, bool ConfigurationExists,
    DoctorTool[] Tools);

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(DoctorReport))]
internal partial class DoctorJsonContext : JsonSerializerContext
{
}

internal static class DoctorUtil
{
    internal const int DefaultTimeoutSeconds = 30;

    internal static async Task<DoctorReport> InspectAsync(string? ffmpeg, string? mkvmerge,
        string? decrypt, DecryptEngine engine, string? configurationPath, bool noConfig, CancellationToken token,
        Action<DoctorReport>? started = null, Action<DoctorTool>? completed = null, TimeSpan? timeout = null)
    {
        var config = noConfig ? null : configurationPath ?? ConfigFile.GetDefaultPath();
        if (!string.IsNullOrEmpty(config))
            config = Path.GetFullPath(config);
        var report = new DoctorReport(CommandInvoker.VERSION_INFO, RuntimeInformation.OSDescription,
            RuntimeInformation.OSArchitecture.ToString(), RuntimeInformation.ProcessArchitecture.ToString(),
            RuntimeInformation.FrameworkDescription, RuntimeFeature.IsDynamicCodeSupported,
            CultureInfo.CurrentCulture.Name, ResString.CurrentLoc, Environment.ProcessPath,
            Environment.CurrentDirectory, config, !noConfig, File.Exists(config), []);
        started?.Invoke(report);
        List<Task<DoctorTool>> probes =
        [
            ProbeAsync("ffmpeg", BinaryToolUtil.Resolve("ffmpeg", ffmpeg), ["-version"], true, ffmpeg != null, token, timeout),
            ProbeAsync("mkvmerge", BinaryToolUtil.Resolve("mkvmerge", mkvmerge), ["--version"], false, mkvmerge != null, token, timeout),
            ProbeAsync("mp4decrypt", BinaryToolUtil.Resolve("mp4decrypt", engine == DecryptEngine.MP4DECRYPT ? decrypt : null),
                [], false, engine == DecryptEngine.MP4DECRYPT && decrypt != null, token, timeout),
            ProbeAsync("shaka-packager", engine == DecryptEngine.SHAKA_PACKAGER && decrypt != null
                ? BinaryToolUtil.Resolve("shaka-packager", decrypt) : BinaryToolUtil.FindShakaPackager(),
                ["--version"], false, engine == DecryptEngine.SHAKA_PACKAGER && decrypt != null, token, timeout)
        ];
        // FFMPEG 解密同样允许单独指定二进制，与下载时的路径语义一致。
        if (engine == DecryptEngine.FFMPEG && decrypt != null)
            probes.Add(ProbeAsync("ffmpeg (decryption)", BinaryToolUtil.Resolve("ffmpeg", decrypt),
                ["-version"], false, true, token, timeout));
        var tools = new DoctorTool[probes.Count];
        var pending = probes.ToList();
        while (pending.Count > 0)
        {
            // 按完成顺序实时输出，JSON 报告仍按工具定义顺序排列。
            var probe = await Task.WhenAny(pending);
            pending.Remove(probe);
            var tool = await probe;
            tools[probes.IndexOf(probe)] = tool;
            completed?.Invoke(tool);
        }
        return report with { Tools = tools };
    }

    internal static async Task<DoctorTool> ProbeAsync(string name, string? path, string[] arguments,
        bool required, bool configured, CancellationToken token, TimeSpan? timeout = null)
    {
        if (path == null)
            return new(name, required, configured, null, "missing", null, null, ResString.doctorMissing);
        path = Path.GetFullPath(path);
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(token);
        var duration = timeout ?? TimeSpan.FromSeconds(DefaultTimeoutSeconds);
        // 首次启动可能需要系统检查动态库；0 取消时限，仍响应用户取消。
        if (duration != TimeSpan.Zero)
            limit.CancelAfter(duration);
        try
        {
            var result = await ProcessUtil.RunAsync(path, arguments, limit.Token);
            var lines = (result.Output + "\n" + result.Error).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            var version = lines.FirstOrDefault(line => line.Contains("version", StringComparison.OrdinalIgnoreCase)) ??
                lines.FirstOrDefault();
            // Bento4 无独立版本选项，无参数时输出版本和用法，退出码为 1。
            var bentoBanner = name == "mp4decrypt" && result.ExitCode == 1 &&
                lines.Any(line => line.Contains("MP4 Decrypter", StringComparison.OrdinalIgnoreCase)) &&
                lines.Any(line => line.Contains("version", StringComparison.OrdinalIgnoreCase));
            var banner = version?.Trim() ?? "";
            var recognized = name switch
            {
                "mp4decrypt" => banner.Contains("MP4 Decrypter", StringComparison.OrdinalIgnoreCase),
                "shaka-packager" => banner.Contains("packager", StringComparison.OrdinalIgnoreCase),
                "ffmpeg (decryption)" => banner.StartsWith("ffmpeg version", StringComparison.OrdinalIgnoreCase),
                "ffmpeg" => banner.StartsWith("ffmpeg version", StringComparison.OrdinalIgnoreCase),
                "mkvmerge" => banner.StartsWith("mkvmerge", StringComparison.OrdinalIgnoreCase),
                _ => false
            };
            var ok = (result.ExitCode == 0 || bentoBanner) && recognized;
            return new(name, required, configured, path, ok ? "ok" : "error", version?.Trim(), result.ExitCode,
                ok ? null : lines.Length == 0 ? ResString.doctorVersionUnknown : string.Join("\n", lines.Take(4)));
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            return new(name, required, configured, path, "timeout", null, null,
                string.Format(CultureInfo.CurrentCulture, ResString.doctorTimeout, duration.TotalSeconds));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception)
        {
            return new(name, required, configured, path, "error", null, null, ex.Message);
        }
    }

    internal static void Write(DoctorReport report, bool json)
    {
        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(report, DoctorJsonContext.Default.DoctorReport));
            return;
        }
        WriteEnvironment(report);
        foreach (var tool in report.Tools)
            WriteTool(tool);
    }

    internal static void WriteEnvironment(DoctorReport report, IAnsiConsole? console = null)
    {
        WriteLine(report.Application, "bold deepskyblue1", console);
        WriteField(ResString.doctorOS, report.OS, console);
        WriteField(ResString.doctorArchitecture, $"OS={report.OSArchitecture}, process={report.ProcessArchitecture}", console);
        WriteField(ResString.doctorRuntime, $"{report.Runtime}, dynamic code={report.DynamicCodeSupported}", console);
        WriteField(ResString.doctorCulture, $"{report.Culture}, UI={report.UILanguage}", console);
        WriteField(ResString.doctorExecutable, report.Executable ?? "", console);
        WriteField(ResString.doctorWorkingDirectory, report.WorkingDirectory, console);
        WriteField(ResString.doctorConfiguration, $"{report.ConfigurationPath} (enabled={report.ConfigurationEnabled}, exists={report.ConfigurationExists})", console);
        WriteLine(ResString.doctorSearchOrder, "grey", console);
    }

    internal static void WriteTool(DoctorTool tool, IAnsiConsole? console = null)
    {
        var requirement = tool.Required ? ResString.doctorRequired : ResString.doctorOptional;
        var color = tool.Status switch
        {
            "ok" => "green",
            "missing" when !tool.Required && !tool.Configured => "yellow",
            _ => "red1"
        };
        if (console == null)
            Console.WriteLine($"\n[{tool.Status}] {tool.Name} ({requirement})");
        else
            console.MarkupLine($"\n[bold {color}][[{tool.Status.EscapeMarkup()}]][/] [bold]{tool.Name.EscapeMarkup()}[/] [grey]({requirement.EscapeMarkup()})[/]");
        if (tool.Path != null)
            WriteField(ResString.doctorPath, tool.Path, console, "  ");
        if (tool.Version != null)
            WriteField(ResString.doctorVersion, tool.Version, console, "  ");
        if (tool.ExitCode != null)
            WriteField(ResString.doctorExitCode, tool.ExitCode.Value.ToString(CultureInfo.InvariantCulture), console, "  ");
        if (!string.IsNullOrEmpty(tool.Detail))
            WriteLine($"  {tool.Detail}", color, console);
    }

    private static void WriteField(string label, string value, IAnsiConsole? console, string indent = "")
    {
        if (console == null)
            Console.WriteLine($"{indent}{label}: {value}");
        else
            console.MarkupLine($"{indent}[deepskyblue1]{label.EscapeMarkup()}:[/] {value.EscapeMarkup()}");
    }

    private static void WriteLine(string text, string style, IAnsiConsole? console)
    {
        if (console == null)
            Console.WriteLine(text);
        else
            console.MarkupLine($"[{style}]{text.EscapeMarkup()}[/]");
    }
}
