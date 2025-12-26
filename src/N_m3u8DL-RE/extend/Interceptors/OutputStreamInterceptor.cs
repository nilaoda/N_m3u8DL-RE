using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace N_m3u8DL_RE.Plugin
{
    // 【输出流拦截】由PluginManager.cs统一管理输出流拦截功能
    // 该类处理输出流拦截和重定向逻辑，确保输出不丢失且可被插件拦截
    
    public class OutputStreamInterceptor
    {
        private static List<IStreamInterceptor> _interceptors = new List<IStreamInterceptor>();
        private static StringWriter? _originalOutput;
        private static bool _isInitialized = false;
        
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
                    _originalOutput?.WriteLine($"[OutputInterceptor] 拦截器 {interceptor.GetType().Name} 输出重定向处理错误: {ex.Message}");
                }
            }
        }
        
        /// <summary>
        /// 恢复原始输出流
        /// 【输出流拦截】由PluginManager.cs统一调用此方法进行清理
        /// </summary>
        public static void Restore()
        {
            try
            {
                if (_originalOutput != null)
                    Console.SetOut(_originalOutput);
                    
                _isInitialized = false;
                Console.WriteLine("[OutputInterceptor] 输出流拦截器已恢复");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[OutputInterceptor] 恢复失败: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 获取拦截器列表（用于调试）
        /// 【输出流拦截】由PluginManager.cs统一管理拦截器状态查询
        /// </summary>
        public static List<IStreamInterceptor> GetInterceptors()
        {
            return new List<IStreamInterceptor>(_interceptors);
        }
    }
}