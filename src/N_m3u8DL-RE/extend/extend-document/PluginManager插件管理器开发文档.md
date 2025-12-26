# PluginManager插件管理器开发文档

## 概述

PluginManager是N_m3u8DL-RE插件系统的核心组件，负责统一管理所有插件的生命周期、流拦截功能以及事件分发机制。本文档详细介绍了PluginManager的架构设计、核心功能、接口定义以及集成方式。

## 核心架构

### 1. 整体架构设计

PluginManager采用单一职责原则设计，主要职责包括：

- **插件生命周期管理** - 插件的加载、初始化、注册和事件分发
- **配置管理** - 统一的JSON配置文件解析和插件启用控制
- **流拦截器统一管理** - 输入流、输出流、日志流的统一拦截和协调
- **事件通知机制** - 输入、输出、日志事件的统一分发
- **扩展接口定义** - IPlugin和IStreamInterceptor接口的标准化

### 2. 核心组件结构

```csharp
public static class PluginManager
{
    // 核心数据结构
    private static readonly List<IPlugin> _plugins = new List<IPlugin>();
    private static readonly List<IStreamInterceptor> _streamInterceptors = new List<IStreamInterceptor>();
    private static int _downloadCount = 0;
    private static PluginConfig? _config;
}
```

## 核心功能模块

### 1. 插件管理系统

#### 插件加载机制

```csharp
public static void LoadPlugins()
{
    // 1. 加载配置文件
    LoadConfig();
    
    // 2. 反射扫描插件类型
    var pluginTypes = Assembly.GetExecutingAssembly().GetTypes()
        .Where(t => t.Namespace == "N_m3u8DL_RE.Plugin" && 
                   t.Name.EndsWith("Plugin") && 
                   !t.IsInterface && 
                   !t.IsAbstract);
    
    // 3. 实例化和初始化插件
    foreach (var type in pluginTypes)
    {
        if (Activator.CreateInstance(type) is IPlugin plugin)
        {
            var pluginName = type.Name.Replace("Plugin", "");
            if (IsPluginEnabled(pluginName))
            {
                plugin.Initialize(_config);
                _plugins.Add(plugin);
                
                // 4. 注册流拦截器
                if (plugin is IStreamInterceptor interceptor)
                {
                    RegisterStreamInterceptor(interceptor);
                }
            }
        }
    }
}
```

#### 配置管理机制

```csharp
private static void LoadConfig()
{
    var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "extend", "PluginConfig.json");
    
    // 手动JSON解析，避免反射限制
    if (json.Contains("\"UASwitcher\""))
    {
        var uaEnabled = ExtractJsonBoolValue(json, "UASwitcher", "Enabled");
        _config.UASwitcher = new UASwitcherConfig { Enabled = uaEnabled };
    }
    
    if (json.Contains("\"StreamInterceptor\""))
    {
        var streamInterceptorEnabled = ExtractJsonBoolValue(json, "StreamInterceptor", "Enabled");
        _config.StreamInterceptor = new StreamInterceptorConfig
        {
            Enabled = streamInterceptorEnabled,
            InterceptLevel = ExtractJsonValue(json, "InterceptLevel", "Info"),
            LogDestination = ExtractJsonValue(json, "LogDestination", "Console")
        };
    }
}
```

### 2. 流拦截器管理系统

#### 统一拦截器注册

```csharp
public static void RegisterStreamInterceptor(IStreamInterceptor interceptor)
{
    if (!_streamInterceptors.Contains(interceptor))
    {
        _streamInterceptors.Add(interceptor);
        
        // 统一注册到三个拦截器
        InputStreamInterceptor.RegisterInterceptor(interceptor);
        OutputStreamInterceptor.RegisterInterceptor(interceptor);
        LogStreamInterceptor.RegisterInterceptor(interceptor);
    }
}
```

#### 事件通知机制

```csharp
// 输入事件通知
internal static void NotifyPluginsOnInput(object args, object option)
{
    foreach (var plugin in _plugins)
    {
        plugin.OnInputReceived(args, option);
    }
}

// 输出事件通知
internal static void NotifyPluginsOnOutput(string outputPath, string outputType)
{
    foreach (var plugin in _plugins)
    {
        plugin.OnOutputGenerated(outputPath, outputType);
    }
}

// 日志事件通知
internal static void NotifyPluginsOnLog(string logMessage, PluginLogLevel logLevel)
{
    foreach (var plugin in _plugins)
    {
        plugin.OnLogGenerated(logMessage, logLevel);
    }
}
```

## 接口定义规范

### 1. 基础插件接口

```csharp
public interface IPlugin
{
    // 核心生命周期方法
    void Initialize(PluginConfig? config);
    void OnFileDownloaded(string filePath, int downloadCount);
    
    // 扩展事件方法
    void OnInputReceived(object args, object option);
    void OnOutputGenerated(string outputPath, string outputType);
    void OnLogGenerated(string logMessage, PluginLogLevel logLevel);
}
```

### 2. 流拦截器接口

```csharp
public interface IStreamInterceptor
{
    // 输入流拦截
    string[] InterceptInput(string[] originalArgs);
    object InterceptOptions(object originalOption);
    
    // 输出流拦截
    string InterceptOutput(string originalOutput, string outputType);
    void OnOutputRedirect(string originalPath, string newPath);
    
    // 日志流拦截
    string InterceptLog(string originalLog, PluginLogLevel level);
    void OnLogRedirect(string originalLog, PluginLogLevel level, string newDestination);
}
```

### 3. 插件配置类

```csharp
public class PluginConfig
{
    public UASwitcherConfig? UASwitcher { get; set; }
    public ProxySwitcherConfig? ProxySwitcher { get; set; }
    public StreamInterceptorConfig? StreamInterceptor { get; set; }
}

public class UASwitcherConfig
{
    public bool Enabled { get; set; } = false;
    public List<string> UserAgents { get; set; } = new List<string>();
}

public class ProxySwitcherConfig
{
    public bool Enabled { get; set; } = false;
    public string ClashApiUrl { get; set; } = "http://127.0.0.1:9090";
    public int SwitchInterval { get; set; } = 3;
}

public class StreamInterceptorConfig
{
    public bool Enabled { get; set; } = false;
    public string InterceptLevel { get; set; } = "Info";
    public string LogDestination { get; set; } = "Console";
}
```

## 配置管理系统

### 1. 配置文件格式

```json
{
  "UASwitcher": {
    "Enabled": true,
    "UserAgents": [
      "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36"
    ]
  },
  "ProxySwitcher": {
    "Enabled": false,
    "ClashApiUrl": "http://127.0.0.1:9090",
    "SwitchInterval": 3
  },
  "StreamInterceptor": {
    "Enabled": true,
    "InterceptLevel": "Info",
    "LogDestination": "Console"
  },
  "BatchDownload": {
    "Enabled": true,
    "CreateSubdirectories": false
  }
}
```

### 2. 配置解析机制

```csharp
private static bool ExtractJsonBoolValue(string json, string sectionName, string propertyName)
{
    var patterns = new[]
    {
        $"\\\"{sectionName}\\\":\\s*\\{{\"\\\"{propertyName}\\\":\\s*(true|false)",
        $"\\\"{sectionName}\\\":\\s*\\{{\"\\\"{propertyName}\\\":\\s*(true|false)",
        $"\\\"{sectionName}\\\":\\s*\\{{[^}}]*\\\"{propertyName}\\\":\\s*(true|false)"
    };
    
    foreach (var pattern in patterns)
    {
        var match = System.Text.RegularExpressions.Regex.Match(json, pattern);
        if (match.Success)
        {
            return match.Groups[1].Value == "true";
        }
    }
    return false;
}
```

## 集成方式

### 1. 主程序集成

在Program.cs中初始化插件系统：

```csharp
static async Task Main(string[] args)
{
    try
    {
        // 初始化插件系统
        PluginManager.LoadPlugins();
        
        // 初始化流拦截器
        InputStreamInterceptor.Initialize();
        OutputStreamInterceptor.Initialize();
        LogStreamInterceptor.Initialize();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[PluginManager] 初始化失败: {ex.Message}");
    }
    
    // 继续主程序逻辑
    // ...
}
```

### 2. 命令行集成

在CommandInvoker.cs中集成输入拦截：

```csharp
public void InvokeArgs(string[] args)
{
    try
    {
        // 输入流拦截
        args = InputStreamInterceptor.InterceptArgs(args);
        
        // 插件输入事件通知
        var notifyInputMethod = pluginManagerType.GetMethod("NotifyPluginsOnInput");
        if (notifyInputMethod != null)
        {
            notifyInputMethod.Invoke(null, new object[] { args, option });
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[InputInterceptor] Failed to process input: {ex.Message}");
    }
}
```

### 3. 下载器集成

在SimpleDownloader.cs中集成输出和日志拦截：

```csharp
private void TriggerOutputInterceptor(string filePath)
{
    try
    {
        // 输出流拦截
        var interceptedPath = OutputStreamInterceptor.InterceptOutput(filePath, "file");
        
        // 插件输出事件通知
        PluginManager.NotifyPluginsOnOutput(interceptedPath, "file");
    }
    catch (Exception ex)
    {
        Logger.Warn($"[OutputInterceptor] Failed to process output: {ex.Message}");
    }
}
```

## 错误处理机制

### 1. 异常捕获策略

```csharp
internal static void NotifyPluginsOnOutput(string outputPath, string outputType)
{
    foreach (var plugin in _plugins)
    {
        try
        {
            plugin.OnOutputGenerated(outputPath, outputType);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PluginManager] Output notification failed for {plugin.GetType().Name}: {ex.Message}");
        }
    }
}
```

### 2. 配置错误处理

```csharp
private static void LoadConfig()
{
    try
    {
        // JSON解析逻辑
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[Plugin] Manual config loading failed: {ex.Message}");
        _config = new PluginConfig(); // 使用默认配置
    }
}
```

## 性能优化

### 1. 反射优化

- 使用Assembly扫描时限定命名空间和类型名称
- 缓存插件类型信息避免重复扫描
- 使用Activator.CreateInstance减少反射开销

### 2. 配置解析优化

- 手动JSON解析避免序列化开销
- 正则表达式缓存提高解析效率
- 渐进式配置加载减少启动时间

### 3. 内存管理

- 使用单例模式管理PluginManager实例
- 及时释放不需要的插件引用
- 配置对象复用减少GC压力

## 扩展指南

### 1. 新增插件类型

1. 创建插件类实现IPlugin接口
2. 在插件类名后添加"Plugin"后缀
3. 在PluginConfig.json中添加相应配置
4. PluginManager会自动发现和加载插件

### 2. 新增流拦截器

1. 创建拦截器类实现IStreamInterceptor接口
2. 在插件初始化时调用PluginManager.RegisterStreamInterceptor()
3. 拦截器会自动注册到三个流拦截器中

### 3. 自定义配置

1. 在PluginConfig类中添加新配置属性
2. 在LoadConfig方法中添加解析逻辑
3. 在IsPluginEnabled方法中添加启用判断

## 最佳实践

### 1. 插件开发

- 插件应该实现IPlugin和IStreamInterceptor接口
- 在Initialize方法中进行插件初始化
- 在事件方法中处理业务逻辑，避免耗时操作
- 正确处理异常，避免影响其他插件

### 2. 配置管理

- 配置文件的格式应该保持一致性
- 使用合理的默认值避免配置错误
- 配置验证应该在插件初始化时进行

### 3. 性能考虑

- 避免在拦截器中进行耗时操作
- 使用异步处理提高性能
- 合理使用缓存减少重复计算

## 故障排除

### 1. 常见问题

**插件未加载**
- 检查插件类名是否以"Plugin"结尾
- 检查插件是否在正确命名空间中
- 检查PluginConfig.json中的启用设置

**流拦截器不工作**
- 检查拦截器是否正确实现IStreamInterceptor接口
- 检查是否调用了RegisterStreamInterceptor方法
- 检查配置中是否启用了流拦截器

**配置加载失败**
- 检查PluginConfig.json文件是否存在
- 检查JSON格式是否正确
- 检查配置路径是否正确

### 2. 调试技巧

- 使用Debug.WriteLine进行调试输出，避免触发Console拦截
- 查看插件加载日志确认插件状态
- 使用配置验证确认配置正确性

## 总结

PluginManager作为N_m3u8DL-RE插件系统的核心，提供了完整的插件生命周期管理、流拦截功能以及事件分发机制。通过统一的接口设计和配置管理，为开发者提供了强大而灵活的插件扩展能力。

本文档涵盖了PluginManager的所有核心功能和使用方式，为插件开发和系统集成提供了完整的参考指南。