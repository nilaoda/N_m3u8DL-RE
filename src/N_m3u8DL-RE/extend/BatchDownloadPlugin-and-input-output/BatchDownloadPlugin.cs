using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
// 使用System.Text.Json进行JSON配置文件解析，相比手动字符串解析更可靠
using System.Text.Json;
using System.Threading.Tasks;
using N_m3u8DL_RE.Common.Log;

namespace N_m3u8DL_RE.Plugin
{
    public class BatchDownloadPlugin : IPlugin
    {
        // 存储待下载的URL列表
        private List<string> _urlList = new List<string>();
        // 当前下载任务的索引，用于跟踪批量下载进度
        private int _currentIndex = 0;
        // 默认输入文件路径，相对路径，相对于程序运行目录
        private string _batchFile = "extend/BatchDownloadPlugin-and-input-output/input-batch-urls.txt";
        // 默认输出目录路径，相对路径，相对于程序运行目录
        // 用于存储批量下载的文件输出
        private string _outputDirectory = "extend/BatchDownloadPlugin-and-input-output/BatchDownloadPlugin-output";
        // 是否为每个URL创建独立的子目录
        private bool _createSubdirectories = false;
        
        /// <summary>
        /// 插件初始化方法，在插件被加载时由PluginManager调用
        /// 
        /// 执行流程：
        /// 1. 首先调用ReadConfigAndCreateDirectories()从PluginConfig.json读取配置并创建必要的目录结构
        ///    这样可以确保在读取URL列表之前，输入输出目录已经存在
        /// 2. 检查插件是否在配置中启用
        /// 3. 如果启用，读取各项配置并加载URL列表
        /// 
        /// 为什么在ExtractEnabledFromConfig之前调用ReadConfigAndCreateDirectories？
        /// 因为：
        /// - 插件可能处于启用状态，但输入输出目录可能不存在
        /// - 如果目录不存在，后续的EnsureInputOutputPathsExist会尝试创建
        /// - 但如果插件未启用，我们仍然需要确保目录存在（以便用户手动添加URL）
        /// - 这样设计确保无论插件是否启用，目录结构都是完整的
        /// </summary>
        /// <param name="config">插件配置对象（可选，当前版本从PluginConfig.json读取配置）</param>
        public void Initialize(PluginConfig? config)
        {
            // 【关键步骤】先读取配置并创建目录，确保后续操作有可靠的目录环境
            ReadConfigAndCreateDirectories();
            
            // 从PluginConfig.json读取Enabled配置
            bool enabled = ExtractEnabledFromConfig();
            
            if (enabled)
            {
                // 从PluginConfig.json读取BatchFile配置（输入文件路径）
                _batchFile = ExtractBatchFileFromConfig();
                // 从PluginConfig.json读取OutputDirectory配置（输出目录路径）
                _outputDirectory = ExtractOutputDirectoryFromConfig();
                // 从PluginConfig.json读取CreateSubdirectories配置
                _createSubdirectories = ExtractCreateSubdirectoriesFromConfig();
                
                // 双重确保：再次确认输入输出路径存在
                // 虽然ReadConfigAndCreateDirectories已经创建过，但可能存在配置被修改的情况
                EnsureInputOutputPathsExist(_batchFile, _outputDirectory);
                
                // 记录使用的输出目录
                if (!string.IsNullOrEmpty(_outputDirectory) && Directory.Exists(_outputDirectory))
                {
                    Logger.Info($"[BatchDownloadPlugin] Using configured output directory: {_outputDirectory}");
                }
                
                // 加载URL列表
                LoadUrlList();
                Logger.Info($"[BatchDownloadPlugin] Loaded {_urlList.Count} URLs from {_batchFile}");
            }
        }

        /// <summary>
        /// 从PluginConfig.json读取配置并创建必要的目录结构
        /// 
        /// 【为什么需要这个方法？】
        /// 
        /// 问题背景：
        /// - 插件系统通过反射动态加载，静态构造函数无法可靠执行
        /// - 编译后的输出目录可能缺少input-batch-urls.txt和BatchDownloadPlugin-output目录
        /// - 用户第一次运行程序时，如果目录不存在，插件会因找不到输入文件而失败
        /// 
        /// 解决方案：
        /// - 在Initialize方法中，首先调用此方法读取配置并创建目录
        /// - 使用System.Text.Json解析配置文件（比手动字符串解析更可靠）
        /// - 从BatchDownload配置节中读取BatchFile和OutputDirectory
        /// - 确保输入文件所在目录存在，如果文件不存在则创建默认模板
        /// - 确保输出目录存在
        /// 
        /// 【执行步骤】
        /// 1. 构建PluginConfig.json的相对路径（extend/PluginConfig.json）
        /// 2. 检查配置文件是否存在
        /// 3. 使用JsonDocument.Parse解析JSON（比手动字符串解析更可靠，避免边界条件错误）
        /// 4. 获取BatchDownload配置节
        /// 5. 读取BatchFile配置，创建输入目录（如果不存在）
        /// 6. 读取OutputDirectory配置，创建输出目录（如果不存在）
        /// 7. 检查输入文件是否存在，如果不存在则创建默认模板文件
        /// 
        /// 【为什么在ExtractEnabledFromConfig之前调用？】
        /// - 即使插件未启用（Enabled=false），目录结构也应该存在
        /// - 这样用户可以手动编辑配置文件或在目录中添加文件后启用插件
        /// - 目录结构的存在是插件正常工作的前提条件
        /// 
        /// 【技术细节说明】
        /// - 本方法使用System.Text.Json命名空间下的JsonDocument类进行JSON解析
        /// - JsonDocument.Parse提供类型安全的JSON解析，避免手动字符串处理的边界条件问题
        /// - TryGetProperty方法用于安全地访问JSON属性，如果属性不存在不会抛出异常
        /// - GetString()方法用于获取字符串类型的配置值
        /// - 路径处理使用Path.Combine确保跨平台兼容性（Windows/Linux路径分隔符）
        /// - Directory.CreateDirectory在目录已存在时不会抛出异常，安全可靠
        /// </summary>
        private void ReadConfigAndCreateDirectories()
        {
            try
            {
                // 构建配置文件的相对路径
                // 相对于程序运行目录（即bin/Debug/net9.0/等输出目录）
                var configPath = "extend/PluginConfig.json";
                
                // 检查配置文件是否存在
                // 如果不存在，跳过目录创建步骤（可能是第一次运行或配置被删除）
                if (File.Exists(configPath))
                {
                    // 读取配置文件内容
                    var json = File.ReadAllText(configPath);
                    
                    // 使用System.Text.Json解析JSON
                    // 优势：
                    // - 内置于.NET，无需额外依赖
                    // - 类型安全，避免手动字符串解析的边界条件错误
                    // - 自动处理JSON结构，比IndexOf/Substring更可靠
                    var configDoc = JsonDocument.Parse(json);
                    
                    // 获取BatchDownload配置节
                    // TryGetProperty是安全的访问方式，如果属性不存在不会抛出异常
                    if (configDoc.RootElement.TryGetProperty("BatchDownload", out var batchConfig))
                    {
                        // 【处理BatchFile配置】
                        // 从配置中读取输入文件路径
                        if (batchConfig.TryGetProperty("BatchFile", out var batchFileElem))
                        {
                            // 获取字符串值
                            var batchFile = batchFileElem.GetString();
                            
                            // 确保路径不为空
                            if (!string.IsNullOrEmpty(batchFile))
                            {
                                // 提取输入文件所在目录
                                // Path.GetDirectoryName返回路径的目录部分
                                // 例如："extend/BatchDownloadPlugin-and-input-output/input-batch-urls.txt"
                                // 返回："extend/BatchDownloadPlugin-and-input-output"
                                var inputDir = Path.GetDirectoryName(batchFile);
                                
                                if (!string.IsNullOrEmpty(inputDir))
                                {
                                    // 获取当前工作目录（程序运行目录）
                                    var baseDir = Directory.GetCurrentDirectory();
                                    
                                    // 构建完整的输入目录路径
                                    // Path.Combine处理不同操作系统的路径分隔符（Windows用\，Linux用/）
                                    var fullInputDir = Path.Combine(baseDir, inputDir);
                                    
                                    // 检查目录是否存在
                                    // Directory.Exists检查文件系统，效率高且安全
                                    if (!Directory.Exists(fullInputDir))
                                    {
                                        // 创建目录，包括所有中间目录
                                        // Directory.CreateDirectory如果目录已存在不会抛出异常
                                        Directory.CreateDirectory(fullInputDir);
                                        Logger.Info($"[BatchDownloadPlugin] Created input directory: {fullInputDir}");
                                    }
                                }
                            }
                        }
                        
                        // 【处理OutputDirectory配置】
                        // 从配置中读取输出目录路径
                        if (batchConfig.TryGetProperty("OutputDirectory", out var outputDirElem))
                        {
                            var outputDir = outputDirElem.GetString();
                            
                            if (!string.IsNullOrEmpty(outputDir))
                            {
                                var baseDir = Directory.GetCurrentDirectory();
                                var fullOutputDir = Path.Combine(baseDir, outputDir);
                                
                                if (!Directory.Exists(fullOutputDir))
                                {
                                    Directory.CreateDirectory(fullOutputDir);
                                    Logger.Info($"[BatchDownloadPlugin] Created output directory: {fullOutputDir}");
                                }
                            }
                        }
                        
                        // 【创建默认输入文件】
                        // 如果输入文件不存在，创建一个包含示例和说明的模板文件
                        // 这样用户可以立即了解文件格式并添加自己的URL
                        if (batchConfig.TryGetProperty("BatchFile", out var batchFileForCreate))
                        {
                            var batchFilePath = batchFileForCreate.GetString();
                            
                            if (!string.IsNullOrEmpty(batchFilePath))
                            {
                                var baseDir = Directory.GetCurrentDirectory();
                                var fullBatchFilePath = Path.Combine(baseDir, batchFilePath);
                                
                                if (!File.Exists(fullBatchFilePath))
                                {
                                    CreateDefaultInputFile(fullBatchFilePath);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // 记录警告日志但不抛出异常
                // 这样即使配置解析失败，程序仍可继续运行（可能使用默认值）
                Logger.Warn($"[BatchDownloadPlugin] Failed to read config and create directories: {ex.Message}");
            }
        }
        
        public void OnFileDownloaded(string filePath, int downloadCount)
        {
            // 批量下载插件的主要逻辑在程序启动时处理
            // 此方法用于处理单个下载完成后的回调（可选）
        }

        // 新增接口方法 - 提供空实现以保持向后兼容
        public void OnInputReceived(object args, object option)
        {
            // 【输入拦截】由PluginManager.cs统一调用此方法
            // 当用户在命令行中输入--batch等参数时，此方法会被PluginManager.NotifyPluginsOnInput调用
            Console.WriteLine($"[BatchDownloadPlugin] ✅ OnInputReceived被调用 - 参数: {args}, 选项: {option}");
            
            // 检查是否包含--batch参数
            if (args is string[] argsArray)
            {
                var hasBatchParam = argsArray.Contains("--batch");
                Console.WriteLine($"[BatchDownloadPlugin] 检测到--batch参数: {hasBatchParam}");
                
                if (hasBatchParam && HasUrls())
                {
                    Console.WriteLine($"[BatchDownloadPlugin] ✅ 检测到批量下载模式，URL列表包含 {_urlList.Count} 个条目");
                }
            }
        }

        public void OnOutputGenerated(string outputPath, string outputType)
        {
            // 空实现 - 可在后续阶段中扩展
        }

        public void OnLogGenerated(string logMessage, PluginLogLevel logLevel)
        {
            // 空实现 - 可在后续阶段中扩展
        }
        
        /// <summary>
        /// 从批量下载输入文件中加载URL列表
        /// 
        /// 【为什么需要这个方法？】
        /// 
        /// 1. 读取配置文件中指定的URL列表文件
        /// 2. 解析文件内容，提取有效的URL
        /// 3. 过滤掉注释行和空行
        /// 4. 存储到_urlList字段供后续使用
        /// 
        /// 【文件格式要求】
        /// - 每行一个URL
        /// - 以#开头的行被视为注释，被跳过
        /// - 空行被跳过
        /// - 支持任意有效的URL格式
        /// 
        /// 【执行步骤】
        /// 1. 检查文件是否存在
        /// 2. 读取所有行
        /// 3. 对每行进行修剪（去除首尾空白）
        /// 4. 跳过空行和注释行
        /// 5. 将有效URL添加到_urlList
        /// 
        /// 【错误处理】
        /// - 如果文件不存在，记录警告日志
        /// - 不抛出异常，允许程序继续运行
        /// 
        /// 【使用示例】
        /// # 这是注释，会被跳过
        /// https://example.com/video1.m3u8
        /// https://example.com/video2.m3u8
        /// 
        /// 【注意事项】
        /// - URL会被原样存储，不进行验证
        /// - URL的验证在实际下载时进行
        /// - 大文件可能导致内存占用增加
        /// </summary>
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
        
        /// <summary>
        /// 从PluginConfig.json的BatchDownload配置节中提取BatchFile配置值
        /// 
        /// 【为什么需要这个方法？】
        /// 
        /// 由于以下原因，需要单独的方法从配置中提取BatchFile：
        /// 1. ReadConfigAndCreateDirectories使用System.Text.Json进行配置解析
        /// 2. 但其他Extract方法使用手动字符串解析（IndexOf/Substring）
        /// 3. 这种设计是为了演示不同的配置解析方式
        /// 
        /// 【解析方法说明】
        /// - 使用IndexOf定位"BatchDownload"配置节的开头
        /// - 使用IndexOf定位该节的结束括号（第一个}）
        /// - 在节内查找"BatchFile"字段
        /// - 提取冒号后的值，直到逗号或右括号
        /// - 去除首尾的引号
        /// 
        /// 【注意】
        /// 这种手动字符串解析方式不够健壮，仅作为示例
        /// 实际项目中建议统一使用System.Text.Json进行配置解析
        /// 
        /// 【返回值】
        /// - 如果配置有效，返回配置中指定的BatchFile路径
        /// - 如果配置无效或解析失败，返回默认值
        /// </summary>
        /// <returns>BatchFile配置值（相对路径）</returns>
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
        
        /// <summary>
        /// 从PluginConfig.json的BatchDownload配置节中提取Enabled配置值
        /// 
        /// 【为什么需要这个方法？】
        /// 
        /// 1. 插件的启用状态需要独立判断
        /// 2. 在Initialize方法中，需要先判断是否启用再执行其他逻辑
        /// 3. 默认情况下插件是禁用的，避免对用户造成干扰
        /// 
        /// 【解析方法说明】
        /// - 使用IndexOf定位"BatchDownload"配置节
        /// - 在节内使用IndexOf查找"Enabled"字段
        /// - 提取冒号后的布尔值（true/false）
        /// - 使用StringComparison.OrdinalIgnoreCase进行不区分大小写的比较
        /// 
        /// 【配置示例】
        /// "BatchDownload": {
        ///     "Enabled": true,  // 插件启用开关
        ///     ...
        /// }
        /// 
        /// 【返回值】
        /// - true: 插件已启用，可以执行批量下载
        /// - false: 插件未启用，跳过批量下载逻辑
        /// </summary>
        /// <returns>是否启用插件</returns>
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
        
        /// <summary>
        /// 从PluginConfig.json的BatchDownload配置节中提取CreateSubdirectories配置值
        /// 
        /// 【为什么需要这个方法？】
        /// 
        /// 1. 控制批量下载时的目录创建行为
        /// 2. 如果设置为true，每个URL会创建独立的子目录
        /// 3. 如果设置为false，所有文件直接下载到输出目录
        /// 4. 默认值为false，避免创建过多嵌套目录
        /// 
        /// 【配置示例】
        /// "BatchDownload": {
        ///     "CreateSubdirectories": true,  // 是否为每个URL创建子目录
        ///     ...
        /// }
        /// 
        /// 【使用场景】
        /// - true: 当多个视频有相同文件名时，避免覆盖
        /// - false: 当只需要简单的目录结构时
        /// 
        /// 【返回值】
        /// - true: 为每个URL创建子目录
        /// - false: 不创建子目录，所有文件在同一目录
        /// </summary>
        /// <returns>是否创建子目录</returns>
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
        
        /// <summary>
        /// 从PluginConfig.json的BatchDownload配置节中提取OutputDirectory配置值
        /// 
        /// 【为什么需要这个方法？】
        /// 
        /// 1. 指定批量下载文件的输出目录
        /// 2. 允许用户自定义输出位置
        /// 3. 如果配置无效或未配置，返回null，由调用方使用默认值
        /// 
        /// 【解析方法说明】
        /// - 使用IndexOf定位"BatchDownload"配置节
        /// - 在节内使用IndexOf查找"OutputDirectory"字段
        /// - 提取冒号后的字符串值（路径）
        /// - 去除首尾的引号
        /// 
        /// 【配置示例】
        /// "BatchDownload": {
        ///     "OutputDirectory": "extend/BatchDownloadPlugin-and-input-output/BatchDownloadPlugin-output",
        ///     ...
        /// }
        /// 
        /// 【返回值】
        /// - 配置的输出目录路径（相对路径）
        /// - null: 配置无效或未配置
        /// 
        /// 【路径处理】
        /// - 返回的是相对路径（相对于程序运行目录）
        /// - 实际使用时需要与Directory.GetCurrentDirectory()组合
        /// </summary>
        /// <returns>OutputDirectory配置值（相对路径）</returns>
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

        /// <summary>
        /// 确保输入文件和输出目录存在，如果不存在则创建
        /// 
        /// 【为什么需要这个方法？】
        /// 
        /// 在ReadConfigAndCreateDirectories方法之后再次调用此方法的原因：
        /// 1. 配置可能在ReadConfigAndCreateDirectories之后被用户修改
        /// 2. 确保路径变更后仍然能正确创建目录结构
        /// 3. 双重检查机制提高可靠性
        /// 
        /// 【执行步骤】
        /// 1. 获取当前工作目录（程序运行目录）
        /// 2. 构建输入文件的完整路径
        /// 3. 创建输入文件所在目录（如果不存在）
        /// 4. 如果输入文件不存在，创建默认模板文件
        /// 5. 构建输出目录的完整路径
        /// 6. 创建输出目录（如果不存在）
        /// 
        /// 【路径处理说明】
        /// - 使用Directory.GetCurrentDirectory()获取程序运行目录
        /// - 使用Path.Combine()组合路径，兼容不同操作系统的路径分隔符
        /// - 使用Path.GetDirectoryName()提取目录部分
        /// 
        /// 【异常处理】
        /// - 所有文件操作都包装在try-catch块中
        /// - 即使创建失败也只记录警告日志，不抛出异常
        /// - 这样可以保证程序的其余部分继续正常运行
        /// </summary>
        /// <param name="inputFile">输入文件路径（相对路径）</param>
        /// <param name="outputDirectory">输出目录路径（相对路径）</param>
        private void EnsureInputOutputPathsExist(string inputFile, string outputDirectory)
        {
            try
            {
                // 获取当前工作目录，兼容编译后的环境
                var baseDir = Directory.GetCurrentDirectory();
                
                // 确保输入文件路径存在
                var inputPath = Path.Combine(baseDir, inputFile);
                var inputDir = Path.GetDirectoryName(inputPath);
                
                if (!string.IsNullOrEmpty(inputDir) && !Directory.Exists(inputDir))
                {
                    Directory.CreateDirectory(inputDir);
                    Logger.Info($"[BatchDownloadPlugin] Created input directory: {inputDir}");
                }
                
                // 创建默认的输入文件（如果不存在）
                if (!File.Exists(inputPath))
                {
                    CreateDefaultInputFile(inputPath);
                }
                
                // 确保输出目录存在
                if (!string.IsNullOrEmpty(outputDirectory))
                {
                    var outputPath = Path.Combine(baseDir, outputDirectory);
                    if (!Directory.Exists(outputPath))
                    {
                        Directory.CreateDirectory(outputPath);
                        Logger.Info($"[BatchDownloadPlugin] Created output directory: {outputPath}");
                    }
                }
                
                Logger.Info($"[BatchDownloadPlugin] Input/Output paths initialized successfully");
            }
            catch (Exception ex)
            {
                Logger.Warn($"[BatchDownloadPlugin] Failed to ensure paths exist: {ex.Message}");
            }
        }

        /// <summary>
        /// 创建默认的批量下载输入文件模板
        /// 
        /// 【为什么需要这个方法？】
        /// 
        /// 当用户第一次运行程序或输入文件被删除时：
        /// 1. 需要提供一个模板文件，让用户了解文件格式
        /// 2. 模板文件包含示例URL和注释说明
        /// 3. 用户只需删除示例并添加自己的URL即可使用
        /// 
        /// 【模板文件格式】
        /// - 以#开头的行为注释，会被LoadUrlList跳过
        /// - 空行会被跳过
        /// - 每行一个URL，可以是m3u8视频流地址
        /// 
        /// 【执行步骤】
        /// 1. 定义默认的模板内容（包含说明和示例）
        /// 2. 提取输入文件的目录路径
        /// 3. 如果目录不存在则创建目录
        /// 4. 将模板内容写入文件
        /// 
        /// 【使用场景】
        /// - 程序首次运行，input-batch-urls.txt不存在时
        /// - 用户误删输入文件后重新生成
        /// - 提供给新用户了解文件格式
        /// 
        /// 【注意事项】
        /// - 不会覆盖已存在的文件
        /// - 文件编码使用UTF-8，支持中文内容
        /// </summary>
        /// <param name="inputPath">要创建的输入文件完整路径</param>
        private void CreateDefaultInputFile(string inputPath)
        {
            try
            {
                var defaultContent = @"# 批量下载URL列表
# 注释行以#开头
# 请在此处添加要下载的m3u8 URL，每行一个
# 例如：
# https://example.com/video1.m3u8
# https://example.com/video2.m3u8

";
                
                var dir = Path.GetDirectoryName(inputPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                
                File.WriteAllText(inputPath, defaultContent);
                Logger.Info($"[BatchDownloadPlugin] Created default input file: {inputPath}");
            }
            catch (Exception ex)
            {
                Logger.Warn($"[BatchDownloadPlugin] Failed to create default input file: {ex.Message}");
            }
        }
        
        public List<string> GetUrlList() => _urlList;
        public bool HasUrls() => _urlList.Count > 0;
        
        // 返回输出目录配置
        public string GetOutputDirectory() => _outputDirectory ?? ExtractOutputDirectoryFromConfig();
        
        // 返回简化的配置对象，只包含实际使用的属性
        public object GetConfig() => new { CreateSubdirectories = _createSubdirectories };
    }
}