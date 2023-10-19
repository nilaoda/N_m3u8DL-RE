using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace N_m3u8DL_RE.Common.Resource
{
    internal class StaticText
    {
        public static Dictionary<string, TextContainer> LANG_DIC = new()
        {
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
            ["cmd_appendUrlParams"] = new TextContainer
            (
                zhCN: "将输入Url的Params添加至分片, 对某些网站很有用, 例如 kakao.com",
                zhTW: "將輸入Url的Params添加至分片, 對某些網站很有用, 例如 kakao.com",
                enUS: "Add Params of input Url to segments, useful for some websites, such as kakao.com"
            ),
            ["cmd_autoSelect"] = new TextContainer
            (
                zhCN: "自动选择所有类型的最佳轨道",
                zhTW: "自動選擇所有類型的最佳軌道",
                enUS: "Automatically selects the best tracks of all types"
            ),
            ["cmd_binaryMerge"] = new TextContainer
            (
                zhCN: "二进制合并",
                zhTW: "二進位制合併",
                enUS: "Binary merge"
            ),
            ["cmd_useFFmpegConcatDemuxer"] = new TextContainer
            (
                zhCN: "使用 ffmpeg 合并时，使用 concat 分离器而非 concat 协议",
                zhTW: "使用 ffmpeg 合併時，使用 concat 分離器而非 concat 協議",
                enUS: "When merging with ffmpeg, use the concat demuxer instead of the concat protocol"
            ),
            ["cmd_checkSegmentsCount"] = new TextContainer
            (
                zhCN: "检测实际下载的分片数量和预期数量是否匹配",
                zhTW: "檢測實際下載的分片數量和預期數量是否匹配",
                enUS: "Check if the actual number of segments downloaded matches the expected number"
            ),
            ["cmd_downloadRetryCount"] = new TextContainer
            (
                zhCN: "每个分片下载异常时的重试次数",
                zhTW: "每個分片下載異常時的重試次數",
                enUS: "The number of retries when download segment error"
            ),
            ["cmd_decryptionBinaryPath"] = new TextContainer
            (
                zhCN: "MP4解密所用工具的全路径, 例如 C:\\Tools\\mp4decrypt.exe",
                zhTW: "MP4解密所用工具的全路徑, 例如 C:\\Tools\\mp4decrypt.exe",
                enUS: "Full path to the tool used for MP4 decryption, like C:\\Tools\\mp4decrypt.exe"
            ),
            ["cmd_delAfterDone"] = new TextContainer
            (
                zhCN: "完成后删除临时文件",
                zhTW: "完成後刪除臨時文件",
                enUS: "Delete temporary files when done"
            ),
            ["cmd_ffmpegBinaryPath"] = new TextContainer
            (
                zhCN: "ffmpeg可执行程序全路径, 例如 C:\\Tools\\ffmpeg.exe",
                zhTW: "ffmpeg可執行程序全路徑, 例如 C:\\Tools\\ffmpeg.exe",
                enUS: "Full path to the ffmpeg binary, like C:\\Tools\\ffmpeg.exe"
            ),
            ["cmd_mkvmergeBinaryPath"] = new TextContainer
            (
                zhCN: "mkvmerge可执行程序全路径, 例如 C:\\Tools\\mkvmerge.exe",
                zhTW: "mkvmerge可執行程序全路徑, 例如 C:\\Tools\\mkvmerge.exe",
                enUS: "Full path to the mkvmerge binary, like C:\\Tools\\mkvmerge.exe"
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
            ["cmd_Input"] = new TextContainer
            (
                zhCN: "链接或文件",
                zhTW: "連結或文件",
                enUS: "Input Url or File"
            ),
            ["cmd_keys"] = new TextContainer
            (
                zhCN: "设置解密密钥, 程序调用mp4decrpyt/shaka-packager进行解密. 格式:\r\n--key KID1:KEY1 --key KID2:KEY2",
                zhTW: "設置解密密鑰, 程序調用mp4decrpyt/shaka-packager進行解密. 格式:\r\n--key KID1:KEY1 --key KID2:KEY2",
                enUS: "Pass decryption key(s) to mp4decrypt/shaka-packager. format:\r\n--key KID1:KEY1 --key KID2:KEY2"
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
                zhCN: "设置保存文件命名模板, 支持使用变量",
                zhTW: "",
                enUS: ""
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
            ["cmd_adKeyword"] = new TextContainer
            (
                zhCN: "设置广告分片的URL关键字(正则表达式)",
                zhTW: "設置廣告分片的URL關鍵字(正則表達式)",
                enUS: "Set URL keywords (regular expressions) for AD segments"
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
                zhCN: "通过正则表达式去除符合要求的视频流.",
                zhTW: "通過正則表達式去除符合要求的影片串流.",
                enUS: "Drop video streams by regular expressions."
            ),
            ["cmd_selectVideo_more"] = new TextContainer
            (
                zhCN: "通过正则表达式选择符合要求的视频流. 你能够以:分隔形式指定如下参数:\r\n\r\n" +
                      "id=REGEX:lang=REGEX:name=REGEX:codec=REGEX:res=REGEX:frame=REGEX\r\n" +
                      "segsMin=number:segsMax=number:ch=REGEX:range=REGEX:url=REGEX\r\n" +
                      "plistDurMin=hms:plistDurMax=hms:role=string:for=FOR\r\n\r\n" +
                      "* for=FOR: 选择方式. best[number], worst[number], all (默认: best)\r\n\r\n" +
                      "例如: \r\n" +
                      "# 选择最佳视频\r\n" +
                      "-sv best\r\n" +
                      "# 选择4K+HEVC视频\r\n" +
                      "-sv res=\"3840*\":codec=hvc1:for=best\r\n" +
                      "# 选择长度大于1小时20分钟30秒的视频\r\n" +
                      "-sv plistDurMin=\"1h20m30s\":for=best\r\n" +
                      "-sv role=\"main\":for:best\r\n",
                zhTW: "通過正則表達式選擇符合要求的影片軌. 你能夠以:分隔形式指定如下參數:\r\n\r\n" +
                      "id=REGEX:lang=REGEX:name=REGEX:codec=REGEX:res=REGEX:frame=REGEX\r\n" +
                      "segsMin=number:segsMax=number:ch=REGEX:range=REGEX:url=REGEX\r\n" +
                      "plistDurMin=hms:plistDurMax=hms:role=string:for=FOR\r\n\r\n" +
                      "* for=FOR: 選擇方式. best[number], worst[number], all (默認: best)\r\n\r\n" +
                      "例如: \r\n" +
                      "# 選擇最佳影片\r\n" +
                      "-sv best\r\n" +
                      "# 選擇4K+HEVC影片\r\n" +
                      "-sv res=\"3840*\":codec=hvc1:for=best\r\n" +
                      "# 選擇長度大於1小時20分鐘30秒的影片\r\n" +
                      "-sv plistDurMin=\"1h20m30s\":for=best\r\n" +
                      "-sv role=\"main\":for:best\r\n",
                enUS: "Select video streams by regular expressions. OPTIONS is a colon separated list of:\r\n\r\n" +
                      "id=REGEX:lang=REGEX:name=REGEX:codec=REGEX:res=REGEX:frame=REGEX\r\n" +
                      "segsMin=number:segsMax=number:ch=REGEX:range=REGEX:url=REGEX\r\n" +
                      "plistDurMin=hms:plistDurMax=hms:role=string:for=FOR\r\n\r\n" +
                      "* for=FOR: Select type. best[number], worst[number], all (Default: best)\r\n\r\n" +
                      "Examples: \r\n" +
                      "# select best video\r\n" +
                      "-sv best\r\n" +
                      "# select 4K+HEVC video\r\n" +
                      "-sv res=\"3840*\":codec=hvc1:for=best\r\n" +
                      "# Select best video with duration longer than 1 hour 20 minutes 30 seconds\r\n" +
                      "-sv plistDurMin=\"1h20m30s\":for=best\r\n" +
                      "-sv role=\"main\":for:best\r\n"
            ),
            ["cmd_selectAudio"] = new TextContainer
            (
                zhCN: "通过正则表达式选择符合要求的音频流. 输入 \"--morehelp select-audio\" 以查看详细信息",
                zhTW: "通過正則表達式選擇符合要求的音軌. 輸入 \"--morehelp select-audio\" 以查看詳細訊息",
                enUS: "Select audio streams by regular expressions. Use \"--morehelp select-audio\" for more details"
            ),
            ["cmd_dropAudio"] = new TextContainer
            (
                zhCN: "通过正则表达式去除符合要求的音频流.",
                zhTW: "通過正則表達式去除符合要求的音軌.",
                enUS: "Drop audio streams by regular expressions."
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
                      "-sa role=\"main\":for:best\r\n",
                zhTW: "通過正則表達式選擇符合要求的音軌. 參考 --select-video\r\n\r\n" +
                      "例如: \r\n" +
                      "# 選擇所有音訊\r\n" +
                      "-sa all\r\n" +
                      "# 選擇最佳英語音軌\r\n" +
                      "-sa lang=en:for=best\r\n" +
                      "# 選擇最佳的2條英語(或日語)音軌\r\n" +
                      "-sa lang=\"ja|en\":for=best2\r\n" +
                      "-sa role=\"main\":for:best\r\n",
                enUS: "Select audio streams by regular expressions. ref --select-video\r\n\r\n" +
                      "Examples: \r\n" +
                      "# select all\r\n" +
                      "-sa all\r\n" +
                      "# select best eng audio\r\n" +
                      "-sa lang=en:for=best\r\n" +
                      "# select best 2, and language is ja or en\r\n" +
                      "-sa lang=\"ja|en\":for=best2\r\n" +
                      "-sa role=\"main\":for:best\r\n"
            ),
            ["cmd_selectSubtitle"] = new TextContainer
            (
                zhCN: "通过正则表达式选择符合要求的字幕流. 输入 \"--morehelp select-subtitle\" 以查看详细信息",
                zhTW: "通過正則表達式選擇符合要求的字幕流. 輸入 \"--morehelp select-subtitle\" 以查看詳細訊息",
                enUS: "Select subtitle streams by regular expressions. Use \"--morehelp select-subtitle\" for more details"
            ),
            ["cmd_dropSubtitle"] = new TextContainer
            (
                zhCN: "通过正则表达式去除符合要求的字幕流.",
                zhTW: "通過正則表達式去除符合要求的字幕流.",
                enUS: "Drop subtitle streams by regular expressions."
            ),
            ["cmd_custom_range"] = new TextContainer
            (
                zhCN: "下载点播内容时, 仅下载部分分片.\r\n\r\n" +
                      "例如: \r\n" +
                      "# 下载[0,10]共11个分片\r\n" +
                      "--custom-range 0-10\r\n" +
                      "# 下载从序号10开始的后续分片\r\n" +
                      "--custom-range 10-\r\n" +
                      "# 下载前100个分片\r\n" +
                      "--custom-range -99\r\n" +
                      "# 下载第5分钟到20分钟的内容\r\n" +
                      "--custom-range 05:00-20:00\r\n",
                zhTW: "下載點播內容時, 僅下載部分分片.\r\n\r\n" +
                      "例如: \r\n" +
                      "# 下載[0,10]共11個分片\r\n" +
                      "--custom-range 0-10\r\n" +
                      "# 下載從序號10開始的後續分片\r\n" +
                      "--custom-range 10-\r\n" +
                      "# 下載前100個分片\r\n" +
                      "--custom-range -99\r\n" +
                      "# 下載第5分鐘到20分鐘的內容\r\n" +
                      "--custom-range 05:00-20:00\r\n",
                enUS: "Download only part of the segments when downloading vod content.\r\n\r\n" +
                      "Examples: \r\n" +
                      "# Download [0,10], a total of 11 segments\r\n" +
                      "--custom-range 0-10\r\n" +
                      "# Download subsequent segments starting from index 10\r\n" +
                      "--custom-range 10-\r\n" +
                      "# Download the first 100 segments\r\n" +
                      "--custom-range -99\r\n" +
                      "# Download content from the 05:00 to 20:00\r\n" +
                      "--custom-range 05:00-20:00\r\n"
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
                      "* format=FORMAT: 指定混流容器 mkv, mp4\r\n" +
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
                      "* format=FORMAT: 指定混流容器 mkv, mp4\r\n" +
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
                      "* format=FORMAT: set container. mkv, mp4\r\n" +
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
                zhCN: "启用实时解密时，建议用shaka-packager而非mp4decrypt",
                zhTW: "啟用即時解密時，建議用shaka-packager而非mp4decrypt",
                enUS: "When enabling real-time decryption, it is recommended to use shaka-packager instead of mp4decrypt"
            ),
            ["liveLimitReached"] = new TextContainer
            (
                zhCN: "到达直播录制上限，即将停止录制",
                zhTW: "到達直播錄製上限，即將停止錄製",
                enUS: "Live recording limit reached, will stop recording soon"
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
            ["ffmpegNotFound"] = new TextContainer
            (
                zhCN: "找不到ffmpeg，请自行下载：https://ffmpeg.org/download.html",
                zhTW: "找不到ffmpeg，請自行下載：https://ffmpeg.org/download.html",
                enUS: "ffmpeg not found, please download at: https://ffmpeg.org/download.html"
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
            ["partMerge"] = new TextContainer
            (
                zhCN: "分片数量大于1800个，开始分块合并...",
                zhTW: "分片數量大於1800個，開始分塊合併...",
                enUS: "Segments more than 1800, start partial merge..."
            ),
            ["notSupported"] = new TextContainer
            (
                zhCN: "当前输入不受支持: ",
                zhTW: "當前輸入不受支援: ",
                enUS: "Input not supported: "
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

        };
    }
}
