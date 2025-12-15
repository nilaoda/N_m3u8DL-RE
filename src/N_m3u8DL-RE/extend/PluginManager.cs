using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using N_m3u8DL_RE.Common.Entity;

namespace N_m3u8DL_RE.Plugin
{
    public static class PluginManager
    {
        private static readonly List<IPlugin> _plugins = new List<IPlugin>();
        private static int _downloadCount = 0;
        private static PluginConfig? _config;

        public static void LoadPlugins()
        {
            try
            {
                // 加载配置
                LoadConfig();

                // 查找并加载所有实现了IPlugin接口的类
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
                        if (Activator.CreateInstance(type) is IPlugin plugin)
                        {
                            // 检查配置中是否启用了该插件
                            var pluginName = type.Name.Replace("Plugin", "");
                            var isEnabled = IsPluginEnabled(pluginName);
                            
                            Console.WriteLine($"[Plugin] Plugin {pluginName} enabled: {isEnabled}");
                            
                            if (isEnabled)
                            {
                                plugin.Initialize(_config);
                                _plugins.Add(plugin);
                                Console.WriteLine($"[Plugin] Loaded plugin: {pluginName}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[Plugin] Failed to create instance of {type.Name}: {ex.Message}");
                        if (ex.InnerException != null)
                        {
                            Console.WriteLine($"[Plugin] Inner exception: {ex.InnerException.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Plugin] LoadPlugins failed: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"[Plugin] Inner exception: {ex.InnerException.Message}");
                }
            }
        }

        private static void LoadConfig()
        {
            var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "extend", "PluginConfig.json");
            if (File.Exists(configPath))
            {
                try
                {
                    var json = File.ReadAllText(configPath);
                    
                    // 手动解析JSON，避免反射序列化限制
                    _config = new PluginConfig();
                    
                    if (json.Contains("\"BatchDownload\""))
                    {
                        var batchEnabled = ExtractJsonBoolValue(json, "BatchDownload", "Enabled");
                        var batchFile = ExtractJsonValue(json, "BatchFile", "extend/BatchDownloadPlugin-and-input-output/input-batch-urls.txt");
                        var createSubdirs = ExtractJsonBoolValue(json, "BatchDownload", "CreateSubdirectories");
                        
                        // 不再创建BatchDownloadConfig对象，插件将直接读取配置
                        Console.WriteLine($"[Plugin] BatchDownload config: Enabled={batchEnabled}, File={batchFile}, CreateSubdirectories={createSubdirs}");
                    }
                    
                    if (json.Contains("\"UASwitcher\""))
                    {
                        var uaEnabled = ExtractJsonBoolValue(json, "UASwitcher", "Enabled");
                        _config.UASwitcher = new UASwitcherConfig
                        {
                            Enabled = uaEnabled
                        };
                        Console.WriteLine($"[Plugin] UASwitcher config: Enabled={uaEnabled}");
                    }
                    
                    if (json.Contains("\"ProxySwitcher\""))
                    {
                        var proxyEnabled = ExtractJsonBoolValue(json, "ProxySwitcher", "Enabled");
                        _config.ProxySwitcher = new ProxySwitcherConfig
                        {
                            Enabled = proxyEnabled
                        };
                        Console.WriteLine($"[Plugin] ProxySwitcher config: Enabled={proxyEnabled}");
                    }
                    
                    Console.WriteLine($"[Plugin] Config loaded manually");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Plugin] Manual config loading failed: {ex.Message}");
                    _config = new PluginConfig();
                }
            }
            else
            {
                _config = new PluginConfig();
            }
        }
        
        private static string ExtractJsonValue(string json, string propertyName, string defaultValue)
        {
            var pattern = $"\\\"{propertyName}\\\":\\s*\\\"([^\\\"]*)\\\"";
            var match = System.Text.RegularExpressions.Regex.Match(json, pattern);
            return match.Success ? match.Groups[1].Value : defaultValue;
        }
        
        private static bool ExtractJsonBoolValue(string json, string sectionName, string propertyName)
        {
            // 尝试多种模式，因为JSON格式可能有变化
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
                    Console.WriteLine($"[Plugin] Found {sectionName}.{propertyName} with pattern: {pattern}");
                    Console.WriteLine($"[Plugin] Value: {match.Groups[1].Value}");
                    return match.Groups[1].Value == "true";
                }
            }
            
            Console.WriteLine($"[Plugin] Could not find {sectionName}.{propertyName}");
            return false;
        }

        private static bool IsPluginEnabled(string pluginName)
        {
            return pluginName switch
            {
                "UASwitcher" => _config?.UASwitcher?.Enabled ?? false,
                "ProxySwitcher" => _config?.ProxySwitcher?.Enabled ?? false,
                "BatchDownload" => ExtractBatchDownloadEnabledFromConfig(),
                _ => false
            };
        }
        
        private static bool ExtractBatchDownloadEnabledFromConfig()
        {
            try
            {
                var configPath = "extend/PluginConfig.json";
                if (File.Exists(configPath))
                {
                    var json = File.ReadAllText(configPath);
                    
                    // 查找"BatchDownload"部分
                    var batchStart = json.IndexOf("\"BatchDownload\"");
                    if (batchStart == -1) return false;
                    
                    // 查找BatchDownload部分的结束位置
                    var batchEnd = json.IndexOf("}", batchStart);
                    if (batchEnd == -1) return false;
                    
                    // 在BatchDownload部分内查找"Enabled"字段
                    var enabledStart = json.IndexOf("\"Enabled\"", batchStart, batchEnd - batchStart);
                    if (enabledStart == -1) return false;
                    
                    var valueStart = json.IndexOf(":", enabledStart) + 1;
                    var valueEnd = json.IndexOf(",", valueStart);
                    if (valueEnd == -1 || valueEnd > batchEnd) valueEnd = json.IndexOf("}", valueStart);
                    
                    if (valueStart > 0 && valueEnd > valueStart)
                    {
                        var enabledValue = json.Substring(valueStart, valueEnd - valueStart).Trim();
                        return enabledValue.Equals("true", StringComparison.OrdinalIgnoreCase);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PluginManager] Failed to extract BatchDownload Enabled from config: {ex.Message}");
            }
            
            return false; // 默认值
        }

        public static List<IPlugin> GetPlugins() => _plugins;

        public static void OnFileDownloaded(string filePath)
        {
            _downloadCount++;
            
            foreach (var plugin in _plugins)
            {
                plugin.OnFileDownloaded(filePath, _downloadCount);
            }
        }

        public static int GetDownloadCount() => _downloadCount;
        
        public static PluginConfig? GetConfig() => _config;
    }

    public interface IPlugin
    {
        void Initialize(PluginConfig? config);
        void OnFileDownloaded(string filePath, int downloadCount);
    }

    public class PluginConfig
    {
        public UASwitcherConfig? UASwitcher { get; set; }
        public ProxySwitcherConfig? ProxySwitcher { get; set; }
        // BatchDownload配置现在由插件直接读取，不再通过PluginConfig传递
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
}