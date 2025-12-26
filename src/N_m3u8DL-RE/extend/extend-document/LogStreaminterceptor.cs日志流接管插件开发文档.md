# LogStreamInterceptor.cs日志流接管插件开发文档

## 概述

LogStreamInterceptor是N_m3u8DL-RE插件系统中的日志流接管组件，负责接管和重定向Console输出流（stdout和stderr），实现日志拦截、过滤和重定向功能。本文档详细介绍了LogStreamInterceptor的架构设计、核心功能、使用方式以及与PluginManager的集成关系。

## 核心架构设计

### 1. 整体架构

LogStreamInterceptor采用静态类设计，作为日志流接管的实现层，具有复杂的Console输出重定向机制和配置控制功能：

```csharp
// 【日志拦截】由PluginManager.cs统一管理日志流拦截功能
// 该类处理Console输出重定向和日志拦截逻辑，确保日志不丢失且可被插件拦截
// 支持通过PluginConfig.json中的StreamInterceptor配置控制启用状态

public class LogStreamInterceptor
{
    private static List<IStreamInterceptor> _interceptors = new List<IStreamInterceptor>();
    private static StringWriter? _originalConsoleOut;
    private static StringWriter? _originalConsoleError;
    private static bool _isInitialized = false;
    private static bool _isEnabled = false; // 新增：是否启用的标志
}
```

### 2. 架构特点

- **Console输出重定向**：通过自定义StringWriter实现Console输出的重定向
- **防递归设计**：使用Debug.WriteLine避免Console输出导致的递归调用
- **初始化机制**：具有专门的初始化方法处理Console重定向
- **链式拦截机制**：支持多个拦截器的依次调用
- **PluginManager托管**：由PluginManager统一管理拦截器的注册和调用
- **配置控制**：支持通过PluginConfig.json控制启用状态
- **动态启用**：可根据配置动态启用或禁用拦截功能

### 3. 核心组件结构

```csharp
public class InterceptedStringWriter : StringWriter
{
    private readonly StringWriter _original;
    private readonly string _streamType;
    
    public InterceptedStringWriter(StringWriter original, string streamType)
    {
        _original = original;
        _streamType = streamType;
    }
    
    public override void Write(string value)
    {
        var intercepted = LogStreamInterceptor.InterceptLog(value, PluginLogLevel.Info);
        _original.Write(intercepted);
        base.Write(intercepted);
    }
}
```

## 核心功能模块

### 1. 初始化机制

#### 初始化实现

```csharp
/// <summary>
/// 初始化日志拦截器，重定向Console输出
/// 【日志拦截】由PluginManager.cs统一调用此方法进行初始化
/// </summary>
/// <param name="enabled">是否启用日志拦截器，由配置决定</param>
public static void Initialize(bool enabled = false)
{
    _isEnabled = enabled;
    
    if (_isInitialized || !_isEnabled) return;
    
    try
    {
        // 【调试】记录Console.Out的类型
        var originalOutType = Console.Out.GetType().Name;
        var originalErrorType = Console.Error.GetType().Name;
        System.Diagnostics.Debug.WriteLine($"[LogInterceptor] Console.Out类型: {originalOutType}, Console.Error类型: {originalErrorType}");
        
        // 先输出初始化消息到实际控制台（避免被拦截）
        Console.WriteLine("[LogInterceptor] 日志流拦截器初始化开始...");
        
        // 保存原始Console引用
        _originalConsoleOut = new StringWriter();
        _originalConsoleError = new StringWriter();
        
        // 复制当前的Console内容到StringWriter
        var currentOut = Console.Out;
        var currentError = Console.Error;
        
        // 创建拦截的StringWriter
        var interceptedOut = new InterceptedStringWriter(_originalConsoleOut, "stdout");
        var interceptedErr = new InterceptedStringWriter(_originalConsoleError, "stderr");
        
        // 输出初始化完成消息到实际控制台
        Console.WriteLine("[LogInterceptor] 日志流拦截器初始化完成");
        
        _isInitialized = true;
        
        // 重定向Console输出
        Console.SetOut(interceptedOut);
        Console.SetError(interceptedErr);
    }
    catch (Exception ex)
    {
        // 使用Debug输出错误信息，避免Console重定向问题
        System.Diagnostics.Debug.WriteLine($"[LogInterceptor] 初始化失败: {ex.Message}");
        System.Diagnostics.Debug.WriteLine($"[LogInterceptor] 异常详情: {ex.StackTrace}");
        
        try
        {
            // 尝试使用Console.WriteLine输出错误
            Console.WriteLine($"[LogInterceptor] 初始化失败: {ex.Message}");
        }
        catch
        {
            // 如果Console不可用，忽略
        }
    }
}
```

#### 初始化流程

1. **启用检查**：检查是否启用，未启用则直接返回
2. **重复检查**：检查是否已经初始化，避免重复初始化
3. **状态设置**：设置启用状态标志
4. **类型调试**：记录Console.Out和Console.Error的类型信息
5. **消息输出**：先输出初始化消息到实际控制台（避免被拦截）
6. **引用保存**：保存原始Console引用以备恢复
7. **拦截器创建**：创建自定义的InterceptedStringWriter
8. **输出重定向**：将Console输出重定向到拦截器
9. **错误处理**：使用Debug输出错误信息避免递归

### 2. Console输出重定向机制

#### 自定义StringWriter实现

```csharp
public class InterceptedStringWriter : StringWriter
{
    private readonly StringWriter _original;
    private readonly string _streamType;
    
    public InterceptedStringWriter(StringWriter original, string streamType)
    {
        _original = original;
        _streamType = streamType;
    }
    
    public override void Write(string value)
    {
        // 调用日志拦截器处理输出
        var intercepted = LogStreamInterceptor.InterceptLog(value, PluginLogLevel.Info);
        
        // 写入原始输出
        _original.Write(intercepted);
        
        // 写入拦截后的输出到基类
        base.Write(intercepted);
    }
    
    public override void WriteLine(string value)
    {
        // 调用日志拦截器处理输出
        var intercepted = LogStreamInterceptor.InterceptLog(value, PluginLogLevel.Info);
        
        // 写入原始输出
        _original.WriteLine(intercepted);
        
        // 写入拦截后的输出到基类
        base.WriteLine(intercepted);
    }
}
```

#### 重定向工作原理

1. **Console输出拦截**：所有Console.Write和Console.WriteLine调用被拦截
2. **日志处理**：调用LogStreamInterceptor.InterceptLog方法处理日志
3. **双重写入**：同时写入原始输出和拦截后的输出
4. **链式处理**：依次调用所有注册拦截器的InterceptLog方法

### 3. 拦截器注册机制

#### 注册方法实现

```csharp
/// <summary>
/// 注册日志拦截器
/// 【日志拦截】由PluginManager.cs统一管理拦截器注册
/// </summary>
public static void RegisterInterceptor(IStreamInterceptor interceptor)
{
    if (!_isEnabled)
    {
        System.Diagnostics.Debug.WriteLine($"[LogInterceptor] 拦截器未启用，跳过注册: {interceptor.GetType().Name}");
        return;
    }
    
    if (!_interceptors.Contains(interceptor))
    {
        _interceptors.Add(interceptor);
        // 使用Debug输出避免触发Console输出拦截
        System.Diagnostics.Debug.WriteLine($"[LogInterceptor] 已注册拦截器: {interceptor.GetType().Name}");
    }
}
```

#### 注册流程

1. **启用检查**：检查拦截器是否启用，未启用则跳过注册
2. **重复检查**：检查拦截器是否已存在，避免重复注册
3. **添加拦截器**：将新拦截器添加到拦截器列表
4. **调试输出**：使用Debug.WriteLine记录注册信息
5. **防递归设计**：避免Console输出导致的递归调用

### 4. 日志拦截机制

#### 日志拦截实现

```csharp
/// <summary>
/// 拦截并处理日志消息
/// 【日志拦截】由PluginManager.cs统一调用此方法进行日志拦截处理
/// </summary>
public static string InterceptLog(string originalLog, PluginLogLevel level)
{
    if (!_isEnabled)
        return originalLog;
        
    if (string.IsNullOrEmpty(originalLog))
        return originalLog;
        
    var result = originalLog;
    
    // 依次调用所有拦截器进行处理
    foreach (var interceptor in _interceptors)
    {
        try
        {
            result = interceptor.InterceptLog(result, level);
            if (string.IsNullOrEmpty(result))
                result = originalLog; // 如果被拦截为空，保留原始日志
        }
        catch (Exception ex)
        {
            // 使用Debug输出错误信息，避免Console输出拦截
            System.Diagnostics.Debug.WriteLine($"[LogInterceptor] 拦截器 {interceptor.GetType().Name} 处理错误: {ex.Message}");
        }
    }
    
    return result;
}
```

#### 拦截流程

1. **启用检查**：检查拦截器是否启用，未启用则直接返回原始日志
2. **空值检查**：检查日志消息是否为空
3. **链式处理**：依次调用所有注册拦截器的InterceptLog方法
4. **结果保护**：如果拦截器返回空值，保留原始日志
5. **错误隔离**：单个拦截器错误不影响其他拦截器
6. **Debug输出**：使用Debug输出错误信息避免递归

### 5. 日志重定向机制

#### 重定向实现

```csharp
/// <summary>
/// 处理日志重定向事件
/// 【日志拦截】由PluginManager.cs统一调用此方法处理日志重定向
/// </summary>
public static void OnLogRedirect(string originalLog, PluginLogLevel level, string newDestination)
{
    if (!_isEnabled)
        return;
        
    foreach (var interceptor in _interceptors)
    {
        try
        {
            interceptor.OnLogRedirect(originalLog, level, newDestination);
        }
        catch (Exception ex)
        {
            // 使用Debug输出错误信息，避免Console输出拦截
            System.Diagnostics.Debug.WriteLine($"[LogInterceptor] 拦截器 {interceptor.GetType().Name} 重定向错误: {ex.Message}");
        }
    }
}
```

## 与PluginManager的集成关系

### 1. 统一管理架构

LogStreamInterceptor作为日志流接管的实现层，由PluginManager统一管理：

```csharp
public static void RegisterStreamInterceptor(IStreamInterceptor interceptor)
{
    if (!_streamInterceptors.Contains(interceptor))
    {
        _streamInterceptors.Add(interceptor);
        
        // 统一注册到各个拦截器
        InputStreamInterceptor.RegisterInterceptor(interceptor);
        OutputStreamInterceptor.RegisterInterceptor(interceptor);
        LogStreamInterceptor.RegisterInterceptor(interceptor);
    }
}
```

### 2. 事件通知机制

```csharp
/// <summary>
/// 通知插件处理日志事件
/// </summary>
internal static void NotifyPluginsOnLog(string logMessage, PluginLogLevel logLevel)
{
    foreach (var plugin in _plugins)
    {
        try
        {
            plugin.OnLogGenerated(logMessage, logLevel);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PluginManager] Log notification failed for {plugin.GetType().Name}: {ex.Message}");
        }
    }
}
```

## 与主程序的集成

### 1. Program.cs集成点

在Program.cs的Main方法中初始化日志拦截器：

```csharp
static async Task Main(string[] args)
{
    // 【日志拦截】由PluginManager.cs统一调用此方法进行日志流拦截初始化
    // 初始化日志拦截器，重定向Console输出到拦截器
    try
    {
        // 首先尝试通过Assembly.GetExecutingAssembly()获取当前程序集
        var executingAssembly = Assembly.GetExecutingAssembly();
        var logInterceptorType = executingAssembly.GetType("N_m3u8DL_RE.Plugin.LogStreamInterceptor");
        
        if (logInterceptorType == null)
        {
            // 如果找不到，尝试通过Type.GetType
            logInterceptorType = Type.GetType("N_m3u8DL_RE.Plugin.LogStreamInterceptor, N_m3u8DL-RE");
        }
        
        Console.WriteLine($"[LogInterceptor] 正在查找LogStreamInterceptor类型: {logInterceptorType != null}");
        
        if (logInterceptorType != null)
        {
            // 检查配置是否启用StreamInterceptor
            bool isLogInterceptorEnabled = false;
            try
            {
                var pluginManagerType = Type.GetType("N_m3u8DL_RE.Plugin.PluginManager, N_m3u8DL-RE");
                if (pluginManagerType != null)
                {
                    var getConfigMethod = pluginManagerType.GetMethod("GetConfig");
                    if (getConfigMethod != null)
                    {
                        var config = getConfigMethod.Invoke(null, null);
                        if (config != null)
                        {
                            var configType = config.GetType();
                            var streamInterceptorProp = configType.GetProperty("StreamInterceptor");
                            if (streamInterceptorProp != null)
                            {
                                var streamConfig = streamInterceptorProp.GetValue(config);
                                if (streamConfig != null)
                                {
                                    var enabledProp = streamConfig.GetType().GetProperty("Enabled");
                                    if (enabledProp != null)
                                    {
                                        isLogInterceptorEnabled = (bool)(enabledProp.GetValue(streamConfig) ?? false);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception configEx)
            {
                Console.WriteLine($"[LogInterceptor] 配置检查失败，使用默认设置: {configEx.Message}");
            }
            
            Console.WriteLine($"[LogInterceptor] StreamInterceptor配置启用状态: {isLogInterceptorEnabled}");
            
            var initializeMethod = logInterceptorType.GetMethod("Initialize");
            if (initializeMethod != null)
            {
                Console.WriteLine("[LogInterceptor] 找到Initialize方法，正在调用...");
                try
                {
                    // 根据配置决定是否启用日志拦截器
                    initializeMethod.Invoke(null, new object[] { isLogInterceptorEnabled });
                    Console.WriteLine($"[LogInterceptor] 日志拦截器已{(isLogInterceptorEnabled ? "启用" : "禁用")}");
                }
                catch (Exception invokeEx)
                {
                    Console.WriteLine($"[LogInterceptor] Initialize方法调用异常: {invokeEx.Message}");
                    Console.WriteLine($"[LogInterceptor] 异常详情: {invokeEx.StackTrace}");
                }
                // 初始化信息由LogStreamInterceptor内部输出
            }
            else
            {
                Console.WriteLine("[LogInterceptor] 未找到Initialize方法");
            }
        }
        else
        {
            Console.WriteLine("[LogInterceptor] 未找到LogStreamInterceptor类型");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[LogInterceptor] 初始化失败: {ex.Message}");
        Console.WriteLine($"[LogInterceptor] 异常详情: {ex.StackTrace}");
    }
    
    // 继续主程序逻辑...
}
```

### 2. 配置控制机制

#### 配置文件控制

LogStreamInterceptor通过PluginConfig.json中的StreamInterceptor配置项控制启用状态：

```json
{
  "StreamInterceptor": {
    "Enabled": true,
    "InterceptLevel": "Info", 
    "LogDestination": "Console"
  }
}
```

- `Enabled`: 控制LogStreamInterceptor是否启用
- `InterceptLevel`: 日志拦截级别
- `LogDestination`: 日志重定向目标

#### 配置检查逻辑

在Program.cs中的配置检查流程：

1. **获取PluginManager类型**：通过反射获取PluginManager类
2. **调用GetConfig方法**：获取当前插件配置
3. **读取StreamInterceptor配置**：提取StreamInterceptor节的配置
4. **检查Enabled属性**：读取Enabled标志
5. **传递启用状态**：将启用状态传递给Initialize方法

#### 启用状态传递

```csharp
// 根据配置决定是否启用日志拦截器
initializeMethod.Invoke(null, new object[] { isLogInterceptorEnabled });
Console.WriteLine($"[LogInterceptor] 日志拦截器已{(isLogInterceptorEnabled ? "启用" : "禁用")}");
```

### 3. 集成流程

在主程序启动过程中，LogStreamInterceptor的集成流程如下：

1. **插件系统初始化**：首先初始化PluginManager和插件系统
2. **配置检查**：读取PluginConfig.json中的StreamInterceptor配置
3. **类型查找**：通过反射查找LogStreamInterceptor类型
4. **方法调用**：调用Initialize方法传递启用状态
5. **初始化完成**：根据启用状态决定是否初始化Console重定向

## 接口定义和实现

### 1. 插件日志级别枚举

```csharp
public enum PluginLogLevel
{
    Debug,
    Info,
    Warn,
    Error,
    Fatal
}
```

### 2. IStreamInterceptor接口定义

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

### 3. 日志拦截接口实现

```csharp
public class StreamInterceptorPlugin : IPlugin, IStreamInterceptor
{
    public string InterceptLog(string originalLog, PluginLogLevel level)
    {
        // 日志拦截处理逻辑
        var result = originalLog;
        
        // 根据日志级别进行不同处理
        switch (level)
        {
            case PluginLogLevel.Debug:
                // 调试日志处理
                result = ProcessDebugLog(originalLog);
                break;
            case PluginLogLevel.Info:
                // 信息日志处理
                result = ProcessInfoLog(originalLog);
                break;
            case PluginLogLevel.Warn:
                // 警告日志处理
                result = ProcessWarnLog(originalLog);
                break;
            case PluginLogLevel.Error:
            case PluginLogLevel.Fatal:
                // 错误日志处理
                result = ProcessErrorLog(originalLog);
                break;
        }
        
        return result;
    }
    
    public void OnLogRedirect(string originalLog, PluginLogLevel level, string newDestination)
    {
        // 日志重定向处理逻辑
        Console.WriteLine($"[LogRedirect] {originalLog} -> {newDestination}");
        
        // 可以在这里执行重定向相关的操作
        UpdateLogDestination(originalLog, newDestination);
    }
}
```

## 使用场景和示例

### 1. 日志过滤插件

```csharp
public class LogFilterPlugin : IPlugin, IStreamInterceptor
{
    private readonly HashSet<string> _filteredKeywords = new HashSet<string>
    {
        "DEBUG", "TRACE", "VERBOSE"
    };
    
    public string InterceptLog(string originalLog, PluginLogLevel level)
    {
        // 过滤包含敏感关键词的日志
        foreach (var keyword in _filteredKeywords)
        {
            if (originalLog.Contains(keyword) && level == PluginLogLevel.Debug)
            {
                return string.Empty; // 返回空字符串表示过滤掉
            }
        }
        
        return originalLog;
    }
}
```

### 2. 日志格式化插件

```csharp
public class LogFormatPlugin : IPlugin, IStreamInterceptor
{
    public string InterceptLog(string originalLog, PluginLogLevel level)
    {
        // 添加时间戳和级别标识
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var levelPrefix = level.ToString().ToUpper().PadRight(5);
        
        return $"[{timestamp}] [{levelPrefix}] {originalLog}";
    }
}
```

### 3. 日志重定向插件

```csharp
public class LogRedirectPlugin : IPlugin, IStreamInterceptor
{
    private readonly Dictionary<PluginLogLevel, string> _logDestinations = new Dictionary<PluginLogLevel, string>
    {
        { PluginLogLevel.Error, "error.log" },
        { PluginLogLevel.Fatal, "fatal.log" },
        { PluginLogLevel.Warn, "warning.log" },
        { PluginLogLevel.Info, "info.log" },
        { PluginLogLevel.Debug, "debug.log" }
    };
    
    public void OnLogRedirect(string originalLog, PluginLogLevel level, string newDestination)
    {
        if (_logDestinations.ContainsKey(level))
        {
            var fileName = _logDestinations[level];
            File.AppendAllText(fileName, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {originalLog}\n");
        }
    }
}
```

### 4. 批量下载日志插件

```csharp
public class BatchDownloadLogPlugin : IPlugin, IStreamInterceptor
{
    private readonly List<string> _logEntries = new List<string>();
    
    public string InterceptLog(string originalLog, PluginLogLevel level)
    {
        // 收集与批量下载相关的日志
        if (originalLog.Contains("BatchDownload") || originalLog.Contains("batch"))
        {
            _logEntries.Add($"[{level}] {originalLog}");
        }
        
        return originalLog;
    }
    
    public void OnLogRedirect(string originalLog, PluginLogLevel level, string newDestination)
    {
        // 在批量下载完成后输出汇总日志
        if (originalLog.Contains("BatchDownload completed"))
        {
            Console.WriteLine($"\n=== 批量下载日志汇总 ===");
            foreach (var entry in _logEntries)
            {
                Console.WriteLine(entry);
            }
            Console.WriteLine("=== 批量下载日志汇总结束 ===\n");
        }
    }
}
```

## 错误处理机制

### 1. 初始化错误处理

```csharp
public static void Initialize()
{
    try
    {
        // 初始化逻辑
    }
    catch (Exception ex)
    {
        // 使用Debug输出错误信息，避免Console重定向问题
        System.Diagnostics.Debug.WriteLine($"[LogInterceptor] 初始化失败: {ex.Message}");
        
        try
        {
            // 尝试使用Console.WriteLine输出错误
            Console.WriteLine($"[LogInterceptor] 初始化失败: {ex.Message}");
        }
        catch
        {
            // 如果Console不可用，忽略
        }
    }
}
```

### 2. 拦截错误处理

```csharp
public static string InterceptLog(string originalLog, PluginLogLevel level)
{
    var result = originalLog;
    
    foreach (var interceptor in _interceptors)
    {
        try
        {
            result = interceptor.InterceptLog(result, level);
            if (string.IsNullOrEmpty(result))
                result = originalLog; // 保护原始日志
        }
        catch (Exception ex)
        {
            // 使用Debug输出错误信息，避免Console输出拦截
            System.Diagnostics.Debug.WriteLine($"[LogInterceptor] 拦截器 {interceptor.GetType().Name} 处理错误: {ex.Message}");
        }
    }
    
    return result;
}
```

### 3. 错误处理原则

- **防递归设计**：始终使用Debug.WriteLine输出错误信息
- **原始数据保护**：出现错误时保留原始日志内容
- **错误隔离**：单个拦截器错误不影响其他拦截器
- **继续处理**：错误后继续执行后续拦截器

## 性能优化

### 1. 重定向优化

- **双重写入优化**：合理管理StringWriter实例
- **字符串处理优化**：避免在拦截器中创建大量临时字符串
- **缓冲区机制**：使用缓冲区减少I/O操作

### 2. 拦截器优化

- **早期退出**：如果拦截器列表为空，直接返回原始日志
- **空值检查**：避免处理空日志消息
- **高效链表**：使用List<T>存储拦截器

### 3. 内存管理

- **及时释放**：拦截器处理完成后及时释放临时资源
- **日志缓存**：合理管理日志缓存大小
- **StringWriter复用**：避免频繁创建StringWriter实例

## 调试和监控

### 1. 调试输出

```csharp
public static void Initialize()
{
    // 【调试】记录Console.Out的类型
    var originalOutType = Console.Out.GetType().Name;
    var originalErrorType = Console.Error.GetType().Name;
    System.Diagnostics.Debug.WriteLine($"[LogInterceptor] Console.Out类型: {originalOutType}, Console.Error类型: {originalErrorType}");
}
```

### 2. 调试技巧

- **Debug.WriteLine**：始终使用Debug输出避免触发Console拦截
- **类型记录**：记录Console输出对象的类型信息
- **初始化日志**：记录初始化过程的详细信息

### 3. 监控指标

- **拦截器数量**：监控注册的拦截器数量
- **日志处理量**：监控处理的日志消息数量
- **重定向次数**：监控Console输出重定向的次数
- **错误率**：监控拦截器执行错误率

## 最佳实践

### 1. 拦截器开发

- **防递归**：始终使用Debug.WriteLine进行调试输出
- **接口实现**：确保正确实现IStreamInterceptor接口
- **日志保护**：确保拦截器不会返回null或空字符串
- **异常处理**：正确处理可能出现的异常情况
- **配置检查**：拦截器需要考虑LogStreamInterceptor的启用状态

### 2. 配置控制使用

- **启用控制**：通过PluginConfig.json中的StreamInterceptor.Enabled控制是否启用
- **动态配置**：可以动态修改配置实现启用/禁用切换
- **错误处理**：配置检查失败时使用默认设置（禁用）
- **状态传递**：确保启用状态正确传递给Initialize方法

### 3. 初始化使用

- **时机选择**：在程序早期初始化，避免遗漏日志
- **错误处理**：妥善处理初始化失败的情况
- **状态检查**：检查初始化是否成功

### 3. 日志处理

- **级别识别**：正确识别和处理不同级别的日志
- **内容过滤**：合理过滤敏感或不需要的日志
- **格式化**：提供一致的日志格式

## 故障排除

### 1. 常见问题

**初始化失败**
- 检查Console输出是否正常
- 检查是否有权限创建StringWriter
- 查看Debug输出中的错误信息

**日志拦截不生效**
- 检查拦截器是否正确注册到PluginManager
- 检查拦截器是否正确实现IStreamInterceptor接口
- 检查调用时机是否正确

**递归调用问题**
- 检查是否使用了Console.WriteLine进行调试输出
- 检查拦截器实现是否正确处理递归调用

### 2. 调试步骤

1. **检查初始化**：确认LogStreamInterceptor.Initialize()被正确调用
2. **检查注册**：确认拦截器已正确注册到LogStreamInterceptor
3. **检查调用**：确认Console输出被正确重定向
4. **检查异常**：查看Debug输出中的错误信息

### 3. 修复建议

- **重新初始化**：重新调用Initialize()方法
- **清理状态**：清理可能的状态冲突
- **Console检查**：检查Console输出是否正常

## 扩展指南

### 1. 新增日志级别

1. **扩展枚举**：在PluginLogLevel枚举中添加新级别
2. **处理逻辑**：为新级别实现特定的处理逻辑
3. **类型注册**：在拦截器中注册新级别处理

### 2. 复杂重定向场景

**条件重定向**
- 根据日志内容或级别进行条件重定向
- 支持多个重定向规则
- 提供重定向规则配置

**动态重定向**
- 根据运行时状态动态生成重定向目标
- 支持模板化的重定向路径
- 提供重定向路径的验证机制

## 总结

LogStreamInterceptor作为N_m3u8DL-RE插件系统的日志流接管组件，提供了强大的Console输出重定向和日志拦截能力。通过与PluginManager的紧密集成，实现了统一、可扩展的日志流处理机制。

本文档详细介绍了LogStreamInterceptor的架构设计、功能实现、使用方式以及最佳实践，为开发者提供了完整的日志流拦截开发指南。通过合理使用LogStreamInterceptor，可以实现灵活的日志处理、过滤、重定向和格式化功能。

需要特别注意LogStreamInterceptor的防递归设计，确保在调试和错误处理时始终使用Debug.WriteLine，避免触发Console输出拦截导致的递归调用问题。