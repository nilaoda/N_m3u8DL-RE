using System.CommandLine;
using System.CommandLine.Help;
using System.CommandLine.Invocation;

namespace N_m3u8DL_RE.CommandLine;

internal sealed class UtilityHelpAction : SynchronousCommandLineAction
{
    private readonly Argument _input;
    private readonly HelpAction _help = new();

    internal UtilityHelpAction(Argument input)
    {
        _input = input;
    }

    public override bool ClearsParseErrors => true;

    public override int Invoke(ParseResult parseResult)
    {
        // 下载位置参数不会传给工具命令，也不应出现在其用法中。
        var hidden = _input.Hidden;
        try
        {
            if (parseResult.CommandResult.Command is not RootCommand)
                _input.Hidden = true;
            return _help.Invoke(parseResult);
        }
        finally
        {
            _input.Hidden = hidden;
        }
    }
}
