# 项目文档

## 开发设计

- [项目组织与任务流程](development/project-layout.md)：目录分工、项目依赖及整体执行流程图。

## 使用与接入

- [Worker API](api/worker/README.md)：统一入口、自动生成的数据模型和 JSON Schema。

- [stdio Worker 协议](usage/stdio-worker.md)：启动方式、消息时序、配置、进度合并和退出行为。
- [Python 客户端使用示例](examples/README.md)：手动选择清晰度、显示进度、停止或取消任务，以及输出原始 JSON 事件。
- [下载示例源码](examples/stdio_client.py) / [文件工具示例源码](examples/stdio_file_client.py)：仅依赖 Python 标准库的异步客户端。

下载参数和常规 CLI 使用方式见根目录 [README](../README.md)。
