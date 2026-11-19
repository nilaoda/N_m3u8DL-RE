#!/usr/bin/env python3
"""通过 stdio Worker 合并或混流已有文件，显示进度并支持取消。"""

import argparse
import asyncio
import json
import sys

from stdio_client import WorkerClient, run


def mux_input(value):
    try:
        entry = json.loads(value)
    except json.JSONDecodeError as error:
        raise argparse.ArgumentTypeError("请提供带 path 字段的 JSON 对象。") from error
    if not isinstance(entry, dict) or not isinstance(entry.get("path"), str) or not entry["path"].strip():
        raise argparse.ArgumentTypeError("导入轨道必须是带非空 path 字段的 JSON 对象。")
    return entry


class FileWorkerClient(WorkerClient):
    def start_fields(self):
        request = {
            "inputs": [{"path": path} for path in self.args.inputs] + self.args.mux_import,
            "output": self.args.output,
            "overwrite": self.args.overwrite,
            "dryRun": self.args.dry_run,
        }
        for field, value in (("pattern", self.args.pattern), ("muxer", self.args.muxer),
                             ("concatMode", self.args.concat_mode), ("title", self.args.title),
                             ("ffmpegBinaryPath", self.args.ffmpeg_binary_path),
                             ("mkvmergeBinaryPath", self.args.mkvmerge_binary_path)):
            if value is not None:
                request[field] = value
        if self.args.no_auto_subtitle_fix:
            request["autoSubtitleFix"] = False
        return {"operation": self.args.operation, "fileRequest": request}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("operation", choices=("concat", "merge", "mux"), help="文件处理类型")
    parser.add_argument("inputs", nargs="*", help="输入文件或录制目录，按给定顺序处理")
    parser.add_argument("-o", "--output", required=True, help="输出文件路径")
    parser.add_argument("--executable", default="N_m3u8DL-RE", help="Native AOT 可执行文件或开发构建 DLL")
    parser.add_argument("--pattern", help="目录输入的文件筛选，例如 *.ts")
    parser.add_argument("--overwrite", action="store_true", help="成功处理后替换已有输出")
    parser.add_argument("--dry-run", action="store_true", help="只校验和预览，不写出文件")
    parser.add_argument("--mux-import", action="append", type=mux_input, default=[], metavar="JSON",
                        help="mux 追加轨道的 JSON 对象，支持 path、language、name；可重复指定")
    parser.add_argument("--muxer", choices=("ffmpeg", "mkvmerge"), help="mux 使用的混流器")
    parser.add_argument("--concat-mode", choices=("LOCAL_HTTP", "PROTOCOL", "DEMUXER"), help="merge 使用的合并模式")
    parser.add_argument("--title", help="mux 输出的标题")
    parser.add_argument("--no-auto-subtitle-fix", action="store_true", help="mux 时关闭字幕时间轴修复")
    parser.add_argument("--ffmpeg-binary-path", help="FFmpeg 路径，适用于 merge 和 mux")
    parser.add_argument("--mkvmerge-binary-path", help="mkvmerge 路径，适用于 mux")
    parser.add_argument("--verbose", action="store_true", help="显示 Worker 的全部日志事件")
    parser.add_argument("--json", action="store_true", help="stdout 输出原始 JSON 事件，提示写入 stderr")
    args = parser.parse_args()
    if args.mux_import and args.operation != "mux":
        parser.error("--mux-import 只适用于 mux。")
    if not args.inputs and not args.mux_import:
        parser.error("至少指定一个输入文件、目录或 --mux-import。")
    try:
        return asyncio.run(run(args, FileWorkerClient))
    except KeyboardInterrupt:
        return 130
    except Exception as error:
        print("错误：" + str(error), file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
