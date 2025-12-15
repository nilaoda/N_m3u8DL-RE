# N_m3u8DL-RE 插件系统开发文档

本文档介绍如何为 N_m3u8DL-RE 开发和使用插件系统。

## 1. 插件系统架构设计

插件系统具有以下特点：

- **非侵入性**: 所有插件代码都放在 `extend` 目录中，不会影响主程序的核心代码
- **可扩展性**: 通过 [IPlugin](file:///workspace/N_m3u8DL-RE-src/extend/PluginManager.cs#L85-L89) 接口可以轻松添加新的插件功能
- **配置驱动**: 插件行为可以通过 [PluginConfig.json](file:///workspace/N_m3u8DL-RE-src/extend/PluginConfig.json) 配置文件进行管理
- **事件驱动**: 通过 `OnFileDownloaded` 事件触发插件逻辑

## 2. 核心组件

### PluginManager.cs (插件管理器)
- 实现了插件的加载、初始化和事件分发功能
- 支持从配置文件中读取插件设置
- 提供了统计下载次数的功能

### UASwitcherPlugin.cs (UA切换插件)
- 实现了每下载一定数量文件切换一次User-Agent的功能
- 支持从配置文件中读取自定义User-Agent列表

### ProxySwitcherPlugin.cs (代理切换插件)
- 实现了每下载一定数量文件切换一次代理的功能
- 通过Clash API控制代理切换

### BatchDownloadPlugin-and-input-output/BatchDownloadPlugin.cs (批量下载插件)
- 实现了批量下载多个URL的功能
- 支持从配置文件读取URL列表
- 自动生成包含原始URL信息的唯一文件名
- 支持批量进度跟踪和错误处理
- **配置架构优化**: 直接读取PluginConfig.json，无需中间配置类

### PluginConfig.json (配置文件)
- 控制各个插件的启用状态和行为参数

## 3. 插件接口规范

所有插件都需要实现 [IPlugin](file:///workspace/N_m3u8DL-RE-src/extend/PluginManager.cs#L85-L89) 接口:

```csharp
public interface IPlugin
{
    void Initialize(PluginConfig? config);
    void OnFileDownloaded(string filePath, int downloadCount);
}
```

- `Initialize`: 插件初始化方法，在程序启动时调用
- `OnFileDownloaded`: 文件下载完成回调，在每个文件下载完成后调用

## 4. 集成到主程序

插件系统已集成到主程序中：

### SimpleDownloader.cs 集成
- 在文件下载完成后添加了插件钩子调用
- 确保无论下载成功还是跳过已存在的文件都会触发插件事件

### N_m3u8DL-RE.csproj 集成
- 添加了对extend目录中插件文件的引用
- 确保插件配置文件会被复制到输出目录

### Program.cs 集成
- 在程序入口点初始化插件管理器

## 5. 设计优势

- **避免冲突**: 所有修改都在extend目录和必要的集成点，与原作者的开发路径完全分离
- **易于维护**: 插件系统采用模块化设计，便于单独维护和升级
- **高度可配置**: 通过配置文件可以灵活控制插件的行为
- **易于扩展**: 可以通过实现[IPlugin](file:///workspace/N_m3u8DL-RE-src/extend/PluginManager.cs#L85-L89)接口轻松添加新功能

## 6. 使用说明

要使用这个插件系统：

1. 确保extend目录中的插件文件被正确编译
2. 根据需要修改[PluginConfig.json](file:///workspace/N_m3u8DL-RE-src/extend/PluginConfig.json)中的配置
3. 程序运行时会自动加载启用的插件
4. 每当一个文件下载完成时，会自动触发相应的插件逻辑

### 批量下载插件使用

要使用批量下载插件：

1. 在[PluginConfig.json](file:///workspace/N_m3u8DL-RE-src/extend/PluginConfig.json)中启用BatchDownload
2. 准备URL列表文件（默认路径：`extend/BatchDownloadPlugin-and-input-output/input-batch-urls.txt`）
3. 使用命令行参数 `--batch` 启用批量模式
4. 指定输出目录：`--save-dir /path/to/output`

**配置架构优化说明**:
- **统一配置源**: 所有配置直接从PluginConfig.json读取，无需中间配置类
- **消除冲突**: 解决了配置冲突问题，确保单一配置来源
- **灵活配置**: 支持直接在PluginConfig.json中修改所有参数

**命令示例**:
```bash
dotnet run -- --batch --save-dir /workspace/mpegts.js/demo/output
```

**输入文件格式**:
```
# 注释行以#开头
https://example1.com/video1.m3u8
https://example2.com/video2.m3u8
```

**输出文件命名**:
批量下载会自动生成包含原始URL信息的唯一文件名，格式为：
`{URL基础名}_batch{索引}_of_{总数}_{时间戳}.{扩展名}`

## 7. 开发新插件

要开发一个新的插件，需要：

1. 创建新的插件类，实现[IPlugin](file:///workspace/N_m3u8DL-RE-src/extend/PluginManager.cs#L85-L89)接口
2. 在[PluginConfig.json](file:///workspace/N_m3u8DL-RE-src/extend/PluginConfig.json)中添加插件配置项
3. 在[PluginManager.cs](file:///workspace/N_m3u8DL-RE-src/extend/PluginManager.cs)的[LoadPlugins](file:///workspace/N_m3u8DL-RE-src/extend/PluginManager.cs#L16-L44)方法中添加插件加载逻辑
4. 编译并测试插件功能

## 8. 配置文件说明

[PluginConfig.json](file:///workspace/N_m3u8DL-RE-src/extend/PluginConfig.json) 是插件系统的配置文件，支持以下配置项：

```json
{
  "UASwitcher": {
    "Enabled": true,
    "UserAgents": [
      "UA1",
      "UA2",
      "UA3"
    ]
  },
  "ProxySwitcher": {
    "Enabled": true,
    "ClashApiUrl": "http://127.0.0.1:9090",
    "SwitchInterval": 3
  },
  "BatchDownload": {
    "Enabled": true,
    "CreateSubdirectories": false,
    "MaxConcurrency": 3
  }
}
```

- `Enabled`: 控制插件是否启用
- `UserAgents`: UA切换插件使用的User-Agent列表
- `ClashApiUrl`: 代理切换插件使用的Clash API地址
- `SwitchInterval`: 切换间隔（每下载多少个文件切换一次）
- `CreateSubdirectories`: 批量下载插件是否为每个URL创建子目录
- `MaxConcurrency`: 批量下载插件的最大并发数（预留功能）