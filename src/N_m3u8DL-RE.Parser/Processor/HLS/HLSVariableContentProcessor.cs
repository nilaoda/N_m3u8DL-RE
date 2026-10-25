using System.Text.RegularExpressions;
using N_m3u8DL_RE.Common.Enum;
using N_m3u8DL_RE.Parser.Config;
using N_m3u8DL_RE.Parser.Constants;
using N_m3u8DL_RE.Parser.Util;

namespace N_m3u8DL_RE.Parser.Processor.HLS;

// 实例属于一次 HLS 提取会话；当前清单 URL 与 master 变量不能放进共享的 ParserConfig。
internal sealed partial class HLSVariableContentProcessor(Func<string> playlistUrl) : ContentProcessor
{
    private Dictionary<string, string> masterVariables = new(StringComparer.Ordinal);

    internal bool IsMasterPlaylist { get; private set; }

    [GeneratedRegex("(?m)^" + HLSTags.ext_x_stream_inf + ":")]
    private static partial Regex MasterPlaylistRegex();

    public override bool CanProcess(ExtractorType extractorType, string rawText, ParserConfig parserConfig) =>
        extractorType == ExtractorType.HLS;

    public override string Process(string rawText, ParserConfig parserConfig)
    {
        IsMasterPlaylist = MasterPlaylistRegex().IsMatch(rawText);
        var content = HLSVariableSubstitution.Process(rawText, playlistUrl(), IsMasterPlaylist,
            masterVariables, out var variables);
        if (IsMasterPlaylist)
            masterVariables = variables;
        return content;
    }

    internal void Reset()
    {
        masterVariables.Clear();
        IsMasterPlaylist = false;
    }
}
