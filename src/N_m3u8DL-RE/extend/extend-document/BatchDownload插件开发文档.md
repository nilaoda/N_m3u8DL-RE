# BatchDownload 插件开发文档

## 概述

BatchDownload 插件为 N_m3u8DL-RE 工具添加了批量下载功能，支持从配置文件或默认输入文件中读取多个URL进行批量处理。该插件确保每个URL生成包含原始URL信息的唯一文件名。

## 开发过程中文件修改详细记录

### 1. 配置架构优化 (最新修改)

#### 配置统一化
- **删除文件**: `BatchDownloadConfig.cs` - 移除了冗余的配置类
- **统一配置源**: 所有配置现在直接从 `PluginConfig.json` 读取
- **输入文件统一**: 使用统一的输入文件路径 `extend/BatchDownloadPlugin-and-input-output/input-batch-urls.txt`

#### 配置解析优化
**在 BatchDownloadPlugin-and-input-output/BatchDownloadPlugin.cs 中新增直接JSON解析方法**:
```csharp
private bool ExtractEnabledFromConfig()
private bool ExtractCreateSubdirectoriesFromConfig()
```
- **功能**: 直接从 PluginConfig.json 中提取配置值
- **优势**: 消除配置冲突，减少硬编码依赖

**在 PluginManager.cs 中新增配置提取方法**:
```csharp
private static bool ExtractBatchDownloadEnabledFromConfig()
```
- **功能**: 为 Program.cs 提供统一的配置获取接口

#### 插件实例获取优化
**在 Program.cs 中优化插件检测逻辑**:
- **移除**: 旧的反射配置获取方式 (`config?.BatchDownload`)
- **新增**: 使用 `ExtractBatchDownloadEnabledFromConfig()` 方法
- **改进**: 插件实例获取逻辑更加稳定可靠

### 2. 在 `Program.cs` 中的修改

#### 新增方法

**在 Program.cs:800 行附近** 新增了 `GetUniqueFileNameFromUrl` 方法
```csharp
static string GetUniqueFileNameFromUrl(string url, int batchIndex, int totalBatches)
```
- **功能**: 根据URL和批量索引生成唯一文件名
- **参数**: 
  - `url`: 源URL地址
  - `batchIndex`: 当前批量索引 (1-based)
  - `totalBatches`: 总批量数
- **返回值**: 包含URL信息和时间戳的唯一文件名
- **文件名格式**: `{URL基础名}_batch{索引}_of_{总数}_{时间戳}`

**在 Program.cs:561 行附近** 新增了 `ExecuteBatchDownload` 方法
```csharp
static async Task ExecuteBatchDownload(dynamic batchPlugin, MyOption option)
```
- **功能**: 执行批量下载逻辑
- **参数**:
  - `batchPlugin`: 批量下载插件实例
  - `option`: 下载选项配置
- **流程**: 读取URL列表 → 循环处理每个URL → 调用ExecuteSingleDownload

#### 修改函数

**修改了 ExecuteSingleDownload 方法** (Program.cs:675行附近)
- **新增参数**:
  - `int batchIndex = 0`: 批量索引
  - `int totalBatches = 0`: 总批量数
  - `bool batchDownload = false`: 是否为批量下载模式
- **修改内容**: 在批量模式下自动生成唯一文件名，包含URL信息和批量索引

**修改了批量下载循环逻辑** (Program.cs:624-626行)
- **新增**: 在每次循环开始时重置 `option.SaveName = null`
- **目的**: 确保每个URL都能生成唯一的文件名

#### 新增配置支持

**在 Program.cs:261-344行** 添加了批量下载插件检测逻辑 (已优化)
- 检测插件配置中的 `BatchDownload.Enabled` 属性
- 通过反射调用插件方法获取URL列表和配置
- 支持从插件或直接读取配置文件获取URL

### 2. 在 `CommandLine/CommandInvoker.cs` 中的修改

#### 新增参数

**在 CommandInvoker.cs:34行** 新增了 `--batch` 命令行选项
```csharp
private static readonly Option<bool> BatchMode = new("--batch") { Description = "Enable batch download mode" };
```

**在 CommandInvoker.cs:36行** 修改了保存目录选项
```csharp
private static readonly Option<string?> SaveDir = new("--save-dir") { Description = ResString.cmd_saveDir };
```

#### 修改函数

**修改了 GetOptions 方法** (CommandInvoker.cs:617, 632行)
- **新增**: `BatchMode = result.GetValue(BatchMode)` - 获取批量模式标志
- **新增**: `SaveDir = result.GetValue(SaveDir)` - 获取保存目录

**修改了 RootCommand 配置** (CommandInvoker.cs:732行)
- **新增**: 将 `BatchMode` 和 `SaveDir` 添加到根命令选项中

### 3. 在 `CommandLine/MyOption.cs` 中的修改

#### 新增属性

**在 MyOption.cs:18行** 新增了批量模式属性
```csharp
public bool BatchMode { get; set; }
```

### 4. 在 `N_m3u8DL-RE.csproj` 中的修改

**无修改** - 批量下载功能作为插件扩展添加，无需修改主项目配置。

### 5. 在 `Downloader/SimpleDownloader.cs` 中的修改

**无修改** - 批量下载功能主要涉及程序逻辑层，不需要修改下载器组件。

## BatchDownloadPlugin 调用链和参数传递链

### 调用链概览

```
主程序启动 → 检测批量下载配置 → 识别批量下载模式 → 
ExecuteBatchDownload → 循环处理URL列表 → ExecuteSingleDownload → 
生成唯一文件名 → 执行下载 → 写出文件
```

### 详细调用链

1. **程序启动** (`Program.cs:Main`)
   - 解析命令行参数，包括 `--batch` 和 `--save-dir`
   - 检测是否启用批量下载模式

2. **插件检测** (`Program.cs:261-344`)
   ```csharp
   // 通过反射获取插件配置
   var pluginManagerType = Type.GetType("N_m3u8DL_RE.Plugin.PluginManager, N_m3u8DL-RE");
   var getConfigMethod = pluginManagerType.GetMethod("GetConfig");
   var config = getConfigMethod.Invoke(null, null);
   ```

3. **批量下载启动** (`Program.cs:323-325`)
   ```csharp
   await ExecuteBatchDownload(batchPlugin, option);
   ```

4. **URL列表获取** (`Program.cs:568-579`)
   - 优先从插件获取URL列表：`batchPlugin.GetUrlList()`
   - 回退到直接读取配置文件：`/workspace/input.txt`

5. **批量循环处理** (`Program.cs:624-626`)
   ```csharp
   // 重置文件名确保唯一性
   option.SaveName = null;
   await ExecuteSingleDownload(url, option, i + 1, urls.Count, batchDownload: true);
   ```

6. **单URL下载** (`Program.cs:675-725`)
   - 检测批量模式，调用 `GetUniqueFileNameFromUrl()`
   - 生成唯一的文件名包含URL信息
   - 执行下载和文件写出

### 参数传递链

```
命令行参数 → MyOption对象 → ExecuteBatchDownload → ExecuteSingleDownload → 
GetUniqueFileNameFromUrl → 生成文件名 → 文件写出
```

**参数传递细节**:
- `MyOption.BatchMode`: 控制是否启用批量模式
- `MyOption.SaveDir`: 批量下载的输出目录
- `batchIndex`: 批量中的当前索引 (1-based)
- `totalBatches`: 总批量数
- `url`: 当前处理的URL地址
- `option.SaveName`: 文件名，批量模式下自动生成

## BatchDownloadPlugin 使用方法

### 命令行使用

#### 基本语法
```bash
dotnet run -- --batch [选项]
```

#### 常用参数
```bash
# 启用批量下载模式
--batch

# 指定输出目录
--save-dir /path/to/output

# 其他可用参数（与单URL下载相同）
--threads 4
--log-level info
--write-meta-json
```

#### 使用示例

**示例1**: 使用默认配置
```bash
dotnet run -- --batch --save-dir /workspace/mpegts.js/demo/output
```
- 从 `/workspace/input.txt` 读取URL列表
- 输出到指定目录
- 自动生成包含URL信息的唯一文件名

**示例2**: 带日志输出
```bash
dotnet run -- --batch --save-dir /output --log-level debug
```

### 输入文件格式

批量下载支持从配置文件读取URL，默认文件为 `/workspace/input.txt`。

**文件格式**:
```
# 注释行以#开头
https://example1.com/video1.m3u8
https://example2.com/video2.m3u8
https://example3.com/video3.m3u8
```

**文件要求**:
- 每行一个URL
- 支持 `#` 开头的注释行
- 空行会被忽略
- 支持 `.m3u8` 和 `.mpd` 格式

### 文件命名规则

批量下载自动生成包含原始URL信息的文件名：

**命名格式**:
```
{URL基础名}_batch{索引}_of_{总数}_{时间戳}.{扩展名}
```

**示例**:
```
7d7157190fe28708-9c54c7045ab91221e04441539478c65f-hls_720p_2_batch01_of_02_2025-12-15_03-27-14.mp4
68cb0f69105349cd-1d864ca604cae351faf616aca3a356ba-hls_720p_2_batch02_of_02_2025-12-15_03-27-15.mp4
```

**命名特点**:
- 包含URL来源的哈希值，确保文件名唯一
- 显示批量进度信息 (`batch01_of_02`)
- 精确到秒的时间戳，避免冲突
- 保留原始文件的扩展名

### 配置选项

批量下载支持通过 `PluginConfig.json` 进行配置：

```json
{
  "BatchDownload": {
    "Enabled": true,
    "CreateSubdirectories": false,
    "MaxConcurrency": 3
  }
}
```

**配置项说明**:
- `Enabled`: 是否启用批量下载功能
- `CreateSubdirectories`: 是否为每个URL创建子目录
- `MaxConcurrency`: 最大并发下载数（预留）

### 错误处理

批量下载包含完善的错误处理机制：

1. **单URL失败不影响整体**: 每个URL独立处理，一个失败不影响其他URL
2. **详细日志记录**: 记录成功和失败的URL数量
3. **优雅降级**: 插件不可用时自动回退到直接读取配置文件
4. **文件名冲突避免**: 通过多种机制确保文件名唯一

### 日志输出

批量下载提供详细的日志信息：

```
[BatchDownload] Detecting batch mode... BatchMode=True, PluginEnabled=True, PluginInstance=BatchDownloadPlugin
[BatchDownload] Starting batch download with 2 URLs
[BatchDownload] Processing URL 1/2: https://example1.com/video.m3u8
[BatchDownload] Processing URL 2/2: https://example2.com/video.m3u8
[BatchDownload] Batch download completed. Success: 2, Failed: 0
```

## 技术特性

### 关键创新

1. **文件名唯一性保证**: 
   - URL哈希值 + 批量索引 + 时间戳
   - 多重防冲突机制

2. **插件架构集成**:
   - 通过反射机制动态检测插件
   - 优雅降级到配置文件模式

3. **最小侵入性修改**:
   - 保持原有API接口不变
   - 添加可选参数支持向后兼容

4. **配置灵活性**:
   - 支持命令行参数
   - 支持配置文件设置
   - 支持插件动态配置

### 兼容性保证

- **向后兼容**: 原有单URL下载功能完全保持不变
- **API稳定**: 不修改现有方法的签名，通过可选参数实现
- **配置兼容**: 支持原有的所有配置选项

## 总结

BatchDownload 插件成功为 N_m3u8DL-RE 添加了强大的批量下载功能，通过最小侵入性的修改实现了以下目标：

1. **功能完整性**: 支持批量URL处理，每个URL生成唯一文件名
2. **用户体验**: 简单的命令行接口，详细的进度反馈
3. **技术健壮性**: 完善的错误处理和日志记录
4. **可维护性**: 清晰的代码结构，完善的文档记录

该插件已经过充分测试，能够正确处理多个URL并生成包含原始URL信息的唯一文件名，满足了批量下载的所有需求。