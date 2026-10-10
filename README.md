# N_m3u8DL-RE

[See English version here](README.en.md)

跨平台的DASH/HLS/MSS下载工具。支持点播、直播(DASH/HLS)。

[![img](https://img.shields.io/github/stars/nilaoda/N_m3u8DL-RE?label=%E7%82%B9%E8%B5%9E)](https://github.com/nilaoda/N_m3u8DL-RE)  [![img](https://img.shields.io/github/last-commit/nilaoda/N_m3u8DL-RE?label=%E6%9C%80%E8%BF%91%E6%8F%90%E4%BA%A4)](https://github.com/nilaoda/N_m3u8DL-RE)  [![img](https://img.shields.io/github/release/nilaoda/N_m3u8DL-RE?label=%E6%9C%80%E6%96%B0%E7%89%88%E6%9C%AC)](https://github.com/nilaoda/N_m3u8DL-RE/releases)  [![img](https://img.shields.io/github/license/nilaoda/N_m3u8DL-RE?label=%E8%AE%B8%E5%8F%AF%E8%AF%81)](https://github.com/nilaoda/N_m3u8DL-RE)   [![img](https://img.shields.io/github/downloads/nilaoda/N_m3u8DL-RE/total?label=%E4%B8%8B%E8%BD%BD%E9%87%8F)](https://github.com/nilaoda/N_m3u8DL-RE/releases)

遇到 BUG 请首先确认软件是否为最新版本（如果是 Release 版本，建议到 [Actions](https://github.com/nilaoda/N_m3u8DL-RE/actions) 页面下载最新自动构建版本后查看问题是否已经被修复），如果确认版本最新且问题依旧存在，可以到 [Issues](https://github.com/nilaoda/N_m3u8DL-RE/issues) 中查找是否有人遇到过相关问题，没有的话再进行询问。

---

版本较低的Windows系统自带的终端可能不支持本程序，替代方案：在 [cmder](https://github.com/cmderdev/cmder) 中运行。

Arch Linux 可以从 AUR 获取：[n-m3u8dl-re-bin](https://aur.archlinux.org/packages/n-m3u8dl-re-bin)、[n-m3u8dl-re-git](https://aur.archlinux.org/packages/n-m3u8dl-re-git)

```bash
# Arch Linux 及其衍生版安装 N_m3u8DL-RE 发行版 (该源非本人维护)
yay -Syu n-m3u8dl-re-bin

# Arch Linux 及其衍生版安装 N_m3u8DL-RE 开发版 (该源非本人维护)
yay -Syu n-m3u8dl-re-git
```

---

## PowerShell 补全

支持 Windows PowerShell 5.1 和 PowerShell 7。补全脚本内嵌在可执行文件中。将 `N_m3u8DL-RE` 加入 `PATH`，然后在 PowerShell 中加载：

```powershell
N_m3u8DL-RE --generate-completion powershell | Out-String | Invoke-Expression
```

输入 `N_m3u8DL-RE --sub-` 后按 Tab 可补全参数名；`--sub-format`、`--log-level`、`--ui-language` 等参数可补全可选值，文件和目录使用 PowerShell 的默认路径补全。以 `./N_m3u8DL-RE` 或完整路径调用程序时也可以使用。

加载仅对当前会话有效。如需每次启动时启用，可将上面的命令写入 `$PROFILE`。也可用 `--generate-completion powershell` 单独查看或保存脚本。

## Cookie 文件

使用 `--cookies cookies.txt` 读取浏览器导出的 Netscape 格式 Cookie 文件：

```text
N_m3u8DL-RE "https://example.com/video.m3u8" --cookies "cookies.txt"
```

Cookie 会按请求的域名、路径、HTTPS 条件及有效期匹配，适用于清单、密钥、初始化文件和分片，支持点播及直播。若同时设置 `-H "Cookie: ..."`，则以手动请求头为准。服务器更新的 Cookie 仅保存在内存中，不回写文件。

## 配置文件

常用选项可以保存到 UTF-8 文本配置文件中，程序启动时自动读取：

- Linux / macOS：`~/.config/N_m3u8DL-RE/config.conf`；若设置了绝对路径的 `XDG_CONFIG_HOME`，则使用 `$XDG_CONFIG_HOME/N_m3u8DL-RE/config.conf`。
- Windows：`%APPDATA%\N_m3u8DL-RE\config.conf`。

程序不会自动创建配置文件，默认文件不存在时使用内置默认值。配置沿用命令行参数语法，支持以 `#` 开头的注释行；包含空格的参数值使用双引号：

```text
# 常用下载选项
-mt
--no-log
--auto-select
--thread-count 16
--save-dir "Downloads/My Videos"
```

优先级为 **命令行 > 配置文件 > 内置默认值**。同一选项按短名和长名识别，单值选项由命令行替换配置值，被替换的值不再进行下载参数转换或文件读取。`--key`、`--ad-keyword`、`--mux-import` 等可重复选项按配置在前、命令行在后的顺序合并。`-H` / `--header` 按请求头名称合并，名称不区分大小写；不同名称保留，同名以命令行为准。布尔选项可用 `false` 关闭，例如 `--no-log false`。配置中的 HTTP 超时也视为手动指定，会关闭直播的自动超时调整。

```text
N_m3u8DL-RE "https://example.com/video.m3u8" --thread-count 8
N_m3u8DL-RE "https://example.com/video.m3u8" --config "custom.conf"
N_m3u8DL-RE "https://example.com/video.m3u8" --no-config
```

`--config FILE` 只读取指定文件，替代默认配置；文件不存在或内容无效时报错。`--no-config` 不读取配置文件，不能与 `--config` 同时使用。配置只允许保存下载选项，不允许保存下载地址、配置加载选项或帮助、版本、补全操作。相对路径按当前工作目录解析，程序目录和当前目录不会被自动搜索。

帮助信息会使用配置中的 UI 语言；生成补全脚本及 Tab 补全请求不读取配置。原有 `@args.txt` 参数文件仍可使用，其中的参数具有命令行优先级。

Docker 部署建议将配置只读挂载到 `/config/config.conf`，并传入 `--config /config/config.conf`，不依赖容器的 HOME 或运行用户。

## 工具命令

```text
# 字节拼接，无需 FFmpeg
N_m3u8DL-RE concat -i 1.ts -i 2.ts -o combined.ts

# 也可从目录读取，按文件名自然排序；默认匹配 *.ts，可用 --pattern 修改
# --input-dir 不能与 -i 混用
N_m3u8DL-RE concat --input-dir segments --pattern "*.ts" -o combined.ts

# 使用 FFmpeg 合并目录中的 TS 分片
N_m3u8DL-RE merge --input-dir segments -o merged.mp4

# 混流视频、音频和字幕
# 默认自动调整字幕时间轴，原文件不变；添加 --auto-subtitle-fix false 可关闭
N_m3u8DL-RE mux -i video.mp4 -i audio.m4a -i subtitle.srt -o output.mp4

# 设置轨道语言（lang）、轨道标题（name）和影片标题（--title）
# --mux-import 可追加外部音轨或字幕；lang、name 均可省略
N_m3u8DL-RE mux -i video.mp4 -i "path=audio.m4a:lang=eng:name=English" --mux-import "path=subtitle.srt:lang=zh-Hans:name=简体中文" --title "影片标题" -o output.mkv
# 复合参数值内的冒号需写为 \:，例如 path=C\:\media\audio.m4a:lang=eng

# 检查工具版本和系统环境
N_m3u8DL-RE doctor
N_m3u8DL-RE doctor --json > doctor.json
```

## 命令行参数

过长的自动保存名、自定义保存名和模板生成的文件名会自动缩短，并附加短哈希以减少重名。长度按 UTF-8 字节计算，不会截断中文或 emoji；自动名称的时间戳和输出文件的媒体扩展名会预留空间。

```
Description:
  N_m3u8DL-RE 0.6.0 20260628

Usage:
  N_m3u8DL-RE <input> [options]

Arguments:
  <input>  链接或文件

Options:
  --config <FILE>                                         读取指定配置文件，替代用户默认配置；命令行选项优先
  --no-config                                             不读取配置文件，不能与 --config 同时使用
  --tmp-dir <tmp-dir>                                     设置临时文件存储目录
  --save-dir <save-dir>                                   设置输出目录
  --save-name <save-name>                                 设置保存文件名
  --save-pattern <save-pattern>                           设置保存文件命名模板. 输入 "--morehelp save-pattern" 以查看变量和示例
  --log-file-path <log-file-path>                         设置日志文件路径, 例如 C:\Logs\log.txt
  --base-url <base-url>                                   设置BaseURL
  --thread-count <number>                                 设置下载线程数 [default: 10]
  --download-retry-count <number>                         每个分片下载异常时的重试次数；分片直播临时网络故障在重试耗尽后仍会等待恢复 [default: 7]
  --http-request-timeout <seconds>                        HTTP请求超时(秒)；分片直播未指定时自动调整，指定后也用于分片连续无数据超时，不限制总下载时长 [default: 100]
  --force-ansi-console                                    强制认定终端为支持ANSI且可交互的终端
  --no-ansi-color                                         去除ANSI颜色
  --auto-select                                           自动选择所有类型的最佳轨道 [default: False]
  --skip-merge                                            跳过合并分片 [default: False]
  --skip-download                                         跳过下载 [default: False]
  --check-segments-count                                  检测实际下载的分片数量和预期数量是否匹配 [default: True]
  --binary-merge                                          二进制合并 [default: False]
  --ffmpeg-concat-mode <DEMUXER|LOCAL_HTTP|PROTOCOL>      FFmpeg 合并输入方式：LOCAL_HTTP 本机虚拟输入(默认)，PROTOCOL 直接打开全部分片，DEMUXER 使用文件列表 [default: LOCAL_HTTP]
  --use-ffmpeg-concat-demuxer                             使用 concat 分离器合并，等同于 --ffmpeg-concat-mode DEMUXER；同一层同时指定时优先 [default: False]
  --del-after-done                                        完成后删除临时文件 [default: True]
  --no-date-info                                          混流时不写入日期信息 [default: False]
  --no-log                                                关闭日志文件输出 [default: False]
  --write-meta-json                                       解析后的信息是否输出json文件 [default: True]
  --append-url-params                                     将输入URL的查询参数添加至分片；本地清单使用 --base-url 的参数 [default: False]
  -mt, --concurrent-download                              并发下载已选择的音频、视频和字幕 [default: False]
  -H, --header <header>                                   为HTTP请求设置特定的请求头, 例如:
                                                          -H "Cookie: mycookie" -H "User-Agent: iOS"
  --cookies <FILE>                                        读取 Netscape 格式的 Cookie 文件；手动设置的 Cookie 请求头优先
  --sub-only                                              只选取字幕轨道 [default: False]
  --sub-format <SRT|VTT>                                  字幕输出类型 [default: SRT]
  --auto-subtitle-fix                                     自动修正字幕 [default: True]
  --ffmpeg-binary-path <PATH>                             ffmpeg可执行程序全路径, 例如 C:\Tools\ffmpeg.exe
  --log-level <DEBUG|ERROR|INFO|OFF|WARN>                 设置日志级别 [default: INFO]
  --ui-language <en-US|zh-CN|zh-TW>                       设置UI语言
  --urlprocessor-args <urlprocessor-args>                 此字符串将直接传递给URL Processor
  --key <key>                                             设置解密密钥, 程序调用mp4decrpyt/shaka-packager/ffmpeg进行解密. 格式:
                                                          --key KID1:KEY1 --key KID2:KEY2
                                                          对于KEY相同的情况可以直接输入 --key KEY
  --key-text-file <key-text-file>                         设置密钥文件,程序将从文件中按KID搜寻KEY以解密.(不建议使用特大文件)
  --decryption-engine <FFMPEG|MP4DECRYPT|SHAKA_PACKAGER>  设置解密时使用的第三方程序 [default: MP4DECRYPT]
  --decryption-binary-path <PATH>                         MP4解密所用工具的全路径, 例如 C:\Tools\mp4decrypt.exe
  --mp4-real-time-decryption                              实时解密MP4分片 [default: False]
  -R, --max-speed <SPEED>                                 设置限速，单位支持 Mbps 或 Kbps，如：15M 100K
  -M, --mux-after-done <OPTIONS>                          所有工作完成时尝试混流分离的音视频. 输入 "--morehelp mux-after-done" 以查看详细信息
  --custom-hls-method <METHOD>                            指定HLS加密方式 (AES_128|AES_128_ECB|CENC|CHACHA20|NONE|SAMPLE_AES|SAMPLE_AES_CTR|UNKNOWN)
  --custom-hls-key <FILE|HEX|BASE64>                      指定HLS解密KEY. 可以是文件, HEX或Base64
  --custom-hls-iv <FILE|HEX|BASE64>                       指定HLS解密IV. 可以是文件, HEX或Base64
  --custom-hls-scope <SCOPE>                              指定自定义HLS加密方式、KEY和IV的适用范围 (ALL|VIDEO|AUDIO) [default: ALL]
  --use-system-proxy                                      使用系统默认代理 [default: True]
  --custom-proxy <URL>                                    设置请求代理, 如 http://127.0.0.1:8888
  --interface <INTERFACE>                                 指定请求使用的网卡名或本机 IP，如 eth1 或 192.168.1.10
  --custom-range <RANGE>                                  仅下载部分分片. 输入 "--morehelp custom-range" 以查看详细信息
  --task-start-at <yyyyMMddHHmmss>                        在此时间之前不会开始执行任务
  --live-perform-as-vod                                   以点播方式下载直播流 [default: False]
  --live-real-time-merge                                  录制直播时实时合并 [default: False]
  --live-keep-segments                                    录制直播并开启实时合并时依然保留分片 [default: True]
  --live-pipe-mux                                         录制直播并开启实时合并时通过管道+ffmpeg实时混流到TS文件 [default: False]
  --live-fix-vtt-by-audio                                 通过读取音频文件的起始时间修正VTT字幕 [default: False]
  --live-record-limit <HH:mm:ss>                          录制直播时的录制时长限制
  --live-wait-time <SEC>                                  手动设置直播列表刷新间隔
  --live-idle-timeout <SEC>                               直播列表连续指定秒数无新分片时停止录制（默认关闭）
  --live-take-count <NUM>                                 手动设置录制直播时首次获取分片的数量 [default: 16]
  --live-catchup <TIME>                                   从指定历史时间下载直播，支持回看时长或日期时间；覆盖 --live-take-count
  --mux-import <OPTIONS>                                  混流时引入外部媒体文件. 输入 "--morehelp mux-import" 以查看详细信息
  -sv, --select-video <OPTIONS>                           通过正则表达式选择符合要求的视频流. 输入 "--morehelp select-video" 以查看详细信息
  -sa, --select-audio <OPTIONS>                           通过正则表达式选择符合要求的音频流. 输入 "--morehelp select-audio" 以查看详细信息
  -ss, --select-subtitle <OPTIONS>                        通过正则表达式选择符合要求的字幕流. 输入 "--morehelp select-subtitle" 以查看详细信息
  -dv, --drop-video <OPTIONS>                             通过正则表达式去除符合要求的视频流. 支持与 --select-video 相同的参数, 输入 "--morehelp select-video" 以查看详细信息
  -da, --drop-audio <OPTIONS>                             通过正则表达式去除符合要求的音频流. 支持与 --select-video 相同的参数, 输入 "--morehelp select-video" 以查看详细信息
  -ds, --drop-subtitle <OPTIONS>                          通过正则表达式去除符合要求的字幕流. 支持与 --select-video 相同的参数, 输入 "--morehelp select-video" 以查看详细信息
  --ad-keyword <REG>                                      设置广告分片的URL关键字(正则表达式)
  --vod-select-parts                                      控制点播选段交互：不传则自动判断，true 强制显示，false 关闭（空格勾选，回车确认）
  --vod-list-parts                                        按媒体配置归组列出点播段的编号和总时长后退出 [default: False]
  --vod-drop-parts <IDS>                                  按 --vod-list-parts 的编号删除整个点播段及对应音频/字幕，例如 0,2-4
  --disable-update-check                                  禁用版本更新检测 [default: False]
  --allow-hls-multi-ext-map                               允许直播HLS中的多个#EXT-X-MAP(实验性；点播默认支持) [default: False]
  --morehelp <OPTION>                                     查看某个选项的详细帮助信息
  --generate-completion <SHELL>                           输出内嵌的补全脚本（powershell）
  -?, -h, --help                                          Show help and usage information
  --version                                               Show version information

```

`--interface` 支持网卡名（如 `eth1`、`en0`、`Wi-Fi`）或本机 IP 地址。网卡名约束实际出口，IP 地址指定连接的源地址。使用代理时，约束应用于本机到代理的连接；DNS 仍由系统解析。绑定失败会明确报错，不会回退到其他网卡。Linux 按网卡名绑定可能需要额外权限，报错会包含系统原因。

`--custom-hls-scope VIDEO` 仅对主播放列表中的视频流应用 `--custom-hls-method`、`--custom-hls-key` 和 `--custom-hls-iv`；`AUDIO` 仅对音频流应用。纯音频变体可通过 `CODECS` 识别；缺少足够类型信息的主变体仍按视频处理。默认 `ALL` 保持原有行为。直接输入单条媒体播放列表时无法识别轨道类型，自定义参数会应用于该播放列表。

交互下载多段点播时，会按实际媒体配置归组，用空格勾选要保留的组、回车确认；默认全部保留。相同配置的重复段只显示一次，时长存在明显间隔时再按段时长分组，不自动判定广告。HLS 会读取各类 init 的实际编码、分辨率及音频配置，界面不显示 init URL。不传 `--vod-select-parts` 时自动判断是否显示；使用 `--auto-select` 或选流过滤器时不自动弹出。`--vod-select-parts`（或 `--vod-select-parts true`）强制显示，`--vod-select-parts false` 明确关闭选段交互；相同配置且时长接近的广告仍需用 URL 规则或编号排除。

脚本可用 `--vod-list-parts` 查看归组后的原始编号，再用 `--vod-drop-parts 0,2-4` 删除指定段。DASH 编号为原 Period 顺序（从 0 开始），HLS 为原不连续序号（起点可能不同）；同一不连续段的 MAP 一并处理，音视频和字幕同步删除。没有独立不连续标记的 HLS MAP 可用 `--ad-keyword` 匹配 init/媒体 URL。`--custom-range` 仍使用源分片编号，选择后不重排。

<details>
<summary>点击查看More Help</summary>

```
More Help:

  --mux-after-done

所有工作完成时尝试混流分离的音视频. 你能够以:分隔形式指定如下参数:

* format=FORMAT: 指定混流容器 mkv, mp4, ts
* muxer=MUXER: 指定混流程序 ffmpeg, mkvmerge (默认: ffmpeg)
* bin_path=PATH: 指定程序路径 (默认: 自动寻找)
* skip_sub=BOOL: 是否忽略字幕文件 (默认: false)
* keep=BOOL: 混流完成是否保留文件 true, false (默认: false)

例如:
# 混流为mp4容器
-M format=mp4
# 使用mkvmerge, 自动寻找程序
-M format=mkv:muxer=mkvmerge
# 使用mkvmerge, 自定义程序路径
-M format=mkv:muxer=mkvmerge:bin_path="C\:\Program Files\MKVToolNix\mkvmerge.exe"
```

```
More Help:

  --mux-import

混流时引入外部媒体文件. 你能够以:分隔形式指定如下参数:

* path=PATH: 指定媒体文件路径
* lang=CODE: 指定媒体文件语言代码 (非必须)
* name=NAME: 指定媒体文件描述信息 (非必须)

例如:
# 引入外部字幕
--mux-import path=zh-Hans.srt:lang=chi:name="中文 (简体)"
# 引入外部音轨+字幕
--mux-import path="D\:\media\atmos.m4a":lang=eng:name="English Description Audio" --mux-import path="D\:\media\eng.vtt":lang=eng:name="English (Description)"
```

```
More Help:

  --select-video

通过正则表达式选择符合要求的视频流. 你能够以:分隔形式指定如下参数.
同样的参数也适用于 --select-audio/-sa, --select-subtitle/-ss 以及对应的 --drop-video/--drop-audio/--drop-subtitle 选项.

* id=REGEX: 按 GroupId 匹配
* lang=REGEX: 按语言代码匹配
* name=REGEX: 按名称匹配
* codecs=REGEX: 按编码匹配 (如 hvc1, avc1, mp4a)
* res=REGEX: 按分辨率匹配 (如 1920*, 3840*)
* frame=REGEX: 按帧率匹配
* channel=REGEX: 按音频声道数匹配 (如 6, 2)
* range=REGEX: 按视频动态范围匹配 (如 SDR, HDR, PQ)
* url=REGEX: 按分片URL匹配
* period=REGEX: 按 DASH Period id 匹配 (多Period MPD, 如广告/分段)
* segsMin=number: 仅保留分片数 >= number 的流
* segsMax=number: 仅保留分片数 <= number 的流
* plistDurMin=hms: 仅保留时长 >= hms 的流 (如 1h20m30s, 90s)
* plistDurMax=hms: 仅保留时长 <= hms 的流
* bwMin=int: 仅保留码率 >= int Kbps 的流
* bwMax=int: 仅保留码率 <= int Kbps 的流
* role=string: 按 DASH role 匹配 (Subtitle, Main, Alternate, Supplementary, Commentary, Dub, Description, Sign, Metadata, ForcedSubtitle)
* for=FOR: 选择方式. best[number], worst[number], all (默认: best)

例如:
# 选择最佳视频
-sv best
# 选择4K+HEVC视频
-sv res="3840*":codecs=hvc1:for=best
# 选择长度大于1小时20分钟30秒的视频
-sv plistDurMin="1h20m30s":for=best
-sv role="main":for=best
# 选择码率在800Kbps至1Mbps之间的视频
-sv bwMin=800:bwMax=1000
# 去除分片数不超过2的字幕流 (如 trick-play/广告列表)
-ds segsMax=2:for=all --auto-select
# 仅保留主内容 Period (排除广告 Period)
-sv period="main":for=best
# 去除广告 Period 的视频
-dv period="ad":for=all
```

```
More Help:

  --select-audio

通过正则表达式选择符合要求的音频流. 参考 --select-video

例如:
# 选择所有音频
-sa all
# 选择最佳英语音轨
-sa lang=en:for=best
# 选择最佳的2条英语(或日语)音轨
-sa lang="ja|en":for=best2
-sa role="main":for=best
```

```
More Help:

  --select-subtitle

通过正则表达式选择符合要求的字幕流. 参考 --select-video

例如:
# 选择所有字幕
-ss all
# 选择所有带有"中文"的字幕
-ss name="中文":for=all
```

```
More Help:

  --custom-range

下载点播内容时, 仅下载部分分片.

时间格式为 MM:SS 或 HH:MM:SS，省略起点表示从头开始，省略终点表示下载到末尾.
时间范围按分片起始时间筛选（包含起止边界），保留完整分片，不进行精确裁切.

例如:
# 下载[0,10]共11个分片
--custom-range 0-10
# 下载从序号10开始的后续分片
--custom-range 10-
# 下载前100个分片
--custom-range -99
# 下载第5分钟到20分钟的内容
--custom-range 05:00-20:00
# 跳过前26秒，下载后续内容
--custom-range 00:26-
# 仅下载前26秒的内容（可能包含跨越26秒边界的完整分片）
--custom-range -00:26
```

```
More Help:

  --save-pattern

使用变量设置各轨道的输出文件名主体，程序自动追加输出扩展名.

* <SaveName>: --save-name 指定的保存名称，未指定时为空
* <Id>: 轨道下载任务ID
* <Codecs>: 编码信息 (如 avc1.64001f, mp4a.40.2)
* <Language>: 语言代码 (如 en, zh-CN)
* <Resolution>: 视频分辨率 (如 1920x1080)
* <Bandwidth>: 码率数值，单位 bit/s (如 5000000)
* <MediaType>: 媒体类型 (VIDEO, AUDIO, SUBTITLES)
* <Channels>: 音频声道信息
* <FrameRate>: 视频帧率
* <VideoRange>: 视频动态范围 (如 SDR, HDR10)
* <GroupId>: 流组标识符

变量区分大小写，缺失的信息替换为空字符串. 模板不需要包含扩展名.

例如:
# 按分辨率命名视频
--save-name video --save-pattern "<SaveName>_<Resolution>"
# 加入码率 (bit/s)
--save-name video --save-pattern "<SaveName>_<Resolution>_<Bandwidth>bps"
# 按语言和声道命名音轨
--save-name audio --save-pattern "<SaveName>_<Language>_<Channels>"
# 用任务ID区分多个配置相同的轨道
--save-name video --save-pattern "<SaveName>_<Id>_<Codecs>"
```

</details>

## FFmpeg 分片合并

默认使用 `--ffmpeg-concat-mode LOCAL_HTTP`：通过仅监听 `127.0.0.1` 的临时输入提供可随机读取的连续字节流，逐个打开分片，不生成整份合并中间文件，也不受分片数量造成的文件句柄和命令行长度限制。

- `LOCAL_HTTP`：默认的本机虚拟输入，保留 concat 协议的字节拼接方式。
- `PROTOCOL`：回退到原有 `concat:文件1|文件2|…`，直接打开全部分片；大量分片仍可能遇到文件句柄上限。
- `DEMUXER`：使用 concat 文件列表，逐文件处理媒体时间轴，结果可能与字节拼接不同。

原有 `--use-ffmpeg-concat-demuxer` 继续支持，命令行选择优先于配置文件；同一层同时指定两种写法时，启用的旧参数优先。该模式选项仅影响 FFmpeg 分片合并；二进制合并、独立 init 的多段拼接及直播管道仍使用各自的处理流程。

## 直播回看

```bash
# 回看一小时前开始的节目，下载 30 分钟并混流为 MKV
N_m3u8DL-RE "URL" --live-catchup "01:00:00" --live-record-limit "00:30:00" --live-real-time-merge -M mkv

# 按节目日期时间回看；未指定时区时使用本地时区
N_m3u8DL-RE "URL" --live-catchup "2026-10-10T21:00:00+08:00" --live-record-limit "00:30:00" --live-real-time-merge -M mkv

# 需要清单提供可靠的时间信息，且起点仍在服务端回看窗口内。
# 按完整分片下载，边界可能略有偏差；未设置录制时长时继续录制直到停止。
```

## 其他
从 v0.1.5 开始，可以尝试开启 `live-pipe-mux` 来代替以上命令

> [!NOTE]
> 如果网络环境不够稳定，请不要开启 `live-pipe-mux`。管道内数据读取由 ffmpeg 负责，在某些环境下容易丢失直播数据。

从 v0.1.8 开始，能够通过设置环境变量 `RE_LIVE_PIPE_OPTIONS` 来改变 `live-pipe-mux` 时 ffmpeg 的某些选项： <https://github.com/nilaoda/N_m3u8DL-RE/issues/162#issuecomment-1592462532>

---

**免责声明**

本软件基于 [MIT License](LICENSE) 开源，按"原样"提供，不附带任何明示或暗示的保证（包括但不限于对适销性、特定用途适用性的保证）。在任何情况下，作者均不对因使用本软件而产生的任何直接或间接损失承担责任。

**合法使用**

本软件仅用于学习和技术研究目的。使用者应遵守所在国家或地区的法律法规，仅下载和获取拥有合法权限的流媒体内容。任何非法或侵权使用行为均与原作者无关，使用者需自行承担全部法律责任。

---

## 赞助

<a href="https://www.buymeacoffee.com/nilaoda" target="_blank"><img src="https://cdn.buymeacoffee.com/buttons/default-orange.png" alt="Buy Me A Coffee" height="41" width="174"></a>
