using N_m3u8DL_RE.Entity;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace N_m3u8DL_RE.Util
{
    class Language
    {
        public string Code;
        public string ExtendCode;
        public string Description;
        public string DescriptionAudio;

        public Language(string extendCode, string code, string desc, string descA)
        {
            Code = code;
            ExtendCode = extendCode;
            Description = desc;
            DescriptionAudio = descA;
        }
    }

    internal class LanguageCodeUtil
    {
        private LanguageCodeUtil() { }

        private readonly static List<Language> ALL_LANGS = @"
af;afr;Afrikaans;Afrikaans
af-ZA;afr;Afrikaans (South Africa);Afrikaans (South Africa)
am;amh;Amharic;Amharic
am-ET;amh;Amharic (Ethiopia);Amharic (Ethiopia)
ar;ara;Arabic;Arabic
ar-SA;ara;Arabic (Saudi Arabia);Arabic (Saudi Arabia)
ar-IQ;ara;Arabic (Iraq);Arabic (Iraq)
ar-EG;ara;Arabic (Egypt);Arabic (Egypt)
ar-LY;ara;Arabic (Libya);Arabic (Libya)
ar-DZ;ara;Arabic (Algeria);Arabic (Algeria)
ar-MA;ara;Arabic (Morocco);Arabic (Morocco)
ar-TN;ara;Arabic (Tunisia);Arabic (Tunisia)
ar-OM;ara;Arabic (Oman);Arabic (Oman)
ar-YE;ara;Arabic (Yemen);Arabic (Yemen)
ar-SY;ara;Arabic (Syria);Arabic (Syria)
ar-JO;ara;Arabic (Jordan);Arabic (Jordan)
ar-LB;ara;Arabic (Lebanon);Arabic (Lebanon)
ar-KW;ara;Arabic (Kuwait);Arabic (Kuwait)
ar-AE;ara;Arabic (United Arab Emirates);Arabic (United Arab Emirates)
ar-BH;ara;Arabic (Bahrain);Arabic (Bahrain)
ar-QA;ara;Arabic (Qatar);Arabic (Qatar)
as;asm;Assamese;Assamese
as-IN;asm;Assamese (India);Assamese (India)
az;aze;Azerbaijani;Azerbaijani
az-Latn-AZ;aze;Azerbaijani (Latin, Azerbaijan);Azerbaijani (Latin, Azerbaijan)
az-Cyrl-AZ;aze;Azerbaijani (Cyrillic, Azerbaijan);Azerbaijani (Cyrillic, Azerbaijan)
az-Cyrl;aze;Azerbaijani (Cyrillic);Azerbaijani (Cyrillic)
az-Latn;aze;Azerbaijani (Latin);Azerbaijani (Latin)
be;bel;Belarusian;Belarusian
be-BY;bel;Belarusian (Belarus);Belarusian (Belarus)
bg;bul;Bulgarian;Bulgarian
bg-BG;bul;Bulgarian (Bulgaria);Bulgarian (Bulgaria)
bn;ben;Bangla;Bangla
bn-IN;ben;Bangla (India);Bangla (India)
bn-BD;ben;Bangla (Bangladesh);Bangla (Bangladesh)
bo;bod;Tibetan;Tibetan
bo-CN;bod;Tibetan (China);Tibetan (China)
br;bre;Breton;Breton
br-FR;bre;Breton (France);Breton (France)
bs-Latn-BA;bos;Bosnian (Latin, Bosnia & Herzegovina);Bosnian (Latin, Bosnia & Herzegovina)
bs-Cyrl-BA;bos;Bosnian (Cyrillic, Bosnia & Herzegovina);Bosnian (Cyrillic, Bosnia & Herzegovina)
bs-Cyrl;bos;Bosnian (Cyrillic);Bosnian (Cyrillic)
bs-Latn;bos;Bosnian (Latin);Bosnian (Latin)
bs;bos;Bosnian;Bosnian
ca;cat;Catalan;Catalan
ca-ES;cat;Catalan (Spain);Catalan (Spain)
ca-ES-valencia;cat;Catalan (Spain);Catalan (Spain)
chr;chr;Cherokee;Cherokee
cs;ces;Czech;Czech
cs-CZ;ces;Czech (Czech Republic);Czech (Czech Republic)
cy;cym;Welsh;Welsh
cy-GB;cym;Welsh (United Kingdom);Welsh (United Kingdom)
da;dan;Danish;Danish
da-DK;dan;Danish (Denmark);Danish (Denmark)
de;deu;German;German
de-DE;deu;German (Germany);German (Germany)
de-CH;deu;German (Switzerland);German (Switzerland)
de-AT;deu;German (Austria);German (Austria)
de-LU;deu;German (Luxembourg);German (Luxembourg)
de-LI;deu;German (Liechtenstein);German (Liechtenstein)
dsb-DE;dsb;Lower Sorbian (Germany);Lower Sorbian (Germany)
dsb;dsb;Lower Sorbian;Lower Sorbian
el;ell;Greek;Greek
el-GR;ell;Greek (Greece);Greek (Greece)
en;eng;English;English
en-US;eng;English (United States);English (United States)
en-GB;eng;English (United Kingdom);English (United Kingdom)
en-AU;eng;English (Australia);English (Australia)
en-CA;eng;English (Canada);English (Canada)
en-NZ;eng;English (New Zealand);English (New Zealand)
en-IE;eng;English (Ireland);English (Ireland)
en-ZA;eng;English (South Africa);English (South Africa)
en-JM;eng;English (Jamaica);English (Jamaica)
en-BZ;eng;English (Belize);English (Belize)
en-TT;eng;English (Trinidad & Tobago);English (Trinidad & Tobago)
en-ZW;eng;English (Zimbabwe);English (Zimbabwe)
en-PH;eng;English (Philippines);English (Philippines)
en-HK;eng;English (Hong Kong SAR China);English (Hong Kong SAR China)
en-IN;eng;English (India);English (India)
en-MY;eng;English (Malaysia);English (Malaysia)
en-SG;eng;English (Singapore);English (Singapore)
es;spa;Spanish;Spanish
es-MX;spa;Spanish (Mexico);Spanish (Mexico)
es-ES;spa;Spanish (Spain);Spanish (Spain)
es-GT;spa;Spanish (Guatemala);Spanish (Guatemala)
es-CR;spa;Spanish (Costa Rica);Spanish (Costa Rica)
es-PA;spa;Spanish (Panama);Spanish (Panama)
es-DO;spa;Spanish (Dominican Republic);Spanish (Dominican Republic)
es-VE;spa;Spanish (Venezuela);Spanish (Venezuela)
es-CO;spa;Spanish (Colombia);Spanish (Colombia)
es-PE;spa;Spanish (Peru);Spanish (Peru)
es-AR;spa;Spanish (Argentina);Spanish (Argentina)
es-EC;spa;Spanish (Ecuador);Spanish (Ecuador)
es-CL;spa;Spanish (Chile);Spanish (Chile)
es-UY;spa;Spanish (Uruguay);Spanish (Uruguay)
es-PY;spa;Spanish (Paraguay);Spanish (Paraguay)
es-BO;spa;Spanish (Bolivia);Spanish (Bolivia)
es-SV;spa;Spanish (El Salvador);Spanish (El Salvador)
es-HN;spa;Spanish (Honduras);Spanish (Honduras)
es-NI;spa;Spanish (Nicaragua);Spanish (Nicaragua)
es-PR;spa;Spanish (Puerto Rico);Spanish (Puerto Rico)
es-US;spa;Spanish (United States);Spanish (United States)
es-CU;spa;Spanish (Cuba);Spanish (Cuba)
et;est;Estonian;Estonian
et-EE;est;Estonian (Estonia);Estonian (Estonia)
eu;eus;Basque;Basque
eu-ES;eus;Basque (Spain);Basque (Spain)
fa;fas;Persian;Persian
fa-IR;fas;Persian (Iran);Persian (Iran)
ff;ful;Fulah;Fulah
fi;fin;Finnish;Finnish
fi-FI;fin;Finnish (Finland);Finnish (Finland)
fil;fil;Filipino;Filipino
fil-PH;fil;Filipino (Philippines);Filipino (Philippines)
fo;fao;Faroese;Faroese
fo-FO;fao;Faroese (Faroe Islands);Faroese (Faroe Islands)
fr;fra;French;French
fr-FR;fra;French (France);French (France)
fr-BE;fra;French (Belgium);French (Belgium)
fr-CA;fra;French (Canada);French (Canada)
fr-CH;fra;French (Switzerland);French (Switzerland)
fr-LU;fra;French (Luxembourg);French (Luxembourg)
fr-MC;fra;French (Monaco);French (Monaco)
fr-RE;fra;French (Réunion);French (Réunion)
fr-CD;fra;French (Congo - Kinshasa);French (Congo - Kinshasa)
fr-SN;fra;French (Senegal);French (Senegal)
fr-CM;fra;French (Cameroon);French (Cameroon)
fr-CI;fra;French (Côte d’Ivoire);French (Côte d’Ivoire)
fr-ML;fra;French (Mali);French (Mali)
fr-MA;fra;French (Morocco);French (Morocco)
fr-HT;fra;French (Haiti);French (Haiti)
fy;fry;Western Frisian;Western Frisian
fy-NL;fry;Western Frisian (Netherlands);Western Frisian (Netherlands)
ga;gle;Irish;Irish
ga-IE;gle;Irish (Ireland);Irish (Ireland)
gd;gla;Scottish Gaelic;Scottish Gaelic
gd-GB;gla;Scottish Gaelic (United Kingdom);Scottish Gaelic (United Kingdom)
gl;glg;Galician;Galician
gl-ES;glg;Galician (Spain);Galician (Spain)
gsw;gsw;Swiss German;Swiss German
gsw-FR;gsw;Swiss German (France);Swiss German (France)
gu;guj;Gujarati;Gujarati
gu-IN;guj;Gujarati (India);Gujarati (India)
ha;hau;Hausa;Hausa
ha-Latn-NG;hau;Hausa (Latin, Nigeria);Hausa (Latin, Nigeria)
ha-Latn;hau;Hausa (Latin);Hausa (Latin)
haw;haw;Hawaiian;Hawaiian
haw-US;haw;Hawaiian (United States);Hawaiian (United States)
he;heb;Hebrew;Hebrew
he-IL;heb;Hebrew (Israel);Hebrew (Israel)
hi;hin;Hindi;Hindi
hi-IN;hin;Hindi (India);Hindi (India)
hr;hrv;Croatian;Croatian
hr-HR;hrv;Croatian (Croatia);Croatian (Croatia)
hr-BA;hrv;Croatian (Bosnia & Herzegovina);Croatian (Bosnia & Herzegovina)
hsb;hsb;Upper Sorbian;Upper Sorbian
hsb-DE;hsb;Upper Sorbian (Germany);Upper Sorbian (Germany)
hu;hun;Hungarian;Hungarian
hu-HU;hun;Hungarian (Hungary);Hungarian (Hungary)
hy;hye;Armenian;Armenian
hy-AM;hye;Armenian (Armenia);Armenian (Armenia)
id;ind;Indonesian;Indonesian
id-ID;ind;Indonesian (Indonesia);Indonesian (Indonesia)
ig;ibo;Igbo;Igbo
ig-NG;ibo;Igbo (Nigeria);Igbo (Nigeria)
ii;iii;Sichuan Yi;Sichuan Yi
ii-CN;iii;Sichuan Yi (China);Sichuan Yi (China)
is;isl;Icelandic;Icelandic
is-IS;isl;Icelandic (Iceland);Icelandic (Iceland)
it;ita;Italian;Italian
it-IT;ita;Italian (Italy);Italian (Italy)
it-CH;ita;Italian (Switzerland);Italian (Switzerland)
ja;jpn;Japanese;Japanese
ja-JP;jpn;Japanese (Japan);Japanese (Japan)
ka;kat;Georgian;Georgian
ka-GE;kat;Georgian (Georgia);Georgian (Georgia)
kk;kaz;Kazakh;Kazakh
kk-KZ;kaz;Kazakh (Kazakhstan);Kazakh (Kazakhstan)
kl;kal;Kalaallisut;Kalaallisut
kl-GL;kal;Kalaallisut (Greenland);Kalaallisut (Greenland)
km;khm;Khmer;Khmer
km-KH;khm;Khmer (Cambodia);Khmer (Cambodia)
kn;kan;Kannada;Kannada
kn-IN;kan;Kannada (India);Kannada (India)
ko;kor;Korean;Korean
ko-KR;kor;Korean (South Korea);Korean (South Korea)
kok;kok;Konkani;Konkani
kok-IN;kok;Konkani (India);Konkani (India)
ky;kir;Kyrgyz;Kyrgyz
ky-KG;kir;Kyrgyz (Kyrgyzstan);Kyrgyz (Kyrgyzstan)
lb;ltz;Luxembourgish;Luxembourgish
lb-LU;ltz;Luxembourgish (Luxembourg);Luxembourgish (Luxembourg)
lo;lao;Lao;Lao
lo-LA;lao;Lao (Laos);Lao (Laos)
lt;lit;Lithuanian;Lithuanian
lt-LT;lit;Lithuanian (Lithuania);Lithuanian (Lithuania)
lv;lav;Latvian;Latvian
lv-LV;lav;Latvian (Latvia);Latvian (Latvia)
mk;mkd;Macedonian;Macedonian
mk-MK;mkd;Macedonian (Macedonia);Macedonian (Macedonia)
ml;mal;Malayalam;Malayalam
ml-IN;mal;Malayalam (India);Malayalam (India)
mn;mon;Mongolian;Mongolian
mn-MN;mon;Mongolian (Mongolia);Mongolian (Mongolia)
mn-Cyrl;mon;Mongolian (Cyrillic);Mongolian (Cyrillic)
mr;mar;Marathi;Marathi
mr-IN;mar;Marathi (India);Marathi (India)
ms;msa;Malay;Malay
ms-MY;msa;Malay (Malaysia);Malay (Malaysia)
ms-BN;msa;Malay (Brunei);Malay (Brunei)
mt;mlt;Maltese;Maltese
mt-MT;mlt;Maltese (Malta);Maltese (Malta)
my;mya;Burmese;Burmese
my-MM;mya;Burmese (Myanmar (Burma));Burmese (Myanmar (Burma))
no;nob;Norwegian;Norwegian
nb-NO;nob;Norwegian Bokmål (Norway);Norwegian Bokmål (Norway)
nb;nob;Norwegian Bokmål;Norwegian Bokmål
ne;nep;Nepali;Nepali
ne-NP;nep;Nepali (Nepal);Nepali (Nepal)
ne-IN;nep;Nepali (India);Nepali (India)
nl;nld;Dutch;Dutch
nl-NL;nld;Dutch (Netherlands);Dutch (Netherlands)
nl-BE;nld;Dutch (Belgium);Dutch (Belgium)
nn-NO;nno;Norwegian Nynorsk (Norway);Norwegian Nynorsk (Norway)
nn;nno;Norwegian Nynorsk;Norwegian Nynorsk
nso;nso;Northern Sotho;Northern Sotho
nso-ZA;nso;Northern Sotho (South Africa);Northern Sotho (South Africa)
om;orm;Oromo;Oromo
om-ET;orm;Oromo (Ethiopia);Oromo (Ethiopia)
or;ori;Odia;Odia
or-IN;ori;Odia (India);Odia (India)
pa;pan;Punjabi;Punjabi
pa-Arab-PK;pan;Punjabi (Arabic, Pakistan);Punjabi (Arabic, Pakistan)
pa-Arab;pan;Punjabi (Arabic);Punjabi (Arabic)
pl;pol;Polish;Polish
pl-PL;pol;Polish (Poland);Polish (Poland)
ps;pus;Pashto;Pashto
ps-AF;pus;Pashto (Afghanistan);Pashto (Afghanistan)
pt;por;Portuguese;Portuguese
pt-BR;por;Portuguese (Brazil);Portuguese (Brazil)
pt-PT;por;Portuguese (Portugal);Portuguese (Portugal)
rm;roh;Romansh;Romansh
rm-CH;roh;Romansh (Switzerland);Romansh (Switzerland)
ro;ron;Romanian;Romanian
ro-RO;ron;Romanian (Romania);Romanian (Romania)
ro-MD;ron;Romanian (Moldova);Romanian (Moldova)
ru;rus;Russian;Russian
ru-RU;rus;Russian (Russia);Russian (Russia)
ru-MD;rus;Russian (Moldova);Russian (Moldova)
rw;kin;Kinyarwanda;Kinyarwanda
rw-RW;kin;Kinyarwanda (Rwanda);Kinyarwanda (Rwanda)
sah;sah;Sakha;Sakha
sah-RU;sah;Sakha (Russia);Sakha (Russia)
se;sme;Northern Sami;Northern Sami
se-NO;sme;Northern Sami (Norway);Northern Sami (Norway)
se-SE;sme;Northern Sami (Sweden);Northern Sami (Sweden)
se-FI;sme;Northern Sami (Finland);Northern Sami (Finland)
si;sin;Sinhala;Sinhala
si-LK;sin;Sinhala (Sri Lanka);Sinhala (Sri Lanka)
sk;slk;Slovak;Slovak
sk-SK;slk;Slovak (Slovakia);Slovak (Slovakia)
sl;slv;Slovenian;Slovenian
sl-SI;slv;Slovenian (Slovenia);Slovenian (Slovenia)
smn-FI;smn;Inari Sami (Finland);Inari Sami (Finland)
smn;smn;Inari Sami;Inari Sami
so;som;Somali;Somali
so-SO;som;Somali (Somalia);Somali (Somalia)
sq;sqi;Albanian;Albanian
sq-AL;sqi;Albanian (Albania);Albanian (Albania)
sr-Latn-BA;srp;Serbian (Latin, Bosnia & Herzegovina);Serbian (Latin, Bosnia & Herzegovina)
sr-Cyrl-BA;srp;Serbian (Cyrillic, Bosnia & Herzegovina);Serbian (Cyrillic, Bosnia & Herzegovina)
sr-Latn-RS;srp;Serbian (Latin, Serbia);Serbian (Latin, Serbia)
sr-Cyrl-RS;srp;Serbian (Cyrillic, Serbia);Serbian (Cyrillic, Serbia)
sr-Latn-ME;srp;Serbian (Latin, Montenegro);Serbian (Latin, Montenegro)
sr-Cyrl-ME;srp;Serbian (Cyrillic, Montenegro);Serbian (Cyrillic, Montenegro)
sr-Cyrl;srp;Serbian (Cyrillic);Serbian (Cyrillic)
sr-Latn;srp;Serbian (Latin);Serbian (Latin)
sr;srp;Serbian;Serbian
st;sot;Southern Sotho;Southern Sotho
st-ZA;sot;Southern Sotho (South Africa);Southern Sotho (South Africa)
sv;swe;Swedish;Swedish
sv-SE;swe;Swedish (Sweden);Swedish (Sweden)
sv-FI;swe;Swedish (Finland);Swedish (Finland)
sw;swa;Swahili;Swahili
sw-KE;swa;Swahili (Kenya);Swahili (Kenya)
ta;tam;Tamil;Tamil
ta-IN;tam;Tamil (India);Tamil (India)
ta-LK;tam;Tamil (Sri Lanka);Tamil (Sri Lanka)
te;tel;Telugu;Telugu
te-IN;tel;Telugu (India);Telugu (India)
tg;tgk;Tajik;Tajik
tg-Cyrl-TJ;tgk;Tajik (Cyrillic, Tajikistan);Tajik (Cyrillic, Tajikistan)
tg-Cyrl;tgk;Tajik (Cyrillic);Tajik (Cyrillic)
th;tha;Thai;Thai
th-TH;tha;Thai (Thailand);Thai (Thailand)
ti;tir;Tigrinya;Tigrinya
ti-ET;tir;Tigrinya (Ethiopia);Tigrinya (Ethiopia)
ti-ER;tir;Tigrinya (Eritrea);Tigrinya (Eritrea)
tk;tuk;Turkmen;Turkmen
tk-TM;tuk;Turkmen (Turkmenistan);Turkmen (Turkmenistan)
tn;tsn;Tswana;Tswana
tn-ZA;tsn;Tswana (South Africa);Tswana (South Africa)
tn-BW;tsn;Tswana (Botswana);Tswana (Botswana)
tr;tur;Turkish;Turkish
tr-TR;tur;Turkish (Turkey);Turkish (Turkey)
ts;tso;Tsonga;Tsonga
ts-ZA;tso;Tsonga (South Africa);Tsonga (South Africa)
tzm;tzm;Central Atlas Tamazight;Central Atlas Tamazight
tzm-Latn;tzm;Central Atlas Tamazight (Latin);Central Atlas Tamazight (Latin)
ug;uig;Uyghur;Uyghur
ug-CN;uig;Uyghur (China);Uyghur (China)
uk;ukr;Ukrainian;Ukrainian
uk-UA;ukr;Ukrainian (Ukraine);Ukrainian (Ukraine)
ur;urd;Urdu;Urdu
ur-PK;urd;Urdu (Pakistan);Urdu (Pakistan)
ur-IN;urd;Urdu (India);Urdu (India)
uz;uzb;Uzbek;Uzbek
uz-Latn-UZ;uzb;Uzbek (Latin, Uzbekistan);Uzbek (Latin, Uzbekistan)
uz-Cyrl-UZ;uzb;Uzbek (Cyrillic, Uzbekistan);Uzbek (Cyrillic, Uzbekistan)
uz-Cyrl;uzb;Uzbek (Cyrillic);Uzbek (Cyrillic)
uz-Latn;uzb;Uzbek (Latin);Uzbek (Latin)
vi;vie;Vietnamese;Vietnamese
vi-VN;vie;Vietnamese (Vietnam);Vietnamese (Vietnam)
xh;xho;Xhosa;Xhosa
xh-ZA;xho;Xhosa (South Africa);Xhosa (South Africa)
yo;yor;Yoruba;Yoruba
yo-NG;yor;Yoruba (Nigeria);Yoruba (Nigeria)
zu;zul;Zulu;Zulu
zu-ZA;zul;Zulu (South Africa);Zulu (South Africa)


zho;chi;中文;中文
chi;chi;中文;中文
chs;chi;中文（简体）;中文
zh-CN;chi;中文（简体）;中文
zh-SG;chi;中文（简体, 新加坡）;中文
zh-MO;chi;中文（繁體, 澳門）;中文
zh-Hans;chi;中文（简体）;中文
zh-Hant;chi;中文（繁體）;中文
zh-TW;chi;中文（繁體, 台灣）;中文
zh-Hant-TW;chi;中文（繁體, 台灣）;中文
zh-HK;chi;中文（繁體, 香港）;中文
zh-Hant-HK;chi;中文（繁體, 香港）;中文
yue;chi;中文（繁體）;粵語
cmn;chi;中文（简体）;普通话
cmn-Hans;chi;中文（简体）;普通话
cmn-Hant;chi;中文（繁體）;普通話
Cantonese;chi;中文;粵語
Mandarin;chi;中文;普通话
Japanese;jpn;日本語;日本語
Korean;kor;한국어;한국어
Vietnamese;vie;Vietnamese;Vietnamese
English;eng;English;English
Thai;tha;Thai;Thai
CN;chi;中文（繁體）;中文
CC;chi;中文（繁體）;中文
CZ;chi;中文（简体）;中文
MA;msa;Melayu;Melayu
"
.Trim().Replace("\r", "").Split('\n').Where(x => !string.IsNullOrWhiteSpace(x)).Select(x =>
{
    var arr = x.Trim().Split(';');
    return new Language(arr[0].Trim(), arr[1].Trim(), arr[2].Trim(), arr[3].Trim());
}).ToList();

        private static Dictionary<string, string> CODE_MAP = @"
iv;IVL
ar;ara
bg;bul
ca;cat
zh;zho
cs;ces
da;dan
de;deu
el;ell
en;eng
es;spa
fi;fin
fr;fra
he;heb
hu;hun
is;isl
it;ita
ja;jpn
ko;kor
nl;nld
nb;nob
pl;pol
pt;por
rm;roh
ro;ron
ru;rus
hr;hrv
sk;slk
sq;sqi
sv;swe
th;tha
tr;tur
ur;urd
id;ind
uk;ukr
be;bel
sl;slv
et;est
lv;lav
lt;lit
tg;tgk
fa;fas
vi;vie
hy;hye
az;aze
eu;eus
mk;mkd
st;sot
ts;tso
tn;tsn
xh;xho
zu;zul
af;afr
ka;kat
fo;fao
hi;hin
mt;mlt
se;sme
ga;gle
ms;msa
kk;kaz
ky;kir
sw;swa
tk;tuk
uz;uzb
bn;ben
pa;pan
gu;guj
or;ori
ta;tam
te;tel
kn;kan
ml;mal
as;asm
mr;mar
mn;mon
bo;bod
cy;cym
km;khm
lo;lao
my;mya
gl;glg
si;sin
am;amh
ne;nep
fy;fry
ps;pus
ff;ful
ha;hau
yo;yor
lb;ltz
kl;kal
ig;ibo
om;orm
ti;tir
so;som
ii;iii
br;bre
ug;uig
rw;kin
gd;gla
nn;nno
bs;bos
sr;srp
"
.Trim().Replace("\r", "").Split('\n').Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToDictionary(x => x.Split(';').First().Trim(), x => x.Split(';').Last().Trim());


        private static string ConvertTwoToThree(string input)
        {
            if (CODE_MAP.TryGetValue(input, out var code)) return code;
            return input;
        }

        /// <summary>
        /// 转换 ISO 639-1 => ISO 639-2
        /// 且当Description为空时将DisplayName写入
        /// </summary>
        /// <param name="outputFile"></param>
        public static void ConvertLangCodeAndDisplayName(OutputFile outputFile)
        {
            if (string.IsNullOrEmpty(outputFile.LangCode)) return;
            var originalLangCode = outputFile.LangCode;

            //先直接查找
            var lang = ALL_LANGS.FirstOrDefault(a => a.ExtendCode.Equals(outputFile.LangCode, StringComparison.OrdinalIgnoreCase) || a.Code.Equals(outputFile.LangCode, StringComparison.OrdinalIgnoreCase));
            //处理特殊的扩展语言标记
            if (lang == null)
            {
                //2位转3位
                var l = ConvertTwoToThree(outputFile.LangCode.Split('-').First());
                lang = ALL_LANGS.FirstOrDefault(a => a.ExtendCode.Equals(l, StringComparison.OrdinalIgnoreCase) || a.Code.Equals(l, StringComparison.OrdinalIgnoreCase));
            }

            if (lang != null)
            {
                outputFile.LangCode = lang.Code;
                if (string.IsNullOrEmpty(outputFile.Description))
                    outputFile.Description = outputFile.MediaType == Common.Enum.MediaType.SUBTITLES ? lang.Description : lang.DescriptionAudio;
            }
            else if (outputFile.LangCode == null) 
            {
                outputFile.LangCode = "und"; //无法识别直接置为und
            }

            //无描述，则把LangCode当作描述
            if (string.IsNullOrEmpty(outputFile.Description)) outputFile.Description = originalLangCode;
        }
    }
}
