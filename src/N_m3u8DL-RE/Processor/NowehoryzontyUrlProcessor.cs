using N_m3u8DL_RE.Common.Enum;
using N_m3u8DL_RE.Common.Log;
using N_m3u8DL_RE.Parser.Config;
using N_m3u8DL_RE.Parser.Processor;
using N_m3u8DL_RE.Parser.Util;
using NiL.JS.BaseLibrary;
using NiL.JS.Core;
using NiL.JS.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace N_m3u8DL_RE.Processor
{
    //"https://1429754964.rsc.cdn77.org/r/nh22/2022/VNUS_DE_NYKE/19_07_22_2302_skt/h264.mpd?secure=mSvVfvuciJt9wufUyzuBnA==,1658505709774" --urlprocessor-args "nowehoryzonty:timeDifference=-2274,filminfo.secureToken=vx54axqjal4f0yy2"
    internal class NowehoryzontyUrlProcessor : UrlProcessor
    {
        private static string START = "nowehoryzonty:";
        private static string? TimeDifferenceStr = null;
        private static int? TimeDifference = null;
        private static string? SecureToken = null;
        private static bool LOG = false;
        private static Function? Function = null;
        public override bool CanProcess(ExtractorType extractorType, string oriUrl, ParserConfig parserConfig)
        {
            if (extractorType == ExtractorType.MPEG_DASH && parserConfig.UrlProcessorArgs != null && parserConfig.UrlProcessorArgs.StartsWith(START)) 
            {
                if (!LOG)
                {
                    Logger.WarnMarkUp($"[white on green]www.nowehoryzonty.pl[/] matched! waiting for calc...");
                    LOG = true;
                }
                var context = new Context();
                context.Eval(JS);
                Function = context.GetVariable("md5").As<Function>();
                var argLine = parserConfig.UrlProcessorArgs![START.Length..];
                TimeDifferenceStr = ParserUtil.GetAttribute(argLine, "timeDifference");
                SecureToken = ParserUtil.GetAttribute(argLine, "filminfo.secureToken");
                if (TimeDifferenceStr != null && SecureToken != null)
                {
                    TimeDifference = Convert.ToInt32(TimeDifferenceStr);
                }
                return true;
            }
            return false;
        }

        public override string Process(string oriUrl, ParserConfig parserConfig)
        {
            var a = new Uri(oriUrl).AbsolutePath;
            var n = oriUrl + "?secure=" + Calc(a);
            return n;
        }

        private static string Calc(string a)
        {
            string returnStr = Function!.Call(new Arguments { a, SecureToken, TimeDifference }).ToString();
            return returnStr;
        }

        ////https://www.nowehoryzonty.pl/packed/videonho.js?v=1114377281:formatted
        private static readonly string JS = """
            var p = function(f, e) {
                var d = f[0]
                  , a = f[1]
                  , b = f[2]
                  , c = f[3];
                d = h(d, a, b, c, e[0], 7, -680876936);
                c = h(c, d, a, b, e[1], 12, -389564586);
                b = h(b, c, d, a, e[2], 17, 606105819);
                a = h(a, b, c, d, e[3], 22, -1044525330);
                d = h(d, a, b, c, e[4], 7, -176418897);
                c = h(c, d, a, b, e[5], 12, 1200080426);
                b = h(b, c, d, a, e[6], 17, -1473231341);
                a = h(a, b, c, d, e[7], 22, -45705983);
                d = h(d, a, b, c, e[8], 7, 1770035416);
                c = h(c, d, a, b, e[9], 12, -1958414417);
                b = h(b, c, d, a, e[10], 17, -42063);
                a = h(a, b, c, d, e[11], 22, -1990404162);
                d = h(d, a, b, c, e[12], 7, 1804603682);
                c = h(c, d, a, b, e[13], 12, -40341101);
                b = h(b, c, d, a, e[14], 17, -1502002290);
                a = h(a, b, c, d, e[15], 22, 1236535329);
                d = k(d, a, b, c, e[1], 5, -165796510);
                c = k(c, d, a, b, e[6], 9, -1069501632);
                b = k(b, c, d, a, e[11], 14, 643717713);
                a = k(a, b, c, d, e[0], 20, -373897302);
                d = k(d, a, b, c, e[5], 5, -701558691);
                c = k(c, d, a, b, e[10], 9, 38016083);
                b = k(b, c, d, a, e[15], 14, -660478335);
                a = k(a, b, c, d, e[4], 20, -405537848);
                d = k(d, a, b, c, e[9], 5, 568446438);
                c = k(c, d, a, b, e[14], 9, -1019803690);
                b = k(b, c, d, a, e[3], 14, -187363961);
                a = k(a, b, c, d, e[8], 20, 1163531501);
                d = k(d, a, b, c, e[13], 5, -1444681467);
                c = k(c, d, a, b, e[2], 9, -51403784);
                b = k(b, c, d, a, e[7], 14, 1735328473);
                a = k(a, b, c, d, e[12], 20, -1926607734);
                d = g(a ^ b ^ c, d, a, e[5], 4, -378558);
                c = g(d ^ a ^ b, c, d, e[8], 11, -2022574463);
                b = g(c ^ d ^ a, b, c, e[11], 16, 1839030562);
                a = g(b ^ c ^ d, a, b, e[14], 23, -35309556);
                d = g(a ^ b ^ c, d, a, e[1], 4, -1530992060);
                c = g(d ^ a ^ b, c, d, e[4], 11, 1272893353);
                b = g(c ^ d ^ a, b, c, e[7], 16, -155497632);
                a = g(b ^ c ^ d, a, b, e[10], 23, -1094730640);
                d = g(a ^ b ^ c, d, a, e[13], 4, 681279174);
                c = g(d ^ a ^ b, c, d, e[0], 11, -358537222);
                b = g(c ^ d ^ a, b, c, e[3], 16, -722521979);
                a = g(b ^ c ^ d, a, b, e[6], 23, 76029189);
                d = g(a ^ b ^ c, d, a, e[9], 4, -640364487);
                c = g(d ^ a ^ b, c, d, e[12], 11, -421815835);
                b = g(c ^ d ^ a, b, c, e[15], 16, 530742520);
                a = g(b ^ c ^ d, a, b, e[2], 23, -995338651);
                d = l(d, a, b, c, e[0], 6, -198630844);
                c = l(c, d, a, b, e[7], 10, 1126891415);
                b = l(b, c, d, a, e[14], 15, -1416354905);
                a = l(a, b, c, d, e[5], 21, -57434055);
                d = l(d, a, b, c, e[12], 6, 1700485571);
                c = l(c, d, a, b, e[3], 10, -1894986606);
                b = l(b, c, d, a, e[10], 15, -1051523);
                a = l(a, b, c, d, e[1], 21, -2054922799);
                d = l(d, a, b, c, e[8], 6, 1873313359);
                c = l(c, d, a, b, e[15], 10, -30611744);
                b = l(b, c, d, a, e[6], 15, -1560198380);
                a = l(a, b, c, d, e[13], 21, 1309151649);
                d = l(d, a, b, c, e[4], 6, -145523070);
                c = l(c, d, a, b, e[11], 10, -1120210379);
                b = l(b, c, d, a, e[2], 15, 718787259);
                a = l(a, b, c, d, e[9], 21, -343485551);
                f[0] = m(d, f[0]);
                f[1] = m(a, f[1]);
                f[2] = m(b, f[2]);
                f[3] = m(c, f[3])
            }, g = function(f, e, d, a, b, c) {
                e = m(m(e, f), m(a, c));
                return m(e << b | e >>> 32 - b, d)
            }
              , h = function(f, e, d, a, b, c, n) {
                return g(e & d | ~e & a, f, e, b, c, n)
            }
              , k = function(f, e, d, a, b, c, n) {
                return g(e & a | d & ~a, f, e, b, c, n)
            }
              , l = function(f, e, d, a, b, c, n) {
                return g(d ^ (e | ~a), f, e, b, c, n)
            }, r = "0123456789abcdef".split("");

            var m = function(f, e) {
                return f + e & 4294967295
            };

            var q = function(f) {
                var e = f.length, d = [1732584193, -271733879, -1732584194, 271733878], a;
                for (a = 64; a <= f.length; a += 64) {
                    var b, c = f.substring(a - 64, a), g = [];
                    for (b = 0; 64 > b; b += 4)
                        g[b >> 2] = c.charCodeAt(b) + (c.charCodeAt(b + 1) << 8) + (c.charCodeAt(b + 2) << 16) + (c.charCodeAt(b + 3) << 24);
                    p(d, g)
                }
                f = f.substring(a - 64);
                b = [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
                for (a = 0; a < f.length; a++)
                    b[a >> 2] |= f.charCodeAt(a) << (a % 4 << 3);
                b[a >> 2] |= 128 << (a % 4 << 3);
                if (55 < a)
                    for (p(d, b),
                    a = 0; 16 > a; a++)
                        b[a] = 0;
                b[14] = 8 * e;
                p(d, b);
                return d
            };

            var md5 = function(f, e, timeDifference) {
                var d = Date.now() + 6E4 + timeDifference;
                e = q(d + f + e);
                f = [];
                for (var a = 0; a < e.length; a++) {
                    var b = e[a];
                    var c = []
                      , g = 4;
                    do
                        c[--g] = b & 255,
                        b >>= 8;
                    while (g);
                    b = c;
                    for (c = b.length - 1; 0 <= c; c--)
                        f.push(b[c])
                }
                g = void 0;
                c = "";
                for (e = a = b = 0; e < 4 * f.length / 3; g = b >> 2 * (++e & 3) & 63,
                c += String.fromCharCode(g + 71 - (26 > g ? 6 : 52 > g ? 0 : 62 > g ? 75 : g ^ 63 ? 90 : 87)) + (75 == (e - 1) % 76 ? "\r\n" : ""))
                    e & 3 ^ 3 && (b = b << 8 ^ f[a++]);
                for (; e++ & 3; )
                    c += "\x3d";
                return c.replace(/\+/g, "-").replace(/\//g, "_") + "," + d
            };

            "5d41402abc4b2a76b9719d911017c592" != function(f) {
                for (var e = 0; e < f.length; e++) {
                    for (var d = e, a = f[e], b = "", c = 0; 4 > c; c++)
                        b += r[a >> 8 * c + 4 & 15] + r[a >> 8 * c & 15];
                    f[d] = b
                }
                return f.join("")
            }(q("hello")) && (m = function(f, e) {
                var d = (f & 65535) + (e & 65535);
                return (f >> 16) + (e >> 16) + (d >> 16) << 16 | d & 65535
            }
            )

            //console.log(md5('/r/nh22/2022/VNUS_DE_NYKE/19_07_22_2302_skt/h264.mpd','vx54axqjal4f0yy2',-2274));
            //console.log(md5('/r/nh22/2022/VNUS_DE_NYKE/19_07_22_2302_skt/subtitle_pl/34.m4s','vx54axqjal4f0yy2',-2274));
            
            """;
    }
}
