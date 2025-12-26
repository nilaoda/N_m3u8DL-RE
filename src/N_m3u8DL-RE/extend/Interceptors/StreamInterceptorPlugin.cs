using System;
using System.IO;

namespace N_m3u8DL_RE.Plugin
{
    // 【流拦截器插件】由PluginManager.cs统一管理此插件的注册和调用
    // 该插件演示如何实现IStreamInterceptor接口以进行流拦截操作
    
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
            Console.WriteLine($"[StreamInterceptorPlugin] File downloaded: {filePath} (count: {downloadCount})");
        }
        
        // IPlugin 新接口实现
        public void OnInputReceived(object args, object option)
        {
            Console.WriteLine($"[StreamInterceptorPlugin] Input received");
        }
        
        public void OnOutputGenerated(string outputPath, string outputType)
        {
            Console.WriteLine($"[StreamInterceptorPlugin] Output generated: {outputPath} (type: {outputType})");
        }
        
        public void OnLogGenerated(string logMessage, PluginLogLevel logLevel)
        {
            Console.WriteLine($"[StreamInterceptorPlugin] Log generated: [{logLevel}] {logMessage}");
        }
        
        // IStreamInterceptor 接口实现
        
        // 输入流拦截
        public string[] InterceptInput(string[] originalArgs)
        {
            Console.WriteLine($"[StreamInterceptorPlugin] Intercepting input: {originalArgs.Length} arguments");
            return originalArgs; // 原样返回，实际实现中可以修改参数
        }
        
        public object InterceptOptions(object originalOption)
        {
            Console.WriteLine("[StreamInterceptorPlugin] Intercepting options");
            return originalOption; // 原样返回，实际实现中可以修改选项
        }
        
        // 输出流拦截
        public string InterceptOutput(string originalOutput, string outputType)
        {
            Console.WriteLine($"[StreamInterceptorPlugin] Intercepting output: {outputType}");
            return originalOutput; // 原样返回，实际实现中可以修改输出
        }
        
        public void OnOutputRedirect(string originalPath, string newPath)
        {
            Console.WriteLine($"[StreamInterceptorPlugin] Output redirected from {originalPath} to {newPath}");
        }
        
        // 日志流拦截
        public string InterceptLog(string originalLog, PluginLogLevel level)
        {
            // 使用Debug输出避免触发Console输出拦截
            System.Diagnostics.Debug.WriteLine($"[StreamInterceptorPlugin] Intercepting log: {level}");
            return originalLog; // 原样返回，实际实现中可以修改日志
        }
        
        public void OnLogRedirect(string originalLog, PluginLogLevel level, string newDestination)
        {
            Console.WriteLine($"[StreamInterceptorPlugin] Log redirected to {newDestination}: {level}");
        }
    }
}