# InputStreamInterceptor.cs输入流接管插件开发文档

## 概述

InputStreamInterceptor是N_m3u8DL-RE插件系统中的输入流接管组件，负责拦截和处理命令行输入流，包括命令行参数和选项对象。本文档详细介绍了InputStreamInterceptor的架构设计、核心功能、使用方式以及与PluginManager的集成关系。

## 核心架构设计

### 1. 整体架构

InputStreamInterceptor采用静态类设计，作为输入流接管的实现层，与PluginManager形成统一的拦截管理体系：

```csharp
/// <summary>
/// 输入流拦截器类
/// 
/// 【接管说明】此类的功能由 PluginManager.cs 接管和调用：
/// - 在 CommandInvoker.cs 的 InvokeArgs 方法开始处被调用
/// - 插件系统通过 PluginManager.cs 统一管理和调用此拦截器
/// - 插件的输入拦截功能由 PluginManager.NotifyPluginsOnInput() 方法协调
/// 
/// 设计目的：
/// 1. 提供参数和选项的拦截机制
/// 2. 支持插件对输入流进行处理和修改
/// 3. 为批量下载等高级插件功能提供输入流控制
/// </summary>
public class InputStreamInterceptor
{
    // 静态拦截器列表 - 用于存储所有注册的拦截器
    private static List<IStreamInterceptor> _interceptors = new List<IStreamInterceptor>();
}
```

### 2. 架构特点

- **静态单例模式**：所有拦截器共享同一个实例，保证拦截链的一致性
- **链式拦截机制**：支持多个拦截器的依次调用，形成拦截链
- **PluginManager托管**：由PluginManager统一管理拦截器的注册和调用
- **非侵入性设计**：不修改主程序逻辑，通过反射机制集成

## 核心功能模块

### 1. 拦截器注册机制

#### 注册方法实现

```csharp
/// <summary>
/// 注册拦截器
/// 【接管说明】此方法由插件系统调用，插件管理器通过PluginManager.cs统一管理
/// 
/// </summary>
/// <param name="interceptor">要注册的拦截器实例</param>
public static void RegisterInterceptor(IStreamInterceptor interceptor)
{
    _interceptors.Add(interceptor);
}
```

#### 注册流程

1. **插件初始化**：插件实现IStreamInterceptor接口
2. **统一注册**：PluginManager.RegisterStreamInterceptor()方法统一注册
3. **三个拦截器注册**：InputStreamInterceptor.RegisterInterceptor()、OutputStreamInterceptor.RegisterInterceptor()、LogStreamInterceptor.RegisterInterceptor()
4. **拦截链构建**：所有拦截器按注册顺序形成拦截链

### 2. 参数拦截机制

#### 参数拦截实现

```csharp
/// <summary>
/// 拦截命令行参数
/// 【接管说明】此方法由 PluginManager.cs 在 CommandInvoker.cs 中统一调用
/// 
/// 工作流程：
/// 1. PluginManager.NotifyPluginsOnInput() 触发输入事件
/// 2. 各插件的 OnInputReceived() 方法会被调用
/// 3. 插件可以通过 IStreamInterceptor 接口处理参数
/// </summary>
/// <param name="originalArgs">原始命令行参数数组</param>
/// <returns>经过拦截器处理后的参数数组</returns>
public static string[] InterceptArgs(string[] originalArgs)
{
    var result = originalArgs;
    foreach (var interceptor in _interceptors)
    {
        try
        {
            result = interceptor.InterceptInput(result);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[InputInterceptor] Error in {interceptor.GetType().Name}: {ex.Message}");
        }
    }
    return result;
}
```

#### 拦截流程

1. **输入接收**：接收原始命令行参数数组
2. **链式处理**：依次调用所有注册拦截器的InterceptInput方法
3. **参数修改**：拦截器可以修改、添加、删除参数
4. **结果返回**：返回经过所有拦截器处理的参数数组
5. **错误处理**：单个拦截器错误不影响整体流程

### 3. 选项对象拦截机制

#### 选项拦截实现

```csharp
/// <summary>
/// 拦截选项对象
/// 【接管说明】此方法由 PluginManager.cs 在 CommandInvoker.cs 中统一调用
/// 
/// 工作流程：
/// 1. PluginManager.NotifyPluginsOnInput() 触发输入事件
/// 2. 各插件的 OnInputReceived() 方法会被调用
/// 3. 插件可以通过 IStreamInterceptor 接口处理选项对象
/// </summary>
/// <param name="originalOption">原始选项对象</param>
/// <returns>经过拦截器处理后的选项对象</returns>
public static object InterceptOptions(object originalOption)
{
    var result = originalOption;
    foreach (var interceptor in _interceptors)
    {
        try
        {
            result = interceptor.InterceptOptions(result);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[InputInterceptor] Error in {interceptor.GetType().Name}: {ex.Message}");
        }
    }
    return result;
}
```

#### 选项对象处理

1. **对象接收**：接收命令行解析生成的选项对象
2. **类型无关处理**：使用object类型支持任意选项对象
3. **链式修改**：依次调用拦截器的InterceptOptions方法
4. **灵活扩展**：支持复杂选项对象的动态修改

## 与PluginManager的集成关系

### 1. 统一管理架构

InputStreamInterceptor作为输入流接管的实现层，由PluginManager统一管理：

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
/// 通知插件处理输入事件
/// </summary>
internal static void NotifyPluginsOnInput(object args, object option)
{
    foreach (var plugin in _plugins)
    {
        try
        {
            plugin.OnInputReceived(args, option);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Plugin] Input notification failed for {plugin.GetType().Name}: {ex.Message}");
        }
    }
}
```

## 与主程序的集成

### 1. CommandInvoker集成点

在CommandInvoker.cs的InvokeArgs方法中集成输入拦截：

```csharp
public void InvokeArgs(string[] args)
{
    try
    {
        // 输入流拦截
        args = InputStreamInterceptor.InterceptArgs(args);
        
        // 插件输入事件通知
        var pluginManagerType = Type.GetType("N_m3u8DL_RE.Plugin.PluginManager, N_m3u8DL-RE");
        if (pluginManagerType != null)
        {
            var notifyInputMethod = pluginManagerType.GetMethod("NotifyPluginsOnInput", 
                BindingFlags.NonPublic | BindingFlags.Static);
            if (notifyInputMethod != null)
            {
                // 解析参数用于通知
                var rootCommand = new RootCommand(VERSION_INFO);
                var parser = new CommandLineBuilder(rootCommand)
                    .UseDefaults()
                    .Build();
                
                var parseResult = parser.Parse(args);
                if (parseResult.GetValueForOption(Input) is MyOption option)
                {
                    notifyInputMethod.Invoke(null, new object[] { args, option });
                }
            }
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[InputInterceptor] Failed to process input: {ex.Message}");
    }
    
    // 继续原有逻辑...
}
```

### 2. 集成流程

1. **参数接收**：CommandInvoker接收原始命令行参数
2. **输入拦截**：调用InputStreamInterceptor.InterceptArgs进行参数拦截
3. **事件通知**：调用PluginManager.NotifyPluginsOnInput通知所有插件
4. **插件处理**：各插件的OnInputReceived方法被调用
5. **后续处理**：继续原有的参数解析和命令执行逻辑

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

### 2. 输入拦截接口实现

```csharp
public class StreamInterceptorPlugin : IPlugin, IStreamInterceptor
{
    public string[] InterceptInput(string[] originalArgs)
    {
        // 参数拦截逻辑
        var modifiedArgs = new List<string>(originalArgs);
        
        // 示例：添加批处理模式参数
        if (!modifiedArgs.Contains("--batch"))
        {
            modifiedArgs.Add("--batch");
        }
        
        return modifiedArgs.ToArray();
    }
    
    public object InterceptOptions(object originalOption)
    {
        // 选项对象拦截逻辑
        // 可以修改选项对象的属性
        
        return originalOption;
    }
}
```

## 使用场景和示例

### 1. 批处理插件示例

```csharp
public class BatchDownloadPlugin : IPlugin, IStreamInterceptor
{
    public void Initialize(PluginConfig? config)
    {
        // 插件初始化
    }
    
    public string[] InterceptInput(string[] originalArgs)
    {
        // 检查是否包含批量URL文件参数
        var argsList = new List<string>(originalArgs);
        
        if (!argsList.Contains("--batch") && !argsList.Contains("--batch-urls"))
        {
            // 自动添加批处理模式
            argsList.Add("--batch");
        }
        
        return argsList.ToArray();
    }
    
    public object InterceptOptions(object originalOption)
    {
        // 修改选项对象
        var option = originalOption as MyOption;
        if (option != null && !option.BatchMode)
        {
            // 启用批处理模式
            // option.BatchMode = true;
        }
        
        return originalOption;
    }
}
```

### 2. 代理切换插件示例

```csharp
public class ProxySwitcherPlugin : IPlugin, IStreamInterceptor
{
    public string[] InterceptInput(string[] originalArgs)
    {
        // 检查是否需要添加代理参数
        var argsList = new List<string>(originalArgs);
        
        if (ShouldUseProxy())
        {
            // 自动添加代理设置
            if (!argsList.Contains("--proxy"))
            {
                argsList.Add("--proxy");
                argsList.Add(GetCurrentProxy());
            }
        }
        
        return argsList.ToArray();
    }
}
```

## 错误处理机制

### 1. 异常捕获策略

```csharp
public static string[] InterceptArgs(string[] originalArgs)
{
    var result = originalArgs;
    foreach (var interceptor in _interceptors)
    {
        try
        {
            result = interceptor.InterceptInput(result);
        }
        catch (Exception ex)
        {
            // 记录错误但不中断拦截流程
            Console.WriteLine($"[InputInterceptor] Error in {interceptor.GetType().Name}: {ex.Message}");
        }
    }
    return result;
}
```

### 2. 错误处理原则

- **隔离错误**：单个拦截器错误不影响其他拦截器
- **错误记录**：详细记录错误信息便于调试
- **继续处理**：错误后继续执行后续拦截器
- **原始参数保留**：出现错误时保留原始参数

### 3. 常见错误类型

1. **参数格式错误**：拦截器修改参数格式错误
2. **选项对象类型错误**：选项对象类型转换失败
3. **空指针异常**：拦截器访问空对象
4. **权限错误**：访问受限资源或方法

## 性能优化

### 1. 拦截器优化

- **早期退出**：如果拦截器列表为空，直接返回原始参数
- **高效链表**：使用List<T>存储拦截器，支持快速添加和遍历
- **异常隔离**：使用try-catch隔离异常，避免性能损耗

### 2. 内存管理

- **对象复用**：避免在拦截器中创建大量临时对象
- **及时释放**：拦截器处理完成后及时释放临时资源
- **字符串优化**：合理使用字符串操作，避免过多字符串拼接

### 3. 并发安全

- **线程安全**：静态成员访问需要考虑线程安全问题
- **同步机制**：在多线程环境中需要适当的同步机制
- **状态管理**：避免拦截器之间共享可变状态

## 调试和监控

### 1. 调试输出

```csharp
public static void RegisterInterceptor(IStreamInterceptor interceptor)
{
    if (!_interceptors.Contains(interceptor))
    {
        _interceptors.Add(interceptor);
        // 使用Debug输出避免触发Console输出拦截
        System.Diagnostics.Debug.WriteLine($"[InputInterceptor] 已注册拦截器: {interceptor.GetType().Name}");
    }
}
```

### 2. 调试技巧

- **Debug.WriteLine**：使用Debug输出避免触发Console拦截
- **日志记录**：记录拦截器注册和调用信息
- **参数跟踪**：跟踪参数修改过程

### 3. 监控指标

- **拦截器数量**：监控注册的拦截器数量
- **执行时间**：监控拦截处理耗时
- **错误率**：监控拦截器执行错误率

## 最佳实践

### 1. 拦截器开发

- **接口实现**：确保正确实现IStreamInterceptor接口
- **参数验证**：验证输入参数的合法性和完整性
- **异常处理**：正确处理可能出现的异常情况
- **性能考虑**：避免在拦截器中进行耗时操作

### 2. 集成使用

- **PluginManager管理**：通过PluginManager统一管理拦截器
- **注册顺序**：考虑拦截器的注册顺序对结果的影响
- **状态管理**：避免拦截器之间的状态冲突

### 3. 测试验证

- **单元测试**：对每个拦截器进行单元测试
- **集成测试**：测试整个拦截链的功能
- **性能测试**：验证拦截器对性能的影响

## 故障排除

### 1. 常见问题

**拦截器不生效**
- 检查拦截器是否正确注册到PluginManager
- 检查拦截器是否正确实现IStreamInterceptor接口
- 检查调用时机是否正确

**参数修改失效**
- 检查拦截器修改逻辑是否正确
- 检查是否有其他拦截器覆盖了修改
- 检查参数类型和格式是否匹配

**选项对象修改无效**
- 检查选项对象类型转换是否正确
- 检查属性设置是否有效
- 检查对象引用是否正确

### 2. 调试步骤

1. **检查注册**：确认拦截器已正确注册到InputStreamInterceptor
2. **检查调用**：确认InputStreamInterceptor.InterceptArgs被正确调用
3. **检查实现**：检查拦截器的InterceptInput方法实现
4. **检查异常**：查看是否有异常被捕获但未处理

### 3. 修复建议

- **重新注册**：删除并重新注册拦截器
- **清理缓存**：清理可能的缓存或状态
- **重新编译**：重新编译项目确保最新代码生效

## 扩展指南

### 1. 新增拦截功能

1. **实现接口**：在拦截器类中实现IStreamInterceptor接口
2. **添加逻辑**：在InterceptInput方法中添加具体拦截逻辑
3. **注册拦截器**：通过PluginManager.RegisterStreamInterceptor注册
4. **测试验证**：测试新功能是否正常工作

### 2. 复杂拦截场景

**多参数依赖处理**
- 拦截器之间可能存在参数依赖关系
- 需要考虑拦截器的执行顺序
- 可以通过配置文件控制拦截器优先级

**动态参数生成**
- 根据环境或配置动态生成参数
- 支持条件性参数添加
- 提供参数模板机制

## 总结

InputStreamInterceptor作为N_m3u8DL-RE插件系统的输入流接管组件，提供了强大的命令行参数和选项对象拦截能力。通过与PluginManager的紧密集成，实现了统一、可扩展的输入流处理机制。

本文档详细介绍了InputStreamInterceptor的架构设计、功能实现、使用方式以及最佳实践，为开发者提供了完整的输入流拦截开发指南。通过合理使用InputStreamInterceptor，可以实现灵活的命令行参数处理和插件功能扩展。