namespace N_m3u8DL_RE.Common.Resource;

internal class TextContainer
{
    public string ZH_CN { get; }
    public string ZH_TW { get; }
    public string EN_US { get; }

    public TextContainer(string zhCN, string zhTW, string enUS)
    {
        ZH_CN = zhCN;
        ZH_TW = zhTW;
        EN_US = enUS;
    }
}