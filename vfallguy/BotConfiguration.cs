using System;
using Dalamud.Configuration;
using Dalamud.Plugin;

namespace vfallguy;

[Serializable]
public class BotConfiguration : IPluginConfiguration
{
    public int Version { get; set; } = 1;  
    public string GameName { get; set; } = string.Empty;  
    public string QqPrivateChatNumber { get; set; } = string.Empty;  
    public string QqBotNumber { get; set; } = string.Empty;  
    public string WebSocketUrl { get; set; } = string.Empty;  
    public int WebSocketPort { get; set; } = 0;  
    public int BattlePlayerCount { get; set; } = 1;  

    [NonSerialized]
    private DalamudPluginInterface? pluginInterface;

    public void Initialize(DalamudPluginInterface pInterface)
    {
        pluginInterface = pInterface;

        // 尝试加载现有配置
        var loadedConfig = pluginInterface.GetPluginConfig() as BotConfiguration;
        if (loadedConfig != null)
        {
            GameName = loadedConfig.GameName;
            QqPrivateChatNumber = loadedConfig.QqPrivateChatNumber;
            QqBotNumber = loadedConfig.QqBotNumber;
            WebSocketUrl = loadedConfig.WebSocketUrl;
            WebSocketPort = loadedConfig.WebSocketPort;
            BattlePlayerCount = loadedConfig.BattlePlayerCount;
        }
    }

    public void Save()
    {
        if (pluginInterface != null)
        {
            pluginInterface.SavePluginConfig(this);
        }
    }

    public void Uninit()
    {
        Save();
    }
}
