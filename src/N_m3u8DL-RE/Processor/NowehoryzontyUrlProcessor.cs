using System.Security.Cryptography;
using System.Text;
using N_m3u8DL_RE.Common.Enum;
using N_m3u8DL_RE.Common.Log;
using N_m3u8DL_RE.Parser.Config;
using N_m3u8DL_RE.Parser.Processor;
using N_m3u8DL_RE.Parser.Util;

namespace N_m3u8DL_RE.Processor;

// "https://1429754964.rsc.cdn77.org/r/nh22/2022/VNUS_DE_NYKE/19_07_22_2302_skt/h264.mpd?secure=mSvVfvuciJt9wufUyzuBnA==,1658505709774" --urlprocessor-args "nowehoryzonty:timeDifference=-2274,filminfo.secureToken=vx54axqjal4f0yy2"
// "https://1244073762.rsc.cdn77.org/r/ps19/ZAPOWIEDZI/CHCE_SPAC_TAK_BY_SNIC__ZAPOWIEDZ/19_10_25_0348_cgg/vp9.mpd?secure=_Xt_Kr6uVhYdZB64LMH5nQ==,1763158705542" --urlprocessor-args "nowehoryzonty:timeDifference=4,filminfo.secureToken=CDt7YToMQiv6RAGc"

internal class NowehoryzontyUrlProcessor : UrlProcessor
{
    private const string ProcessorTag = "nowehoryzonty:";
    
    private static int _timeDifference;
    private static string _secureToken = null!;
    
    private static bool _log;
    
    public override bool CanProcess(ExtractorType extractorType, string oriUrl, ParserConfig parserConfig)
    {
        if (extractorType == ExtractorType.MPEG_DASH && parserConfig.UrlProcessorArgs != null && parserConfig.UrlProcessorArgs.StartsWith(ProcessorTag)) 
        {
            if (!_log)
            {
                Logger.WarnMarkUp("[white on green]www.nowehoryzonty.pl[/] matched!");
                _log = true;
            }

            var argLine = parserConfig.UrlProcessorArgs![ProcessorTag.Length..];
            
            _secureToken = ParserUtil.GetAttribute(argLine, "filminfo.secureToken")!;
            _timeDifference = Convert.ToInt32(ParserUtil.GetAttribute(argLine, "timeDifference")!);
            
            return true;
        }
        
        return false;
    }

    public override string Process(string oriUrl, ParserConfig parserConfig)
    {
        var path = new Uri(oriUrl).AbsolutePath;
        return oriUrl + "?secure=" + Calc(path);
    }

    private static string Calc(string path)
    {
        var msTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 60000 + _timeDifference;

        var hashPayload = msTime + path + _secureToken;
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(hashPayload));
        var hashText = Convert.ToBase64String(hash).Replace('+', '-').Replace('/', '_');
        
        return $"{hashText},{msTime}";
    }
}