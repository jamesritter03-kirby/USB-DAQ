using System;
using System.Collections.Generic;

namespace UsbDaq.App.Models;

public sealed class DaqProfile
{
    public string Name { get; set; } = "Default";
    public DateTime Created { get; set; } = DateTime.Now;

    public int SampleIntervalMs { get; set; } = 250;
    public int HistoryDurationSecs { get; set; } = 120;
    public double LowAlarmPsig { get; set; } = 500;
    public double HighAlarmPsig { get; set; } = 28000;

    public string GraphMode { get; set; } = "Sliding Window";
    public bool StackedPlots { get; set; }
    public bool ShowLegend { get; set; } = true;
    public bool ShowCursors { get; set; } = true;
    public bool ShowAlarmLines { get; set; } = true;
    public bool CursorSnapToData { get; set; }
    public bool CursorFollowsData { get; set; } = true;
    public bool ShowPointMarkers { get; set; }
    public bool GraphAutoFollow { get; set; } = true;

    public string MqttHost { get; set; } = "localhost";
    public string MqttPort { get; set; } = "1883";
    public string MqttTopic { get; set; } = "daq/{channel}";
    public string MqttUsername { get; set; } = "";
    public string MqttPassword { get; set; } = "";
    public int MqttPublishIntervalMs { get; set; }

    public string RedisConnStr { get; set; } = "localhost:6379";
    public string RedisKey { get; set; } = "daq:{channel}";
    public bool RedisStream { get; set; }
    public int RedisPublishIntervalMs { get; set; }
    public int RedisExpirySeconds { get; set; }

    public string TbHost { get; set; } = "demo.thingsboard.io";
    public bool TbHttps { get; set; } = true;
    public string TbToken { get; set; } = "";
    public string TbKeyTemplate { get; set; } = "{channel}";
    public string TbPathPrefix { get; set; } = "";
    public int TbPublishIntervalMs { get; set; }

    public string DefaultProtocolName { get; set; } = "GP50 ASCII \u2014 Poll";

    // Streaming auto-start: which targets were running when the profile was saved,
    // and whether to reconnect them automatically when the profile is loaded.
    public bool AutoStartStreaming { get; set; }
    public bool MqttWasStreaming { get; set; }
    public bool RedisWasStreaming { get; set; }
    public bool TbWasStreaming { get; set; }

    public List<ChannelEntry> Channels { get; set; } = new();
}

public sealed class ChannelEntry
{
    public string DeviceId { get; set; } = "";
    public string DeviceDisplayName { get; set; } = "";
    public string DeviceTransport { get; set; } = "";
    public string SignalName { get; set; } = "";
    public string ColorHex { get; set; } = "#3B82F6";
    public string ProtocolName { get; set; } = "GP50 ASCII \u2014 Poll";
    public int StationNumber { get; set; } = 1;
    public bool IsVisible { get; set; } = true;
    public double LowAlarm { get; set; } = 500;
    public double HighAlarm { get; set; } = 28000;
    public bool AlarmEnabled { get; set; } = true;
    public string MqttTopicOverride { get; set; } = "";
    public string RedisKeyOverride { get; set; } = "";
    public string TbKeyOverride { get; set; } = "";
    public string TbTokenOverride { get; set; } = "";
}
