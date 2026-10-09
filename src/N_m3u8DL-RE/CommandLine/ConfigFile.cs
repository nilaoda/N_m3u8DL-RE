using System.CommandLine;
using System.CommandLine.Parsing;
using N_m3u8DL_RE.Common.Resource;

namespace N_m3u8DL_RE.CommandLine;

internal sealed record ConfigFile(string[] Arguments, string[] Defaults)
{
    internal static bool IsUtilityRequest(string[] args) =>
        ParseSyntax(args).CommandResult.Command is not RootCommand;

    internal static bool IsCompletionRequest(string[] args)
    {
        if (args.Length > 0 && (args[0] == "[suggest]" ||
            args[0].StartsWith("[suggest:", StringComparison.Ordinal) && args[0].EndsWith(']')))
            return true;
        return ParseSyntax(args).CommandResult.Children.OfType<OptionResult>()
            .Any(result => !result.Implicit && result.Option.Name == "--generate-completion");
    }

    internal static string GetDefaultPath() => GetDefaultPath(OperatingSystem.IsWindows(),
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile, Environment.SpecialFolderOption.DoNotVerify),
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.DoNotVerify),
        Environment.GetEnvironmentVariable("XDG_CONFIG_HOME"));

    internal static string GetDefaultPath(bool windows, string home, string appData, string? xdgConfigHome)
    {
        // Linux 和 macOS 共用家目录中的配置位置；XDG 规范只接受绝对路径。
        var directory = windows ? appData :
            !string.IsNullOrEmpty(xdgConfigHome) && Path.IsPathRooted(xdgConfigHome) ? xdgConfigHome :
            Path.Combine(home, ".config");
        // 容器中可能没有有效的用户家目录，此时不回退到当前工作目录。
        if (!Path.IsPathRooted(directory))
            return string.Empty;
        return Path.Combine(directory, "N_m3u8DL-RE", "config.conf");
    }

    internal static ConfigFile Load(string[] args, string? defaultPath = null)
    {
        // 补全只需要参数定义，不读取用户配置，也不展开待补全文本中的 @。
        if (IsCompletionRequest(args))
            return new(args, []);

        args = ExpandResponseFiles(args);
        if (IsCompletionRequest(args))
            return new(args, []);

        // 完整的选项定义才能区分 --save-name 的值与真正的配置控制选项。
        var result = ParseSyntax(args);
        var errors = result.Errors.Where(error => error.SymbolResult is OptionResult option &&
            (option.Option.Name == CommandInvoker.Config.Name || option.Option.Name == CommandInvoker.NoConfig.Name)).ToList();
        if (errors.Count > 0)
            throw new ArgumentException(string.Join(Environment.NewLine, errors.Select(error => error.Message)));

        var path = result.GetValue<string>(CommandInvoker.Config.Name);
        if (result.GetValue<bool>(CommandInvoker.NoConfig.Name))
        {
            if (path != null)
                throw new ArgumentException(ResString.configFileConflict);
            return new(args, []);
        }

        if (path == null)
        {
            path = defaultPath ?? GetDefaultPath();
            if (string.IsNullOrEmpty(path))
                return new(args, []);
            // 只有不存在才跳过；权限错误或配置路径是目录时应提示，不能静默丢弃配置。
            try
            {
                File.GetAttributes(path);
            }
            catch (FileNotFoundException)
            {
                return new(args, []);
            }
            catch (DirectoryNotFoundException)
            {
                return new(args, []);
            }
        }

        // 使用命令行库已有的响应文件语法，保持引号、注释和 UTF-8/BOM 的处理一致。
        return new(args, ExpandResponseFiles(["@" + Path.GetFullPath(path)]));
    }

    private static string[] ExpandResponseFiles(string[] args)
    {
        const int MaxDepth = 32;
        var activeFiles = new HashSet<string>(OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        List<string> expanded = [];
        var endOfOptions = false;

        void Expand(IReadOnlyList<string> tokens, int depth)
        {
            foreach (var token in tokens)
            {
                if (token == "--")
                    endOfOptions = true;
                if (endOfOptions || token.Length <= 1 || !token.StartsWith('@'))
                {
                    expanded.Add(token);
                    continue;
                }

                var path = Path.GetFullPath(token[1..]);
                // 命令行库的文件读取器会自行递归；只复用公开分词器，由此处控制引用深度。
                if (depth >= MaxDepth || !activeFiles.Add(path))
                    throw new ArgumentException($"{ResString.responseFileRecursion}: {path}");
                try
                {
                    var contents = File.ReadAllLines(path).Select(line => line.Trim())
                        .Where(line => line.Length > 0 && !line.StartsWith('#'))
                        .SelectMany(CommandLineParser.SplitCommandLine).ToArray();
                    Expand(contents, depth + 1);
                }
                finally
                {
                    activeFiles.Remove(path);
                }
            }
        }

        // 仅展开参数文件，不预解析下载地址或 directive，保留原始参数的含义。
        Expand(args, 0);
        return [.. expanded];
    }

    internal string? GetLanguage()
    {
        var explicitLanguage = ParseSyntax(Arguments).GetResult(CommandInvoker.UILanguage.Name) as OptionResult;
        var language = explicitLanguage is { Implicit: false } ? explicitLanguage :
            ParseSyntax(Defaults).GetResult(CommandInvoker.UILanguage.Name) as OptionResult;
        // 启动时只提取单个合法语言值；缺值或重复选项留给后续解析器报告，避免提前取值抛异常。
        var value = language is { Tokens.Count: 1 } ? language.Tokens[0].Value : null;
        return value is "en-US" or "zh-CN" or "zh-TW" ? value : null;
    }

    internal string[] Merge(RootCommand command)
    {
        if (Defaults.Length == 0)
            return Arguments;

        var configuration = new ParserConfiguration { EnablePosixBundling = false, ResponseFileTokenReplacer = null };
        var syntaxCommand = CreateSyntaxCommand(command);
        var suppliedResult = syntaxCommand.Parse(Arguments, configuration);
        if (suppliedResult.CommandResult.Command is not RootCommand)
            return MergeUtility(command, suppliedResult, configuration);
        var supplied = suppliedResult.CommandResult.Children.OfType<OptionResult>()
            .Where(result => !result.Implicit).Select(result => result.Option.Name).ToHashSet();
        // 多值选项按原有解析规则合并：配置在前，命令行在后；只替换单值选项。
        supplied.ExceptWith(command.Options.Where(option => option.Arity.MaximumNumberOfValues > 1)
            .Select(option => option.Name));
        // 两种写法描述同一个合并模式，配置中的旧参数不能压过命令行的新参数。
        // 旧参数为 false 时只取消强制 demuxer，不抹掉配置中显式选择的其他模式。
        var concatMode = CommandInvoker.FFmpegConcatMode.Name;
        var concatDemuxer = CommandInvoker.UseFFmpegConcatDemuxer.Name;
        if (supplied.Contains(concatDemuxer) && suppliedResult.GetValue<bool>(concatDemuxer))
            supplied.Add(concatMode);
        if (supplied.Contains(concatMode))
            supplied.Add(concatDemuxer);
        // 占住唯一的位置参数，配置中再出现下载地址或裸值时由原解析器报错。
        var defaults = syntaxCommand.Parse(["<config-input>", .. Defaults], configuration);
        if (defaults.CommandResult.Command is not RootCommand)
            throw new ArgumentException(ResString.configFileOptionsOnly);
        var syntaxErrors = defaults.Errors.Where(error => error.SymbolResult is not OptionResult optionResult ||
            !supplied.Contains(optionResult.Option.Name)).ToList();
        if (syntaxErrors.Count > 0)
            throw new ArgumentException(string.Join(Environment.NewLine, syntaxErrors.Select(error => error.Message)));

        var configuredOptions = defaults.CommandResult.Children.OfType<OptionResult>()
            .Where(result => !result.Implicit).ToList();
        var options = command.Options.ToDictionary(option => option.Name);
        if (configuredOptions.Any(result => options[result.Option.Name].Action != null ||
                IsConfigurationControlOption(result.Option)) ||
            defaults.Tokens.Any(token => token.Type is TokenType.DoubleDash or TokenType.Directive))
            throw new ArgumentException(ResString.configFileOptionsOnly);

        var names = command.Options.SelectMany(option => option.Aliases.Prepend(option.Name)
            .Select(name => (Name: name, Option: option))).ToDictionary(pair => pair.Name, pair => pair.Option);
        List<string> merged = [];
        var keep = true;
        var previousWasOption = false;
        // 依照解析器标注的 token 类型划分参数，不把参数值中的 --xxx 误当成选项。
        // 可重复选项保留原有顺序，header 的同名覆盖由现有请求头解析器处理。
        foreach (var token in defaults.Tokens.Skip(1))
        {
            if (token.Type == TokenType.Option)
            {
                keep = !supplied.Contains(names[token.Value].Name);
                if (keep)
                    merged.Add(token.Value);
                previousWasOption = true;
            }
            else
            {
                if (keep)
                {
                    // 首个值与选项连写，避免值为 -- 时在最终解析中变成结束标记。
                    if (previousWasOption)
                        merged[^1] += "=" + token.Value;
                    else
                        merged.Add(token.Value);
                }
                previousWasOption = false;
            }
        }
        // 被命令行覆盖的值不做类型转换或文件读取；只校验最终保留的配置值。
        var validationCommand = new RootCommand();
        validationCommand.Directives.Clear();
        validationCommand.Options.Clear();
        foreach (var option in command.Options)
            validationCommand.Options.Add(option);
        validationCommand.Arguments.Add(new Argument<string>("config-input"));
        var validated = validationCommand.Parse(["<config-input>", .. merged], configuration);
        if (validated.Errors.Count > 0)
            throw new ArgumentException(string.Join(Environment.NewLine, validated.Errors.Select(error => error.Message)));
        return [.. merged, .. Arguments];
    }

    private static RootCommand CreateSyntaxCommand(RootCommand command)
    {
        var syntax = new RootCommand();
        syntax.SetAction(_ => { });
        // 此阶段只识别选项边界和别名，避免自定义解析器读取随后会被覆盖的文件。
        syntax.Directives.Clear();
        syntax.Options.Clear();
        syntax.Arguments.Add(new Argument<string>("config-input"));
        foreach (var option in command.Options)
        {
            Option copy = option.ValueType == typeof(bool)
                ? new Option<bool>(option.Name, [.. option.Aliases])
                : option.Arity.MaximumNumberOfValues <= 1
                    ? new Option<string>(option.Name, [.. option.Aliases])
                    : new Option<string[]>(option.Name, [.. option.Aliases]);
            copy.Arity = option.Arity;
            copy.AllowMultipleArgumentsPerToken = option.AllowMultipleArgumentsPerToken;
            copy.Recursive = option.Recursive;
            syntax.Options.Add(copy);
        }
        foreach (var child in command.Subcommands)
            syntax.Subcommands.Add(CopySyntaxCommand(child));
        return syntax;
    }

    private static Command CopySyntaxCommand(Command command)
    {
        var copy = new Command(command.Name, command.Description);
        foreach (var option in command.Options)
        {
            Option syntaxOption = option.ValueType == typeof(bool)
                ? new Option<bool>(option.Name, [.. option.Aliases])
                : option.Arity.MaximumNumberOfValues <= 1
                    ? new Option<string>(option.Name, [.. option.Aliases])
                    : new Option<string[]>(option.Name, [.. option.Aliases]);
            syntaxOption.Arity = option.Arity;
            syntaxOption.Recursive = option.Recursive;
            syntaxOption.AllowMultipleArgumentsPerToken = option.AllowMultipleArgumentsPerToken;
            copy.Options.Add(syntaxOption);
        }
        foreach (var child in command.Subcommands)
            copy.Subcommands.Add(CopySyntaxCommand(child));
        return copy;
    }

    private string[] MergeUtility(RootCommand root, ParseResult supplied, ParserConfiguration configuration)
    {
        // 配置仍按下载选项解析边界，仅将当前工具支持的选项带入子命令。
        // 不转换被忽略的下载参数，避免读取 key、cookie 等文件。
        var defaults = CreateSyntaxCommand(root).Parse(["<config-input>", .. Defaults], configuration);
        if (defaults.CommandResult.Command is not RootCommand)
            throw new ArgumentException(ResString.configFileOptionsOnly);
        if (defaults.Errors.Count > 0)
            throw new ArgumentException(string.Join(Environment.NewLine, defaults.Errors.Select(e => e.Message)));
        var configured = defaults.CommandResult.Children.OfType<OptionResult>().Where(r => !r.Implicit).ToList();
        var rootOptions = root.Options.ToDictionary(o => o.Name);
        if (configured.Any(r => rootOptions[r.Option.Name].Action != null ||
                IsConfigurationControlOption(r.Option)) ||
            defaults.Tokens.Any(t => t.Type is TokenType.DoubleDash or TokenType.Directive))
            throw new ArgumentException(ResString.configFileOptionsOnly);

        var path = new List<string>();
        var explicitOptions = new HashSet<string>();
        for (CommandResult? current = supplied.CommandResult; current != null; current = current.Parent as CommandResult)
        {
            if (current.Command is not RootCommand)
                path.Insert(0, current.Command.Name);
            explicitOptions.UnionWith(current.Children.OfType<OptionResult>().Where(r => !r.Implicit).Select(r => r.Option.Name));
        }
        Command selected = root;
        foreach (var name in path)
            selected = selected.Subcommands.Single(c => c.Name == name);
        var allowed = selected.Options.Concat(root.Options.Where(o => o.Recursive)).Select(o => o.Name).ToHashSet();
        var names = root.Options.SelectMany(o => o.Aliases.Prepend(o.Name).Select(n => (Name: n, Option: o)))
            .ToDictionary(p => p.Name, p => p.Option);
        List<string> retained = [];
        var keep = false;
        var previousWasOption = false;
        foreach (var token in defaults.Tokens.Skip(1))
        {
            if (token.Type == TokenType.Option)
            {
                var name = names[token.Value].Name;
                keep = allowed.Contains(name) && !explicitOptions.Contains(name);
                if (keep)
                    retained.Add(token.Value);
                previousWasOption = true;
            }
            else
            {
                if (keep)
                {
                    if (previousWasOption)
                        retained[^1] += "=" + token.Value;
                    else
                        retained.Add(token.Value);
                }
                previousWasOption = false;
            }
        }
        // 前置参数继承只涉及全局选项，子命令参数放在结束标记 -- 之前。
        var insertion = Array.IndexOf(Arguments, "--");
        if (insertion < 0)
            insertion = Arguments.Length;
        return [.. Arguments.Take(insertion), .. retained, .. Arguments.Skip(insertion)];
    }

    private static bool IsConfigurationControlOption(Option option) =>
        option.Name == CommandInvoker.Config.Name || option.Name == CommandInvoker.NoConfig.Name ||
        option.Name == CommandInvoker.MoreHelp.Name;

    private static ParseResult ParseSyntax(string[] args) =>
        CreateSyntaxCommand(CommandInvoker.CreateRootCommand()).Parse(args, new ParserConfiguration
        {
            EnablePosixBundling = false,
            ResponseFileTokenReplacer = null
        });
}
