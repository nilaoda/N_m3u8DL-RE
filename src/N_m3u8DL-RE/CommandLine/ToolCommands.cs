using System.ComponentModel;
using System.CommandLine;
using System.CommandLine.Parsing;
using N_m3u8DL_RE.Common.Log;
using N_m3u8DL_RE.Common.Resource;
using N_m3u8DL_RE.Entity;
using N_m3u8DL_RE.Enum;
using N_m3u8DL_RE.Util;
using Spectre.Console;

namespace N_m3u8DL_RE.CommandLine;

internal static class ToolCommands
{
    internal static void AddTo(RootCommand root)
    {
        root.Subcommands.Add(CreateFileCommand("concat", ResString.cmd_toolsConcat));
        root.Subcommands.Add(CreateFileCommand("merge", ResString.cmd_toolsMerge));
        root.Subcommands.Add(CreateFileCommand("mux", ResString.cmd_toolsMux));

        var doctor = new Command("doctor", ResString.cmd_doctor);
        var json = new Option<bool>("--json") { Description = ResString.cmd_doctorJson };
        var timeout = new Option<int>("--tool-timeout")
        {
            HelpName = "SEC", Description = ResString.cmd_doctorToolTimeout,
            DefaultValueFactory = _ => DoctorUtil.DefaultTimeoutSeconds,
            Arity = ArgumentArity.ExactlyOne, CustomParser = ParseToolTimeout
        };
        var forceAnsi = CommandInvoker.ForceAnsiConsole;
        var noAnsiColor = CommandInvoker.NoAnsiColor;
        var ffmpeg = CommandInvoker.FFmpegBinaryPath;
        var mkvmerge = CreateMkvmergeOption();
        var decrypt = CommandInvoker.DecryptionBinaryPath;
        var engine = CommandInvoker.DecryptionEngine;
        foreach (var option in new Option[] { json, timeout, forceAnsi, noAnsiColor, ffmpeg, mkvmerge, decrypt, engine })
            doctor.Options.Add(option);
        doctor.SetAction(async (result, token) =>
        {
            var asJson = result.GetValue(json);
            // JSON 保持纯文本；文本报告复用现有终端库，重定向时关闭颜色。
            var noColor = result.GetValue(noAnsiColor) || Console.IsOutputRedirected ||
                !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR"));
            var console = asJson || noColor ? null : AnsiConsole.Create(new AnsiConsoleSettings
            {
                Ansi = result.GetValue(forceAnsi) ? AnsiSupport.Yes : AnsiSupport.Detect,
                Interactive = InteractionSupport.No,
                Out = new AnsiConsoleOutput(Console.Out)
            });
            var report = await DoctorUtil.InspectAsync(result.GetValue(ffmpeg),
                result.GetValue(mkvmerge), result.GetValue(decrypt), result.GetValue(engine),
                result.GetValue(CommandInvoker.Config), result.GetValue(CommandInvoker.NoConfig), token,
                environment =>
                {
                    if (asJson)
                        Console.Error.WriteLine(ResString.doctorChecking);
                    else
                    {
                        DoctorUtil.WriteEnvironment(environment, console);
                        if (console == null)
                            Console.WriteLine(ResString.doctorChecking);
                        else
                            console.MarkupLine($"[grey]{ResString.doctorChecking.EscapeMarkup()}[/]");
                    }
                },
                tool =>
                {
                    if (asJson)
                        Console.Error.WriteLine($"[{tool.Status}] {tool.Name}");
                    else
                        DoctorUtil.WriteTool(tool, console);
                }, timeout: TimeSpan.FromSeconds(result.GetValue(timeout)));
            if (asJson)
                DoctorUtil.Write(report, true);
            return report.Tools.Any(t => (t.Required || t.Configured) && t.Status != "ok") ? 1 : 0;
        });
        root.Subcommands.Add(doctor);
    }

    private static Option<string?> CreateMkvmergeOption() =>
        new("--mkvmerge-binary-path") { HelpName = "PATH", Description = ResString.cmd_mkvmergeBinaryPath, Arity = ArgumentArity.ExactlyOne };

    private static int ParseToolTimeout(ArgumentResult result)
    {
        // CancelAfter 的最大时限为 uint.MaxValue - 1 毫秒。
        if (result.Tokens.Count == 1 && int.TryParse(result.Tokens[0].Value, out var seconds) &&
            seconds >= 0 && seconds <= (uint.MaxValue - 1) / 1000)
            return seconds;
        result.AddError(ResString.doctorToolTimeoutInvalid);
        return 0;
    }

    private static Command CreateFileCommand(string name, string description)
    {
        var command = new Command(name, description);
        var input = new Option<string[]>("-i", "--input")
        {
            HelpName = "FILE", Description = ResString.cmd_toolsInput,
            Arity = ArgumentArity.OneOrMore, AllowMultipleArgumentsPerToken = false
        };
        var output = new Option<string>("-o", "--output")
        {
            HelpName = "FILE", Description = ResString.cmd_toolsOutput,
            Required = true, Arity = ArgumentArity.ExactlyOne
        };
        var overwrite = new Option<bool>("--overwrite") { Description = ResString.cmd_toolsOverwrite };
        var dryRun = new Option<bool>("--dry-run") { Description = ResString.cmd_toolsDryRun };
        var forceAnsi = CommandInvoker.ForceAnsiConsole;
        var noAnsiColor = CommandInvoker.NoAnsiColor;
        var directory = new Option<string?>("--input-dir") { Description = ResString.cmd_toolsInputDir };
        var pattern = new Option<string>("--pattern") { Description = ResString.cmd_toolsPattern, DefaultValueFactory = _ => "*.ts" };
        var ffmpeg = CommandInvoker.FFmpegBinaryPath;
        var autoSubtitleFix = CommandInvoker.CreateAutoSubtitleFixOption();
        autoSubtitleFix.Description = ResString.cmd_toolsAutoSubtitleFix;
        var mkvmerge = CreateMkvmergeOption();
        var muxer = new Option<string>("--muxer") { Description = ResString.cmd_toolsMuxer, DefaultValueFactory = _ => "ffmpeg" };
        muxer.AcceptOnlyFromAmong("ffmpeg", "mkvmerge");
        var imports = CommandInvoker.CreateMuxImportsOption();
        imports.Description = ResString.cmd_toolsMuxImport;
        var title = new Option<string?>("--title") { Description = ResString.cmd_toolsTitle };
        var mode = CommandInvoker.FFmpegConcatMode;
        foreach (var option in new Option[] { input, output, overwrite, dryRun, forceAnsi, noAnsiColor })
            command.Options.Add(option);
        if (name != "mux")
        {
            command.Options.Add(directory);
            command.Options.Add(pattern);
        }
        if (name != "concat")
            command.Options.Add(ffmpeg);
        if (name == "merge")
            command.Options.Add(mode);
        if (name == "mux")
        {
            input.HelpName = "FILE|OPTIONS";
            input.Description = ResString.cmd_toolsMuxInput;
            command.Options.Add(muxer);
            command.Options.Add(mkvmerge);
            command.Options.Add(autoSubtitleFix);
            command.Options.Add(imports);
            command.Options.Add(title);
        }
        command.SetAction(async (result, token) =>
        {
            var originalConsole = CustomAnsiConsole.Console;
            var originalLevel = Logger.LogLevel;
            var originalWriteFile = Logger.IsWriteFile;
            try
            {
                // 工具状态写到 stderr，stdout 保留结果路径；复用主界面的彩色日志。
                var noColor = result.GetValue(noAnsiColor) || Console.IsOutputRedirected || Console.IsErrorRedirected ||
                    !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR"));
                // 纯文本仍使用 ANSI 后端，再由 NonAnsiWriter 剥离控制码，与主界面一致。
                CustomAnsiConsole.Console = AnsiConsole.Create(new AnsiConsoleSettings
                {
                    Ansi = noColor || result.GetValue(forceAnsi) ? AnsiSupport.Yes : AnsiSupport.Detect,
                    Interactive = InteractionSupport.No,
                    Out = new AnsiConsoleOutput(noColor ? new NonAnsiWriter(Console.Error) : Console.Error)
                });
                CustomAnsiConsole.Console.Profile.Width = int.MaxValue;
                Logger.LogLevel = LogLevel.INFO;
                Logger.IsWriteFile = false;
                Logger.Info(CommandInvoker.VERSION_INFO);
                var inputs = result.GetValue(input) ?? [];
                var files = name == "mux" ? inputs.Select(CommandInvoker.ParseMuxInput)
                    .Concat(result.GetValue(imports) ?? []).ToArray() : null;
                return await FileToolUtil.ExecuteAsync(name, files?.Select(file => file.FilePath).ToArray() ?? inputs, result.GetValue(output)!,
                    name == "mux" ? null : result.GetValue(directory), name == "mux" ? "*.ts" : result.GetValue(pattern)!,
                    result.GetValue(overwrite), result.GetValue(dryRun),
                    name == "concat" ? null : result.GetValue(ffmpeg), name == "mux" ? result.GetValue(mkvmerge) : null,
                    name == "mux" ? result.GetValue(muxer)! : "ffmpeg",
                    name == "merge" ? result.GetValue(mode) : FFmpegConcatMode.LOCAL_HTTP,
                    name == "mux" && result.GetValue(autoSubtitleFix), token, files,
                    name == "mux" ? result.GetValue(title) : null);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or
                Win32Exception or OperationCanceledException or FormatException or OverflowException)
            {
                Logger.Error(ex.Message);
                return 1;
            }
            finally
            {
                CustomAnsiConsole.Console = originalConsole;
                Logger.LogLevel = originalLevel;
                Logger.IsWriteFile = originalWriteFile;
            }
        });
        return command;
    }
}
