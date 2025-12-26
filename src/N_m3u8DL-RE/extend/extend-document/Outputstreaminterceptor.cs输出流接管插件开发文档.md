# OutputStreamInterceptor.cs输出流接管插件开发文档

## 概述

OutputStreamInterceptor是N_m3u8DL-RE插件系统中的输出流接管组件，负责拦截和处理程序输出流，包括文件路径重定向、输出消息处理以及输出事件通知。本文档详细介绍了OutputStreamInterceptor的架构设计、核心功能、使用方式以及与PluginManager的集成关系。

## 核心架构设计

### 1. 整体架构

OutputStreamInterceptor采用静态类设计，作为输出流接管的实现层，与PluginManager形成统一的拦截管理体系：

```csharp
// 【输出流拦截】由PluginManager.cs统一管理输出流拦截功能
// 该类处理输出流拦截和重定向逻辑，确保输出不丢失且可被插件拦截

public class OutputStreamInterceptor
{
    private static List<IStreamInterceptor> _interceptors = new List<IStreamInterceptor>();
    private static StringWriter? _originalOutput;
    private static bool _isInitialized = false;
}
```

### 2. 架构特点

- **静态单例模式**：所有拦截器共享同一个实例，保证拦截链的一致性
- **初始化机制**：具有专门的初始化方法，确保输出重定向的安全性
- **链式拦截机制**：支持多个拦截器的依次调用，形成拦截链
- **PluginManager托管**：由PluginManager统一管理拦截器的注册和调用
- **防递归设计**：使用Debug.WriteLine避免Console输出导致的递归调用

## 核心功能模块

### 1. 初始化机制

#### 初始化实现

```csharp
/// <summary>
/// 初始化输出流拦截器
/// 【输出流拦截】由PluginManager.cs统一调用此方法进行初始化
/// </summary>
public static void Initialize()
{
    if (_isInitialized) return;
    
    try
    {
        // 先输出初始化消息到实际控制台（避免被拦截）
        Console.WriteLine("[OutputInterceptor] 输出流拦截器初始化开始...");
        
        // 保存原始输出引用
        _originalOutput = new StringWriter();
        
        _isInitialized = true;
        
        // 输出初始化完成消息到实际控制台
        Console.WriteLine("[OutputInterceptor] 输出流拦截器初始化完成");
    }
    catch (Exception ex)
    {
        // 使用Debug输出错误信息
        System.Diagnostics.Debug.WriteLine($"[OutputInterceptor] 初始化失败: {ex.Message}");
        System.Diagnostics.Debug.WriteLine($"[OutputInterceptor] 异常详情: {ex.StackTrace}");
    }
}
```

#### 初始化流程

1. **重复检查**：检查是否已经初始化，避免重复初始化
2. **消息输出**：先输出初始化消息到实际控制台（避免被拦截）
3. **资源保存**：保存原始输出引用以备恢复
4. **状态标记**：设置初始化状态标记
5. **错误处理**：使用Debug输出错误信息避免递归

### 2. 拦截器注册机制

#### 注册方法实现

```csharp
/// <summary>
/// 注册输出流拦截器
/// 【输出流拦截】由PluginManager.cs统一管理拦截器注册
/// </summary>
public static void RegisterInterceptor(IStreamInterceptor interceptor)
{
    if (!_interceptors.Contains(interceptor))
    {
        _interceptors.Add(interceptor);
        // 使用Debug输出避免触发Console输出拦截
        System.Diagnostics.Debug.WriteLine($"[OutputInterceptor] 已注册输出流拦截器: {interceptor.GetType().Name}");
    }
}
```

#### 注册流程

1. **重复检查**：检查拦截器是否已存在，避免重复注册
2. **添加拦截器**：将新拦截器添加到拦截器列表
3. **调试输出**：使用Debug.WriteLine记录注册信息
4. **防递归设计**：避免Console输出导致的递归调用

### 3. 输出消息拦截机制

#### 输出拦截实现

```csharp
/// <summary>
/// 拦截并处理输出消息
/// 【输出流拦截】由PluginManager.cs统一调用此方法进行输出拦截处理
/// </summary>
public static string InterceptOutput(string originalOutput, string outputType)
{
    if (string.IsNullOrEmpty(originalOutput))
        return originalOutput;
        
    var result = originalOutput;
    
    // 依次调用所有拦截器进行处理
    foreach (var interceptor in _interceptors)
    {
        try
        {
            result = interceptor.InterceptOutput(result, outputType);
            if (string.IsNullOrEmpty(result))
                result = originalOutput; // 如果被拦截为空，保留原始输出
        }
        catch (Exception ex)
        {
            _originalOutput?.WriteLine($"[OutputInterceptor] 拦截器 {interceptor.GetType().Name} 处理错误: {ex.Message}");
        }
    }
    
    return result;
}
```

#### 拦截流程

1. **空值检查**：检查输出消息是否为空
2. **链式处理**：依次调用所有注册拦截器的InterceptOutput方法
3. **结果保护**：如果拦截器返回空值，保留原始输出
4. **错误隔离**：单个拦截器错误不影响其他拦截器
5. **结果返回**：返回经过所有拦截器处理的输出消息

### 4. 输出重定向机制

#### 重定向实现

```csharp
/// <summary>
/// 处理输出重定向事件
/// 【输出流拦截】由PluginManager.cs统一调用此方法处理输出重定向
/// </summary>
public static void OnOutputRedirect(string originalPath, string newPath)
{
    foreach (var interceptor in _interceptors)
    {
        try
        {
            interceptor.OnOutputRedirect(originalPath, newPath);
        }
        catch (Exception ex)
        {
            _originalOutput?.WriteLine($"[OutputInterceptor] 拦截器 {interceptor.GetType().Name} 重定向错误: {ex.Message}");
        }
    }
}
```

#### 重定向流程

1. **事件接收**：接收输出重定向事件
2. **链式通知**：依次通知所有拦截器的OnOutputRedirect方法
3. **错误处理**：记录重定向过程中的错误
4. **状态更新**：拦截器可以更新内部状态或配置

## 与PluginManager的集成关系

### 1. 统一管理架构

OutputStreamInterceptor作为输出流接管的实现层，由PluginManager统一管理：

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
/// 通知插件处理输出事件
/// </summary>
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

## 与主程序的集成

### 1. 下载器集成点

在SimpleDownloader.cs中集成输出拦截：

```csharp
private void TriggerOutputInterceptor(string filePath)
{
    try
    {
        // 输出流拦截
        var interceptedPath = OutputStreamInterceptor.InterceptOutput(filePath, "file");
        
        if (interceptedPath != filePath)
        {
            // 处理重定向的输出路径
            var pluginManagerType = Type.GetType("N_m3u8DL_RE.Plugin.PluginManager, N_m3u8DL-RE");
            if (pluginManagerType != null)
            {
                var redirectOutputMethod = pluginManagerType.GetMethod("RedirectOutput", 
                    BindingFlags.NonPublic | BindingFlags.Static);
                if (redirectOutputMethod != null)
                {
                    redirectOutputMethod.Invoke(null, new object[] { filePath, interceptedPath });
                }
            }
        }
        
        // 插件输出事件通知
        PluginManager.NotifyPluginsOnOutput(interceptedPath, "file");
    }
    catch (Exception ex)
    {
        Logger.Warn($"[OutputInterceptor] Failed to process output redirection: {ex.Message}");
    }
}
```

### 2. 下载管理器集成

在SimpleDownloadManager.cs中集成输出路径处理：

```csharp
private string InterceptFileOutput(string filePath, string outputType)
{
    try
    {
        // 输出流拦截
        var pluginManagerType = Type.GetType("N_m3u8DL_RE.Plugin.PluginManager, N_m3u8DL-RE");
        if (pluginManagerType != null)
        {
            var notifyOutputMethod = pluginManagerType.GetMethod("NotifyPluginsOnOutput", 
                BindingFlags.NonPublic | BindingFlags.Static);
            if (notifyOutputMethod != null)
            {
                notifyOutputMethod.Invoke(null, new object[] { filePath, outputType });
            }
        }
        
        // 使用输出拦截器
        filePath = OutputStreamInterceptor.RedirectOutputPath(filePath, outputType);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[OutputInterceptor] Failed to process output: {ex.Message}");
    }
    
    return filePath;
}
```

## 接口定义和实现

### 1. IStreamInterceptor接口定义

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

### 2. 输出拦截接口实现

```csharp
public class StreamInterceptorPlugin : IPlugin, IStreamInterceptor
{
    public string InterceptOutput(string originalOutput, string outputType)
    {
        // 输出消息拦截逻辑
        var result = originalOutput;
        
        // 示例：根据输出类型进行不同处理
        switch (outputType.ToLower())
        {
            case "file":
                // 文件输出处理
                result = ProcessFileOutput(originalOutput);
                break;
            case "console":
                // 控制台输出处理
                result = ProcessConsoleOutput(originalOutput);
                break;
            case "log":
                // 日志输出处理
                result = ProcessLogOutput(originalOutput);
                break;
        }
        
        return result;
    }
    
    public void OnOutputRedirect(string originalPath, string newPath)
    {
        // 输出重定向处理逻辑
        Console.WriteLine($"[OutputRedirect] {originalPath} -> {newPath}");
        
        // 可以在这里执行重定向相关的操作
        UpdateFileMapping(originalPath, newPath);
    }
}
```

## 使用场景和示例

### 1. 文件路径重定向插件

```csharp
public class FileRedirectPlugin : IPlugin, IStreamInterceptor
{
    private readonly Dictionary<string, string> _pathMappings = new Dictionary<string, string>();
    
    public string InterceptOutput(string originalOutput, string outputType)
    {
        if (outputType == "file" && _pathMappings.ContainsKey(originalOutput))
        {
            return _pathMappings[originalOutput];
        }
        
        return originalOutput;
    }
    
    public void OnOutputRedirect(string originalPath, string newPath)
    {
        _pathMappings[originalPath] = newPath;
        Console.WriteLine($"[FileRedirect] 路径重定向: {originalPath} -> {newPath}");
    }
}
```

### 2. 输出格式转换插件

```csharp
public class OutputFormatPlugin : IPlugin, IStreamInterceptor
{
    public string InterceptOutput(string originalOutput, string outputType)
    {
        switch (outputType.ToLower())
        {
            case "file":
                // 为文件输出添加时间戳
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                return $"[{timestamp}] {originalOutput}";
                
            case "console":
                // 为控制台输出添加颜色标记
                return $"[CONSOLE] {originalOutput}";
                
            default:
                return originalOutput;
        }
    }
}
```

### 3. 批量下载输出插件

```csharp
public class BatchDownloadPlugin : IPlugin, IStreamInterceptor
{
    private int _downloadCount = 0;
    
    public string InterceptOutput(string originalOutput, string outputType)
    {
        if (outputType == "file" && originalOutput.Contains(".m3u8"))
        {
            _downloadCount++;
            var baseName = Path.GetFileNameWithoutExtension(originalOutput);
            var extension = Path.GetExtension(originalOutput);
            
            // 生成批量下载文件名
            var batchFileName = $"{baseName}_batch{_downloadCount:03d}{extension}";
            return Path.Combine(Path.GetDirectoryName(originalOutput)!, batchFileName);
        }
        
        return originalOutput;
    }
    
    public void OnOutputRedirect(string originalPath, string newPath)
    {
        // 记录批量下载的文件映射
        LogFileMapping(originalPath, newPath);
    }
}
```

## 错误处理机制

### 1. 异常捕获策略

```csharp
public static string InterceptOutput(string originalOutput, string outputType)
{
    var result = originalOutput;
    
    foreach (var interceptor in _interceptors)
    {
        try
        {
            result = interceptor.InterceptOutput(result, outputType);
            if (string.IsNullOrEmpty(result))
                result = originalOutput; // 保护原始输出
        }
        catch (Exception ex)
        {
            _originalOutput?.WriteLine($"[OutputInterceptor] 拦截器 {interceptor.GetType().Name} 处理错误: {ex.Message}");
        }
    }
    
    return result;
}
```

### 2. 错误处理原则

- **错误隔离**：单个拦截器错误不影响其他拦截器
- **原始输出保护**：出现错误时保留原始输出
- **错误记录**：详细记录错误信息便于调试
- **继续处理**：错误后继续执行后续拦截器

### 3. 常见错误类型

1. **空值处理错误**：拦截器处理空输出消息
2. **路径格式错误**：文件路径格式不正确
3. **权限错误**：访问受限的文件或目录
4. **字符编码错误**：输出内容编码问题

## 性能优化

### 1. 拦截器优化

- **早期退出**：如果拦截器列表为空，直接返回原始输出
- **空值检查**：避免处理空输出消息
- **高效链表**：使用List<T>存储拦截器，支持快速遍历
- **异常隔离**：使用try-catch隔离异常，避免性能损耗

### 2. 内存管理

- **StringWriter复用**：合理管理StringWriter实例
- **字符串优化**：避免在拦截器中创建大量临时字符串
- **及时释放**：拦截器处理完成后及时释放临时资源

### 3. 输出优化

- **批量处理**：对于大量输出，考虑批量处理
- **缓冲机制**：使用缓冲区减少I/O操作
- **异步处理**：对于耗时操作，考虑异步处理

## 调试和监控

### 1. 调试输出

```csharp
public static void RegisterInterceptor(IStreamInterceptor interceptor)
{
    if (!_interceptors.Contains(interceptor))
    {
        _interceptors.Add(interceptor);
        // 使用Debug输出避免触发Console输出拦截
        System.Diagnostics.Debug.WriteLine($"[OutputInterceptor] 已注册输出流拦截器: {interceptor.GetType().Name}");
    }
}
```

### 2. 调试技巧

- **Debug.WriteLine**：使用Debug输出避免触发Console拦截
- **初始化日志**：记录初始化过程的详细信息
- **错误日志**：记录拦截器执行过程中的错误

### 3. 监控指标

- **拦截器数量**：监控注册的拦截器数量
- **处理次数**：监控输出拦截处理的次数
- **错误率**：监控拦截器执行错误率
- **处理时间**：监控拦截处理耗时

## 最佳实践

### 1. 拦截器开发

- **接口实现**：确保正确实现IStreamInterceptor接口
- **输出保护**：确保拦截器不会返回null或空字符串
- **异常处理**：正确处理可能出现的异常情况
- **性能考虑**：避免在拦截器中进行耗时操作

### 2. 集成使用

- **PluginManager管理**：通过PluginManager统一管理拦截器
- **注册顺序**：考虑拦截器的注册顺序对结果的影响
- **状态管理**：避免拦截器之间的状态冲突

### 3. 重定向处理

- **路径验证**：验证重定向路径的有效性
- **权限检查**：确保有权限访问重定向目标
- **错误恢复**：提供重定向失败时的恢复机制

## 故障排除

### 1. 常见问题

**输出拦截不生效**
- 检查拦截器是否正确注册到PluginManager
- 检查拦截器是否正确实现IStreamInterceptor接口
- 检查调用时机是否正确

**文件重定向失效**
- 检查拦截器的OnOutputRedirect方法实现
- 检查是否有权限访问目标路径
- 检查路径格式是否正确

**初始化失败**
- 检查Console输出是否正常
- 检查是否有权限创建StringWriter
- 查看Debug输出中的错误信息

### 2. 调试步骤

1. **检查注册**：确认拦截器已正确注册到OutputStreamInterceptor
2. **检查初始化**：确认OutputStreamInterceptor.Initialize()被正确调用
3. **检查调用**：确认OutputStreamInterceptor.InterceptOutput()被正确调用
4. **检查异常**：查看Debug输出中的错误信息

### 3. 修复建议

- **重新初始化**：重新调用Initialize()方法
- **清理状态**：清理可能的状态冲突
- **权限检查**：检查文件访问权限

## 扩展指南

### 1. 新增输出类型

1. **扩展输出类型**：在interceptOutput方法中支持新的输出类型
2. **类型处理**：为新输出类型实现特定的处理逻辑
3. **类型注册**：在PluginManager中注册新输出类型

### 2. 复杂重定向场景

**条件重定向**
- 根据输出内容或环境条件进行重定向
- 支持多个重定向规则
- 提供重定向规则配置

**动态重定向**
- 根据运行时状态动态生成重定向路径
- 支持模板化的路径生成
- 提供重定向路径的验证机制

## 总结

OutputStreamInterceptor作为N_m3u8DL-RE插件系统的输出流接管组件，提供了强大的输出消息拦截和文件路径重定向能力。通过与PluginManager的紧密集成，实现了统一、可扩展的输出流处理机制。

本文档详细介绍了OutputStreamInterceptor的架构设计、功能实现、使用方式以及最佳实践，为开发者提供了完整的输出流拦截开发指南。通过合理使用OutputStreamInterceptor，可以实现灵活的文件输出处理和插件功能扩展。