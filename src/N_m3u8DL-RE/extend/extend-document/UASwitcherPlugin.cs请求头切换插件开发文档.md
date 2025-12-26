# UASwitcherPlugin.cs 请求头切换插件开发文档

## 插件概述

UASwitcherPlugin 是 N_m3u8DL-RE 插件系统中的一个核心组件，主要功能是在批量下载过程中自动切换 HTTP 请求头中的 User-Agent，实现请求头轮换和反反爬策略。

## 功能特性

### 1. User-Agent 轮换
- **自动切换**: 每下载 1 个文件自动切换一次 User-Agent
- **循环使用**: 当所有 User-Agent 使用完后，重新从列表开头开始
- **配置驱动**: 支持从 PluginConfig.json 读取自定义 User-Agent 列表

### 2. 默认 User-Agent 列表
插件内置了三个主流浏览器的 User-Agent：
```csharp
"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36"
"Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36"
"Mozilla/5.0 (Linux; Android 10; SM-G981B) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Mobile Safari/537.36"
```

### 3. 插件接口实现
实现了 IPlugin 接口的所有必需方法：
- `Initialize()`: 插件初始化和配置加载
- `OnFileDownloaded()`: 文件下载完成回调
- `OnInputReceived()`: 输入处理回调（预留接口）
- `OnOutputGenerated()`: 输出生成回调（预留接口）
- `OnLogGenerated()`: 日志生成回调（预留接口）

## 调试环境设置

### 1. 必要工具
- **mitmdump**: HTTP/HTTPS 代理服务器，用于拦截和分析网络请求
- **ua_monitor.py**: 自定义的 User-Agent 监控脚本
- **dotnet**: .NET 运行时，用于运行 N_m3u8DL-RE

### 2. 调试环境架构
```
客户端应用 → mitmdump 代理 → 目标服务器
    ↓              ↓           ↓
 ua_monitor.py  拦截请求   响应数据
```

### 3. 启动步骤

#### 步骤 1: 启动 UA 监听器
```bash
mitmdump --listen-port 8083 --listen-host 127.0.0.1 -s /workspace/ua_monitor.py
```

#### 步骤 2: 启动批量下载（设置代理）
```bash
cd /workspace/N_m3u8DL-RE-src/src/N_m3u8DL-RE
export HTTP_PROXY=http://127.0.0.1:8083
export HTTPS_PROXY=http://127.0.0.1:8083
dotnet run -- --batch
```

## 源代码结构分析

### 核心类定义
```csharp
public class UASwitcherPlugin : IPlugin
```

### 主要字段
- `_config`: 插件配置对象
- `_userAgents`: User-Agent 字符串列表
- `_currentIndex`: 当前使用的 User-Agent 索引

### 核心方法

#### 1. Initialize() 方法
```csharp
public void Initialize(PluginConfig? config)
```
**功能**: 
- 加载插件配置
- 如果配置中有自定义 User-Agent，则替换默认列表
- 记录初始化日志

**逻辑流程**:
1. 保存配置引用
2. 检查配置中的 User-Agent 列表
3. 如果有自定义配置，清空默认列表并添加自定义 UA
4. 记录初始化信息

#### 2. OnFileDownloaded() 方法
```csharp
public void OnFileDownloaded(string filePath, int downloadCount)
```
**功能**: 
- 在每个文件下载完成后调用
- 根据下载计数计算下一个要使用的 User-Agent
- 记录切换日志

**逻辑流程**:
1. 记录文件下载完成信息
2. 计算 User-Agent 索引：`downloadCount % _userAgents.Count`
3. 获取新的 User-Agent
4. 记录切换信息
5. 实际应用中需要修改全局 HTTP 客户端的默认请求头

## 使用方法

### 1. 配置文件设置
在 `PluginConfig.json` 中启用和配置 UASwitcher 插件：

```json
{
  "UASwitcher": {
    "Enabled": true,
    "UserAgents": [
      "自定义 User-Agent 1",
      "自定义 User-Agent 2",
      "自定义 User-Agent 3"
    ]
  }
}
```

### 2. 程序内调用
插件通过 PluginManager 自动加载和调用：
```csharp
// 程序启动时
pluginManager.InitializePlugins(config);

// 文件下载完成后
pluginManager.OnFileDownloaded(filePath, downloadCount);
```

### 3. 命令行使用
```bash
# 启用批量模式
dotnet run -- --batch

# 设置代理（调试模式）
export HTTP_PROXY=http://127.0.0.1:8083
export HTTPS_PROXY=http://127.0.0.1:8083
```

## 调试指南

### 1. 验证插件加载
检查程序启动日志，寻找类似以下信息：
```
[UASwitcherPlugin] Initialized with headers: -H "User-Agent: Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36..."
```

### 2. 监控 User-Agent 切换
通过 mitmdump 代理观察每次请求的 User-Agent 是否按预期切换：
```bash
# 监控代理日志
mitmdump --listen-port 8083 -s ua_monitor.py
```

### 3. 验证切换逻辑
观察程序日志，确认 User-Agent 按预期切换：
```
[UASwitcherPlugin] File downloaded: /path/to/file.ts, count: 1
[UASwitcherPlugin] Downloaded 1 files, switching UA to: Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7)...
```

### 4. 常见问题排查

#### 问题 1: 插件未生效
**可能原因**: 
- 插件未在配置中启用
- 配置文件路径错误

**解决方案**: 
- 检查 `PluginConfig.json` 中的 `Enabled` 字段
- 确认配置文件被正确加载

#### 问题 2: User-Agent 未切换
**可能原因**:
- HTTP 客户端未集成插件逻辑
- 代理设置不正确

**解决方案**:
- 确认 HTTP 客户端使用插件返回的 User-Agent
- 检查代理设置和环境变量

#### 问题 3: 调试信息未显示
**可能原因**:
- 日志级别设置过高
- 日志输出重定向

**解决方案**:
- 检查日志配置
- 确认控制台输出未被重定向

## 扩展开发

### 1. 添加新的回调方法
插件预留了扩展接口，可以实现更多功能：
```csharp
public void OnInputReceived(object args, object option)
public void OnOutputGenerated(string outputPath, string outputType)
public void OnLogGenerated(string logMessage, PluginLogLevel logLevel)
```

### 2. 集成 HTTP 客户端
实际使用中需要将 User-Agent 应用到 HTTP 客户端：
```csharp
// 示例：在 HTTP 客户端中应用 User-Agent
HttpClient.DefaultRequestHeaders.UserAgent.ParseAdd(newUA);
```

### 3. 添加请求头轮换
可以扩展支持更多 HTTP 请求头的轮换：
```csharp
private readonly Dictionary<string, List<string>> _headers = new Dictionary<string, List<string>>
{
    { "User-Agent", userAgents },
    { "Accept-Language", new List<string> { "en-US", "zh-CN", "ja-JP" } }
};
```

## 性能考虑

### 1. 内存使用
- User-Agent 列表存储在内存中
- 默认列表较小（3个元素），内存占用可忽略

### 2. 性能影响
- 计算 User-Agent 索引使用模运算，性能影响微乎其微
- 日志记录可能对性能有一定影响，可根据需要调整日志级别

### 3. 扩展性
- 支持任意数量的 User-Agent
- 可扩展支持其他请求头的轮换

## 最佳实践

### 1. 配置管理
- 使用配置文件管理 User-Agent 列表
- 根据目标网站特性选择合适的 User-Agent

### 2. 调试建议
- 在开发环境使用代理工具验证请求头
- 记录详细的调试日志便于问题排查

### 3. 部署考虑
- 生产环境中适当调整日志级别
- 监控 User-Agent 切换效果

## 注意事项

1. **向后兼容**: 插件实现了向后兼容的接口方法
2. **配置优先**: 自定义配置会覆盖默认 User-Agent 列表
3. **线程安全**: 当前实现不是线程安全的，在多线程环境下需要额外的同步机制
4. **错误处理**: 插件具有基础的错误处理机制，但可以进一步完善
5. **代理依赖**: 调试功能依赖 mitmdump 代理，代理异常可能影响调试效果

## 版本信息

- **当前版本**: 1.0.0
- **兼容性**: N_m3u8DL-RE 插件系统
- **依赖项**: .NET 9.0+, System.Net.Http
- **最后更新**: 2025-12-16

---

本文档详细介绍了 UASwitcherPlugin 的功能、使用方法和调试过程。如有问题，请参考调试章节的故障排除部分。