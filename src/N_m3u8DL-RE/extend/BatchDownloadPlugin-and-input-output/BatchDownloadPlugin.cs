using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using N_m3u8DL_RE.Common.Log;

namespace N_m3u8DL_RE.Plugin
{
    public class BatchDownloadPlugin : IPlugin
    {
        private List<string> _urlList = new List<string>();
        private int _currentIndex = 0;
        private string _batchFile = "extend/BatchDownloadPlugin-and-input-output/input-batch-urls.txt";
        private bool _createSubdirectories = false;
        
        public void Initialize(PluginConfig? config)
        {
            // 直接检查PluginConfig.json中的Enabled配置
            bool enabled = ExtractEnabledFromConfig();
            
            if (enabled)
            {
                // 从PluginConfig.json读取BatchFile配置
                _batchFile = ExtractBatchFileFromConfig();
                // 从PluginConfig.json读取CreateSubdirectories配置
                _createSubdirectories = ExtractCreateSubdirectoriesFromConfig();
                // 从PluginConfig.json读取OutputDirectory配置
                var outputDirectory = ExtractOutputDirectoryFromConfig();
                
                // 设置默认输出目录（如果配置中存在）
                if (!string.IsNullOrEmpty(outputDirectory) && Directory.Exists(outputDirectory))
                {
                    Logger.Info($"[BatchDownloadPlugin] Using configured output directory: {outputDirectory}");
                    // 注意：这里只是记录信息，实际的输出目录设置在 Program.cs 中处理
                }
                
                LoadUrlList();
                Logger.Info($"[BatchDownloadPlugin] Loaded {_urlList.Count} URLs from {_batchFile}");
            }
        }
        
        public void OnFileDownloaded(string filePath, int downloadCount)
        {
            // 批量下载插件的主要逻辑在程序启动时处理
            // 此方法用于处理单个下载完成后的回调（可选）
        }
        
        private void LoadUrlList()
        {
            if (File.Exists(_batchFile))
            {
                var lines = File.ReadAllLines(_batchFile);
                foreach (var line in lines)
                {
                    var trimmedLine = line.Trim();
                    if (!string.IsNullOrEmpty(trimmedLine) && !trimmedLine.StartsWith("#"))
                    {
                        _urlList.Add(trimmedLine);
                    }
                }
            }
            else
            {
                Logger.Warn($"[BatchDownloadPlugin] Batch file not found: {_batchFile}");
            }
        }
        
        private string ExtractBatchFileFromConfig()
        {
            try
            {
                var configPath = "extend/PluginConfig.json";
                if (File.Exists(configPath))
                {
                    var json = File.ReadAllText(configPath);
                    
                    // 查找"BatchDownload"部分
                    var batchStart = json.IndexOf("\"BatchDownload\"");
                    if (batchStart == -1) return "extend/BatchDownloadPlugin-and-input-output/input-batch-urls.txt";
                    
                    // 查找BatchDownload部分的结束位置
                    var batchEnd = json.IndexOf("}", batchStart);
                    if (batchEnd == -1) return "extend/BatchDownloadPlugin-and-input-output/input-batch-urls.txt";
                    
                    // 在BatchDownload部分内查找"BatchFile"字段
                    var batchFileStart = json.IndexOf("\"BatchFile\"", batchStart, batchEnd - batchStart);
                    if (batchFileStart == -1) return "extend/BatchDownloadPlugin-and-input-output/input-batch-urls.txt";
                    
                    var valueStart = json.IndexOf(":", batchFileStart) + 1;
                    var valueEnd = json.IndexOf(",", valueStart);
                    if (valueEnd == -1 || valueEnd > batchEnd) valueEnd = json.IndexOf("}", valueStart);
                    
                    if (valueStart > 0 && valueEnd > valueStart)
                    {
                        var batchFileValue = json.Substring(valueStart, valueEnd - valueStart).Trim();
                        batchFileValue = batchFileValue.Trim('"');
                        return batchFileValue;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"[BatchDownloadPlugin] Failed to extract BatchFile from config: {ex.Message}");
            }
            
            return "extend/BatchDownloadPlugin-and-input-output/input-batch-urls.txt"; // 默认值
        }
        
        private bool ExtractEnabledFromConfig()
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
                Logger.Warn($"[BatchDownloadPlugin] Failed to extract Enabled from config: {ex.Message}");
            }
            
            return false; // 默认值
        }
        
        private bool ExtractCreateSubdirectoriesFromConfig()
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
                    
                    // 在BatchDownload部分内查找"CreateSubdirectories"字段
                    var createSubdirsStart = json.IndexOf("\"CreateSubdirectories\"", batchStart, batchEnd - batchStart);
                    if (createSubdirsStart == -1) return false;
                    
                    var valueStart = json.IndexOf(":", createSubdirsStart) + 1;
                    var valueEnd = json.IndexOf(",", valueStart);
                    if (valueEnd == -1 || valueEnd > batchEnd) valueEnd = json.IndexOf("}", valueStart);
                    
                    if (valueStart > 0 && valueEnd > valueStart)
                    {
                        var createSubdirsValue = json.Substring(valueStart, valueEnd - valueStart).Trim();
                        return createSubdirsValue.Equals("true", StringComparison.OrdinalIgnoreCase);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"[BatchDownloadPlugin] Failed to extract CreateSubdirectories from config: {ex.Message}");
            }
            
            return false; // 默认值
        }
        
        private string ExtractOutputDirectoryFromConfig()
        {
            try
            {
                var configPath = "extend/PluginConfig.json";
                if (File.Exists(configPath))
                {
                    var json = File.ReadAllText(configPath);
                    
                    // 查找"BatchDownload"部分
                    var batchStart = json.IndexOf("\"BatchDownload\"");
                    if (batchStart == -1) return null;
                    
                    // 查找BatchDownload部分的结束位置
                    var batchEnd = json.IndexOf("}", batchStart);
                    if (batchEnd == -1) return null;
                    
                    // 在BatchDownload部分内查找"OutputDirectory"字段
                    var outputDirStart = json.IndexOf("\"OutputDirectory\"", batchStart, batchEnd - batchStart);
                    if (outputDirStart == -1) return null;
                    
                    var valueStart = json.IndexOf(":", outputDirStart) + 1;
                    var valueEnd = json.IndexOf(",", valueStart);
                    if (valueEnd == -1 || valueEnd > batchEnd) valueEnd = json.IndexOf("}", valueStart);
                    
                    if (valueStart > 0 && valueEnd > valueStart)
                    {
                        var outputDirValue = json.Substring(valueStart, valueEnd - valueStart).Trim();
                        outputDirValue = outputDirValue.Trim('"');
                        return outputDirValue;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"[BatchDownloadPlugin] Failed to extract OutputDirectory from config: {ex.Message}");
            }
            
            return null; // 默认值
        }
        
        public List<string> GetUrlList() => _urlList;
        public bool HasUrls() => _urlList.Count > 0;
        
        // 返回输出目录配置
        public string GetOutputDirectory() => ExtractOutputDirectoryFromConfig();
        
        // 返回简化的配置对象，只包含实际使用的属性
        public object GetConfig() => new { CreateSubdirectories = _createSubdirectories };
    }
}