#!/usr/bin/env python3
"""通过 stdio Worker 选择清晰度、查看进度并控制下载或录制。仅使用 Python 标准库。"""

import argparse
import asyncio
import codecs
import contextlib
import json
import os
import shutil
import signal
import subprocess
import sys
import threading
import time


PHASES = {
    "analyzing": "解析", "downloading": "下载", "preparing": "准备",
    "decrypting": "解密", "subtitles": "字幕处理", "merging": "合并",
    "muxing": "混流", "finalizing": "保存", "finished": "结束",
}
MEDIA_TYPES = {"video": "视频 / 清晰度", "audio": "音轨", "subtitles": "字幕", "binary": "文件", "file": "文件任务"}
STATUSES = {"running": "运行中", "waitingForInput": "等待选择", "stopping": "正在停止",
            "succeeded": "成功", "failed": "失败", "cancelled": "已取消"}


def size_text(value):
    if value is None:
        return "未知"
    value = float(value)
    for unit in ("B", "KiB", "MiB", "GiB", "TiB"):
        if abs(value) < 1024 or unit == "TiB":
            return f"{value:.1f} {unit}"
        value /= 1024


def duration_text(seconds):
    seconds = max(0, int(seconds))
    return f"{seconds // 3600:02d}:{seconds // 60 % 60:02d}:{seconds % 60:02d}"


def selection_ids(answer, interaction):
    choices = interaction["choices"]
    answer = answer.strip().lower()
    if not answer:
        selected = list(interaction.get("defaultIds", []))
    elif answer in ("a", "all"):
        selected = [choice["id"] for choice in choices]
    else:
        numbers = set()
        for item in answer.replace("，", ",").split(","):
            bounds = item.strip().split("-")
            if len(bounds) not in (1, 2) or any(not part.isdigit() for part in bounds):
                raise ValueError("请输入编号或范围，例如 1,3-5。")
            first, last = int(bounds[0]), int(bounds[-1])
            if not 1 <= first <= last <= len(choices):
                raise ValueError(f"编号必须在 1～{len(choices)} 之间。")
            numbers.update(range(first, last + 1))
        selected = [choice["id"] for index, choice in enumerate(choices, 1) if index in numbers]
    if not selected:
        raise ValueError("至少选择一项。")
    return selected


class ConsoleInput:
    def __init__(self):
        self.lines = asyncio.Queue()
        loop = asyncio.get_running_loop()

        def deliver(line):
            try:
                loop.call_soon_threadsafe(self.lines.put_nowait, line)
                return True
            except RuntimeError:
                return False

        def read():
            decoder = codecs.getincrementaldecoder(sys.stdin.encoding or "utf-8")(errors="replace")
            pending = ""
            try:
                # 守护线程只读原始文件描述符；退出时不等待终端输入，也不持有 stdin 的缓冲锁。
                while chunk := os.read(sys.stdin.fileno(), 4096):
                    pending += decoder.decode(chunk)
                    while "\n" in pending:
                        line, pending = pending.split("\n", 1)
                        if not deliver(line.rstrip("\r")):
                            return
                pending += decoder.decode(b"", final=True)
                if pending:
                    deliver(pending.rstrip("\r"))
            except (OSError, ValueError):
                pass
            deliver(None)

        threading.Thread(target=read, daemon=True).start()


class ConsoleView:
    def __init__(self, json_output=False):
        self.stream = sys.stderr if json_output else sys.stdout
        self.json_output = json_output
        self.terminal = self.stream.isatty() and not json_output and os.environ.get("TERM") != "dumb"
        self.tracks = {}
        self.progress = {}
        self.prompting = False
        self.drawn = 0
        self.last_render = 0
        self.last_plain = {}

    def clear(self):
        if self.drawn:
            self.stream.write(f"\x1b[{self.drawn}A\r\x1b[J")
            self.stream.flush()
            self.drawn = 0

    def message(self, text):
        self.clear()
        print(text, file=self.stream, flush=True)

    def track_name(self, track_id):
        track = self.tracks.get(track_id, {})
        kind = MEDIA_TYPES.get(track.get("mediaType"), "轨道")
        details = " ".join(str(track[key]) for key in ("resolution", "language", "codecs") if track.get(key))
        return f"#{track_id} {kind}" + (f" {details}" if details else "")

    def update(self, value):
        track_id = value["trackId"]
        record = self.progress.setdefault(track_id, {"download": {}, "processing": {}})
        phase = value["phase"]
        if record.get("phase") != phase:
            record["processing"] = {}
        record["phase"] = phase
        # 事件是局部更新；后处理省略的下载计数必须保留，处理百分比不能沿用下载百分比。
        record["download"].update({key: item for key, item in value.items() if item is not None and key in (
            "downloadedBytes", "receivedBytes", "totalBytes", "speedBytesPerSecond",
            "completedSegments", "totalSegments", "recordedSeconds")})
        current = record["download"] if phase == "downloading" else record["processing"]
        current.update({key: value[key] for key in ("processedBytes", "processedSeconds") if value.get(key) is not None})
        current["fraction"] = value.get("fraction")
        self.render()

    def progress_line(self, track_id, record):
        phase = record["phase"]
        download = record["download"]
        current = download if phase == "downloading" else record["processing"]
        fraction = current.get("fraction")
        percent = f"{max(0, min(1, fraction)) * 100:6.2f}%" if fraction is not None else "   -- %"
        pieces = [self.track_name(track_id), PHASES.get(phase, phase), percent]
        if phase == "downloading":
            if "downloadedBytes" in download:
                total = "/" + size_text(download["totalBytes"]) if "totalBytes" in download else ""
                pieces.append(size_text(download["downloadedBytes"]) + total)
            if "speedBytesPerSecond" in download:
                pieces.append(size_text(download["speedBytesPerSecond"]) + "/s")
            if "completedSegments" in download:
                total = str(download["totalSegments"]) if "totalSegments" in download else "?"
                pieces.append(f"分片 {download['completedSegments']}/{total}")
            if "recordedSeconds" in download:
                pieces.append("录制 " + duration_text(download["recordedSeconds"]))
        else:
            if "processedBytes" in current:
                pieces.append("已处理 " + size_text(current["processedBytes"]))
            if "processedSeconds" in current:
                pieces.append("媒体时间 " + duration_text(current["processedSeconds"]))
            if "downloadedBytes" in download:
                pieces.append("已下载 " + size_text(download["downloadedBytes"]))
        return " | ".join(pieces)

    def render(self, final=False):
        if self.prompting or self.json_output:
            return
        now = time.monotonic()
        if not final and now - self.last_render < 0.2:
            return
        self.last_render = now
        lines = [(key, self.progress_line(key, record)) for key, record in self.progress.items()]
        if self.terminal:
            self.clear()
            width = shutil.get_terminal_size().columns
            for _, line in lines:
                # 只输出单行，避免自动折行后终端光标位置与进度行数不一致。
                budget = max(10, width - 1)
                visible, used = [], 0
                for char in line:
                    used += 2 if ord(char) > 255 else 1
                    if used > budget:
                        break
                    visible.append(char)
                print("".join(visible), file=self.stream, flush=True)
            self.drawn = 0 if final else len(lines)
        else:
            for key, line in lines:
                previous, timestamp = self.last_plain.get(key, (None, 0))
                if final or line != previous and now - timestamp >= 1:
                    print(line, file=self.stream, flush=True)
                    self.last_plain[key] = (line, now)

    def choose(self, interaction):
        self.clear()
        self.prompting = True
        title = "选择需要保留的媒体段" if interaction["kind"] == "parts" else "选择视频清晰度、音轨和字幕"
        self.message("\n" + title + "（* 为默认项）")
        defaults = set(interaction.get("defaultIds", []))
        previous_group = None
        for index, choice in enumerate(interaction["choices"], 1):
            group = "分段配置" if interaction["kind"] == "parts" else MEDIA_TYPES.get(choice.get("mediaType"), "轨道")
            if group != previous_group:
                self.message(f"  [{group}]")
                previous_group = group
            marker = "*" if choice["id"] in defaults else " "
            self.message(f"  {index:>3}. {marker} {choice['label']}")
        if interaction["kind"] == "streams":
            self.message("独立音轨和字幕也需要选中，例如：2,4,6。")
        self.prompt()

    def prompt(self):
        print("编号 / 范围（1,3-5），回车默认，a 全选，q 取消：", end="", file=self.stream, flush=True)

    def finish(self, result):
        self.prompting = False
        self.render(final=True)
        self.message("\n任务" + STATUSES.get(result["status"], result["status"]))
        if result.get("error"):
            self.message("错误：" + result["error"]["message"])
        for output in result.get("outputs", []):
            size = " (" + size_text(output["size"]) + ")" if output.get("size") is not None else ""
            self.message("输出：" + output["path"] + size)


class WorkerClient:
    def __init__(self, process, args):
        self.process = process
        self.args = args
        self.view = ConsoleView(args.json)
        self.input = ConsoleInput()
        self.pending = None
        self.selection_ready = asyncio.Event()
        self.send_lock = asyncio.Lock()
        self.command_number = 0
        self.result = None
        self.live = False
        self.cancelling = False
        self.started = False
        self.input_closed = False

    def start_fields(self):
        request = {"input": self.args.input, "autoSelect": self.args.auto_select, "noLog": True}
        for field, value in (("saveDir", self.args.save_dir), ("saveName", self.args.save_name),
                             ("tmpDir", self.args.tmp_dir), ("liveRecordLimit", self.args.live_record_limit)):
            if value is not None:
                request[field] = value
        return {"request": request}

    async def send(self, kind, **fields):
        async with self.send_lock:
            self.command_number += 1
            message = {"type": kind, "id": f"command-{self.command_number}", **fields}
            self.process.stdin.write((json.dumps(message, ensure_ascii=False) + "\n").encode("utf-8"))
            await self.process.stdin.drain()

    def kill(self):
        if self.process.returncode is None:
            if os.name == "posix":
                with contextlib.suppress(ProcessLookupError):
                    os.killpg(self.process.pid, signal.SIGKILL)
            else:
                with contextlib.suppress(OSError):
                    subprocess.run(["taskkill", "/PID", str(self.process.pid), "/T", "/F"],
                                   stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, check=False)
                with contextlib.suppress(ProcessLookupError):
                    self.process.kill()

    async def cancel(self):
        if self.result is not None:
            return
        if self.cancelling:
            self.view.message("强制终止 Worker。")
            self.kill()
            return
        self.cancelling = True
        self.pending = None
        self.selection_ready.clear()
        self.view.prompting = False
        self.view.message("正在取消任务，再次按 Ctrl+C 或输入 q 可强制终止。")
        if not self.started:
            self.process.stdin.close()
            return
        with contextlib.suppress(BrokenPipeError, ConnectionResetError):
            await self.send("cancel")

    async def read_input(self):
        while True:
            # 管道输入可预先提供多次选择；没有交互请求时不提前消费下一份答案。
            if not sys.stdin.isatty() and not self.pending:
                await self.selection_ready.wait()
            line = await self.input.lines.get()
            if line is None:
                self.input_closed = True
                if self.pending:
                    raise RuntimeError("输入已关闭，无法完成选择；无人值守运行请使用 --auto-select。")
                return
            command = line.strip().lower()
            if command in ("q", "quit", "cancel"):
                await self.cancel()
            elif self.pending:
                try:
                    selected = selection_ids(line, self.pending)
                except ValueError as error:
                    self.view.message(str(error))
                    self.view.prompt()
                    continue
                interaction = self.pending
                self.pending = None
                self.selection_ready.clear()
                self.view.prompting = False
                await self.send("respond", interactionId=interaction["id"], selectedIds=selected)
                self.view.message("已提交选择。")
            elif command in ("s", "stop"):
                if self.live:
                    await self.send("stop")
                    self.view.message("已请求正常停止录制，等待保存和收尾。")
                else:
                    self.view.message("s 仅在直播录制开始后可用；取消下载请使用 q。")
            elif command:
                self.view.message("输入 q 取消任务，直播录制时输入 s 正常停止。")

    async def read_events(self):
        async def receive():
            line = await self.process.stdout.readline()
            return json.loads(line) if line else None

        ready = await asyncio.wait_for(receive(), timeout=10)
        if not ready or ready.get("type") != "ready" or ready.get("protocolVersion") != 1:
            raise RuntimeError("Worker 未就绪或协议版本不兼容。")
        if self.args.json:
            print(json.dumps(ready, ensure_ascii=False), flush=True)
        if self.cancelling:
            await asyncio.wait_for(self.process.wait(), timeout=5)
            return 130
        await self.send("start", protocolVersion=1, **self.start_fields())
        self.started = True
        while event := await receive():
            if self.args.json:
                print(json.dumps(event, ensure_ascii=False), flush=True)
            kind = event["type"]
            if kind == "interaction":
                if self.input_closed:
                    raise RuntimeError("输入已关闭，无法完成选择；无人值守运行请使用 --auto-select。")
                self.pending = event["interaction"]
                self.view.choose(self.pending)
                self.selection_ready.set()
            elif kind == "tracks":
                self.view.tracks.update((track["id"], track) for track in event["tracks"])
                for track in event["tracks"]:
                    self.view.message("选中：" + track["label"])
            elif kind == "state" and not self.args.json:
                self.view.message(f"状态：{STATUSES.get(event['status'], event['status'])} / {PHASES.get(event['phase'], event['phase'])}")
            elif kind == "progress":
                self.live |= event["progress"].get("recordedSeconds") is not None
                self.view.update(event["progress"])
            elif kind == "log" and (self.args.verbose or event.get("level") in ("warn", "error")):
                self.view.message(event["message"])
            elif kind == "error":
                raise RuntimeError(event["error"]["message"])
            elif kind == "result":
                self.result = event["result"]
                self.view.finish(self.result)
                break
        code = await asyncio.wait_for(self.process.wait(), timeout=5)
        if self.result is None:
            raise RuntimeError(f"Worker 未返回结果，退出码：{code}")
        if self.result["status"] == "succeeded" and code != 0:
            raise RuntimeError(f"Worker 报告成功但退出码为 {code}")
        return code

    async def read_diagnostics(self):
        while line := await self.process.stderr.readline():
            self.view.message("[诊断] " + line.decode("utf-8", errors="replace").rstrip())


async def run(args, client_type=WorkerClient):
    command = [args.executable]
    if args.executable.lower().endswith(".dll"):
        command.insert(0, "dotnet")
    process = await asyncio.create_subprocess_exec(
        *command, "worker", "--stdio", stdin=asyncio.subprocess.PIPE,
        stdout=asyncio.subprocess.PIPE, stderr=asyncio.subprocess.PIPE,
        limit=4 * 1024 * 1024, **({"start_new_session": True} if os.name == "posix" else {}),
    )
    client = client_type(process, args)
    loop = asyncio.get_running_loop()
    old_handler = signal.getsignal(signal.SIGINT)
    signal.signal(signal.SIGINT, lambda *_: loop.call_soon_threadsafe(lambda: asyncio.create_task(client.cancel())))
    events = asyncio.create_task(client.read_events())
    inputs = asyncio.create_task(client.read_input())
    diagnostics = asyncio.create_task(client.read_diagnostics())
    if not args.json:
        client.view.message("已启动 Worker。输入 q / Ctrl+C 取消，直播录制时输入 s 正常停止。")
    try:
        monitored = {events, inputs, diagnostics}
        while not events.done():
            done, _ = await asyncio.wait(monitored, return_when=asyncio.FIRST_COMPLETED)
            # 输入 EOF 不影响已提交的下载；持续观察辅助任务，避免忽略稍后发生的输入错误。
            for task in done:
                if task.exception() is not None:
                    raise task.exception()
            monitored.difference_update(done)
        return events.result()
    finally:
        client.view.clear()
        inputs.cancel()
        process.stdin.close()
        # 关闭 Worker 输入后仍持续排空输出，直到它完成收尾或达到等待上限。
        if not events.done():
            with contextlib.suppress(Exception, asyncio.CancelledError):
                await asyncio.wait_for(asyncio.shield(events), timeout=15)
        if process.returncode is None:
            client.kill()
            await process.wait()
        for task in (events, inputs, diagnostics):
            task.cancel()
        await asyncio.gather(events, inputs, diagnostics, return_exceptions=True)
        signal.signal(signal.SIGINT, old_handler)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("input", help="媒体 URL 或清单路径")
    parser.add_argument("--executable", default="N_m3u8DL-RE", help="Native AOT 可执行文件或开发构建 DLL")
    parser.add_argument("--save-dir", help="输出目录")
    parser.add_argument("--save-name", help="输出文件名（不含默认扩展名）")
    parser.add_argument("--tmp-dir", help="临时目录")
    parser.add_argument("--auto-select", action="store_true", help="使用 Worker 自动选择策略，适合无人值守运行")
    parser.add_argument("--live-record-limit", help="直播录制时长，例如 00:01:00")
    parser.add_argument("--verbose", action="store_true", help="显示 Worker 的全部日志事件")
    parser.add_argument("--json", action="store_true", help="stdout 输出原始 JSON 事件，选择提示和摘要写入 stderr")
    args = parser.parse_args()
    try:
        return asyncio.run(run(args))
    except KeyboardInterrupt:
        return 130
    except Exception as error:
        print("错误：" + str(error), file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
