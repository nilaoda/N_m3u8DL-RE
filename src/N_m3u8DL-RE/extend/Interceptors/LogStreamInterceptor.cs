using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace N_m3u8DL_RE.Plugin
{
    // 【日志拦截】由PluginManager.cs统一管理日志流拦截功能
    // 该类处理Console输出重定向和日志拦截逻辑，确保日志不丢失且可被插件拦截

    public class LogStreamInterceptor
    {
        private static List<IStreamInterceptor> _interceptors = new List<IStreamInterceptor>();
        private static StringWriter? _originalConsoleOut;
        private static StringWriter? _originalConsoleError;
        private static bool _isInitialized = false;
        private static bool _isEnabled = false; // 新增：是否启用的标志
        
        /// <summary>
        /// 初始化日志拦截器，重定向Console输出
        /// 【日志拦截】由PluginManager.cs统一调用此方法进行初始化
        /// </summary>
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
                
                System.Diagnostics.Debug.WriteLine("[LogInterceptor] 日志流拦截器已启用并重定向Console输出");
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
                    _originalConsoleOut?.WriteLine($"[LogInterceptor] 拦截器 {interceptor.GetType().Name} 处理错误: {ex.Message}");
                }
            }
            
            return result;
        }
        
        /// <summary>
        /// 恢复原始Console输出
        /// 【日志拦截】由PluginManager.cs统一调用此方法进行清理
        /// </summary>
        public static void Restore()
        {
            if (!_isEnabled || !_isInitialized)
                return;
                
            try
            {
                if (_originalConsoleOut != null)
                    Console.SetOut(_originalConsoleOut);
                    
                if (_originalConsoleError != null)
                    Console.SetError(_originalConsoleError);
                    
                _isInitialized = false;
                Console.WriteLine("[LogInterceptor] 日志流拦截器已恢复");
                
                System.Diagnostics.Debug.WriteLine("[LogInterceptor] 日志流拦截器已禁用并恢复Console输出");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LogInterceptor] 恢复失败: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 获取拦截器列表（用于调试）
        /// 【日志拦截】由PluginManager.cs统一管理拦截器状态查询
        /// </summary>
        public static List<IStreamInterceptor> GetInterceptors()
        {
            return new List<IStreamInterceptor>(_interceptors);
        }
    }
    
    /// <summary>
    /// 拦截的StringWriter实现
    /// 【日志拦截】由PluginManager.cs统一创建和管理此类的实例
    /// </summary>
    public class InterceptedStringWriter : StringWriter
    {
        private readonly StringWriter _original;
        private readonly string _streamType;
        
        public InterceptedStringWriter(StringWriter original, string streamType)
        {
            _original = original ?? throw new ArgumentNullException(nameof(original));
            _streamType = streamType ?? throw new ArgumentNullException(nameof(streamType));
        }
        
        public override void Write(string? value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                var intercepted = LogStreamInterceptor.InterceptLog(value, PluginLogLevel.Info);
                
                // 输出到原始Console
                _original?.Write(intercepted);
                
                // 同时输出到此StringWriter以保持功能
                base.Write(intercepted);
            }
        }
        
        public override void WriteLine(string? value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                var intercepted = LogStreamInterceptor.InterceptLog(value, PluginLogLevel.Info);
                
                // 输出到原始Console
                _original?.WriteLine(intercepted);
                
                // 同时输出到此StringWriter以保持功能
                base.WriteLine(intercepted);
            }
        }
        
        public override void WriteLine()
        {
            _original?.WriteLine();
            base.WriteLine();
        }
        
        public override Encoding Encoding => _original?.Encoding ?? Encoding.UTF8;
        
        public override string ToString()
        {
            return base.ToString();
        }
    }
}