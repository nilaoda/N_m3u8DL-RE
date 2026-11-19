# Worker API 数据模型

<!-- 此文件由 tools/WorkerApiDocs.cs 自动生成，请修改协议代码后重新生成。 -->

协议版本：`1`。单行输入上限：`1048576` 个 UTF-16 字符。

入口：[Worker API](README.md) · [使用指南](../../usage/stdio-worker.md) · [命令 Schema](schemas/command.schema.json) · [事件 Schema](schemas/event.schema.json)

字段名来自实际 JSON 契约。“声明可空”来自 C# 可空类型标记；“JSON 必填”表示反序列化时字段必须出现；按消息类型决定的条件必填项、默认值和业务校验见使用指南。可空字段在输出时省略 null；省略字段是否保留旧值取决于字段语义。

JSON Schema 描述数据结构，不包含文件存在性、操作适用范围、事件顺序等运行规则。

## FileTaskInput

| 字段 | JSON 类型 / 模型 | 声明可空 | JSON 必填 |
| --- | --- | --- | --- |
| `path` | string | 否 | 是 |
| `language` | string | 是 | 否 |
| `name` | string | 是 | 否 |

## FileTaskRequest

| 字段 | JSON 类型 / 模型 | 声明可空 | JSON 必填 |
| --- | --- | --- | --- |
| `inputs` | [FileTaskInput](#filetaskinput)[] | 否 | 是 |
| `output` | string | 否 | 是 |
| `pattern` | string | 是 | 否 |
| `overwrite` | boolean | 否 | 否 |
| `dryRun` | boolean | 否 | 否 |
| `ffmpegBinaryPath` | string | 是 | 否 |
| `mkvmergeBinaryPath` | string | 是 | 否 |
| `muxer` | string | 是 | 否 |
| `concatMode` | string | 是 | 否 |
| `autoSubtitleFix` | boolean | 是 | 否 |
| `title` | string | 是 | 否 |

## InteractionRequest

| 字段 | JSON 类型 / 模型 | 声明可空 | JSON 必填 |
| --- | --- | --- | --- |
| `id` | string | 否 | 否 |
| `kind` | string | 否 | 否 |
| `choices` | [SelectionChoice](#selectionchoice)[] | 否 | 否 |
| `defaultIds` | string[] | 否 | 否 |

## OutputArtifact

| 字段 | JSON 类型 / 模型 | 声明可空 | JSON 必填 |
| --- | --- | --- | --- |
| `path` | string | 否 | 否 |
| `size` | integer（int64） | 是 | 否 |

## SelectionChoice

| 字段 | JSON 类型 / 模型 | 声明可空 | JSON 必填 |
| --- | --- | --- | --- |
| `id` | string | 否 | 是 |
| `label` | string | 否 | 是 |
| `mediaType` | string | 是 | 否 |
| `language` | string | 是 | 否 |
| `codecs` | string | 是 | 否 |
| `resolution` | string | 是 | 否 |
| `bandwidth` | integer（int64） | 是 | 否 |
| `durationSeconds` | number（double） | 是 | 否 |
| `partIds` | integer（int64）[] | 是 | 否 |

## TaskError

| 字段 | JSON 类型 / 模型 | 声明可空 | JSON 必填 |
| --- | --- | --- | --- |
| `code` | string | 否 | 否 |
| `message` | string | 否 | 否 |

## TaskProgress

| 字段 | JSON 类型 / 模型 | 声明可空 | JSON 必填 |
| --- | --- | --- | --- |
| `trackId` | string | 否 | 是 |
| `phase` | string | 否 | 是 |
| `downloadedBytes` | integer（int64） | 是 | 否 |
| `receivedBytes` | integer（int64） | 是 | 否 |
| `totalBytes` | integer（int64） | 是 | 否 |
| `speedBytesPerSecond` | number（double） | 是 | 否 |
| `completedSegments` | integer（int64） | 是 | 否 |
| `totalSegments` | integer（int64） | 是 | 否 |
| `recordedSeconds` | number（double） | 是 | 否 |
| `fraction` | number（double） | 是 | 否 |
| `processedBytes` | integer（int64） | 是 | 否 |
| `processedSeconds` | number（double） | 是 | 否 |

## TaskRequest

| 字段 | JSON 类型 / 模型 | 声明可空 | JSON 必填 |
| --- | --- | --- | --- |
| `input` | string | 否 | 是 |
| `saveDir` | string | 是 | 否 |
| `tmpDir` | string | 是 | 否 |
| `saveName` | string | 是 | 否 |
| `savePattern` | string | 是 | 否 |
| `urlProcessorArgs` | string | 是 | 否 |
| `headers` | object（字符串键 → string） | 是 | 否 |
| `cookies` | string | 是 | 否 |
| `autoSelect` | boolean | 是 | 否 |
| `subOnly` | boolean | 是 | 否 |
| `subFormat` | string | 是 | 否 |
| `selectVideo` | string | 是 | 否 |
| `selectAudio` | string | 是 | 否 |
| `selectSubtitle` | string | 是 | 否 |
| `dropVideo` | string | 是 | 否 |
| `dropAudio` | string | 是 | 否 |
| `dropSubtitle` | string | 是 | 否 |
| `vodSelectParts` | boolean | 是 | 否 |
| `vodListParts` | boolean | 是 | 否 |
| `vodDropParts` | string | 是 | 否 |
| `threadCount` | integer（int32） | 是 | 否 |
| `downloadRetryCount` | integer（int32） | 是 | 否 |
| `httpRequestTimeout` | number（double） | 是 | 否 |
| `httpVersion` | string | 是 | 否 |
| `maxSpeed` | integer（int64） | 是 | 否 |
| `concurrentDownload` | boolean | 是 | 否 |
| `skipMerge` | boolean | 是 | 否 |
| `skipDownload` | boolean | 是 | 否 |
| `binaryMerge` | boolean | 是 | 否 |
| `ffmpegConcatMode` | string | 是 | 否 |
| `autoSubtitleFix` | boolean | 是 | 否 |
| `checkSegmentsCount` | boolean | 是 | 否 |
| `noDateInfo` | boolean | 是 | 否 |
| `writeMetaJson` | boolean | 是 | 否 |
| `delAfterDone` | boolean | 是 | 否 |
| `ffmpegBinaryPath` | string | 是 | 否 |
| `decryptionBinaryPath` | string | 是 | 否 |
| `decryptionEngine` | string | 是 | 否 |
| `mp4RealTimeDecryption` | boolean | 是 | 否 |
| `customHlsMethod` | string | 是 | 否 |
| `customHlsKey` | string | 是 | 否 |
| `customHlsIv` | string | 是 | 否 |
| `customHlsScope` | string | 是 | 否 |
| `allowHlsMultiExtMap` | boolean | 是 | 否 |
| `keys` | string[] | 是 | 否 |
| `keyTextFile` | string | 是 | 否 |
| `muxAfterDone` | string | 是 | 否 |
| `muxImports` | string[] | 是 | 否 |
| `customProxy` | string | 是 | 否 |
| `useSystemProxy` | boolean | 是 | 否 |
| `networkInterface` | string | 是 | 否 |
| `baseUrl` | string | 是 | 否 |
| `appendUrlParams` | boolean | 是 | 否 |
| `customRange` | string | 是 | 否 |
| `adKeywords` | string[] | 是 | 否 |
| `livePerformAsVod` | boolean | 是 | 否 |
| `liveRealTimeMerge` | boolean | 是 | 否 |
| `livePipeMux` | boolean | 是 | 否 |
| `liveFixVttByAudio` | boolean | 是 | 否 |
| `liveKeepSegments` | boolean | 是 | 否 |
| `liveRecordLimit` | string | 是 | 否 |
| `liveWaitTime` | integer（int32） | 是 | 否 |
| `liveIdleTimeout` | integer（int32） | 是 | 否 |
| `liveTakeCount` | integer（int32） | 是 | 否 |
| `liveCatchup` | string | 是 | 否 |
| `taskStartAt` | string | 是 | 否 |
| `logFilePath` | string | 是 | 否 |
| `logLevel` | string | 是 | 否 |
| `noLog` | boolean | 是 | 否 |

## TaskResult

| 字段 | JSON 类型 / 模型 | 声明可空 | JSON 必填 |
| --- | --- | --- | --- |
| `status` | string | 否 | 否 |
| `outputs` | [OutputArtifact](#outputartifact)[] | 否 | 否 |
| `error` | [TaskError](#taskerror) | 是 | 否 |

## WorkerCommand

| 字段 | JSON 类型 / 模型 | 声明可空 | JSON 必填 |
| --- | --- | --- | --- |
| `type` | string | 否 | 是 |
| `id` | string | 否 | 是 |
| `protocolVersion` | integer（int32） | 是 | 否 |
| `operation` | string | 是 | 否 |
| `request` | [TaskRequest](#taskrequest) | 是 | 否 |
| `fileRequest` | [FileTaskRequest](#filetaskrequest) | 是 | 否 |
| `interactionId` | string | 是 | 否 |
| `selectedIds` | string[] | 是 | 否 |

## WorkerEvent

| 字段 | JSON 类型 / 模型 | 声明可空 | JSON 必填 |
| --- | --- | --- | --- |
| `type` | string | 否 | 是 |
| `sequence` | integer（int64） | 否 | 否 |
| `commandId` | string | 是 | 否 |
| `protocolVersion` | integer（int32） | 是 | 否 |
| `programVersion` | string | 是 | 否 |
| `capabilities` | string[] | 是 | 否 |
| `status` | string | 是 | 否 |
| `phase` | string | 是 | 否 |
| `tracks` | [SelectionChoice](#selectionchoice)[] | 是 | 否 |
| `progress` | [TaskProgress](#taskprogress) | 是 | 否 |
| `interaction` | [InteractionRequest](#interactionrequest) | 是 | 否 |
| `result` | [TaskResult](#taskresult) | 是 | 否 |
| `level` | string | 是 | 否 |
| `message` | string | 是 | 否 |
| `error` | [TaskError](#taskerror) | 是 | 否 |
