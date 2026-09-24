using N_m3u8DL_RE.Common.Resource;

namespace N_m3u8DL_RE.Util;

public static class CultureUtil
{
    public static string GetCurrentCultureName()
    {
        string loc = ResString.CurrentLoc;
        string currLoc = Thread.CurrentThread.CurrentUICulture.Name;
        
        if (string.IsNullOrEmpty(currLoc))
            currLoc = GetCurrentCultureNameFromEnvironment();
        
        if (currLoc is "zh-CN" or "zh-SG")
            loc = "zh-CN";
        else if (currLoc.StartsWith("zh-"))
            loc = "zh-TW";
        
        return loc;
    }

    public static void ChangeCurrentCultureName(string newName)
    {
        try
        {
            System.Globalization.CultureInfo.DefaultThreadCurrentCulture = new System.Globalization.CultureInfo(newName);
            Thread.CurrentThread.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo(newName);
            Thread.CurrentThread.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo(newName);
        }
        catch (Exception)
        {
            // Culture not work on NT6.0, so catch the exception
        }
    }

    private static string GetCurrentCultureNameFromEnvironment()
    {
        // 尝试读取 LC_ALL, LANG
        string langEnv = Environment.GetEnvironmentVariable("LC_ALL") 
                         ?? Environment.GetEnvironmentVariable("LANG") 
                         ?? ResString.CurrentLoc;
        return langEnv.Split('.')[0].Replace('_', '-');
    }
}