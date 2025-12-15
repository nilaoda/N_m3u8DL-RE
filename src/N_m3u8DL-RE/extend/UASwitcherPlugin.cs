using System;
using System.Collections.Generic;
using System.Net.Http;
using N_m3u8DL_RE.Common.Log;

namespace N_m3u8DL_RE.Plugin
{
    public class UASwitcherPlugin : IPlugin
    {
        private PluginConfig? _config;
        private readonly List<string> _userAgents = new List<string>
        {
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36",
            "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36",
            "Mozilla/5.0 (Linux; Android 10; SM-G981B) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Mobile Safari/537.36"
        };
        private int _currentIndex = 0;

        public void Initialize(PluginConfig? config)
        {
            _config = config;
            
            // 如果配置中有自定义UA，则使用配置中的
            if (config?.UASwitcher?.UserAgents != null && config.UASwitcher.UserAgents.Count > 0)
            {
                _userAgents.Clear();
                _userAgents.AddRange(config.UASwitcher.UserAgents);
            }
            
            // Convert User-Agent list to command line arguments
            var headers = _userAgents.Select(ua => $"-H \"User-Agent: {ua}\"").ToList();
            Logger.Info($"[UASwitcherPlugin] Initialized with headers: {string.Join(", ", headers)}");
        }
        
        public void OnFileDownloaded(string filePath, int downloadCount)
        {
            Logger.Info($"[UASwitcherPlugin] File downloaded: {filePath}, count: {downloadCount}");
            
            // 每1个文件切换一次UA（原来是每3个文件切换一次）
            if (_userAgents.Count > 0)
            {
                string newUA = _userAgents[downloadCount % _userAgents.Count];
                Logger.Info($"[UASwitcherPlugin] Downloaded {downloadCount} files, switching UA to: {newUA}");
                
                // 注意：这里只是示例输出，实际应用中需要与HTTP客户端集成
                // 可以通过修改全局HTTP客户端的默认请求头来实现
                // HttpClient.DefaultRequestHeaders.Add("User-Agent", newUA);
            }
        }
    }
}