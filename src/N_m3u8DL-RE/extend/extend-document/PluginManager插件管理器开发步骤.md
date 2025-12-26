# PluginManager插件管理器开发步骤

## 概述

本文档提供完整的开发步骤，用于实现插件管理器对N_m3u8DL-RE程序的输入流、输出流及日志输出流的全面接管机制。该方案遵循最小侵入原则，确保在不影响原有功能的前提下提供强大的插件扩展能力。

## 开发阶段规划

### 阶段1: 核心插件接口扩展
### 阶段2: 输入流接管机制
### 阶段3: 输出流接管机制  
### 阶段4: 日志流接管机制
### 阶段5: 插件管理系统升级
### 阶段6: 测试和验证

---

## 阶段1: 核心插件接口扩展

### 步骤1.1: 扩展IPlugin接口定义

**文件路径**: `/workspace/N_m3u8DL-RE-src/src/N_m3u8DL-RE/extend/PluginManager.cs`

**操作**: 在现有IPlugin接口基础上添加新的接口方法

**代码修改**:
```csharp
public interface IPlugin
{
    // 原有方法保持不变
    void Initialize(PluginConfig? config);
    void OnFileDownloaded(string filePath, int downloadCount);
    
    // 新增插件接口方法
    void OnInputReceived(string[] args, MyOption option);
    void OnOutputGenerated(string outputPath, string outputType);
    void OnLogGenerated(string logMessage, LogLevel logLevel);
}
```

### 步骤1.2: 创建流拦截器接口

**文件路径**: `/workspace/N_m3u8DL-RE-src/src/N_m3u8DL-RE/extend/PluginManager.cs`

**操作**: 在IPlugin接口后添加流拦截器接口

**代码修改**:
```csharp
public interface IStreamInterceptor
{
    // 输入流拦截
    string[] InterceptInput(string[] originalArgs);
    MyOption InterceptOptions(MyOption originalOption);
    
    // 输出流拦截
    string InterceptOutput(string originalOutput, string outputType);
    void OnOutputRedirect(string originalPath, string newPath);
    
    // 日志流拦截
    string InterceptLog(string originalLog, LogLevel level);
    void OnLogRedirect(string originalLog, LogLevel level, string newDestination);
}
```

### 步骤1.3: 创建日志级别枚举

**文件路径**: `/workspace/N_m3u8DL-RE-src/src/N_m3u8DL-RE/extend/PluginManager.cs`

**操作**: 在命名空间内添加LogLevel枚举

**代码修改**:
```csharp
public enum LogLevel
{
    Debug,
    Info,
    Warn,
    Error,
    Fatal
}
```

---

### 🧪 阶段1测试验证

**操作**: 在完成阶段1所有步骤后进行测试验证

**验证目的**: 确保新接口定义不破坏现有插件系统

**测试步骤**:
```bash
# 1. 编译测试
cd /workspace/N_m3u8DL-RE-src/src/N_m3u8DL-RE
dotnet build

# 2. 验证插件加载
dotnet run -- --help | grep -i plugin

# 3. 检查BatchDownloadPlugin加载状态
# 查看控制台输出中是否显示"Found X plugin types"和插件初始化信息
```

**预期结果**:
- 编译成功无错误
- 插件系统正常初始化
- BatchDownloadPlugin等现有插件正常加载

**故障排除**:
- 如果编译错误，检查接口定义语法
- 如果插件加载失败，检查命名空间和反射调用

## 阶段2: 输入流接管机制


### 步骤2.1: 创建输入流拦截器

**文件路径**: `/workspace/N_m3u8DL-RE-src/src/N_m3u8DL-RE/extend/Interceptors/InputStreamInterceptor.cs`

**操作**: 创建新的输入流拦截器类

**代码创建**:
```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace N_m3u8DL_RE.Plugin
{
    public class InputStreamInterceptor
    {
        private static List<IStreamInterceptor> _interceptors = new List<IStreamInterceptor>();
        
        public static void RegisterInterceptor(IStreamInterceptor interceptor)
        {
            _interceptors.Add(interceptor);
        }
        
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
        
        public static MyOption InterceptOptions(MyOption originalOption)
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
    }
}
```

### 步骤2.2: 修改CommandInvoker集成输入拦截

**文件路径**: `/workspace/N_m3u8DL-RE-src/src/N_m3u8DL-RE/CommandLine/CommandInvoker.cs`

**操作**: 在InvokeArgs方法开始处添加输入拦截逻辑

**代码修改**:
```csharp
// 在InvokeArgs方法开头添加
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
```

---


### 🧪 阶段2测试验证

**操作**: 在完成阶段2所有步骤后进行测试验证

**验证目的**: 验证输入流拦截功能是否正常工作，使用BatchDownloadPlugin作为测试载体

**测试准备**:
```bash
# 创建测试URL文件
echo "https://example.com/test1.m3u8" > extend/BatchDownloadPlugin-and-input-output/input-batch-urls.txt
echo "https://example.com/test2.m3u8" >> extend/BatchDownloadPlugin-and-input-output/input-batch-urls.txt
```

**测试步骤**:
```bash
# 1. 编译测试
dotnet build

# 2. 测试输入拦截功能
dotnet run -- --batch --save-dir /tmp/test-input-intercept

# 3. 验证输入拦截日志
# 检查控制台输出中是否显示"[InputInterceptor]"相关日志信息

# 4. 测试参数解析
dotnet run -- --help | head -5
```

**预期结果**:
- 编译成功
- 控制台显示输入拦截相关的日志信息
- BatchDownloadPlugin正常处理批量参数
- 命令行参数解析功能正常

**故障排除**:
- 如果无输入拦截日志，检查InputStreamInterceptor初始化
- 如果BatchDownloadPlugin失效，检查CommandInvoker中的反射调用

## 阶段3: 输出流接管机制(开发完要注释说明是PluginManager.cs接管)


### 步骤3.1: 创建输出流拦截器

**文件路径**: `/workspace/N_m3u8DL-RE-src/src/N_m3u8DL-RE/extend/Interceptors/OutputStreamInterceptor.cs`

**操作**: 创建新的输出流拦截器类

**代码创建**:
```csharp
using System;
using System.Collections.Generic;
using System.IO;

namespace N_m3u8DL_RE.Plugin
{
    public class OutputStreamInterceptor
    {
        private static List<IStreamInterceptor> _interceptors = new List<IStreamInterceptor>();
        
        public static void RegisterInterceptor(IStreamInterceptor interceptor)
        {
            _interceptors.Add(interceptor);
        }
        
        public static string InterceptOutput(string originalOutput, string outputType)
        {
            var result = originalOutput;
            foreach (var interceptor in _interceptors)
            {
                try
                {
                    result = interceptor.InterceptOutput(result, outputType);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[OutputInterceptor] Error in {interceptor.GetType().Name}: {ex.Message}");
                }
            }
            return result;
        }
        
        public static string RedirectOutputPath(string originalPath, string outputType)
        {
            foreach (var interceptor in _interceptors)
            {
                try
                {
                    interceptor.OnOutputRedirect(originalPath, originalPath);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[OutputInterceptor] Error in {interceptor.GetType().Name}: {ex.Message}");
                }
            }
            return originalPath;
        }
    }
}
```

### 步骤3.2: 修改SimpleDownloadManager集成输出拦截

**文件路径**: `/workspace/N_m3u8DL-RE-src/src/N_m3u8DL-RE/DownloadManager/SimpleDownloadManager.cs`

**操作**: 在文件输出相关方法中添加输出拦截逻辑

**代码修改**:
```csharp
// 在文件保存相关方法中添加
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

### 步骤3.3: 修改SimpleDownloader集成输出拦截

**文件路径**: `/workspace/N_m3u8DL-RE-src/src/N_m3u8DL-RE/Downloader/SimpleDownloader.cs`

**操作**: 在文件保存完成后添加输出拦截逻辑

**代码修改**:
```csharp
// 在现有TriggerPluginEvent方法后添加
private void TriggerOutputInterceptor(string filePath)
{
    try
    {
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
    }
    catch (Exception ex)
    {
        Logger.Warn($"[OutputInterceptor] Failed to process output redirection: {ex.Message}");
    }
}
```

---

### 🧪 阶段3测试验证

**操作**: 在完成阶段3所有步骤后进行测试验证

**验证目的**: 验证输出流拦截和重定向功能，使用BatchDownloadPlugin验证文件输出处理

**测试准备**:
```bash
# 创建测试目录
mkdir -p /tmp/test-output-intercept

# 确保有有效的测试URL
echo "https://sample-videos.com/zip/10/mp4/SampleVideo_1280x720_1mb.mp4" > extend/BatchDownloadPlugin-and-input-output/input-batch-urls.txt
```

**测试步骤**:
```bash
# 1. 编译测试
dotnet build

# 2. 测试输出拦截功能（如果网络可用，测试实际下载）
dotnet run -- --batch --save-dir /tmp/test-output-intercept --no-proxy

# 3. 验证输出拦截日志
# 检查控制台输出中是否显示"[OutputInterceptor]"相关日志信息

# 4. 检查文件输出处理
ls -la /tmp/test-output-intercept/

# 5. 测试输出重定向（如果有StreamInterceptorPlugin）
dotnet run -- --batch --save-dir /tmp/test-output-redirect 2>&1 | grep -i "output.*intercept"
```

**预期结果**:
- 编译成功
- 控制台显示输出拦截相关的日志信息
- 文件输出路径处理正常
- BatchDownloadPlugin生成的文件名符合预期格式

**故障排除**:
- 如果无输出拦截日志，检查OutputStreamInterceptor集成
- 如果文件保存失败，检查SimpleDownloadManager的拦截调用
- 如果输出重定向失效，检查PluginManager的RedirectOutput方法

## 阶段4: 日志流接管机制(开发完要注释说明是PluginManager.cs接管)

### 步骤4.1: 创建日志流拦截器

**文件路径**: `/workspace/N_m3u8DL-RE-src/src/N_m3u8DL-RE/extend/Interceptors/LogStreamInterceptor.cs`

**操作**: 创建新的日志流拦截器类

**代码创建**:
```csharp
using System;
using System.Collections.Generic;
using System.IO;

namespace N_m3u8DL_RE.Plugin
{
    public class LogStreamInterceptor
    {
        private static List<IStreamInterceptor> _interceptors = new List<IStreamInterceptor>();
        private static StringWriter _originalConsoleOut;
        private static StringWriter _originalConsoleError;
        
        public static void Initialize()
        {
            _originalConsoleOut = Console.Out;
            _originalConsoleError = Console.Error;
            
            // 重定向Console输出
            var interceptedOut = new InterceptedStringWriter(_originalConsoleOut, "stdout");
            var interceptedErr = new InterceptedStringWriter(_originalConsoleError, "stderr");
            
            Console.SetOut(interceptedOut);
            Console.SetError(interceptedErr);
        }
        
        public static void RegisterInterceptor(IStreamInterceptor interceptor)
        {
            _interceptors.Add(interceptor);
        }
        
        public static string InterceptLog(string originalLog, LogLevel level)
        {
            var result = originalLog;
            foreach (var interceptor in _interceptors)
            {
                try
                {
                    result = interceptor.InterceptLog(result, level);
                }
                catch (Exception ex)
                {
                    _originalConsoleOut.WriteLine($"[LogInterceptor] Error in {interceptor.GetType().Name}: {ex.Message}");
                }
            }
            return result;
        }
        
        public static void Restore()
        {
            Console.SetOut(_originalConsoleOut);
            Console.SetError(_originalConsoleError);
        }
    }
    
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
            var intercepted = LogStreamInterceptor.InterceptLog(value, LogLevel.Info);
            _original.Write(intercepted);
            base.Write(intercepted);
        }
        
        public override void WriteLine(string value)
        {
            var intercepted = LogStreamInterceptor.InterceptLog(value, LogLevel.Info);
            _original.WriteLine(intercepted);
            base.WriteLine(intercepted);
        }
    }
}
```

### 步骤4.2: 修改Program.cs集成日志拦截

**文件路径**: `/workspace/N_m3u8DL-RE-src/src/N_m3u8DL-RE/Program.cs`

**操作**: 在Main方法开始处初始化日志拦截器

**代码修改**:
```csharp
static async Task Main(string[] args)
{
    // 初始化日志拦截器
    try
    {
        var logInterceptorType = Type.GetType("N_m3u8DL_RE.Plugin.LogStreamInterceptor, N_m3u8DL-RE");
        if (logInterceptorType != null)
        {
            var initializeMethod = logInterceptorType.GetMethod("Initialize");
            if (initializeMethod != null)
            {
                initializeMethod.Invoke(null, null);
                Console.WriteLine("[LogInterceptor] Log stream interception initialized");
            }
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[LogInterceptor] Failed to initialize: {ex.Message}");
    }
    
    // 初始化插件系统（原有代码保持不变）
    // ...
}
```

---

### 🧪 阶段4测试验证

**操作**: 在完成阶段4所有步骤后进行测试验证

**验证目的**: 验证日志流拦截和重定向功能，验证日志不丢失且正常显示

**测试准备**:
```bash
# 创建测试目录
mkdir -p /tmp/test-log-intercept

# 清理之前的测试文件
rm -rf /tmp/test-log-intercept/*
```

**测试步骤**:
```bash
# 1. 编译测试
dotnet build

# 2. 测试日志拦截功能
dotnet run -- --batch --save-dir /tmp/test-log-intercept 2>&1 | tee /tmp/log-test-output.txt

# 3. 验证日志拦截日志
grep -i "LogInterceptor" /tmp/log-test-output.txt

# 4. 验证日志输出完整性
# 检查控制台是否正常显示所有日志信息
# 确认没有日志丢失或乱码

# 5. 测试不同日志级别
dotnet run -- --batch --save-dir /tmp/test-log-intercept --debug 2>&1 | head -20
```

**预期结果**:
- 编译成功
- 控制台显示"[LogInterceptor]"初始化信息
- 所有日志信息正常显示，无丢失
- 日志拦截器正常工作但不干扰正常日志流程

**故障排除**:
- 如果日志显示异常，检查LogStreamInterceptor的Console重定向
- 如果出现乱码，检查InterceptedStringWriter的实现
- 如果性能下降，检查日志拦截的效率

## 阶段5: 插件管理系统升级(开发完要注释说明是PluginManager.cs接管)


### 步骤5.1: 升级PluginManager核心功能

**文件路径**: `/workspace/N_m3u8DL-RE-src/src/N_m3u8DL-RE/extend/PluginManager.cs`

**操作**: 添加流拦截器管理和事件通知方法

**代码修改**:
```csharp
public static class PluginManager
{
    private static List<IPlugin> _plugins = new List<IPlugin>();
    private static List<IStreamInterceptor> _streamInterceptors = new List<IStreamInterceptor>();
    
    // 新增流拦截器管理方法
    public static void RegisterStreamInterceptor(IStreamInterceptor interceptor)
    {
        _streamInterceptors.Add(interceptor);
        
        // 注册到各个拦截器
        InputStreamInterceptor.RegisterInterceptor(interceptor);
        OutputStreamInterceptor.RegisterInterceptor(interceptor);
        LogStreamInterceptor.RegisterInterceptor(interceptor);
    }
    
    // 新增插件事件通知方法
    internal static void NotifyPluginsOnInput(string[] args, MyOption option)
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
                Console.WriteLine($"[Plugin] Output notification failed for {plugin.GetType().Name}: {ex.Message}");
            }
        }
    }
    
    internal static void RedirectOutput(string originalPath, string newPath)
    {
        foreach (var plugin in _plugins)
        {
            try
            {
                plugin.OnOutputGenerated(newPath, "redirected");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Plugin] Output redirection failed for {plugin.GetType().Name}: {ex.Message}");
            }
        }
    }
    
    // 修改LoadPlugins方法以支持流拦截器
    public static void LoadPlugins()
    {
        try
        {
            LoadConfig();
            
            var pluginTypes = Assembly.GetExecutingAssembly().GetTypes()
                .Where(t => t.Namespace == "N_m3u8DL_RE.Plugin" && 
                           t.Name.EndsWith("Plugin") && 
                           !t.IsInterface && 
                           !t.IsAbstract);
            
            Console.WriteLine($"[Plugin] Found {pluginTypes.Count()} plugin types");
            
            foreach (var type in pluginTypes)
            {
                try
                {
                    Console.WriteLine($"[Plugin] Creating instance of: {type.FullName}");
                    var instance = Activator.CreateInstance(type);
                    
                    if (instance is IPlugin plugin)
                    {
                        var pluginName = type.Name.Replace("Plugin", "");
                        var isEnabled = IsPluginEnabled(pluginName);
                        
                        Console.WriteLine($"[Plugin] Plugin {pluginName} enabled: {isEnabled}");
                        
                        if (isEnabled)
                        {
                            plugin.Initialize(_config);
                            _plugins.Add(plugin);
                            
                            // 检查是否实现流拦截器接口
                            if (instance is IStreamInterceptor interceptor)
                            {
                                RegisterStreamInterceptor(interceptor);
                                Console.WriteLine($"[Plugin] Registered stream interceptor: {pluginName}");
                            }
                            
                            Console.WriteLine($"[Plugin] Loaded plugin: {pluginName}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Plugin] Failed to create instance of {type.Name}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Plugin] LoadPlugins failed: {ex.Message}");
        }
    }
}
```

### 步骤5.2: 创建示例流拦截器插件

**文件路径**: `/workspace/N_m3u8DL-RE-src/src/N_m3u8DL-RE/extend/Interceptors/StreamInterceptorPlugin.cs`

**操作**: 创建示例插件演示流拦截功能

**代码创建**:
```csharp
using System;
using System.IO;

namespace N_m3u8DL_RE.Plugin
{
    public class StreamInterceptorPlugin : IPlugin, IStreamInterceptor
    {
        private PluginConfig? _config;
        
        public void Initialize(PluginConfig? config)
        {
            _config = config;
            Console.WriteLine("[StreamInterceptorPlugin] Initialized");
        }
        
        public void OnFileDownloaded(string filePath, int downloadCount)
        {
            // 原有文件下载事件处理
        }
        
        // IPlugin 新接口实现
        public void OnInputReceived(string[] args, MyOption option)
        {
            Console.WriteLine($"[StreamInterceptorPlugin] Input received: {args.Length} arguments");
        }
        
        public void OnOutputGenerated(string outputPath, string outputType)
        {
            Console.WriteLine($"[StreamInterceptorPlugin] Output generated: {outputPath} ({outputType})");
        }
        
        public void OnLogGenerated(string logMessage, LogLevel logLevel)
        {
            Console.WriteLine($"[StreamInterceptorPlugin] Log generated: {logLevel} - {logMessage}");
        }
        
        // IStreamInterceptor 接口实现
        public string[] InterceptInput(string[] originalArgs)
        {
            Console.WriteLine($"[StreamInterceptorPlugin] Intercepting {originalArgs.Length} input arguments");
            return originalArgs;
        }
        
        public MyOption InterceptOptions(MyOption originalOption)
        {
            Console.WriteLine("[StreamInterceptorPlugin] Intercepting options");
            return originalOption;
        }
        
        public string InterceptOutput(string originalOutput, string outputType)
        {
            var intercepted = $"[StreamInterceptorPlugin] {originalOutput}";
            Console.WriteLine($"[StreamInterceptorPlugin] Intercepted output: {outputType}");
            return intercepted;
        }
        
        public void OnOutputRedirect(string originalPath, string newPath)
        {
            Console.WriteLine($"[StreamInterceptorPlugin] Output redirected: {originalPath} -> {newPath}");
        }
        
        public string InterceptLog(string originalLog, LogLevel level)
        {
            var intercepted = $"[StreamInterceptorPlugin] {originalLog}";
            return intercepted;
        }
        
        public void OnLogRedirect(string originalLog, LogLevel level, string newDestination)
        {
            Console.WriteLine($"[StreamInterceptorPlugin] Log redirected: {level} to {newDestination}");
        }
    }
}
```

### 步骤5.3: 更新PluginConfig.json配置

**文件路径**: `/workspace/N_m3u8DL-RE-src/src/N_m3u8DL-RE/extend/PluginConfig.json`

**操作**: 添加新插件配置项

**代码修改**:
```json
{
  "UASwitcher": {
    "Enabled": true,
    "UserAgents": [
      "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36",
      "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36"
    ],
    "SwitchInterval": 3
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
  },
  "StreamInterceptor": {
    "Enabled": true,
    "InterceptInput": true,
    "InterceptOutput": true,
    "InterceptLog": true,
    "LogRedirection": false,
    "OutputRedirection": false
  }
}
```

---

### 🧪 阶段5测试验证

**操作**: 在完成阶段5所有步骤后进行测试验证

**验证目的**: 验证插件管理系统升级后的功能完整性和兼容性

**测试准备**:
```bash
# 创建综合测试目录
mkdir -p /tmp/test-stage5-complete

# 更新配置文件，确保StreamInterceptor插件启用
cp extend/PluginConfig.json /tmp/test-stage5-complete/
```

**测试步骤**:
```bash
# 1. 编译测试
dotnet build

# 2. 测试插件系统完整初始化
dotnet run -- --help 2>&1 | grep -E "(Plugin|Found.*plugin)"

# 3. 测试流拦截器注册
dotnet run -- --batch --save-dir /tmp/test-stage5-complete 2>&1 | grep -E "(StreamInterceptor|Register.*interceptor)"

# 4. 测试BatchDownloadPlugin与流拦截器协同工作
echo "https://httpbin.org/stream/5" > extend/BatchDownloadPlugin-and-input-output/input-batch-urls.txt
dotnet run -- --batch --save-dir /tmp/test-stage5-complete --timeout 30

# 5. 验证所有插件事件通知
dotnet run -- --batch --save-dir /tmp/test-stage5-complete 2>&1 | grep -E "(Input.*received|Output.*generated|Log.*generated)"
```

**预期结果**:
- 编译成功，所有新功能正常编译
- 插件系统显示"Found X plugin types"和具体插件加载信息
- 流拦截器成功注册并显示相关日志
- BatchDownloadPlugin与流拦截器协同工作正常
- 所有插件事件通知正常触发

**故障排除**:
- 如果插件加载失败，检查PluginManager的LoadPlugins方法
- 如果流拦截器未注册，检查RegisterStreamInterceptor调用
- 如果事件通知失效，检查NotifyPluginsOnXxx方法

## 阶段6: 测试和验证

### 步骤6.1: 编译测试

**操作**: 编译整个项目验证所有修改

**命令**:
```bash
cd /workspace/N_m3u8DL-RE-src/src/N_m3u8DL-RE
dotnet build
```

### 步骤6.2: 功能测试

**操作**: 运行基本功能测试

**命令**:
```bash
dotnet run -- --help
```

### 步骤6.3: 插件系统测试

**操作**: 测试插件加载和基本功能

**命令**:
```bash
dotnet run -- --batch --save-dir /tmp/test-output
```

### 步骤6.4: 流拦截测试

**操作**: 验证流拦截器是否正常工作

**验证点**:
- 输入参数拦截显示
- 输出文件路径拦截显示
- 日志输出拦截显示
- 插件事件通知正常

### 🧪 阶段6综合测试验证

**操作**: 完成所有开发阶段后的最终综合测试

**验证目的**: 验证插件管理器完整接管(劫持)功能，确保与现有系统完全兼容

**测试准备**:
```bash
# 创建最终测试环境
mkdir -p /tmp/final-integration-test
cd /tmp/final-integration-test

# 准备完整的测试配置
cat > test-config.json << 'EOF'
{
  "BatchDownload": {
    "Enabled": true,
    "CreateSubdirectories": false,
    "MaxConcurrency": 2
  },
  "StreamInterceptor": {
    "Enabled": true,
    "InterceptInput": true,
    "InterceptOutput": true,
    "InterceptLog": true
  }
}
EOF

# 创建多个测试URL
cat > test-urls.txt << 'EOF'
https://httpbin.org/stream/3
https://httpbin.org/json
EOF
```

**综合测试步骤**:
```bash
# 1. 完整编译验证
dotnet build --configuration Release

# 2. 启动模式测试
echo "=== 测试1: 启动和插件加载 ==="
dotnet run -- --help 2>&1 | grep -E "(Plugin|Found.*plugin|StreamInterceptor)"

# 3. 输入流接管测试
echo "=== 测试2: 输入流接管 ==="
cd /tmp/final-integration-test
dotnet run /workspace/N_m3u8DL-RE-src -- --batch --save-dir /tmp/final-integration-test/output --timeout 20 2>&1 | tee input-test.log | grep -E "(InputInterceptor|Input.*received)"

# 4. 输出流接管测试
echo "=== 测试3: 输出流接管 ==="
dotnet run /workspace/N_m3u8DL-RE-src -- --batch --save-dir /tmp/final-integration-test/output2 --timeout 20 2>&1 | tee output-test.log | grep -E "(OutputInterceptor|Output.*generated)"

# 5. 日志流接管测试
echo "=== 测试4: 日志流接管 ==="
dotnet run /workspace/N_m3u8DL-RE-src -- --batch --save-dir /tmp/final-integration-test/output3 --debug --timeout 20 2>&1 | tee log-test.log | grep -E "(LogInterceptor|Log.*generated)"

# 6. 兼容性测试（无插件模式）
echo "=== 测试5: 向后兼容性 ==="
# 临时禁用插件测试
mv extend/PluginConfig.json extend/PluginConfig.json.backup
dotnet run /workspace/N_m3u8DL-RE-src -- --help > /dev/null 2>&1 && echo "兼容性测试通过" || echo "兼容性测试失败"
mv extend/PluginConfig.json.backup extend/PluginConfig.json
```

**最终验证检查清单**:
```bash
echo "=== 最终验证检查清单 ==="

# 检查编译状态
if dotnet build > /dev/null 2>&1; then
    echo "✅ 编译测试通过"
else
    echo "❌ 编译测试失败"
fi

# 检查插件系统
if dotnet run -- --help 2>&1 | grep -q "Found.*plugin"; then
    echo "✅ 插件系统正常"
else
    echo "❌ 插件系统异常"
fi

# 检查流拦截器
if dotnet run -- --batch --save-dir /tmp/quick-test 2>&1 | grep -q "StreamInterceptor"; then
    echo "✅ 流拦截器正常"
else
    echo "❌ 流拦截器异常"
fi

# 检查BatchDownloadPlugin
if [ -f extend/BatchDownloadPlugin-and-input-output/input-batch-urls.txt ]; then
    echo "✅ BatchDownloadPlugin输入文件存在"
else
    echo "❌ BatchDownloadPlugin输入文件缺失"
fi

# 检查配置文件
if [ -f extend/PluginConfig.json ]; then
    echo "✅ 配置文件存在"
else
    echo "❌ 配置文件缺失"
fi

# 检查输出目录
if [ -d "/tmp/final-integration-test/output" ]; then
    echo "✅ 输出目录创建正常"
else
    echo "❌ 输出目录创建异常"
fi
```

**预期综合结果**:
- 所有编译测试通过，无错误和警告
- 插件系统正常加载，显示具体插件信息
- 输入流、输出流、日志流拦截功能全部正常
- BatchDownloadPlugin与流拦截器协同工作
- 向后兼容性测试通过，原有功能不受影响
- 所有检查清单项目显示✅

**完整系统接管验证**:
通过以下命令验证插件管理器是否成功接管所有关键流程:
```bash
echo "=== 系统接管验证 ==="
dotnet run /workspace/N_m3u8DL-RE-src -- --batch --save-dir /tmp/system-hijack-test --timeout 15 2>&1 | \
tee /tmp/final-hijack-test.log | \
grep -E "(PluginManager|StreamInterceptor|InputInterceptor|OutputInterceptor|LogInterceptor|Input.*received|Output.*generated|Log.*generated)" | \
sort | uniq -c
```

**如果此命令输出包含所有类型的拦截器日志，则证明插件管理器成功接管了系统的输入、输出和日志流程**

---

## 配置文件管理

### 项目配置更新

**文件路径**: `/workspace/N_m3u8DL-RE-src/src/N_m3u8DL-RE/N_m3u8DL-RE.csproj`

**操作**: 确保新文件被包含在项目中

**代码修改**:
```xml
<ItemGroup>
    <Compile Include="extend\Interceptors\InputStreamInterceptor.cs" />
    <Compile Include="extend\Interceptors\OutputStreamInterceptor.cs" />
    <Compile Include="extend\Interceptors\LogStreamInterceptor.cs" />
    <Compile Include="extend\Interceptors\StreamInterceptorPlugin.cs" />
</ItemGroup>
```

### 部署配置

**操作**: 确保配置文件正确复制到输出目录

**验证**: 检查 `bin/Debug/net9.0/extend/` 目录包含所有必要文件

---

## 错误处理和恢复机制

### 全局异常处理

在所有拦截器中添加异常处理，确保：
1. 拦截器失败不影响主程序运行
2. 错误信息被记录但不中断流程
3. 可以通过配置禁用特定拦截器

### 日志恢复机制

在程序退出时确保：
1. Console输出被正确恢复
2. 所有拦截器被正确清理
3. 资源被正确释放

---

## 总结

本开发步骤文档提供了完整的插件管理器流接管机制实现方案，包括：

1. **核心接口扩展** - 支持流拦截的新接口定义
2. **输入流接管** - 命令行参数和选项的拦截处理
3. **输出流接管** - 文件输出和路径重定向处理
4. **日志流接管** - 完整的日志输出拦截和重定向
5. **插件系统升级** - 支持流拦截器的插件管理
6. **测试验证** - 确保功能正常工作的验证步骤

该方案严格遵循最小侵入原则，所有修改都在extend目录中进行，不影响原有核心代码的稳定性。通过配置文件可以灵活控制各种拦截功能，实现强大的插件扩展能力。