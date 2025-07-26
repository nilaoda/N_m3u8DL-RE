namespace N_m3u8DL_RE.Common.Resource;

internal class TextContainer(string zhCN, string zhTW, string enUS)
{
    public string ZH_CN { get; } = zhCN;
    public string ZH_TW { get; } = zhTW;
    public string EN_US { get; } = enUS;
}