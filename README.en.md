# N_m3u8DL-RE [EN]

Cross-platform DASH/HLS/MSS download tool. Supports on-demand and live streaming (DASH/HLS).

[![img](https://img.shields.io/github/stars/nilaoda/N_m3u8DL-RE?label=%E7%82%B9%E8%B5%9E)](https://github.com/nilaoda/N_m3u8DL-RE)  [![img](https://img.shields.io/github/last-commit/nilaoda/N_m3u8DL-RE?label=%E6%9C%80%E8%BF%91%E6%8F%90%E4%BA%A4)](https://github.com/nilaoda/N_m3u8DL-RE)  [![img](https://img.shields.io/github/release/nilaoda/N_m3u8DL-RE?label=%E6%9C%80%E6%96%B0%E7%89%88%E6%9C%AC)](https://github.com/nilaoda/N_m3u8DL-RE/releases)  [![img](https://img.shields.io/github/license/nilaoda/N_m3u8DL-RE?label=%E8%AE%B8%E5%8F%AF%E8%AF%81)](https://github.com/nilaoda/N_m3u8DL-RE)   [![img](https://img.shields.io/github/downloads/nilaoda/N_m3u8DL-RE/total?label=%E4%B8%8B%E8%BD%BD%E9%87%8F)](https://github.com/nilaoda/N_m3u8DL-RE/releases)

If you encounter a bug, please first confirm whether you are using the latest version of the software. (If you are using a release version, it is recommended to go to the [Actions](https://github.com/nilaoda/N_m3u8DL-RE/actions) page to download the latest automatically built version and check if the issue has already been fixed.) If you are using the latest version and the issue still exists, you can check the [Issues](https://github.com/nilaoda/N_m3u8DL-RE/issues) section to see if someone else has encountered a similar problem. If not, feel free to open a new issue.

---

The built-in terminal in older versions of Windows may not support this program. As an alternative, try running it in [cmder](https://github.com/cmderdev/cmder).

Arch Linux users can install from AUR: [n-m3u8dl-re-bin](https://aur.archlinux.org/packages/n-m3u8dl-re-bin), [n-m3u8dl-re-git](https://aur.archlinux.org/packages/n-m3u8dl-re-git)

```bash
# Install N_m3u8DL-RE release version on Arch Linux and its derivatives (not maintained by the author)
yay -Syu n-m3u8dl-re-bin

# Install N_m3u8DL-RE development version on Arch Linux and its derivatives (not maintained by the author)
yay -Syu n-m3u8dl-re-git
```

---

## PowerShell completion

Supports Windows PowerShell 5.1 and PowerShell 7. The completion script is embedded in the executable. Add `N_m3u8DL-RE` to `PATH`, then load it in PowerShell:

```powershell
N_m3u8DL-RE --generate-completion powershell | Out-String | Invoke-Expression
```

Type `N_m3u8DL-RE --sub-` and press Tab to complete option names. Options such as `--sub-format`, `--log-level` and `--ui-language` also complete their accepted values. Files and directories use PowerShell's default path completion. Calling the executable with `./N_m3u8DL-RE` or its full path also works.

Loading applies to the current session. To enable completion on startup, add the command above to `$PROFILE`. Use `--generate-completion powershell` on its own to view or save the script.

## Cookie files

Use `--cookies cookies.txt` to load a browser export in Netscape cookie format:

```text
N_m3u8DL-RE "https://example.com/video.m3u8" --cookies "cookies.txt"
```

Cookies are matched by domain, path, HTTPS requirement and expiration for manifests, keys, initialization files and segments, including VOD and live streams. A custom `-H "Cookie: ..."` header takes precedence. Server cookie updates are kept in memory and are not written back to the file.

## Configuration file

Save common options in a UTF-8 text file. The program automatically reads:

- Linux / macOS: `~/.config/N_m3u8DL-RE/config.conf`, or `$XDG_CONFIG_HOME/N_m3u8DL-RE/config.conf` when `XDG_CONFIG_HOME` is an absolute path.
- Windows: `%APPDATA%\N_m3u8DL-RE\config.conf`.

The program does not create this file automatically. A missing default file uses the built-in defaults. Configuration files use command-line argument syntax, support comment lines starting with `#`, and accept double-quoted values containing spaces:

```text
# Common download options
-mt
--no-log
--auto-select
--thread-count 16
--save-dir "Downloads/My Videos"
```

Precedence is **command line > configuration file > built-in defaults**. Short and long aliases identify the same option. Command-line values replace configured values for single-value options; replaced values are not converted or read by download option parsers. Repeatable options such as `--key`, `--ad-keyword` and `--mux-import` are combined with configuration values first and command-line values last. `-H` / `--header` values are merged by case-insensitive header name: distinct headers are retained, and command-line values override configured headers with the same name. Use `false` to disable a configured boolean, for example `--no-log false`. A configured HTTP timeout counts as an explicit timeout and disables automatic live timeout adjustment.

```text
N_m3u8DL-RE "https://example.com/video.m3u8" --thread-count 8
N_m3u8DL-RE "https://example.com/video.m3u8" --config "custom.conf"
N_m3u8DL-RE "https://example.com/video.m3u8" --no-config
```

`--config FILE` loads only the specified file instead of the default configuration; missing or invalid files are errors. `--no-config` disables configuration loading and cannot be combined with `--config`. Configuration files may contain download options only, not input URLs, configuration-loading options, help, version or completion actions. Relative paths use the current working directory. The executable directory and working directory are not searched automatically.

Help uses the configured UI language. Completion script generation and Tab completion do not read configuration files. Existing `@args.txt` response files remain supported and their arguments have command-line precedence.

For Docker, mount the file read-only at `/config/config.conf` and pass `--config /config/config.conf`, independently of the container's HOME or runtime user.

## Command line parameters

Long automatic names, custom save names and expanded filename patterns are shortened with a hash suffix to reduce collisions. Limits use UTF-8 bytes without splitting Chinese characters or emoji, with space reserved for automatic timestamps and media extensions.

```
Description:
  N_m3u8DL-RE 0.6.0 20260628

Usage:
  N_m3u8DL-RE <input> [options]

Arguments:
  <input>  Input Url or File

Options:
  --config <FILE>                                         Read a configuration file instead of the user default; command-line options take precedence
  --no-config                                             Disable configuration loading; cannot be combined with --config
  --tmp-dir <tmp-dir>                                     Set temporary file directory
  --save-dir <save-dir>                                   Set output directory
  --save-name <save-name>                                 Set output filename
  --save-pattern <save-pattern>                           Set output filename pattern. Use "--morehelp save-pattern" for variables and examples
  --log-file-path <log-file-path>                         Set log file path, Example: C:\Logs\log.txt
  --base-url <base-url>                                   Set BaseURL
  --thread-count <number>                                 Set download thread count [default: CPU thread count]
  --download-retry-count <number>                         Retries per segment; segmented live recording keeps waiting for recovery after transient network failures [default: 3]
  --http-request-timeout <seconds>                        HTTP timeout in seconds; segmented live recording adjusts automatically unless specified, also bounds segment read stalls, not total download time [default: 100]
  --force-ansi-console                                    Force assuming the terminal is ANSI-compatible and interactive
  --no-ansi-color                                         Remove ANSI colors
  --auto-select                                           Automatically selects the best tracks of all types [default: False]
  --skip-merge                                            Skip segments merge [default: False]
  --skip-download                                         Skip download [default: False]
  --check-segments-count                                  Check if the actual number of segments downloaded matches the expected number [default: True]
  --binary-merge                                          Binary merge [default: False]
  --use-ffmpeg-concat-demuxer                             When merging with ffmpeg, use the concat demuxer instead of the concat protocol [default: False]
  --del-after-done                                        Delete temporary files when done [default: True]
  --no-date-info                                          Date information is not written during muxing [default: False]
  --no-log                                                Disable log file output [default: False]
  --write-meta-json                                       Write meta json after parsed [default: True]
  --append-url-params                                     Append input URL query parameters to segments; local manifests use --base-url parameters [default: False]
  -mt, --concurrent-download                              Concurrently download the selected audio, video and subtitles [default: False]
  -H, --header <header>                                   Pass custom header(s) to server, Example:
                                                          -H "Cookie: mycookie" -H "User-Agent: iOS"
  --cookies <FILE>                                        Load cookies from a Netscape cookie file; a custom Cookie header takes precedence
  --sub-only                                              Select only subtitle tracks [default: False]
  --sub-format <SRT|VTT>                                  Subtitle output format [default: SRT]
  --auto-subtitle-fix                                     Automatically fix subtitles [default: True]
  --ffmpeg-binary-path <PATH>                             Full path to the ffmpeg binary, like C:\Tools\ffmpeg.exe
  --log-level <DEBUG|ERROR|INFO|OFF|WARN>                 Set log level [default: INFO]
  --ui-language <en-US|zh-CN|zh-TW>                       Set UI language
  --urlprocessor-args <urlprocessor-args>                 Give these arguments to the URL Processors.
  --key <key>                                             Set decryption key(s) to mp4decrypt/shaka-packager/ffmpeg. format:
                                                          --key KID1:KEY1 --key KID2:KEY2
                                                          or use --key KEY if all tracks share the same key.
  --key-text-file <key-text-file>                         Set the kid-key file, the program will search the KEY with KID from the file.(Very large file are not recommended)
  --decryption-engine <FFMPEG|MP4DECRYPT|SHAKA_PACKAGER>  Set the third-party program used for decryption [default: MP4DECRYPT]
  --decryption-binary-path <PATH>                         Full path to the tool used for MP4 decryption, like C:\Tools\mp4decrypt.exe
  --mp4-real-time-decryption                              Decrypt MP4 segments in real time [default: False]
  -R, --max-speed <SPEED>                                 Set speed limit, Mbps or Kbps, for example: 15M 100K.
  -M, --mux-after-done <OPTIONS>                          When all works is done, try to mux the downloaded streams. Use "--morehelp mux-after-done" for more details
  --custom-hls-method <METHOD>                            Set HLS encryption method (AES_128|AES_128_ECB|CENC|CHACHA20|NONE|SAMPLE_AES|SAMPLE_AES_CTR|UNKNOWN)
  --custom-hls-key <FILE|HEX|BASE64>                      Set the HLS decryption key. Can be file, HEX or Base64
  --custom-hls-iv <FILE|HEX|BASE64>                       Set the HLS decryption iv. Can be file, HEX or Base64
  --custom-hls-scope <SCOPE>                              Apply custom HLS method, key and IV to selected media type (ALL|VIDEO|AUDIO) [default: ALL]
  --use-system-proxy                                      Use system default proxy [default: True]
  --custom-proxy <URL>                                    Set web request proxy, like http://127.0.0.1:8888
  --interface <INTERFACE>                                 Use the specified network interface or local IP address, e.g. eth1 or 192.168.1.10
  --custom-range <RANGE>                                  Download only part of the segments. Use "--morehelp custom-range" for more details
  --task-start-at <yyyyMMddHHmmss>                        Task execution will not start before this time
  --live-perform-as-vod                                   Download live streams as vod [default: False]
  --live-real-time-merge                                  Real-time merge into file when recording live [default: False]
  --live-keep-segments                                    Keep segments when recording a live (liveRealTimeMerge enabled) [default: True]
  --live-pipe-mux                                         Real-time muxing to TS file through pipeline + ffmpeg (liveRealTimeMerge enabled) [default: False]
  --live-fix-vtt-by-audio                                 Correct VTT sub by reading the start time of the audio file [default: False]
  --live-record-limit <HH:mm:ss>                          Recording time limit when recording live
  --live-wait-time <SEC>                                  Manually set the live playlist refresh interval
  --live-idle-timeout <SEC>                               Stop recording when a live playlist has no new segments for this many seconds (disabled by default)
  --live-take-count <NUM>                                 Manually set the number of segments downloaded for the first time when recording live [default: 16]
  --mux-import <OPTIONS>                                  When MuxAfterDone enabled, allow to import local media files. Use "--morehelp mux-import" for more details
  -sv, --select-video <OPTIONS>                           Select video streams by regular expressions. Use "--morehelp select-video" for more details
  -sa, --select-audio <OPTIONS>                           Select audio streams by regular expressions. Use "--morehelp select-audio" for more details
  -ss, --select-subtitle <OPTIONS>                        Select subtitle streams by regular expressions. Use "--morehelp select-subtitle" for more details
  -dv, --drop-video <OPTIONS>                             Drop video streams by regular expressions. Accepts the same options as --select-video, use "--morehelp select-video" for more details
  -da, --drop-audio <OPTIONS>                             Drop audio streams by regular expressions. Accepts the same options as --select-video, use "--morehelp select-video" for more details
  -ds, --drop-subtitle <OPTIONS>                          Drop subtitle streams by regular expressions. Accepts the same options as --select-video, use "--morehelp select-video" for more details
  --ad-keyword <REG>                                      Set URL keywords (regular expressions) for AD segments
  --vod-select-parts                                      Control VOD section selection: omitted = automatic, true = always prompt, false = disable (Space/Enter)
  --vod-list-parts                                        List VOD sections grouped by media configuration, with IDs and total durations, then exit [default: False]
  --vod-drop-parts <IDS>                                  Drop VOD sections and matching audio/subtitles by --vod-list-parts IDs, e.g. 0,2-4
  --disable-update-check                                  Disable version update check [default: False]
  --allow-hls-multi-ext-map                               Allow multiple #EXT-X-MAP in live HLS (experimental; enabled for VOD) [default: False]
  --morehelp <OPTION>                                     Set more help info about one option
  --generate-completion <SHELL>                           Print the embedded completion script (powershell)
  -?, -h, --help                                          Show help and usage information
  --version                                               Show version information
```

`--interface` accepts a network interface name (e.g. `eth1`, `en0` or `Wi-Fi`) or a local IP address. A name constrains the outgoing interface; an IP binds the connection source address. With a proxy, it applies to the connection to the proxy. DNS uses the system resolver. Binding failures are reported without falling back to another interface. Linux binding by name may require additional permissions; the diagnostic includes the system error.

`--custom-hls-scope VIDEO` applies `--custom-hls-method`, `--custom-hls-key`, and `--custom-hls-iv` only to video renditions in a master playlist; `AUDIO` selects audio renditions. Audio-only variants can be identified through `CODECS`; variants without enough type information still use the video scope. The default `ALL` preserves the existing behavior. A standalone media playlist has no rendition type, so the custom settings apply to that playlist.

Interactive VOD downloads group repeated sections by actual media configuration. Use Space to toggle groups and Enter to confirm; all groups are kept by default. A clear gap in section durations further separates groups without classifying ads automatically. HLS inspects each distinct init for codecs, resolution and audio configuration; init URLs are omitted from the UI. Omitting `--vod-select-parts` keeps automatic detection; `--auto-select` and stream filters do not trigger this prompt automatically. Use `--vod-select-parts` (or `--vod-select-parts true`) to force the prompt, or `--vod-select-parts false` to disable section selection. Ads with the same configuration and similar durations still require URL rules or explicit section IDs.

For scripts, `--vod-list-parts` lists grouped original section IDs and `--vod-drop-parts 0,2-4` removes specified sections across video, audio and subtitles. DASH IDs are original Period positions starting at 0; HLS uses original discontinuity sequence numbers. All MAPs within a discontinuity section stay together. For MAP changes without discontinuities, use `--ad-keyword` to match init/media URLs. `--custom-range` retains source segment indices after selection.

<details>
<summary>Click to view "More Help" section</summary>

```
More Help:

  --mux-after-done

When all works is done, try to mux the downloaded streams. OPTIONS is a colon separated list of:

* format=FORMAT: set container. mkv, mp4, ts
* muxer=MUXER: set muxer. ffmpeg, mkvmerge (Default: ffmpeg)
* bin_path=PATH: set binary file path. (Default: auto)
* skip_sub=BOOL: set whether or not skip subtitle files (Default: false)
* keep=BOOL: set whether or not keep files. true, false (Default: false)

Examples:
# mux to mp4
-M format=mp4
# use mkvmerge, auto detect bin path
-M format=mkv:muxer=mkvmerge
# use mkvmerge, set bin path
-M format=mkv:muxer=mkvmerge:bin_path="C\:\Program Files\MKVToolNix\mkvmerge.exe"
```

```
More Help:

  --mux-import

When MuxAfterDone enabled, allow to import local media files. OPTIONS is a colon separated list of:

* path=PATH: set file path
* lang=CODE: set media language code (not required)
* name=NAME: set description (not required)

Examples:
# import subtitle
--mux-import path=en-US.srt:lang=eng:name="English (Original)"
# import audio and subtitle
--mux-import path="D\:\media\atmos.m4a":lang=eng:name="English Description Audio" --mux-import path="D\:\media\eng.vtt":lang=eng:name="English (Description)"
```

```
More Help:

  --select-video

Select video streams by regular expressions. OPTIONS is a colon (:) separated list of the following sub-keys.
The same sub-keys also work for --select-audio/-sa, --select-subtitle/-ss and the matching --drop-video/--drop-audio/--drop-subtitle options.

* id=REGEX: match by group/stream id
* lang=REGEX: match by language code
* name=REGEX: match by stream name
* codecs=REGEX: match by codecs (e.g. hvc1, avc1, mp4a)
* res=REGEX: match by resolution (e.g. 1920*, 3840*)
* frame=REGEX: match by frame rate
* channel=REGEX: match by audio channel count (e.g. 6, 2)
* range=REGEX: match by video range (e.g. SDR, HDR, PQ)
* url=REGEX: match by segment url
* period=REGEX: match by DASH Period id (multi-Period MPD, e.g. ads/chapters)
* segsMin=number: keep streams with at least number segments
* segsMax=number: keep streams with at most number segments
* plistDurMin=hms: keep streams whose playlist duration >= hms (e.g. 1h20m30s, 90s)
* plistDurMax=hms: keep streams whose playlist duration <= hms
* bwMin=int: keep streams with bandwidth >= int Kbps
* bwMax=int: keep streams with bandwidth <= int Kbps
* role=string: match by DASH role (Subtitle, Main, Alternate, Supplementary, Commentary, Dub, Description, Sign, Metadata, ForcedSubtitle)
* for=FOR: how many of the matched streams to keep. best[number], worst[number], all (Default: best)

Examples:
# select best video
-sv best
# select 4K+HEVC video
-sv res="3840*":codecs=hvc1:for=best
# Select best video with duration longer than 1 hour 20 minutes 30 seconds
-sv plistDurMin="1h20m30s":for=best
-sv role="main":for=best
# Select video with bandwidth between 800Kbps and 1Mbps
-sv bwMin=800:bwMax=1000
# Drop subtitle streams that have at most 2 segments (e.g. trick-play/ad playlists)
-ds segsMax=2:for=all --auto-select
# Keep only the main content Period (exclude ad Periods)
-sv period="main":for=best
# Drop video from the ad Period
-dv period="ad":for=all
```

```
More Help:

  --select-audio

Select audio streams by regular expressions. ref --select-video

Examples:
# select all
-sa all
# select best eng audio
-sa lang=en:for=best
# select best 2, and language is ja or en
-sa lang="ja|en":for=best2
-sa role="main":for=best
```

```
More Help:

  --select-subtitle

Select subtitle streams by regular expressions. ref --select-video

Examples:
# select all subs
-ss all
# select all subs containing "English"
-ss name="English":for=all
```

```
More Help:

  --custom-range

Download only part of the segments when downloading vod content.

Examples:
# Download [0,10], a total of 11 segments
--custom-range 0-10
# Download subsequent segments starting from index 10
--custom-range 10-
# Download the first 100 segments
--custom-range -99
# Download content from the 05:00 to 20:00
--custom-range 05:00-20:00
```

```
More Help:

  --save-pattern

Set each track's output filename stem using variables. The output extension is appended automatically.

* <SaveName>: name specified by --save-name, or empty when omitted
* <Id>: track download task ID
* <Codecs>: codec information (e.g. avc1.64001f, mp4a.40.2)
* <Language>: language code (e.g. en, zh-CN)
* <Resolution>: video resolution (e.g. 1920x1080)
* <Bandwidth>: bitrate value in bit/s (e.g. 5000000)
* <MediaType>: media type (VIDEO, AUDIO, SUBTITLES)
* <Channels>: audio channel information
* <FrameRate>: video frame rate
* <VideoRange>: video dynamic range (e.g. SDR, HDR10)
* <GroupId>: stream group identifier

Variables are case-sensitive. Missing values become empty strings. Do not include the output extension in the pattern.

Examples:
# Name video tracks by resolution
--save-name video --save-pattern "<SaveName>_<Resolution>"
# Include bitrate (bit/s)
--save-name video --save-pattern "<SaveName>_<Resolution>_<Bandwidth>bps"
# Name audio tracks by language and channels
--save-name audio --save-pattern "<SaveName>_<Language>_<Channels>"
# Use task IDs to distinguish tracks with the same configuration
--save-name video --save-pattern "<SaveName>_<Id>_<Codecs>"
```

</details>

## Others
From v0.1.5, you can try to enable `live-pipe-mux` instead of the above command

> [!NOTE]
> If the network environment is not stable, do not enable `live-pipe-mux`. The data read in the pipeline is handled by ffmpeg, and it is easy to lose live data in some environments.

From v0.1.8, you can set the environment variable `RE_LIVE_PIPE_OPTIONS` to change some options of ffmpeg when `live-pipe-mux` is enabled: <https://github.com/nilaoda/N_m3u8DL-RE/issues/162#issuecomment-1592462532>

---

**Disclaimer**

This software is open-sourced under the [MIT License](LICENSE) and is provided "as is", without any express or implied warranties (including but not limited to the warranties of merchantability and fitness for a particular purpose). In no event shall the authors be liable for any direct or indirect damages arising from the use of this software.

**Legal Use**

This software is intended for learning and technical research purposes only. Users shall comply with applicable laws and regulations in their country or region, and only download streaming content for which they have legal permission. Any illegal or infringing use is unrelated to the original author, and users shall bear all legal responsibilities themselves.

---

## Donate

<a href="https://www.buymeacoffee.com/nilaoda" target="_blank"><img src="https://cdn.buymeacoffee.com/buttons/default-orange.png" alt="Buy Me A Coffee" height="41" width="174"></a>
