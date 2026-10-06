namespace N_m3u8DL_RE.Common.Resource;

internal static class StaticText
{
    public static readonly Dictionary<string, TextContainer> LANG_DIC = new()
    {
        ["cmd_config"] = new TextContainer
        (
            zhCN: "读取指定配置文件，替代用户默认配置；命令行选项优先",
            zhTW: "讀取指定設定檔，取代使用者預設設定；命令列選項優先",
            enUS: "Read a configuration file instead of the user default; command-line options take precedence"
        ),
        ["cmd_noConfig"] = new TextContainer
        (
            zhCN: "不读取配置文件，不能与 --config 同时使用",
            zhTW: "不讀取設定檔，不能與 --config 同時使用",
            enUS: "Disable configuration loading; cannot be combined with --config"
        ),
        ["configFileLoadFailed"] = new TextContainer
        (
            zhCN: "配置文件加载失败",
            zhTW: "設定檔載入失敗",
            enUS: "Failed to load configuration"
        ),
        ["configFileConflict"] = new TextContainer
        (
            zhCN: "--config 与 --no-config 不能同时使用",
            zhTW: "--config 與 --no-config 不能同時使用",
            enUS: "--config and --no-config cannot be used together"
        ),
        ["configFileOptionsOnly"] = new TextContainer
        (
            zhCN: "配置文件只能包含下载选项，不能包含下载地址、--config、--no-config、帮助、版本或补全操作",
            zhTW: "設定檔只能包含下載選項，不能包含下載網址、--config、--no-config、說明、版本或補全操作",
            enUS: "Configuration files may only contain download options, not input URLs, --config, --no-config, help, version or completion actions"
        ),
        ["responseFileRecursion"] = new TextContainer
        (
            zhCN: "参数文件存在循环引用或嵌套层数过多",
            zhTW: "參數檔案存在循環引用或巢狀層數過多",
            enUS: "Response files contain a reference cycle or are nested too deeply"
        ),
        ["singleFileSplitWarn"] = new TextContainer
        (
            zhCN: "整段文件已被自动切割为小分片以加速下载",
            zhTW: "整段文件已被自動切割為小分片以加速下載",
            enUS: "The entire file has been cut into small segments to accelerate"
        ),
        ["singleFileRealtimeDecryptWarn"] = new TextContainer
        (
            zhCN: "实时解密已被强制关闭",
            zhTW: "即時解密已被強制關閉",
            enUS: "Real-time decryption has been disabled"
        ),
        ["cmd_forceAnsiConsole"] = new TextContainer
        (
            zhCN: "强制认定终端为支持ANSI且可交互的终端",
            zhTW: "強制認定終端為支援ANSI且可交往的終端",
            enUS: "Force assuming the terminal is ANSI-compatible and interactive"
        ),
        ["cmd_noAnsiColor"] = new TextContainer
        (
            zhCN: "去除ANSI颜色",
            zhTW: "關閉ANSI顏色",
            enUS: "Remove ANSI colors"
        ),
        ["customRangeWarn"] = new TextContainer
        (
            zhCN: "请注意，自定义下载范围有时会导致音画不同步",
            zhTW: "請注意，自定義下載範圍有時會導致音畫不同步",
            enUS: "Please note that custom range may sometimes result in audio and video being out of sync"
        ),
        ["customRangeInvalid"] = new TextContainer
        (
            zhCN: "自定义下载范围无效",
            zhTW: "自定義下載範圍無效",
            enUS: "User customed range invalid"
        ),
        ["customAdKeywordsFound"] = new TextContainer
        (
            zhCN: "用户自定义广告分片URL关键字：",
            zhTW: "用戶自定義廣告分片URL關鍵字：",
            enUS: "User customed Ad keyword: "
        ),
        ["customRangeFound"] = new TextContainer
        (
            zhCN: "用户自定义下载范围：",
            zhTW: "用戶自定義下載範圍：",
            enUS: "User customed range: "
        ),
        ["consoleRedirected"] = new TextContainer
        (
            zhCN: "输出被重定向, 将清除ANSI颜色",
            zhTW: "輸出被重定向, 將清除ANSI顏色",
            enUS: "Output is redirected, ANSI colors are cleared."
        ),
        ["processImageSub"] = new TextContainer
        (
            zhCN: "正在处理图形字幕",
            zhTW: "正在處理圖形字幕",
            enUS: "Processing Image Sub"
        ),
        ["newVersionFound"] = new TextContainer
        (
            zhCN: "检测到新版本，请尽快升级！",
            zhTW: "檢測到新版本，請盡快升級！",
            enUS: "New version detected!"
        ),
        ["namedPipeCreated"] = new TextContainer
        (
            zhCN: "已创建命名管道：",
            zhTW: "已創建命名管道：",
            enUS: "Named pipe created: "
        ),
        ["namedPipeMux"] = new TextContainer
        (
            zhCN: "通过命名管道混流到",
            zhTW: "通過命名管道混流到",
            enUS: "Mux with named pipe, to"
        ),
        ["taskStartAt"] = new TextContainer
        (
            zhCN: "程序将等待，直到：",
            zhTW: "程序將等待，直到：",
            enUS: "The program will wait until: "
        ),
        ["autoBinaryMerge"] = new TextContainer
        (
            zhCN: "检测到fMP4，自动开启二进制合并",
            zhTW: "檢測到fMP4，自動開啟二進位制合併",
            enUS: "fMP4 is detected, binary merging is automatically enabled"
        ),
        ["autoBinaryMerge2"] = new TextContainer
        (
            zhCN: "检测到杜比视界内容，自动开启二进制合并",
            zhTW: "檢測到杜比視界內容，自動開啟二進位制合併",
            enUS: "Dolby Vision content is detected, binary merging is automatically enabled"
        ),
        ["autoBinaryMerge3"] = new TextContainer
        (
            zhCN: "检测到无法识别的加密方式，自动开启二进制合并",
            zhTW: "檢測到無法識別的加密方式，自動開啟二進位制合併",
            enUS: "An unrecognized encryption method is detected, binary merging is automatically enabled"
        ),
        ["autoBinaryMerge4"] = new TextContainer
        (
            zhCN: "检测到CENC加密方式，自动开启二进制合并",
            zhTW: "檢測到CENC加密方式，自動開啟二進位制合併",
            enUS: "When CENC encryption is detected, binary merging is automatically enabled"
        ),
        ["autoBinaryMerge5"] = new TextContainer
        (
            zhCN: "检测到杜比视界内容，混流功能已禁用",
            zhTW: "檢測到杜比視界內容，混流功能已禁用",
            enUS: "Dolby Vision content is detected, mux after done is automatically disabled"
        ),
        ["autoBinaryMerge6"] = new TextContainer
        (
            zhCN: "你已开启下载完成后混流，自动开启二进制合并",
            zhTW: "你已開啟下載完成後混流，自動開啟二進制合併",
            enUS: "MuxAfterDone is detected, binary merging is automatically enabled"
        ),
        ["badM3u8"] = new TextContainer
        (
            zhCN: "错误的m3u8",
            zhTW: "錯誤的m3u8",
            enUS: "Bad m3u8"
        ),
        ["binaryMerge"] = new TextContainer
        (
            zhCN: "二进制合并中...",
            zhTW: "二進位制合併中...",
            enUS: "Binary merging..."
        ),
        ["checkingLast"] = new TextContainer
        (
            zhCN: "验证最后一个分片有效性",
            zhTW: "驗證最後一個分片有效性",
            enUS: "Verifying the validity of the last segment"
        ),
        ["cmd_baseUrl"] = new TextContainer
        (
            zhCN: "设置BaseURL",
            zhTW: "設置BaseURL",
            enUS: "Set BaseURL"
        ),
        ["cmd_maxSpeed"] = new TextContainer
        (
            zhCN: "设置限速，单位支持 Mbps 或 Kbps，如：15M 100K",
            zhTW: "設置限速，單位支持 Mbps 或 Kbps，如：15M 100K",
            enUS: "Set speed limit, Mbps or Kbps, for example: 15M 100K."
        ),
        ["cmd_noDateInfo"] = new TextContainer
        (
            zhCN: "混流时不写入日期信息",
            zhTW: "混流時不寫入日期訊息",
            enUS: "Date information is not written during muxing"
        ),
        ["cmd_noLog"] = new TextContainer
        (
            zhCN: "关闭日志文件输出",
            zhTW: "關閉日誌文件輸出",
            enUS: "Disable log file output"
        ),
        ["cmd_allowHlsMultiExtMap"] = new TextContainer
        (
            zhCN: "允许直播HLS中的多个#EXT-X-MAP(实验性；点播默认支持)",
            zhTW: "允許直播HLS中的多個#EXT-X-MAP(實驗性；點播預設支援)",
            enUS: "Allow multiple #EXT-X-MAP in live HLS (experimental; enabled for VOD)"
        ),
        ["cmd_appendUrlParams"] = new TextContainer
        (
            zhCN: "将输入URL的查询参数添加至分片；本地清单使用 --base-url 的参数",
            zhTW: "將輸入URL的查詢參數添加至分片；本地清單使用 --base-url 的參數",
            enUS: "Append input URL query parameters to segments; local manifests use --base-url parameters"
        ),
        ["cmd_autoSelect"] = new TextContainer
        (
            zhCN: "自动选择所有类型的最佳轨道",
            zhTW: "自動選擇所有類型的最佳軌道",
            enUS: "Automatically selects the best tracks of all types"
        ),
        ["cmd_disableUpdateCheck"] = new TextContainer
        (
            zhCN: "禁用版本更新检测",
            zhTW: "禁用版本更新檢測",
            enUS: "Disable version update check"
        ),
        ["cmd_binaryMerge"] = new TextContainer
        (
            zhCN: "二进制合并",
            zhTW: "二進位制合併",
            enUS: "Binary merge"
        ),
        ["cmd_ffmpegConcatMode"] = new TextContainer
        (
            zhCN: "FFmpeg 合并输入方式：LOCAL_HTTP 本机虚拟输入(默认)，PROTOCOL 直接打开全部分片，DEMUXER 使用文件列表",
            zhTW: "FFmpeg 合併輸入方式：LOCAL_HTTP 本機虛擬輸入(預設)，PROTOCOL 直接開啟全部分片，DEMUXER 使用檔案清單",
            enUS: "FFmpeg merge input: LOCAL_HTTP local virtual input (default), PROTOCOL opens all segments directly, DEMUXER uses a file list"
        ),
        ["ffmpegConcatInputFailed"] = new TextContainer
        (
            zhCN: "本机合并输入失败：{0}。已下载的分片保留，可使用 --ffmpeg-concat-mode PROTOCOL 回退到直接打开分片的 concat 协议。",
            zhTW: "本機合併輸入失敗：{0}。已下載的分片保留，可使用 --ffmpeg-concat-mode PROTOCOL 回退至直接開啟分片的 concat 協議。",
            enUS: "Local merge input failed: {0}. Downloaded segments are preserved; use --ffmpeg-concat-mode PROTOCOL to fall back to opening segments directly with the concat protocol."
        ),
        ["concatInputLengthChanged"] = new TextContainer
        (
            zhCN: "合并期间分片长度发生变化",
            zhTW: "合併期間分片長度發生變化",
            enUS: "A segment's length changed during merging"
        ),
        ["cmd_useFFmpegConcatDemuxer"] = new TextContainer
        (
            zhCN: "使用 concat 分离器合并，等同于 --ffmpeg-concat-mode DEMUXER；同一层同时指定时优先",
            zhTW: "使用 concat 分離器合併，等同於 --ffmpeg-concat-mode DEMUXER；同一層同時指定時優先",
            enUS: "Merge with the concat demuxer; equivalent to --ffmpeg-concat-mode DEMUXER, taking precedence when both are supplied at the same level"
        ),
        ["cmd_checkSegmentsCount"] = new TextContainer
        (
            zhCN: "检测实际下载的分片数量和预期数量是否匹配",
            zhTW: "檢測實際下載的分片數量和預期數量是否匹配",
            enUS: "Check if the actual number of segments downloaded matches the expected number"
        ),
        ["cmd_downloadRetryCount"] = new TextContainer
        (
            zhCN: "每个分片下载异常时的重试次数；分片直播临时网络故障在重试耗尽后仍会等待恢复",
            zhTW: "每個分片下載異常時的重試次數；分片直播暫時網路故障在重試耗盡後仍會等待恢復",
            enUS: "Retries per segment; segmented live recording keeps waiting for recovery after transient network failures"
        ),
        ["cmd_httpRequestTimeout"] = new TextContainer
        (
            zhCN: "HTTP请求超时(秒)；分片直播未指定时自动调整，指定后也用于分片连续无数据超时，不限制总下载时长",
            zhTW: "HTTP請求逾時(秒)；分片直播未指定時自動調整，指定後也用於分片連續無資料逾時，不限制總下載時長",
            enUS: "HTTP timeout in seconds; segmented live recording adjusts automatically unless specified, also bounds segment read stalls, not total download time"
        ),
        ["cmd_decryptionBinaryPath"] = new TextContainer
        (
            zhCN: @"MP4解密所用工具的全路径, 例如 C:\Tools\mp4decrypt.exe",
            zhTW: @"MP4解密所用工具的全路徑, 例如 C:\Tools\mp4decrypt.exe",
            enUS: @"Full path to the tool used for MP4 decryption, like C:\Tools\mp4decrypt.exe"
        ),
        ["cmd_delAfterDone"] = new TextContainer
        (
            zhCN: "完成后删除临时文件",
            zhTW: "完成後刪除臨時文件",
            enUS: "Delete temporary files when done"
        ),
        ["cmd_ffmpegBinaryPath"] = new TextContainer
        (
            zhCN: @"ffmpeg可执行程序全路径, 例如 C:\Tools\ffmpeg.exe",
            zhTW: @"ffmpeg可執行程序全路徑, 例如 C:\Tools\ffmpeg.exe",
            enUS: @"Full path to the ffmpeg binary, like C:\Tools\ffmpeg.exe"
        ),
        ["cmd_mkvmergeBinaryPath"] = new TextContainer
        (
            zhCN: @"mkvmerge可执行程序全路径, 例如 C:\Tools\mkvmerge.exe",
            zhTW: @"mkvmerge可執行程序全路徑, 例如 C:\Tools\mkvmerge.exe",
            enUS: @"Full path to the mkvmerge binary, like C:\Tools\mkvmerge.exe"
        ),
        ["cmd_liveFixVttByAudio"] = new TextContainer
        (
            zhCN: "通过读取音频文件的起始时间修正VTT字幕",
            zhTW: "透過讀取音訊檔案的起始時間修正VTT字幕",
            enUS: "Correct VTT sub by reading the start time of the audio file"
        ),
        ["cmd_header"] = new TextContainer
        (
            zhCN: "为HTTP请求设置特定的请求头, 例如:\r\n-H \"Cookie: mycookie\" -H \"User-Agent: iOS\"",
            zhTW: "為HTTP請求設置特定的請求頭, 例如:\r\n-H \"Cookie: mycookie\" -H \"User-Agent: iOS\"",
            enUS: "Pass custom header(s) to server, Example:\r\n-H \"Cookie: mycookie\" -H \"User-Agent: iOS\""
        ),
        ["cmd_cookies"] = new TextContainer
        (
            zhCN: "读取 Netscape 格式的 Cookie 文件；手动设置的 Cookie 请求头优先",
            zhTW: "讀取 Netscape 格式的 Cookie 檔案；手動設定的 Cookie 請求標頭優先",
            enUS: "Load cookies from a Netscape cookie file; a custom Cookie header takes precedence"
        ),
        ["cookiesFileReadFailed"] = new TextContainer
        (
            zhCN: "无法读取 Cookie 文件",
            zhTW: "無法讀取 Cookie 檔案",
            enUS: "Unable to read cookie file"
        ),
        ["cookiesFileInvalidLine"] = new TextContainer
        (
            zhCN: "Cookie 文件第 {0} 行格式无效（需要 Netscape 格式）",
            zhTW: "Cookie 檔案第 {0} 行格式無效（需要 Netscape 格式）",
            enUS: "Invalid cookie file format at line {0} (Netscape format required)"
        ),
        ["cookiesFileSkippedLine"] = new TextContainer
        (
            zhCN: "已跳过 Cookie 文件第 {0} 行：名称或值无法用于请求头",
            zhTW: "已略過 Cookie 檔案第 {0} 行：名稱或值無法用於請求標頭",
            enUS: "Skipped cookie file line {0}: name or value cannot be sent in a request header"
        ),
        ["cmd_Input"] = new TextContainer
        (
            zhCN: "链接或文件",
            zhTW: "連結或文件",
            enUS: "Input Url or File"
        ),
        ["cmd_keys"] = new TextContainer
        (
            zhCN: "设置解密密钥, 程序调用mp4decrpyt/shaka-packager/ffmpeg进行解密. 格式:\r\n--key KID1:KEY1 --key KID2:KEY2\r\n对于KEY相同的情况可以直接输入 --key KEY",
            zhTW: "設置解密密鑰, 程序調用mp4decrpyt/shaka-packager/ffmpeg進行解密. 格式:\r\n--key KID1:KEY1 --key KID2:KEY2\r\n對於KEY相同的情況可以直接輸入 --key KEY",
            enUS: "Set decryption key(s) to mp4decrypt/shaka-packager/ffmpeg. format:\r\n--key KID1:KEY1 --key KID2:KEY2\r\nor use --key KEY if all tracks share the same key."
        ),
        ["cmd_keyText"] = new TextContainer
        (
            zhCN: "设置密钥文件,程序将从文件中按KID搜寻KEY以解密.(不建议使用特大文件)",
            zhTW: "設置密鑰文件,程序將從文件中按KID搜尋KEY以解密.(不建議使用特大文件)",
            enUS: "Set the kid-key file, the program will search the KEY with KID from the file.(Very large file are not recommended)"
        ),
        ["cmd_loadKeyFailed"] = new TextContainer
        (
            zhCN: "获取KEY失败，忽略读取.",
            zhTW: "獲取KEY失敗，忽略讀取.",
            enUS: "Failed to get KEY, ignore."
        ),
        ["cmd_logLevel"] = new TextContainer
        (
            zhCN: "设置日志级别",
            zhTW: "設置日誌級別",
            enUS: "Set log level"
        ),
        ["cmd_MP4RealTimeDecryption"] = new TextContainer
        (
            zhCN: "实时解密MP4分片",
            zhTW: "即時解密MP4分片",
            enUS: "Decrypt MP4 segments in real time"
        ),
        ["cmd_saveDir"] = new TextContainer
        (
            zhCN: "设置输出目录",
            zhTW: "設置輸出目錄",
            enUS: "Set output directory"
        ),
        ["cmd_saveName"] = new TextContainer
        (
            zhCN: "设置保存文件名",
            zhTW: "設置保存檔案名",
            enUS: "Set output filename"
        ),
        ["cmd_savePattern"] = new TextContainer
        (
            zhCN: "设置保存文件命名模板. 输入 \"--morehelp save-pattern\" 以查看变量和示例",
            zhTW: "設置保存檔案命名模板. 輸入 \"--morehelp save-pattern\" 以查看變數和範例",
            enUS: "Set output filename pattern. Use \"--morehelp save-pattern\" for variables and examples"
        ),
        ["cmd_savePattern_more"] = new TextContainer
        (
            zhCN: "使用变量设置各轨道的输出文件名主体，程序自动追加输出扩展名.\r\n\r\n" +
                  "* <SaveName>: --save-name 指定的保存名称，未指定时为空\r\n" +
                  "* <Id>: 轨道下载任务ID\r\n" +
                  "* <Codecs>: 编码信息 (如 avc1.64001f, mp4a.40.2)\r\n" +
                  "* <Language>: 语言代码 (如 en, zh-CN)\r\n" +
                  "* <Resolution>: 视频分辨率 (如 1920x1080)\r\n" +
                  "* <Bandwidth>: 码率数值，单位 bit/s (如 5000000)\r\n" +
                  "* <MediaType>: 媒体类型 (VIDEO, AUDIO, SUBTITLES)\r\n" +
                  "* <Channels>: 音频声道信息\r\n" +
                  "* <FrameRate>: 视频帧率\r\n" +
                  "* <VideoRange>: 视频动态范围 (如 SDR, HDR10)\r\n" +
                  "* <GroupId>: 流组标识符\r\n\r\n" +
                  "变量区分大小写，缺失的信息替换为空字符串. 模板不需要包含扩展名.\r\n\r\n" +
                  "例如:\r\n" +
                  "# 按分辨率命名视频\r\n" +
                  "--save-name video --save-pattern \"<SaveName>_<Resolution>\"\r\n" +
                  "# 加入码率 (bit/s)\r\n" +
                  "--save-name video --save-pattern \"<SaveName>_<Resolution>_<Bandwidth>bps\"\r\n" +
                  "# 按语言和声道命名音轨\r\n" +
                  "--save-name audio --save-pattern \"<SaveName>_<Language>_<Channels>\"\r\n" +
                  "# 用任务ID区分多个配置相同的轨道\r\n" +
                  "--save-name video --save-pattern \"<SaveName>_<Id>_<Codecs>\"\r\n",
            zhTW: "使用變數設置各軌道的輸出檔案名稱主體，程式自動附加輸出副檔名.\r\n\r\n" +
                  "* <SaveName>: --save-name 指定的保存名稱，未指定時為空\r\n" +
                  "* <Id>: 軌道下載任務ID\r\n" +
                  "* <Codecs>: 編碼資訊 (如 avc1.64001f, mp4a.40.2)\r\n" +
                  "* <Language>: 語言代碼 (如 en, zh-CN)\r\n" +
                  "* <Resolution>: 影片解析度 (如 1920x1080)\r\n" +
                  "* <Bandwidth>: 碼率數值，單位 bit/s (如 5000000)\r\n" +
                  "* <MediaType>: 媒體類型 (VIDEO, AUDIO, SUBTITLES)\r\n" +
                  "* <Channels>: 音訊聲道資訊\r\n" +
                  "* <FrameRate>: 影片影格率\r\n" +
                  "* <VideoRange>: 影片動態範圍 (如 SDR, HDR10)\r\n" +
                  "* <GroupId>: 串流群組識別碼\r\n\r\n" +
                  "變數區分大小寫，缺失的資訊替換為空字串. 模板不需要包含副檔名.\r\n\r\n" +
                  "例如:\r\n" +
                  "# 按解析度命名影片\r\n" +
                  "--save-name video --save-pattern \"<SaveName>_<Resolution>\"\r\n" +
                  "# 加入碼率 (bit/s)\r\n" +
                  "--save-name video --save-pattern \"<SaveName>_<Resolution>_<Bandwidth>bps\"\r\n" +
                  "# 按語言和聲道命名音軌\r\n" +
                  "--save-name audio --save-pattern \"<SaveName>_<Language>_<Channels>\"\r\n" +
                  "# 用任務ID區分多個配置相同的軌道\r\n" +
                  "--save-name video --save-pattern \"<SaveName>_<Id>_<Codecs>\"\r\n",
            enUS: "Set each track's output filename stem using variables. The output extension is appended automatically.\r\n\r\n" +
                  "* <SaveName>: name specified by --save-name, or empty when omitted\r\n" +
                  "* <Id>: track download task ID\r\n" +
                  "* <Codecs>: codec information (e.g. avc1.64001f, mp4a.40.2)\r\n" +
                  "* <Language>: language code (e.g. en, zh-CN)\r\n" +
                  "* <Resolution>: video resolution (e.g. 1920x1080)\r\n" +
                  "* <Bandwidth>: bitrate value in bit/s (e.g. 5000000)\r\n" +
                  "* <MediaType>: media type (VIDEO, AUDIO, SUBTITLES)\r\n" +
                  "* <Channels>: audio channel information\r\n" +
                  "* <FrameRate>: video frame rate\r\n" +
                  "* <VideoRange>: video dynamic range (e.g. SDR, HDR10)\r\n" +
                  "* <GroupId>: stream group identifier\r\n\r\n" +
                  "Variables are case-sensitive. Missing values become empty strings. Do not include the output extension in the pattern.\r\n\r\n" +
                  "Examples:\r\n" +
                  "# Name video tracks by resolution\r\n" +
                  "--save-name video --save-pattern \"<SaveName>_<Resolution>\"\r\n" +
                  "# Include bitrate (bit/s)\r\n" +
                  "--save-name video --save-pattern \"<SaveName>_<Resolution>_<Bandwidth>bps\"\r\n" +
                  "# Name audio tracks by language and channels\r\n" +
                  "--save-name audio --save-pattern \"<SaveName>_<Language>_<Channels>\"\r\n" +
                  "# Use task IDs to distinguish tracks with the same configuration\r\n" +
                  "--save-name video --save-pattern \"<SaveName>_<Id>_<Codecs>\"\r\n"
        ),
        ["cmd_logFilePath"] = new TextContainer
        (
            zhCN: @"设置日志文件路径, 例如 C:\Logs\log.txt",
            zhTW: @"設定日誌檔案路徑, 例如 C:\Logs\log.txt",
            enUS: @"Set log file path, Example: C:\Logs\log.txt"
        ),
        ["cmd_skipDownload"] = new TextContainer
        (
            zhCN: "跳过下载",
            zhTW: "跳過下載",
            enUS: "Skip download"
        ),
        ["cmd_skipMerge"] = new TextContainer
        (
            zhCN: "跳过合并分片",
            zhTW: "跳過合併分片",
            enUS: "Skip segments merge"
        ),
        ["cmd_subFormat"] = new TextContainer
        (
            zhCN: "字幕输出类型",
            zhTW: "字幕輸出類型",
            enUS: "Subtitle output format"
        ),
        ["cmd_subOnly"] = new TextContainer
        (
            zhCN: "只选取字幕轨道",
            zhTW: "只選取字幕軌道",
            enUS: "Select only subtitle tracks"
        ),
        ["cmd_subtitleFix"] = new TextContainer
        (
            zhCN: "自动修正字幕",
            zhTW: "自動修正字幕",
            enUS: "Automatically fix subtitles"
        ),
        ["cmd_threadCount"] = new TextContainer
        (
            zhCN: "设置下载线程数",
            zhTW: "設置下載執行緒數",
            enUS: "Set download thread count"
        ),
        ["cmd_tmpDir"] = new TextContainer
        (
            zhCN: "设置临时文件存储目录",
            zhTW: "設置臨時文件儲存目錄",
            enUS: "Set temporary file directory"
        ),
        ["cmd_uiLanguage"] = new TextContainer
        (
            zhCN: "设置UI语言",
            zhTW: "設置UI語言",
            enUS: "Set UI language"
        ),
        ["cmd_moreHelp"] = new TextContainer
        (
            zhCN: "查看某个选项的详细帮助信息",
            zhTW: "查看某個選項的詳細幫助訊息",
            enUS: "Set more help info about one option"
        ),
        ["cmd_generateCompletion"] = new TextContainer
        (
            zhCN: "输出内嵌的补全脚本（powershell）",
            zhTW: "輸出內嵌的補全腳本（powershell）",
            enUS: "Print the embedded completion script (powershell)"
        ),
        ["completionShellInvalid"] = new TextContainer
        (
            zhCN: "--generate-completion 需要指定支持的 Shell: powershell",
            zhTW: "--generate-completion 需要指定支援的 Shell: powershell",
            enUS: "--generate-completion requires a supported shell: powershell"
        ),
        ["cmd_urlProcessorArgs"] = new TextContainer
        (
            zhCN: "此字符串将直接传递给URL Processor",
            zhTW: "此字符串將直接傳遞給URL Processor",
            enUS: "Give these arguments to the URL Processors."
        ),
        ["cmd_liveRealTimeMerge"] = new TextContainer
        (
            zhCN: "录制直播时实时合并",
            zhTW: "錄製直播時即時合併",
            enUS: "Real-time merge into file when recording live"
        ),
        ["cmd_networkInterface"] = new TextContainer
        (
            zhCN: "指定请求使用的网卡名或本机 IP，如 eth1 或 192.168.1.10",
            zhTW: "指定請求使用的網卡名稱或本機 IP，如 eth1 或 192.168.1.10",
            enUS: "Use the specified network interface or local IP address, e.g. eth1 or 192.168.1.10"
        ),
        ["networkInterfaceInvalid"] = new TextContainer
        (
            zhCN: "找不到可用的网络接口或本机 IP 地址: {0}",
            zhTW: "找不到可用的網路介面或本機 IP 位址: {0}",
            enUS: "No usable network interface or local IP address found: {0}"
        ),
        ["networkInterfaceUnsupported"] = new TextContainer
        (
            zhCN: "当前系统不支持按网卡名绑定，请指定本机 IP 地址",
            zhTW: "目前系統不支援依網卡名稱綁定，請指定本機 IP 位址",
            enUS: "Binding by interface name is unsupported on this system; specify a local IP address"
        ),
        ["networkInterfaceBindFailed"] = new TextContainer
        (
            zhCN: "无法绑定网络接口或本机 IP 地址 {0}: {1}",
            zhTW: "無法綁定網路介面或本機 IP 位址 {0}: {1}",
            enUS: "Cannot bind network interface or local IP address {0}: {1}"
        ),
        ["networkInterfaceConnectFailed"] = new TextContainer
        (
            zhCN: "无法通过指定网络接口或本机 IP 地址 {0} 连接到 {1}",
            zhTW: "無法透過指定網路介面或本機 IP 位址 {0} 連線至 {1}",
            enUS: "Cannot connect to {1} using network interface or local IP address {0}"
        ),
        ["cmd_customProxy"] = new TextContainer
        (
            zhCN: "设置请求代理, 如 http://127.0.0.1:8888",
            zhTW: "設置請求代理, 如 http://127.0.0.1:8888",
            enUS: "Set web request proxy, like http://127.0.0.1:8888"
        ),
        ["cmd_customRange"] = new TextContainer
        (
            zhCN: "仅下载部分分片. 输入 \"--morehelp custom-range\" 以查看详细信息",
            zhTW: "僅下載部分分片. 輸入 \"--morehelp custom-range\" 以查看詳細訊息",
            enUS: "Download only part of the segments. Use \"--morehelp custom-range\" for more details"
        ),
        ["cmd_useSystemProxy"] = new TextContainer
        (
            zhCN: "使用系统默认代理",
            zhTW: "使用系統默認代理",
            enUS: "Use system default proxy"
        ),
        ["cmd_livePerformAsVod"] = new TextContainer
        (
            zhCN: "以点播方式下载直播流",
            zhTW: "以點播方式下載直播流",
            enUS: "Download live streams as vod"
        ),
        ["cmd_liveWaitTime"] = new TextContainer
        (
            zhCN: "手动设置直播列表刷新间隔",
            zhTW: "手動設置直播列表刷新間隔",
            enUS: "Manually set the live playlist refresh interval"
        ),
        ["cmd_liveIdleTimeout"] = new TextContainer
        (
            zhCN: "直播列表连续指定秒数无新分片时停止录制（默认关闭）",
            zhTW: "直播列表連續指定秒數無新分片時停止錄製（預設關閉）",
            enUS: "Stop recording when a live playlist has no new segments for this many seconds (disabled by default)"
        ),
        ["cmd_adKeyword"] = new TextContainer
        (
            zhCN: "设置广告分片的URL关键字(正则表达式)",
            zhTW: "設置廣告分片的URL關鍵字(正則表達式)",
            enUS: "Set URL keywords (regular expressions) for AD segments"
        ),
        ["vodPartsConcat"] = new TextContainer
        (
            zhCN: "正在拼接 {0} 个独立初始化的点播段...",
            zhTW: "正在拼接 {0} 個獨立初始化的點播段...",
            enUS: "Concatenating {0} independently initialized VOD sections..."
        ),
        ["vodPeriodsPlanned"] = new TextContainer
        (
            zhCN: "点播：已为 {1} 规划 {0} 个 Period",
            zhTW: "點播：已為 {1} 規劃 {0} 個 Period",
            enUS: "VOD: {0} Periods for {1}"
        ),
        ["vodPeriodNoMatch"] = new TextContainer
        (
            zhCN: "Period {0}：没有找到与 {1} 匹配的媒体流。",
            zhTW: "Period {0}：沒有找到與 {1} 匹配的媒體流。",
            enUS: "Period {0}: no matching representation for {1}."
        ),
        ["vodPeriodIncompatible"] = new TextContainer
        (
            zhCN: "Period {0}：与 {1} 的编码或声道配置不兼容。请用 --vod-select-parts 选择要保留的配置，或用 --vod-drop-parts 排除不需要的段。",
            zhTW: "Period {0}：與 {1} 的編碼或聲道配置不相容。請用 --vod-select-parts 選擇要保留的配置，或用 --vod-drop-parts 排除不需要的段。",
            enUS: "Period {0}: incompatible codec/channel configuration for {1}. Use --vod-select-parts to keep desired configurations, or --vod-drop-parts to exclude unwanted sections."
        ),
        ["vodPartsIncompatible"] = new TextContainer
        (
            zhCN: "点播段的编码、采样率或声道配置不兼容，已保留各段输出文件。请用 --vod-select-parts 选择要保留的配置，或用 --vod-drop-parts 排除不需要的段。",
            zhTW: "點播段的編碼、取樣率或聲道配置不相容，已保留各段輸出檔案。請用 --vod-select-parts 選擇要保留的配置，或用 --vod-drop-parts 排除不需要的段。",
            enUS: "VOD sections have incompatible codec/sample-rate/channel configurations; separate outputs are preserved. Use --vod-select-parts to keep desired configurations, or --vod-drop-parts to exclude unwanted sections."
        ),
        ["vodPartStillEncrypted"] = new TextContainer
        (
            zhCN: "点播段仍含加密媒体，已停止拼接并保留下载文件。请提供完整的解密密钥后重试。",
            zhTW: "點播段仍含加密媒體，已停止拼接並保留下載檔案。請提供完整的解密金鑰後重試。",
            enUS: "A VOD section still contains encrypted media. Concatenation stopped and downloaded files are preserved. Retry with all required decryption keys."
        ),
        ["webmInvalid"] = new TextContainer
        (
            zhCN: "WebM 元素格式无效。",
            zhTW: "WebM 元素格式無效。",
            enUS: "Invalid WebM element."
        ),
        ["vodMediaOutsidePeriod"] = new TextContainer
        (
            zhCN: "Period {0}：媒体超出该段的播放时间范围。",
            zhTW: "Period {0}：媒體超出該段的播放時間範圍。",
            enUS: "Period {0}: media lies outside its presentation interval."
        ),
        ["vodDropPartsInvalid"] = new TextContainer
        (
            zhCN: "--vod-drop-parts 编号格式无效，请使用 0,2-4 这样的格式。",
            zhTW: "--vod-drop-parts 編號格式無效，請使用 0,2-4 這樣的格式。",
            enUS: "Invalid --vod-drop-parts: use IDs such as 0,2-4."
        ),
        ["vodDropPartsRangeInvalid"] = new TextContainer
        (
            zhCN: "--vod-drop-parts 编号范围无效。",
            zhTW: "--vod-drop-parts 編號範圍無效。",
            enUS: "Invalid --vod-drop-parts range."
        ),
        ["vodPartIdsUnknown"] = new TextContainer
        (
            zhCN: "不存在的点播段编号：{0}。请用 --vod-list-parts 查看编号。",
            zhTW: "不存在的點播段編號：{0}。請用 --vod-list-parts 查看編號。",
            enUS: "Unknown VOD part IDs: {0}. Use --vod-list-parts."
        ),
        ["vodSelectAtLeastOne"] = new TextContainer
        (
            zhCN: "请至少选择一组要保留的点播配置。",
            zhTW: "請至少選擇一組要保留的點播配置。",
            enUS: "Select at least one VOD configuration."
        ),
        ["vodPartsRequireVod"] = new TextContainer
        (
            zhCN: "点播选段选项仅适用于点播清单。",
            zhTW: "點播選段選項僅適用於點播清單。",
            enUS: "VOD part selection options require a VOD playlist."
        ),
        ["vodPartsRequireInteractive"] = new TextContainer
        (
            zhCN: "--vod-select-parts 需要可交互的终端。脚本请使用 --vod-drop-parts。",
            zhTW: "--vod-select-parts 需要可互動的終端。指令碼請使用 --vod-drop-parts。",
            enUS: "--vod-select-parts requires an interactive terminal. Use --vod-drop-parts for scripts."
        ),
        ["hlsMediaOriginReadFailed"] = new TextContainer
        (
            zhCN: "无法读取 HLS 媒体的时间戳起点。",
            zhTW: "無法讀取 HLS 媒體的時間戳起點。",
            enUS: "Cannot read HLS media timestamp origin."
        ),
        ["hlsSubtitleOriginMissing"] = new TextContainer
        (
            zhCN: "无法确定字幕同步所需的 HLS 媒体时间戳起点。",
            zhTW: "無法確定字幕同步所需的 HLS 媒體時間戳起點。",
            enUS: "Cannot determine the HLS media timestamp origin for subtitles."
        ),
        ["hlsTimestampMapInvalid"] = new TextContainer
        (
            zhCN: "HLS X-TIMESTAMP-MAP 格式无效。",
            zhTW: "HLS X-TIMESTAMP-MAP 格式無效。",
            enUS: "Invalid HLS X-TIMESTAMP-MAP."
        ),
        ["hlsByteRangeMissingPrevious"] = new TextContainer
        (
            zhCN: "HLS BYTERANGE 省略偏移时，必须存在前一个字节范围。",
            zhTW: "HLS BYTERANGE 省略偏移時，必須存在前一個位元組範圍。",
            enUS: "Implicit HLS BYTERANGE requires a preceding byte range."
        ),
        ["hlsInvalidDuration"] = new TextContainer
        (
            zhCN: "HLS 分片时长无效，清单中也没有可用于估算的有效时长。",
            zhTW: "HLS 分片時長無效，清單中也沒有可用於估算的有效時長。",
            enUS: "Invalid HLS segment duration, with no valid playlist duration available for estimation."
        ),
        ["hlsInvalidDurationFallback"] = new TextContainer
        (
            zhCN: "检测到异常 HLS 分片时长，已用清单中的有效时长估算；录制时长可能存在偏差。",
            zhTW: "偵測到異常 HLS 分片時長，已用清單中的有效時長估算；錄製時長可能存在偏差。",
            enUS: "Invalid HLS segment durations were estimated from valid playlist durations; recording duration may be approximate."
        ),
        ["mediaPartInputMismatch"] = new TextContainer
        (
            zhCN: "每个媒体段必须对应一个输入文件。",
            zhTW: "每個媒體段必須對應一個輸入檔案。",
            enUS: "Each media part must have one input file."
        ),
        ["concatInputPathInvalid"] = new TextContainer
        (
            zhCN: "拼接输入文件的路径无效。",
            zhTW: "拼接輸入檔案的路徑無效。",
            enUS: "Invalid concat input path."
        ),
        ["tfdtVersionUnsupported"] = new TextContainer
        (
            zhCN: "TFDT 版本只能为 0 或 1。",
            zhTW: "TFDT 版本只能為 0 或 1。",
            enUS: "TFDT version can only be 0 or 1."
        ),
        ["vodPartIdsLabel"] = new TextContainer
        (
            zhCN: "编号",
            zhTW: "編號",
            enUS: "IDs"
        ),
        ["downloadCancelled"] = new TextContainer
        (
            zhCN: "已取消下载。",
            zhTW: "已取消下載。",
            enUS: "Download cancelled."
        ),
        ["cmd_vodSelectParts"] = new TextContainer
        (
            zhCN: "控制点播选段交互：不传则自动判断，true 强制显示，false 关闭（空格勾选，回车确认）",
            zhTW: "控制點播選段互動：未指定則自動判斷，true 強制顯示，false 關閉（空格勾選，確認鍵完成）",
            enUS: "Control VOD section selection: omitted = automatic, true = always prompt, false = disable (Space/Enter)"
        ),
        ["vodReadingConfigs"] = new TextContainer(zhCN: "正在识别点播媒体配置…", zhTW: "正在識別點播媒體配置…", enUS: "Inspecting VOD media configurations…"),
        ["vodPromptTitle"] = new TextContainer
        (
            zhCN: "请选择[green]要保留的点播段[/]（按配置和时长归组，默认全部保留）：",
            zhTW: "請選擇[green]要保留的點播段[/]（按配置和時長歸組，預設全部保留）：",
            enUS: "Select [green]VOD sections to keep[/] (grouped by configuration and duration; all kept by default):"
        ),
        ["vodPromptInfo"] = new TextContainer
        (
            zhCN: "(按 [blue]空格键[/] 勾选/取消，按 [green]回车键[/] 确认；音视频和字幕同步处理)",
            zhTW: "(按 [blue]空格鍵[/] 勾選/取消，按 [green]確認鍵[/] 完成；音訊視訊和字幕同步處理)",
            enUS: "(Press [blue]<space>[/] to toggle, [green]<enter>[/] to accept; audio/video/subtitles stay together)"
        ),
        ["vodSectionDuration"] = new TextContainer(zhCN: "每段 {0} 秒", zhTW: "每段 {0} 秒", enUS: "{0} s per section"),
        ["vodPartCount"] = new TextContainer(zhCN: "{0} 段", zhTW: "{0} 段", enUS: "{0} sections"),
        ["vodAvailableConfigs"] = new TextContainer(zhCN: "{0} 种可选配置", zhTW: "{0} 種可選配置", enUS: "{0} available configurations"),
        ["vodConfigUnknown"] = new TextContainer(zhCN: "配置未验证", zhTW: "配置未驗證", enUS: "unverified configuration"),
        ["cmd_vodListParts"] = new TextContainer
        (
            zhCN: "按媒体配置归组列出点播段的编号和总时长后退出",
            zhTW: "按媒體配置歸組列出點播段的編號和總時長後退出",
            enUS: "List VOD sections grouped by media configuration, with IDs and total durations, then exit"
        ),
        ["cmd_vodDropParts"] = new TextContainer
        (
            zhCN: "按 --vod-list-parts 的编号删除整个点播段及对应音频/字幕，例如 0,2-4",
            zhTW: "按 --vod-list-parts 的編號刪除整個點播段及對應音訊/字幕，例如 0,2-4",
            enUS: "Drop VOD sections and matching audio/subtitles by --vod-list-parts IDs, e.g. 0,2-4"
        ),
        ["cmd_liveTakeCount"] = new TextContainer
        (
            zhCN: "手动设置录制直播时首次获取分片的数量",
            zhTW: "手動設置錄製直播時首次獲取分片的數量",
            enUS: "Manually set the number of segments downloaded for the first time when recording live"
        ),
        ["cmd_customHLSMethod"] = new TextContainer
        (
            zhCN: "指定HLS加密方式 (AES_128|AES_128_ECB|CENC|CHACHA20|NONE|SAMPLE_AES|SAMPLE_AES_CTR|UNKNOWN)",
            zhTW: "指定HLS加密方式 (AES_128|AES_128_ECB|CENC|CHACHA20|NONE|SAMPLE_AES|SAMPLE_AES_CTR|UNKNOWN)",
            enUS: "Set HLS encryption method (AES_128|AES_128_ECB|CENC|CHACHA20|NONE|SAMPLE_AES|SAMPLE_AES_CTR|UNKNOWN)"
        ),
        ["cmd_customHLSKey"] = new TextContainer
        (
            zhCN: "指定HLS解密KEY. 可以是文件, HEX或Base64",
            zhTW: "指定HLS解密KEY. 可以是文件, HEX或Base64",
            enUS: "Set the HLS decryption key. Can be file, HEX or Base64"
        ),
        ["cmd_customHLSIv"] = new TextContainer
        (
            zhCN: "指定HLS解密IV. 可以是文件, HEX或Base64",
            zhTW: "指定HLS解密IV. 可以是文件, HEX或Base64",
            enUS: "Set the HLS decryption iv. Can be file, HEX or Base64"
        ),
        ["cmd_customHLSScope"] = new TextContainer
        (
            zhCN: "指定自定义HLS加密方式、KEY和IV的适用范围 (ALL|VIDEO|AUDIO)",
            zhTW: "指定自訂HLS加密方式、KEY和IV的適用範圍 (ALL|VIDEO|AUDIO)",
            enUS: "Apply custom HLS method, key and IV to selected media type (ALL|VIDEO|AUDIO)"
        ),
        ["cmd_livePipeMux"] = new TextContainer
        (
            zhCN: "录制直播并开启实时合并时通过管道+ffmpeg实时混流到TS文件",
            zhTW: "錄製直播並開啟即時合併時通過管道+ffmpeg即時混流到TS文件",
            enUS: "Real-time muxing to TS file through pipeline + ffmpeg (liveRealTimeMerge enabled)"
        ),
        ["cmd_liveKeepSegments"] = new TextContainer
        (
            zhCN: "录制直播并开启实时合并时依然保留分片",
            zhTW: "錄製直播並開啟即時合併時依然保留分片",
            enUS: "Keep segments when recording a live (liveRealTimeMerge enabled)"
        ),
        ["cmd_liveRecordLimit"] = new TextContainer
        (
            zhCN: "录制直播时的录制时长限制",
            zhTW: "錄製直播時的錄製時長限制",
            enUS: "Recording time limit when recording live"
        ),
        ["cmd_taskStartAt"] = new TextContainer
        (
            zhCN: "在此时间之前不会开始执行任务",
            zhTW: "在此時間之前不會開始執行任務",
            enUS: "Task execution will not start before this time"
        ),
        ["cmd_useShakaPackager"] = new TextContainer
        (
            zhCN: "解密时使用shaka-packager替代mp4decrypt",
            zhTW: "解密時使用shaka-packager替代mp4decrypt",
            enUS: "Use shaka-packager instead of mp4decrypt to decrypt"
        ),
        ["cmd_decryptionEngine"] = new TextContainer
        (
            zhCN: "设置解密时使用的第三方程序",
            zhTW: "設置解密時使用的第三方程序",
            enUS: "Set the third-party program used for decryption"
        ),
        ["cmd_concurrentDownload"] = new TextContainer
        (
            zhCN: "并发下载已选择的音频、视频和字幕",
            zhTW: "並發下載已選擇的音訊、影片和字幕",
            enUS: "Concurrently download the selected audio, video and subtitles"
        ),
        ["cmd_selectVideo"] = new TextContainer
        (
            zhCN: "通过正则表达式选择符合要求的视频流. 输入 \"--morehelp select-video\" 以查看详细信息",
            zhTW: "通過正則表達式選擇符合要求的影片軌. 輸入 \"--morehelp select-video\" 以查看詳細訊息",
            enUS: "Select video streams by regular expressions. Use \"--morehelp select-video\" for more details"
        ),
        ["cmd_dropVideo"] = new TextContainer
        (
            zhCN: "通过正则表达式去除符合要求的视频流. 支持与 --select-video 相同的参数, 输入 \"--morehelp select-video\" 以查看详细信息",
            zhTW: "通過正則表達式去除符合要求的影片串流. 支援與 --select-video 相同的參數, 輸入 \"--morehelp select-video\" 以查看詳細訊息",
            enUS: "Drop video streams by regular expressions. Accepts the same options as --select-video, use \"--morehelp select-video\" for more details"
        ),
        ["cmd_selectVideo_more"] = new TextContainer
        (
            zhCN: "通过正则表达式选择符合要求的视频流. 你能够以:分隔形式指定如下参数.\r\n" +
                  "同样的参数也适用于 --select-audio/-sa, --select-subtitle/-ss 以及对应的 --drop-video/--drop-audio/--drop-subtitle 选项.\r\n\r\n" +
                  "* id=REGEX: 按 GroupId 匹配\r\n" +
                  "* lang=REGEX: 按语言代码匹配\r\n" +
                  "* name=REGEX: 按名称匹配\r\n" +
                  "* codecs=REGEX: 按编码匹配 (如 hvc1, avc1, mp4a)\r\n" +
                  "* res=REGEX: 按分辨率匹配 (如 1920*, 3840*)\r\n" +
                  "* frame=REGEX: 按帧率匹配\r\n" +
                  "* channel=REGEX: 按音频声道数匹配 (如 6, 2)\r\n" +
                  "* range=REGEX: 按视频动态范围匹配 (如 SDR, HDR, PQ)\r\n" +
                  "* url=REGEX: 按分片URL匹配\r\n" +
                  "* period=REGEX: 按 DASH Period id 匹配 (多Period MPD, 如广告/分段)\r\n" +
                  "* segsMin=number: 仅保留分片数 >= number 的流\r\n" +
                  "* segsMax=number: 仅保留分片数 <= number 的流\r\n" +
                  "* plistDurMin=hms: 仅保留时长 >= hms 的流 (如 1h20m30s, 90s)\r\n" +
                  "* plistDurMax=hms: 仅保留时长 <= hms 的流\r\n" +
                  "* bwMin=int: 仅保留码率 >= int Kbps 的流\r\n" +
                  "* bwMax=int: 仅保留码率 <= int Kbps 的流\r\n" +
                  "* role=string: 按 DASH role 匹配 (Subtitle, Main, Alternate, Supplementary, Commentary, Dub, Description, Sign, Metadata, ForcedSubtitle)\r\n" +
                  "* for=FOR: 选择方式. best[number], worst[number], all (默认: best)\r\n\r\n" +
                  "例如: \r\n" +
                  "# 选择最佳视频\r\n" +
                  "-sv best\r\n" +
                  "# 选择4K+HEVC视频\r\n" +
                  "-sv res=\"3840*\":codecs=hvc1:for=best\r\n" +
                  "# 选择长度大于1小时20分钟30秒的视频\r\n" +
                  "-sv plistDurMin=\"1h20m30s\":for=best\r\n" +
                  "-sv role=\"main\":for=best\r\n" +
                  "# 选择码率在800Kbps至1Mbps之间的视频\r\n" +
                  "-sv bwMin=800:bwMax=1000\r\n" +
                  "# 去除分片数不超过2的字幕流 (如 trick-play/广告列表)\r\n" +
                  "-ds segsMax=2:for=all --auto-select\r\n" +
                  "# 仅保留主内容 Period (排除广告 Period)\r\n" +
                  "-sv period=\"main\":for=best\r\n" +
                  "# 去除广告 Period 的视频\r\n" +
                  "-dv period=\"ad\":for=all\r\n",
            zhTW: "通過正則表達式選擇符合要求的影片軌. 你能夠以:分隔形式指定如下參數.\r\n" +
                  "同樣的參數也適用於 --select-audio/-sa, --select-subtitle/-ss 以及對應的 --drop-video/--drop-audio/--drop-subtitle 選項.\r\n\r\n" +
                  "* id=REGEX: 按 GroupId 匹配\r\n" +
                  "* lang=REGEX: 按語言代碼匹配\r\n" +
                  "* name=REGEX: 按名稱匹配\r\n" +
                  "* codecs=REGEX: 按編碼匹配 (如 hvc1, avc1, mp4a)\r\n" +
                  "* res=REGEX: 按解析度匹配 (如 1920*, 3840*)\r\n" +
                  "* frame=REGEX: 按影格率匹配\r\n" +
                  "* channel=REGEX: 按音訊聲道數匹配 (如 6, 2)\r\n" +
                  "* range=REGEX: 按影片動態範圍匹配 (如 SDR, HDR, PQ)\r\n" +
                  "* url=REGEX: 按分片URL匹配\r\n" +
                  "* period=REGEX: 按 DASH Period id 匹配 (多Period MPD, 如廣告/分段)\r\n" +
                  "* segsMin=number: 僅保留分片數 >= number 的串流\r\n" +
                  "* segsMax=number: 僅保留分片數 <= number 的串流\r\n" +
                  "* plistDurMin=hms: 僅保留時長 >= hms 的串流 (如 1h20m30s, 90s)\r\n" +
                  "* plistDurMax=hms: 僅保留時長 <= hms 的串流\r\n" +
                  "* bwMin=int: 僅保留碼率 >= int Kbps 的串流\r\n" +
                  "* bwMax=int: 僅保留碼率 <= int Kbps 的串流\r\n" +
                  "* role=string: 按 DASH role 匹配 (Subtitle, Main, Alternate, Supplementary, Commentary, Dub, Description, Sign, Metadata, ForcedSubtitle)\r\n" +
                  "* for=FOR: 選擇方式. best[number], worst[number], all (默認: best)\r\n\r\n" +
                  "例如: \r\n" +
                  "# 選擇最佳影片\r\n" +
                  "-sv best\r\n" +
                  "# 選擇4K+HEVC影片\r\n" +
                  "-sv res=\"3840*\":codecs=hvc1:for=best\r\n" +
                  "# 選擇長度大於1小時20分鐘30秒的影片\r\n" +
                  "-sv plistDurMin=\"1h20m30s\":for=best\r\n" +
                  "-sv role=\"main\":for=best\r\n" +
                  "# 選擇碼率在800Kbps至1Mbps之間的影片\r\n" +
                  "-sv bwMin=800:bwMax=1000\r\n" +
                  "# 去除分片數不超過2的字幕串流 (如 trick-play/廣告列表)\r\n" +
                  "-ds segsMax=2:for=all --auto-select\r\n" +
                  "# 僅保留主內容 Period (排除廣告 Period)\r\n" +
                  "-sv period=\"main\":for=best\r\n" +
                  "# 去除廣告 Period 的影片\r\n" +
                  "-dv period=\"ad\":for=all\r\n",
            enUS: "Select video streams by regular expressions. OPTIONS is a colon (:) separated list of the following sub-keys.\r\n" +
                  "The same sub-keys also work for --select-audio/-sa, --select-subtitle/-ss and the matching --drop-video/--drop-audio/--drop-subtitle options.\r\n\r\n" +
                  "* id=REGEX: match by group/stream id\r\n" +
                  "* lang=REGEX: match by language code\r\n" +
                  "* name=REGEX: match by stream name\r\n" +
                  "* codecs=REGEX: match by codecs (e.g. hvc1, avc1, mp4a)\r\n" +
                  "* res=REGEX: match by resolution (e.g. 1920*, 3840*)\r\n" +
                  "* frame=REGEX: match by frame rate\r\n" +
                  "* channel=REGEX: match by audio channel count (e.g. 6, 2)\r\n" +
                  "* range=REGEX: match by video range (e.g. SDR, HDR, PQ)\r\n" +
                  "* url=REGEX: match by segment url\r\n" +
                  "* period=REGEX: match by DASH Period id (multi-Period MPD, e.g. ads/chapters)\r\n" +
                  "* segsMin=number: keep streams with at least number segments\r\n" +
                  "* segsMax=number: keep streams with at most number segments\r\n" +
                  "* plistDurMin=hms: keep streams whose playlist duration >= hms (e.g. 1h20m30s, 90s)\r\n" +
                  "* plistDurMax=hms: keep streams whose playlist duration <= hms\r\n" +
                  "* bwMin=int: keep streams with bandwidth >= int Kbps\r\n" +
                  "* bwMax=int: keep streams with bandwidth <= int Kbps\r\n" +
                  "* role=string: match by DASH role (Subtitle, Main, Alternate, Supplementary, Commentary, Dub, Description, Sign, Metadata, ForcedSubtitle)\r\n" +
                  "* for=FOR: how many of the matched streams to keep. best[number], worst[number], all (Default: best)\r\n\r\n" +
                  "Examples: \r\n" +
                  "# select best video\r\n" +
                  "-sv best\r\n" +
                  "# select 4K+HEVC video\r\n" +
                  "-sv res=\"3840*\":codecs=hvc1:for=best\r\n" +
                  "# Select best video with duration longer than 1 hour 20 minutes 30 seconds\r\n" +
                  "-sv plistDurMin=\"1h20m30s\":for=best\r\n" +
                  "-sv role=\"main\":for=best\r\n" +
                  "# Select video with bandwidth between 800Kbps and 1Mbps\r\n" +
                  "-sv bwMin=800:bwMax=1000\r\n" +
                  "# Drop subtitle streams that have at most 2 segments (e.g. trick-play/ad playlists)\r\n" +
                  "-ds segsMax=2:for=all --auto-select\r\n" +
                  "# Keep only the main content Period (exclude ad Periods)\r\n" +
                  "-sv period=\"main\":for=best\r\n" +
                  "# Drop video from the ad Period\r\n" +
                  "-dv period=\"ad\":for=all\r\n"
        ),
        ["cmd_selectAudio"] = new TextContainer
        (
            zhCN: "通过正则表达式选择符合要求的音频流. 输入 \"--morehelp select-audio\" 以查看详细信息",
            zhTW: "通過正則表達式選擇符合要求的音軌. 輸入 \"--morehelp select-audio\" 以查看詳細訊息",
            enUS: "Select audio streams by regular expressions. Use \"--morehelp select-audio\" for more details"
        ),
        ["cmd_dropAudio"] = new TextContainer
        (
            zhCN: "通过正则表达式去除符合要求的音频流. 支持与 --select-video 相同的参数, 输入 \"--morehelp select-video\" 以查看详细信息",
            zhTW: "通過正則表達式去除符合要求的音軌. 支援與 --select-video 相同的參數, 輸入 \"--morehelp select-video\" 以查看詳細訊息",
            enUS: "Drop audio streams by regular expressions. Accepts the same options as --select-video, use \"--morehelp select-video\" for more details"
        ),
        ["cmd_selectAudio_more"] = new TextContainer
        (
            zhCN: "通过正则表达式选择符合要求的音频流. 参考 --select-video\r\n\r\n" +
                  "例如: \r\n" +
                  "# 选择所有音频\r\n" +
                  "-sa all\r\n" +
                  "# 选择最佳英语音轨\r\n" +
                  "-sa lang=en:for=best\r\n" +
                  "# 选择最佳的2条英语(或日语)音轨\r\n" +
                  "-sa lang=\"ja|en\":for=best2\r\n" +
                  "-sa role=\"main\":for=best\r\n",
            zhTW: "通過正則表達式選擇符合要求的音軌. 參考 --select-video\r\n\r\n" +
                  "例如: \r\n" +
                  "# 選擇所有音訊\r\n" +
                  "-sa all\r\n" +
                  "# 選擇最佳英語音軌\r\n" +
                  "-sa lang=en:for=best\r\n" +
                  "# 選擇最佳的2條英語(或日語)音軌\r\n" +
                  "-sa lang=\"ja|en\":for=best2\r\n" +
                  "-sa role=\"main\":for=best\r\n",
            enUS: "Select audio streams by regular expressions. ref --select-video\r\n\r\n" +
                  "Examples: \r\n" +
                  "# select all\r\n" +
                  "-sa all\r\n" +
                  "# select best eng audio\r\n" +
                  "-sa lang=en:for=best\r\n" +
                  "# select best 2, and language is ja or en\r\n" +
                  "-sa lang=\"ja|en\":for=best2\r\n" +
                  "-sa role=\"main\":for=best\r\n"
        ),
        ["cmd_selectSubtitle"] = new TextContainer
        (
            zhCN: "通过正则表达式选择符合要求的字幕流. 输入 \"--morehelp select-subtitle\" 以查看详细信息",
            zhTW: "通過正則表達式選擇符合要求的字幕流. 輸入 \"--morehelp select-subtitle\" 以查看詳細訊息",
            enUS: "Select subtitle streams by regular expressions. Use \"--morehelp select-subtitle\" for more details"
        ),
        ["cmd_dropSubtitle"] = new TextContainer
        (
            zhCN: "通过正则表达式去除符合要求的字幕流. 支持与 --select-video 相同的参数, 输入 \"--morehelp select-video\" 以查看详细信息",
            zhTW: "通過正則表達式去除符合要求的字幕流. 支援與 --select-video 相同的參數, 輸入 \"--morehelp select-video\" 以查看詳細訊息",
            enUS: "Drop subtitle streams by regular expressions. Accepts the same options as --select-video, use \"--morehelp select-video\" for more details"
        ),
        ["cmd_custom_range"] = new TextContainer
        (
            zhCN: "下载点播内容时, 仅下载部分分片.\r\n\r\n" +
                  "时间格式为 MM:SS 或 HH:MM:SS，省略起点表示从头开始，省略终点表示下载到末尾.\r\n" +
                  "时间范围按分片起始时间筛选（包含起止边界），保留完整分片，不进行精确裁切.\r\n\r\n" +
                  "例如: \r\n" +
                  "# 下载[0,10]共11个分片\r\n" +
                  "--custom-range 0-10\r\n" +
                  "# 下载从序号10开始的后续分片\r\n" +
                  "--custom-range 10-\r\n" +
                  "# 下载前100个分片\r\n" +
                  "--custom-range -99\r\n" +
                  "# 下载第5分钟到20分钟的内容\r\n" +
                  "--custom-range 05:00-20:00\r\n" +
                  "# 跳过前26秒，下载后续内容\r\n" +
                  "--custom-range 00:26-\r\n" +
                  "# 仅下载前26秒的内容（可能包含跨越26秒边界的完整分片）\r\n" +
                  "--custom-range -00:26\r\n",
            zhTW: "下載點播內容時, 僅下載部分分片.\r\n\r\n" +
                  "時間格式為 MM:SS 或 HH:MM:SS，省略起點表示從頭開始，省略終點表示下載到末尾.\r\n" +
                  "時間範圍按分片起始時間篩選（包含起止邊界），保留完整分片，不進行精確裁切.\r\n\r\n" +
                  "例如: \r\n" +
                  "# 下載[0,10]共11個分片\r\n" +
                  "--custom-range 0-10\r\n" +
                  "# 下載從序號10開始的後續分片\r\n" +
                  "--custom-range 10-\r\n" +
                  "# 下載前100個分片\r\n" +
                  "--custom-range -99\r\n" +
                  "# 下載第5分鐘到20分鐘的內容\r\n" +
                  "--custom-range 05:00-20:00\r\n" +
                  "# 跳過前26秒，下載後續內容\r\n" +
                  "--custom-range 00:26-\r\n" +
                  "# 僅下載前26秒的內容（可能包含跨越26秒邊界的完整分片）\r\n" +
                  "--custom-range -00:26\r\n",
            enUS: "Download only part of the segments when downloading vod content.\r\n\r\n" +
                  "Use MM:SS or HH:MM:SS. Omit the start to download from the beginning, or omit the end to download to the end.\r\n" +
                  "Time ranges select segments by their start times, including both boundaries. Whole segments are retained; no precise trimming is performed.\r\n\r\n" +
                  "Examples: \r\n" +
                  "# Download [0,10], a total of 11 segments\r\n" +
                  "--custom-range 0-10\r\n" +
                  "# Download subsequent segments starting from index 10\r\n" +
                  "--custom-range 10-\r\n" +
                  "# Download the first 100 segments\r\n" +
                  "--custom-range -99\r\n" +
                  "# Download content from the 05:00 to 20:00\r\n" +
                  "--custom-range 05:00-20:00\r\n" +
                  "# Skip the first 26 seconds and download the remaining content\r\n" +
                  "--custom-range 00:26-\r\n" +
                  "# Download only the first 26 seconds (whole segments may extend beyond 26 seconds)\r\n" +
                  "--custom-range -00:26\r\n"
        ),
        ["cmd_selectSubtitle_more"] = new TextContainer
        (
            zhCN: "通过正则表达式选择符合要求的字幕流. 参考 --select-video\r\n\r\n" +
                  "例如: \r\n" +
                  "# 选择所有字幕\r\n" +
                  "-ss all\r\n" +
                  "# 选择所有带有\"中文\"的字幕\r\n" +
                  "-ss name=\"中文\":for=all\r\n",
            zhTW: "通過正則表達式選擇符合要求的字幕流. 參考 --select-video\r\n\r\n" +
                  "例如: \r\n" +
                  "# 選擇所有字幕\r\n" +
                  "-ss all\r\n" +
                  "# 選擇所有帶有\"中文\"的字幕\r\n" +
                  "-ss name=\"中文\":for=all\r\n",
            enUS: "Select subtitle streams by regular expressions. ref --select-video\r\n\r\n" +
                  "Examples: \r\n" +
                  "# select all subs\r\n" +
                  "-ss all\r\n" +
                  "# select all subs containing \"English\"\r\n" +
                  "-ss name=\"English\":for=all\r\n"
        ),
        ["cmd_muxAfterDone_more"] = new TextContainer
        (
            zhCN: "所有工作完成时尝试混流分离的音视频. 你能够以:分隔形式指定如下参数:\r\n\r\n" +
                  "* format=FORMAT: 指定混流容器 mkv, mp4, ts\r\n" +
                  "* muxer=MUXER: 指定混流程序 ffmpeg, mkvmerge (默认: ffmpeg)\r\n" +
                  "* bin_path=PATH: 指定程序路径 (默认: 自动寻找)\r\n" +
                  "* skip_sub=BOOL: 是否忽略字幕文件 (默认: false)\r\n" +
                  "* keep=BOOL: 混流完成是否保留文件 true, false (默认: false)\r\n\r\n" +
                  "例如: \r\n" +
                  "# 混流为mp4容器\r\n" +
                  "-M format=mp4\r\n" +
                  "# 使用mkvmerge, 自动寻找程序\r\n" +
                  "-M format=mkv:muxer=mkvmerge\r\n" +
                  "# 使用mkvmerge, 自定义程序路径\r\n" +
                  "-M format=mkv:muxer=mkvmerge:bin_path=\"C\\:\\Program Files\\MKVToolNix\\mkvmerge.exe\"\r\n",
            zhTW: "所有工作完成時嘗試混流分離的影音. 你能夠以:分隔形式指定如下參數:\r\n\r\n" +
                  "* format=FORMAT: 指定混流容器 mkv, mp4, ts\r\n" +
                  "* muxer=MUXER: 指定混流程序 ffmpeg, mkvmerge (默認: ffmpeg)\r\n" +
                  "* bin_path=PATH: 指定程序路徑 (默認: 自動尋找)\r\n" +
                  "* skip_sub=BOOL: 是否忽略字幕文件 (默認: false)\r\n" +
                  "* keep=BOOL: 混流完成是否保留文件 true, false (默認: false)\r\n\r\n" +
                  "例如: \r\n" +
                  "# 混流為mp4容器\r\n" +
                  "-M format=mp4\r\n" +
                  "# 使用mkvmerge, 自動尋找程序\r\n" +
                  "-M format=mkv:muxer=mkvmerge\r\n" +
                  "# 使用mkvmerge, 自訂程序路徑\r\n" +
                  "-M format=mkv:muxer=mkvmerge:bin_path=\"C\\:\\Program Files\\MKVToolNix\\mkvmerge.exe\"\r\n",
            enUS: "When all works is done, try to mux the downloaded streams. OPTIONS is a colon separated list of:\r\n\r\n" +
                  "* format=FORMAT: set container. mkv, mp4, ts\r\n" +
                  "* muxer=MUXER: set muxer. ffmpeg, mkvmerge (Default: ffmpeg)\r\n" +
                  "* bin_path=PATH: set binary file path. (Default: auto)\r\n" +
                  "* skip_sub=BOOL: set whether or not skip subtitle files (Default: false)\r\n" +
                  "* keep=BOOL: set whether or not keep files. true, false (Default: false)\r\n\r\n" +
                  "Examples: \r\n" +
                  "# mux to mp4\r\n" +
                  "-M format=mp4\r\n" +
                  "# use mkvmerge, auto detect bin path\r\n" +
                  "-M format=mkv:muxer=mkvmerge\r\n" +
                  "# use mkvmerge, set bin path\r\n" +
                  "-M format=mkv:muxer=mkvmerge:bin_path=\"C\\:\\Program Files\\MKVToolNix\\mkvmerge.exe\"\r\n"
        ),
        ["cmd_muxAfterDone"] = new TextContainer
        (
            zhCN: "所有工作完成时尝试混流分离的音视频. 输入 \"--morehelp mux-after-done\" 以查看详细信息",
            zhTW: "所有工作完成時嘗試混流分離的影音. 輸入 \"--morehelp mux-after-done\" 以查看詳細訊息",
            enUS: "When all works is done, try to mux the downloaded streams. Use \"--morehelp mux-after-done\" for more details"
        ),
        ["cmd_muxImport"] = new TextContainer
        (
            zhCN: "混流时引入外部媒体文件. 输入 \"--morehelp mux-import\" 以查看详细信息",
            zhTW: "混流時引入外部媒體檔案. 輸入 \"--morehelp mux-import\" 以查看詳細訊息",
            enUS: "When MuxAfterDone enabled, allow to import local media files. Use \"--morehelp mux-import\" for more details"
        ),
        ["cmd_muxImport_more"] = new TextContainer
        (
            zhCN: "混流时引入外部媒体文件. 你能够以:分隔形式指定如下参数:\r\n\r\n" +
                  "* path=PATH: 指定媒体文件路径\r\n" +
                  "* lang=CODE: 指定媒体文件语言代码 (非必须)\r\n" +
                  "* name=NAME: 指定媒体文件描述信息 (非必须)\r\n\r\n" +
                  "例如: \r\n" +
                  "# 引入外部字幕\r\n" +
                  "--mux-import path=zh-Hans.srt:lang=chi:name=\"中文 (简体)\"\r\n" +
                  "# 引入外部音轨+字幕\r\n" +
                  "--mux-import path=\"D\\:\\media\\atmos.m4a\":lang=eng:name=\"English Description Audio\" --mux-import path=\"D\\:\\media\\eng.vtt\":lang=eng:name=\"English (Description)\"",
            zhTW: "混流時引入外部媒體檔案. 你能夠以:分隔形式指定如下參數:\r\n\r\n" +
                  "* path=PATH: 指定媒體檔案路徑\r\n" +
                  "* lang=CODE: 指定媒體檔案語言代碼 (非必須)\r\n" +
                  "* name=NAME: 指定媒體檔案描述訊息 (非必須)\r\n\r\n" +
                  "例如: \r\n" +
                  "# 引入外部字幕\r\n" +
                  "--mux-import path=zh-Hant.srt:lang=chi:name=\"中文 (繁體)\"\r\n" +
                  "# 引入外部音軌+字幕\r\n" +
                  "--mux-import path=\"D\\:\\media\\atmos.m4a\":lang=eng:name=\"English Description Audio\" --mux-import path=\"D\\:\\media\\eng.vtt\":lang=eng:name=\"English (Description)\"",
            enUS: "When MuxAfterDone enabled, allow to import local media files. OPTIONS is a colon separated list of:\r\n\r\n" +
                  "* path=PATH: set file path\r\n" +
                  "* lang=CODE: set media language code (not required)\r\n" +
                  "* name=NAME: set description (not required)\r\n\r\n" +
                  "Examples: \r\n" +
                  "# import subtitle\r\n" +
                  "--mux-import path=en-US.srt:lang=eng:name=\"English (Original)\"\r\n" +
                  "# import audio and subtitle\r\n" +
                  "--mux-import path=\"D\\:\\media\\atmos.m4a\":lang=eng:name=\"English Description Audio\" --mux-import path=\"D\\:\\media\\eng.vtt\":lang=eng:name=\"English (Description)\""
        ),
        ["cmd_writeMetaJson"] = new TextContainer
        (
            zhCN: "解析后的信息是否输出json文件",
            zhTW: "解析後的訊息是否輸出json文件",
            enUS: "Write meta json after parsed"
        ),
        ["liveLimit"] = new TextContainer
        (
            zhCN: "本次直播录制时长上限: ",
            zhTW: "本次直播錄製時長上限: ",
            enUS: "Live recording duration limit: "
        ),
        ["realTimeDecMessage"] = new TextContainer
        (
            zhCN: "启用实时解密时，建议用shaka-packager而非mp4decrypt/ffmpeg",
            zhTW: "啟用即時解密時，建議用shaka-packager而非mp4decrypt/ffmpeg",
            enUS: "When enabling real-time decryption, it is recommended to use shaka-packager instead of mp4decrypt/ffmpeg"
        ),
        ["liveLimitReached"] = new TextContainer
        (
            zhCN: "到达直播录制上限，即将停止录制",
            zhTW: "到達直播錄製上限，即將停止錄製",
            enUS: "Live recording limit reached, will stop recording soon"
        ),
        ["liveStreamEnded"] = new TextContainer
        (
            zhCN: "直播已结束，即将停止录制",
            zhTW: "直播已結束，即將停止錄製",
            enUS: "Live stream ended, will stop recording soon"
        ),
        ["liveIdleTimeoutReached"] = new TextContainer
        (
            zhCN: "连续 {0} 秒没有新分片，即将停止录制",
            zhTW: "連續 {0} 秒沒有新分片，即將停止錄製",
            enUS: "No new segments for {0} seconds, stopping live recording"
        ),
        ["liveNetworkRetry"] = new TextContainer
        (
            zhCN: "直播请求暂时失败，等待网络恢复后重试...",
            zhTW: "直播請求暫時失敗，等待網路恢復後重試...",
            enUS: "Live request temporarily failed, waiting to retry..."
        ),
        ["liveNetworkRecovered"] = new TextContainer
        (
            zhCN: "直播请求已恢复，继续录制",
            zhTW: "直播請求已恢復，繼續錄製",
            enUS: "Live request recovered, continuing recording"
        ),
        ["liveNetworkTimeout"] = new TextContainer
        (
            zhCN: "直播请求等待超时",
            zhTW: "直播請求等待逾時",
            enUS: "Live request timed out"
        ),
        ["liveSegmentUnavailable"] = new TextContainer
        (
            zhCN: "无法获取直播分片，跳过并继续录制；录制结果将标记为不完整",
            zhTW: "無法取得直播分片，跳過並繼續錄製；錄製結果將標記為不完整",
            enUS: "Unable to retrieve live segment, skipping it; the recording will be marked incomplete"
        ),
        ["liveSegmentNotReady"] = new TextContainer
        (
            zhCN: "直播分片暂时不可用，稍后重试...",
            zhTW: "直播分片暫時無法取得，稍後重試...",
            enUS: "Live segment is temporarily unavailable, retrying shortly..."
        ),
        ["httpTooManyRedirects"] = new TextContainer
        (
            zhCN: "HTTP重定向次数过多，请检查资源URL",
            zhTW: "HTTP重新導向次數過多，請檢查資源URL",
            enUS: "Too many HTTP redirects, please check the resource URL"
        ),
        ["saveName"] = new TextContainer
        (
            zhCN: "保存文件名: ",
            zhTW: "保存檔案名: ",
            enUS: "Save Name: "
        ),
        ["fetch"] = new TextContainer
        (
            zhCN: "获取: ",
            zhTW: "獲取: ",
            enUS: "Fetch: "
        ),
        ["ffmpegMerge"] = new TextContainer
        (
            zhCN: "调用ffmpeg合并中...",
            zhTW: "調用ffmpeg合併中...",
            enUS: "ffmpeg merging..."
        ),
        ["ffmpegMergeReachLimit"] = new TextContainer
        (
            zhCN: "合并失败：打开的文件过多(Too many open files)。已下载的分片仍保留在临时目录，可改用 --ffmpeg-concat-mode LOCAL_HTTP，或提高系统文件句柄上限(如 ulimit -n)后重试。",
            zhTW: "合併失敗：開啟的檔案過多(Too many open files)。已下載的分片仍保留在臨時目錄，可改用 --ffmpeg-concat-mode LOCAL_HTTP，或提高系統檔案句柄上限(如 ulimit -n)後重試。",
            enUS: "Merge failed: too many open files. The downloaded segments are kept in the temp directory; use --ffmpeg-concat-mode LOCAL_HTTP, or raise the open-file limit (e.g. ulimit -n) and retry."
        ),
        ["ffmpegNotFound"] = new TextContainer
        (
            zhCN: "找不到ffmpeg，请自行下载：https://ffmpeg.org/download.html",
            zhTW: "找不到ffmpeg，請自行下載：https://ffmpeg.org/download.html",
            enUS: "ffmpeg not found, please download at: https://ffmpeg.org/download.html"
        ),
        ["mkvmergeNotFound"] = new TextContainer
        (
            zhCN: "找不到mkvmerge，请自行下载：https://mkvtoolnix.download/downloads.html",
            zhTW: "找不到mkvmerge，請自行下載：https://mkvtoolnix.download/downloads.html",
            enUS: "mkvmerge not found, please download at: https://mkvtoolnix.download/downloads.html"
        ),
        ["shakaPackagerNotFound"] = new TextContainer
        (
            zhCN: "找不到shaka-packager，请自行下载：https://github.com/shaka-project/shaka-packager/releases",
            zhTW: "找不到shaka-packager，請自行下載：https://github.com/shaka-project/shaka-packager/releases",
            enUS: "shaka-packager not found, please download at: https://github.com/shaka-project/shaka-packager/releases"
        ),
        ["mp4decryptNotFound"] = new TextContainer
        (
            zhCN: "找不到mp4decrypt，请自行下载：https://www.bento4.com/downloads/",
            zhTW: "找不到mp4decrypt，請自行下載：https://www.bento4.com/downloads/",
            enUS: "mp4decrypt not found, please download at: https://www.bento4.com/downloads/"
        ),
        ["fixingTTML"] = new TextContainer
        (
            zhCN: "正在提取TTML(raw)字幕...",
            zhTW: "正在提取TTML(raw)字幕...",
            enUS: "Extracting TTML(raw) subtitle..."
        ),
        ["fixingTTMLmp4"] = new TextContainer
        (
            zhCN: "正在提取TTML(mp4)字幕...",
            zhTW: "正在提取TTML(mp4)字幕...",
            enUS: "Extracting TTML(mp4) subtitle..."
        ),
        ["fixingVTT"] = new TextContainer
        (
            zhCN: "正在提取VTT(raw)字幕...",
            zhTW: "正在提取VTT(raw)字幕...",
            enUS: "Extracting VTT(raw) subtitle..."
        ),
        ["fixingVTTmp4"] = new TextContainer
        (
            zhCN: "正在提取VTT(mp4)字幕...",
            zhTW: "正在提取VTT(mp4)字幕...",
            enUS: "Extracting VTT(mp4) subtitle..."
        ),
        ["keyProcessorNotFound"] = new TextContainer
        (
            zhCN: "找不到支持的Processor",
            zhTW: "找不到支持的Processor",
            enUS: "No Processor matched"
        ),
        ["liveFound"] = new TextContainer
        (
            zhCN: "检测到直播流",
            zhTW: "檢測到直播流",
            enUS: "Live stream found"
        ),
        ["loadingUrl"] = new TextContainer
        (
            zhCN: "加载URL: ",
            zhTW: "載入URL: ",
            enUS: "Loading URL: "
        ),
        ["masterM3u8Found"] = new TextContainer
        (
            zhCN: "检测到Master列表，开始解析全部流信息",
            zhTW: "檢測到Master列表，開始解析全部流訊息",
            enUS: "Master List detected, try parse all streams"
        ),
        ["allowHlsMultiExtMap"] = new TextContainer
        (
            zhCN: "已经允许识别多个#EXT-X-MAP标签, 本软件可能无法正确处理, 请手动确认内容完整性",
            zhTW: "已經允許識別多個#EXT-X-MAP標籤, 本軟件可能無法正確處理, 請手動確認內容完整性",
            enUS: "Multiple #EXT-X-MAP tags are now allowed for detection. However, this software may not handle them correctly. Please manually verify the content's integrity"
        ),
        ["matchTS"] = new TextContainer
        (
            zhCN: "内容匹配: [white on green3]HTTP Live MPEG2-TS[/]",
            zhTW: "內容匹配: [white on green3]HTTP Live MPEG2-TS[/]",
            enUS: "Content Matched: [white on green3]HTTP Live MPEG2-TS[/]"
        ),
        ["matchDASH"] = new TextContainer
        (
            zhCN: "内容匹配: [white on mediumorchid1]Dynamic Adaptive Streaming over HTTP[/]",
            zhTW: "內容匹配: [white on mediumorchid1]Dynamic Adaptive Streaming over HTTP[/]",
            enUS: "Content Matched: [white on mediumorchid1]Dynamic Adaptive Streaming over HTTP[/]"
        ),
        ["matchMSS"] = new TextContainer
        (
            zhCN: "内容匹配: [white on steelblue1]Microsoft Smooth Streaming[/]",
            zhTW: "內容匹配: [white on steelblue1]Microsoft Smooth Streaming[/]",
            enUS: "Content Matched: [white on steelblue1]Microsoft Smooth Streaming[/]"
        ),
        ["matchHLS"] = new TextContainer
        (
            zhCN: "内容匹配: [white on deepskyblue1]HTTP Live Streaming[/]",
            zhTW: "內容匹配: [white on deepskyblue1]HTTP Live Streaming[/]",
            enUS: "Content Matched: [white on deepskyblue1]HTTP Live Streaming[/]"
        ),
        ["matchBinaryData"] = new TextContainer
        (
            zhCN: "内容匹配: [white on deepskyblue1]Binary Data[/]",
            zhTW: "內容匹配: [white on deepskyblue1]Binary Data[/]",
            enUS: "Content Matched: [white on deepskyblue1]Binary Data[/]"
        ),
        ["partMerge"] = new TextContainer
        (
            zhCN: "分片数量大于1800个，开始分块合并...",
            zhTW: "分片數量大於1800個，開始分塊合併...",
            enUS: "Segments more than 1800, start partial merge..."
        ),
        ["notSupported"] = new TextContainer
        (
            zhCN: "当前输入不受支持 ",
            zhTW: "當前輸入不受支援 ",
            enUS: "Input not supported "
        ),
        ["parsingStream"] = new TextContainer
        (
            zhCN: "正在解析媒体信息...",
            zhTW: "正在解析媒體信息...",
            enUS: "Parsing streams..."
        ),
        ["promptChoiceText"] = new TextContainer
        (
            zhCN: "[grey](按键盘上下键以浏览更多内容)[/]",
            zhTW: "[grey](按鍵盤上下鍵以瀏覽更多內容)[/]",
            enUS: "[grey](Move up and down to reveal more streams)[/]"
        ),
        ["promptInfo"] = new TextContainer
        (
            zhCN: "(按 [blue]空格键[/] 选择流, [green]回车键[/] 完成选择)",
            zhTW: "(按 [blue]空格鍵[/] 選擇流, [green]確認鍵[/] 完成選擇)",
            enUS: "(Press [blue]<space>[/] to toggle a stream, [green]<enter>[/] to accept)"
        ),
        ["promptTitle"] = new TextContainer
        (
            zhCN: "请选择 [green]你要下载的内容[/]:",
            zhTW: "請選擇 [green]你要下載的內容[/]:",
            enUS: "Please select [green]what you want to download[/]:"
        ),
        ["readingInfo"] = new TextContainer
        (
            zhCN: "读取媒体信息...",
            zhTW: "讀取媒體訊息...",
            enUS: "Reading media info..."
        ),
        ["searchKey"] = new TextContainer
        (
            zhCN: "正在尝试从文本文件搜索KEY...",
            zhTW: "正在嘗試從文本文件搜尋KEY...",
            enUS: "Trying to search for KEY from text file..."
        ),
        ["decryptionFailed"] = new TextContainer
        (
            zhCN: "解密失败",
            zhTW: "解密失敗",
            enUS: "Decryption failed"
        ),
        ["segmentCountCheckNotPass"] = new TextContainer
        (
            zhCN: "分片数量校验不通过, 共{}个,已下载{}.",
            zhTW: "分片數量校驗不通過, 共{}個,已下載{}.",
            enUS: "Segment count check not pass, total: {}, downloaded: {}."
        ),
        ["selectedStream"] = new TextContainer
        (
            zhCN: "已选择的流:",
            zhTW: "已選擇的流:",
            enUS: "Selected streams:"
        ),
        ["startDownloading"] = new TextContainer
        (
            zhCN: "开始下载...",
            zhTW: "開始下載...",
            enUS: "Start downloading..."
        ),
        ["streamsInfo"] = new TextContainer
        (
            zhCN: "已解析, 共计 {} 条媒体流, 基本流 {} 条, 可选音频流 {} 条, 可选字幕流 {} 条",
            zhTW: "已解析, 共計 {} 條媒體流, 基本流 {} 條, 可選音頻流 {} 條, 可選字幕流 {} 條",
            enUS: "Extracted, there are {} streams, with {} basic streams, {} audio streams, {} subtitle streams"
        ),
        ["writeJson"] = new TextContainer
        (
            zhCN: "写出meta json",
            zhTW: "寫出meta json",
            enUS: "Writing meta json"
        ),
        ["noStreamsToDownload"] = new TextContainer
        (
            zhCN: "没有找到需要下载的流",
            zhTW: "沒有找到需要下載的流",
            enUS: "No stream found to download"
        ),
        ["loadUrlFailed"] = new TextContainer
        (
            zhCN: "加载URL失败",
            zhTW: "載入URL失敗",
            enUS: "Failed to load URL"
        ),

    };
}
