# Worker API

Worker 通过 stdio 的 JSON 行消息提供下载、录制、文件合并和混流能力。每个进程执行一个任务。

## 文档入口

- [使用指南](../../usage/stdio-worker.md)：启动、命令、交互、进度上报、日志、取消和退出。
- [数据模型参考](models.md)：命令、事件、下载与文件配置、进度、交互和结果的完整字段表。
- [客户端命令 JSON Schema](schemas/command.schema.json) / [Worker 事件 JSON Schema](schemas/event.schema.json)：数据结构定义。
- [Python 示例](../../examples/README.md)：下载选流、文件处理、进度显示和取消。

stdio 是进程通信接口；JSON Schema 用于描述消息结构，不是 HTTP OpenAPI 定义。

## 文档同步

字段参考和 Schema 从 Worker 使用的 `WorkerJsonContext` 源生成契约导出，包含实际 JSON 字段名、类型、可空性和必填标记。

在仓库目录执行：

```bash
# 生成字段参考和 Schema
dotnet run tools/WorkerApiDocs.cs -c Release

# 检查生成文档是否与代码一致
dotnet run tools/WorkerApiDocs.cs -c Release -- --check
```

CI 通过生成器的 `--check` 检查生成产物，文档测试检查使用指南中的 JSON 示例能否按实际协议解析。字段模型或 JSON 配置变更后，生成产物必须一并更新。

进度单位、默认值、字段间约束、消息顺序和文件收尾属于行为约定，由使用指南说明，并通过 Worker 的行为测试验证；它们不能仅从 DTO 自动推导。修改这些行为时需要同步修改说明、示例和相应测试。

生成工具是 .NET 10 的单文件应用，通过 `#:project` 引用实际 Worker 项目，仅用于开发。
