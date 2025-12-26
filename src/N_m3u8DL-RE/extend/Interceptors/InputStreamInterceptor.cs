using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace N_m3u8DL_RE.Plugin
{
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
    }
}