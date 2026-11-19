using System.Text;
using System.Text.Json;
using N_m3u8DL_RE.CommandLine;
using N_m3u8DL_RE.Common.Log;
using N_m3u8DL_RE.Protocol;
using N_m3u8DL_RE.Runtime;
using Spectre.Console;

namespace N_m3u8DL_RE.Worker;

internal static class WorkerHost
{
    internal static async Task<int> RunConsoleAsync(CancellationToken cancellationToken = default)
    {
        // stdin 的后台读取可能仍在等待父进程；交给进程退出回收，避免 Dispose 与读取竞争。
        var input = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(true, true), detectEncodingFromByteOrderMarks: false);
        var output = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true };
        // 标准输出独占协议；遗留终端写入和诊断都转到 stderr。
        Console.SetOut(Console.Error);
        CustomAnsiConsole.Console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No, Interactive = InteractionSupport.No,
            Out = new AnsiConsoleOutput(Console.Error)
        });
        return await RunAsync(input, output, cancellationToken);
    }

    private static Func<Task<ExecutionResult>> PrepareTask(WorkerCommand command, TaskExecutionContext context)
    {
        var operation = command.Operation ?? "download";
        if (operation == "download")
        {
            if (command.FileRequest != null)
                throw new ArgumentException("download accepts request, not fileRequest.");
            var options = ProtocolMapper.ToOptions(command.Request ?? throw new ArgumentException("request is required."));
            return async () =>
            {
                Logger.IsWriteFile = !options.NoLog;
                Logger.LogFilePath = options.LogFilePath;
                Logger.InitLogFile();
                Logger.LogLevel = options.LogLevel;
                return await DownloadTaskRunner.RunAsync(options, context);
            };
        }
        if (operation is "concat" or "merge" or "mux")
        {
            if (command.Request != null)
                throw new ArgumentException("File operations accept fileRequest, not request.");
            var options = ProtocolMapper.ToFileOptions(operation,
                command.FileRequest ?? throw new ArgumentException("fileRequest is required."));
            return () =>
            {
                Logger.IsWriteFile = false;
                Logger.LogLevel = LogLevel.INFO;
                return FileTaskRunner.RunAsync(options, context);
            };
        }
        throw new ArgumentException("Unsupported operation. Use download, concat, merge, or mux.");
    }

    internal static async Task<int> RunAsync(TextReader input, TextWriter output, CancellationToken cancellationToken = default)
    {
        await using var transport = new StdioTransport(input, output);
        using var control = new TaskControl();
        using var reading = CancellationTokenSource.CreateLinkedTokenSource(transport.Disconnected, cancellationToken);
        using var disconnect = transport.Disconnected.Register(control.Cancel);
        using var invocationCancellation = cancellationToken.Register(control.Cancel);
        var previousSink = Logger.Sink;
        var previousConsoleOutput = Logger.ConsoleOutputEnabled;
        Logger.ConsoleOutputEnabled = false;
        Logger.Sink = entry => transport.Publish(new WorkerEvent
        {
            Type = "log", Level = entry.Level.ToString().ToLowerInvariant(), Message = entry.Message
        });
        var writer = transport.WriteAsync();
        var observer = new WorkerTaskObserver(transport);
        var interaction = new WorkerUserInteraction(transport, observer);
        var context = new TaskExecutionContext(observer, interaction, control, isWorker: true);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        Task? execution = null;
        var exitCode = 1;
        transport.Publish(new WorkerEvent
        {
            Type = "ready", ProtocolVersion = WorkerProtocol.Version,
            ProgramVersion = typeof(WorkerHost).Assembly.GetName().Version?.ToString(),
            Capabilities = ["progress", "selection", "cancel", "stop", "binary", "hls", "dash", "mss", "httpLive", "iptv", "concat", "merge", "mux"]
        });
        try
        {
            while (!reading.IsCancellationRequested)
            {
                WorkerCommand? command;
                try
                {
                    command = await transport.ReadAsync(reading.Token);
                }
                catch (JsonException)
                {
                    Error(null, "invalid_message", "Invalid JSON, missing required fields, or unsupported fields.");
                    continue;
                }
                catch (DecoderFallbackException)
                {
                    Error(null, "invalid_message", "Commands must use valid UTF-8.");
                    control.Cancel();
                    break;
                }
                catch (InvalidDataException ex)
                {
                    Error(null, "invalid_message", ex.Message);
                    control.Cancel();
                    break;
                }
                if (reading.IsCancellationRequested)
                    break;
                if (command == null)
                {
                    control.Cancel();
                    break;
                }
                try
                {
                    if (string.IsNullOrWhiteSpace(command.Id) || command.Id.Length > 128 || ids.Count >= 4096 || !ids.Add(command.Id))
                        throw new ArgumentException("Command ID must be unique, nonempty, and at most 128 characters; maximum 4096 commands per worker.");
                    switch (command.Type)
                    {
                        case "start":
                            if (execution != null)
                                throw new ArgumentException("This worker already accepted a task.");
                            if (command.ProtocolVersion != WorkerProtocol.Version)
                                throw new ArgumentException("Unsupported protocolVersion.");
                            var runTask = PrepareTask(command, context);
                            Ack(command);
                            execution = Task.Run(async () =>
                            {
                                ExecutionResult result;
                                try
                                {
                                    result = await runTask();
                                }
                                catch (Exception ex)
                                {
                                    result = new("failed", [], "task_failed", ex.Message);
                                }
                                observer.State(result.Status, "finished");
                                transport.Publish(new WorkerEvent { Type = "result", Result = ProtocolMapper.ToResult(result) });
                                exitCode = result.Status == "succeeded" ? 0 : result.Status == "cancelled" ? 2 : 1;
                                reading.Cancel();
                            });
                            break;
                        case "respond":
                            if (execution == null || execution.IsCompleted)
                                throw new ArgumentException("No task is waiting for input.");
                            interaction.Respond(command, () => Ack(command));
                            break;
                        case "cancel":
                            if (execution == null)
                                throw new ArgumentException("No task has started.");
                            Ack(command);
                            observer.State("stopping", context.Phase);
                            control.Cancel();
                            break;
                        case "stop":
                            if (execution == null || !context.IsLive)
                                throw new ArgumentException("Stop is only available during live recording.");
                            Ack(command);
                            observer.State("stopping", context.Phase);
                            control.Stop();
                            break;
                        default:
                            throw new ArgumentException("Unknown command type.");
                    }
                }
                catch (Exception ex) when (ex is ArgumentException or FormatException or OverflowException)
                {
                    Error(command.Id, "invalid_command", ex.Message);
                }
            }
        }
        catch (OperationCanceledException) when (reading.IsCancellationRequested) { }
        catch (IOException)
        {
            control.Cancel();
        }
        finally
        {
            if (execution != null)
                await execution;
            transport.Complete();
            await writer;
            Logger.Sink = previousSink;
            Logger.ConsoleOutputEnabled = previousConsoleOutput;
        }
        return transport.Disconnected.IsCancellationRequested ? 1 : exitCode;

        void Ack(WorkerCommand command) => transport.Publish(new WorkerEvent { Type = "accepted", CommandId = command.Id });
        void Error(string? id, string code, string message) => transport.Publish(new WorkerEvent
        {
            Type = "error", CommandId = id, Error = new(code, message)
        });
    }
}
