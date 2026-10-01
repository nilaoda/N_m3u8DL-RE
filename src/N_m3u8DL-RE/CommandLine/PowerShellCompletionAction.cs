using System.CommandLine;
using System.CommandLine.Invocation;
using N_m3u8DL_RE.Common.Resource;

namespace N_m3u8DL_RE.CommandLine;

internal sealed class PowerShellCompletionAction : SynchronousCommandLineAction
{
    // 和 --help 一样独立执行，不要求提供下载地址；SHELL 参数在操作中单独校验。
    public override bool ClearsParseErrors => true;

    public override int Invoke(ParseResult parseResult)
    {
        // 当前命令行库会清除终止操作的解析错误，不能因此接受缺失或不支持的 SHELL。
        var optionResult = parseResult.GetResult("--generate-completion")!;
        if (optionResult.Tokens.Count != 1 || optionResult.Tokens[0].Value != "powershell")
        {
            Console.Error.WriteLine(ResString.completionShellInvalid);
            return 1;
        }

        // 脚本作为资源编译进可执行文件，Native AOT 发布也无需携带独立的 .ps1 文件。
        using var stream = typeof(PowerShellCompletionAction).Assembly
            .GetManifestResourceStream("N_m3u8DL_RE.CommandLine.PowerShellCompletion.ps1")!;
        using var reader = new StreamReader(stream);
        Console.Write(reader.ReadToEnd());
        return 0;
    }
}
