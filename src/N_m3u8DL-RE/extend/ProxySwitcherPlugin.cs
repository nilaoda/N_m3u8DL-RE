using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using N_m3u8DL_RE.Common.Log;

namespace N_m3u8DL_RE.Plugin
{
    public class ProxySwitcherPlugin : IPlugin
    {
        private PluginConfig? _config;
        private int _downloadCount = 0;
        private readonly HttpClient _httpClient = new HttpClient();

        public void Initialize(PluginConfig? config)
        {
            _config = config;
            Logger.Info("[ProxySwitcherPlugin] Initialized");
        }

        public void OnFileDownloaded(string filePath, int downloadCount)
        {
            _downloadCount = downloadCount;
            
            // 检查是否启用插件且达到切换间隔
            if (_config?.ProxySwitcher?.Enabled == true && 
                _downloadCount % _config.ProxySwitcher.SwitchInterval == 0)
            {
                SwitchProxy();
            }
        }

        private async void SwitchProxy()
        {
            try
            {
                var clashApiUrl = _config?.ProxySwitcher?.ClashApiUrl ?? "http://127.0.0.1:9090";
                var proxiesResponse = await _httpClient.GetAsync($"{clashApiUrl}/proxies");
                var json = await proxiesResponse.Content.ReadAsStringAsync();
                
                // 这里应该解析JSON并选择一个代理进行切换
                // 为简化示例，我们只记录日志
                Logger.Info($"[ProxySwitcherPlugin] Would switch proxy via Clash API at {clashApiUrl}");
                Logger.Debug($"[ProxySwitcherPlugin] Proxies response: {json}");
            }
            catch (Exception ex)
            {
                Logger.Error($"[ProxySwitcherPlugin] Failed to switch proxy: {ex.Message}");
            }
        }
    }
}