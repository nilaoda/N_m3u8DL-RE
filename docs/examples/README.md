# Python stdio 客户端示例

`stdio_client.py` 是一个只使用 Python 标准库的终端客户端，演示桌面 GUI 也需要的几项能力：读取轨道、提交选择、合并进度更新、接收最终结果，以及停止或取消任务。

使用 Python 3.9 或更高版本，无需安装额外依赖。Worker 可使用 Native AOT 可执行文件，也可以使用开发构建的 DLL。

## 手动选择清晰度

```bash
python3 docs/examples/stdio_client.py \
  --executable /path/to/N_m3u8DL-RE \
  --save-dir ./downloads \
  --tmp-dir ./downloads/.tmp \
  'https://example.com/master.m3u8'
```

收到轨道选择请求后，示例按视频、音轨和字幕展示带编号的候选项，`*` 表示 Worker 建议的默认项。多分段点播可能先显示媒体段选择，再显示轨道选择。

输入方式：

| 输入 | 含义 |
| --- | --- |
| 回车 | 使用当前请求的默认选择 |
| `2` | 选择第 2 项 |
| `2,4,6` | 选择多个轨道，例如视频、音频和字幕 |
| `1,3-5` | 组合使用编号和范围 |
| `a` | 选择所有项 |
| `q` | 取消任务 |

有独立音轨时，需要同时选中需要的音轨；只选视频编号不会自动附加音轨。候选项使用从 1 开始的显示编号，脚本负责映射为协议中的不透明选项 ID。

终端输入关闭后，脚本无法继续完成手动选择。无人值守运行应使用 `--auto-select`。

## 自动选择

```bash
python3 docs/examples/stdio_client.py \
  --executable /path/to/N_m3u8DL-RE \
  --auto-select --save-dir ./downloads \
  'https://example.com/master.m3u8'
```

开发构建可直接传入 DLL：

```bash
python3 docs/examples/stdio_client.py \
  --executable src/N_m3u8DL-RE/bin/Release/net10.0/N_m3u8DL-RE.dll \
  --auto-select 'https://example.com/video.m3u8'
```

## 进度与控制

交互终端会刷新多条轨道的进度行；重定向输出时改为按行打印进度，便于保存记录。

- 下载：百分比、有效大小、总大小（已知时）、速度和分片数量。
- 直播：已写入大小、速度、累计分片和录制时长。
- 后处理：当前阶段、工具报告的百分比、已处理大小或媒体时间。
- 总量或处理百分比未知时显示 `-- %`，不推测进度。
- 后处理省略的下载统计会保留，任务完成以 `result` 为准。

交互终端中可随时输入 `q` 或按 Ctrl+C 取消任务；再次取消可强制终止 Worker。直播开始报告录制时长后，输入 `s` 请求正常停止并保存。

录制定长直播：

```bash
python3 docs/examples/stdio_client.py \
  --executable /path/to/N_m3u8DL-RE \
  --auto-select --live-record-limit 00:01:00 \
  --save-dir ./recordings 'https://example.com/live.m3u8'
```

完成后，脚本显示成功、失败或取消状态，以及返回的文件路径和大小。

## 查看日志和原始事件

`--verbose` 显示 Worker 的全部日志事件。默认只显示警告、错误、阶段和进度。

`--json` 将 stdout 用于原始 JSON 事件；交互提示、诊断和摘要写入 stderr。配合 `--auto-select` 可记录供程序分析的事件流：

```bash
python3 docs/examples/stdio_client.py \
  --executable /path/to/N_m3u8DL-RE \
  --auto-select --json 'https://example.com/video.m3u8' > events.ndjson
```

该示例使用异步任务分别读取 stdout、stderr 和用户输入。选择过程中继续排空 Worker 管道；退出时关闭 Worker 输入、等待收尾，必要时终止进程。退出码沿用 Worker 的结果；示例本身发生错误时返回 1，任务启动前取消返回 130。

## 处理已有文件

`stdio_file_client.py` 复用同目录 `stdio_client.py` 中的进度显示、日志读取和取消控制。复制示例时需同时保留这两个文件。

```bash
# 按给定顺序做字节拼接
python3 docs/examples/stdio_file_client.py concat part1.ts part2.ts -o joined.ts

# 解封装后合并完整媒体文件
python3 docs/examples/stdio_file_client.py merge part1.mp4 part2.mp4 -o joined.mp4 --concat-mode DEMUXER

# 将已有视频与字幕混流
python3 docs/examples/stdio_file_client.py mux video.mp4 english.srt -o movie.mkv --muxer mkvmerge
```

同样可以通过 `--executable` 指定可执行文件或 DLL，用 `--json` 输出事件。支持目录输入、`--pattern`、`--dry-run` 和 `--overwrite`；输入 `q` 或按 Ctrl+C 取消。文件任务没有直播的 `s` 停止操作。

外部音轨或字幕可作为普通路径输入，也可用 `--mux-import` 追加带元数据的轨道：

```bash
python3 docs/examples/stdio_file_client.py mux video.mp4 -o movie.mkv \
  --mux-import '{"path":"english.srt","language":"en","name":"English subtitles"}'
```

示例脚本的 `--mux-import` 接受 JSON 对象，可重复指定；`language` 和 `name` 可省略，也支持只通过 `--mux-import` 提供全部输入。脚本将它们按顺序追加到 `fileRequest.inputs`。这里的 JSON 参数写法适用于 Python 示例，下载器 CLI 的 `--mux-import` 使用 `path=...:lang=...:name=...` 格式。

API 定义见 [Worker API](../api/worker/README.md)，通信约定见 [stdio Worker 使用指南](../usage/stdio-worker.md)，项目结构与整体流程见 [项目组织与任务流程](../development/project-layout.md)。
