using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using Newtonsoft.Json;
using Unity.NetCode;

public class UnitLogEventItem
{
    [JsonProperty("logType")]
    public string? LogType { get; set; }

    [JsonProperty("index")]
    public uint Index { get; set; }

    [JsonProperty("entity")]
    public string? Entity { get; set; }

    [JsonProperty("timestamp")]
    public long Timestamp { get; set; }

    [JsonProperty("content", DefaultValueHandling = DefaultValueHandling.Ignore)]
    public object? Content { get; set; }

    public UnitLogEventItem(LogPieceHeader header, SpawnedGhost entity, object? content = null)
    {
        LogType = header.Type switch
        {
            LogPieceType.CombatTurret_Shoot => "Combat Turret Shoot",
            LogPieceType.Command => "Command",
            LogPieceType.Radar => "Radar",
            LogPieceType.Transmission_WiredOut => "Transmission Send",
            LogPieceType.Transmission_WiredIn => "Transmission Receive",
            LogPieceType.Transmission_WirelessOut => "Transmission SendW",
            LogPieceType.Transmission_WirelessIn => "Transmission ReceiveW",
            LogPieceType.Unknown0 or LogPieceType.Unknown1 or _ => "?",
        };
        Index = header.Index;
        Entity = $"{entity.ghostId}:{entity.spawnTick}";
        Timestamp = header.Timestamp;
        Content = content;
    }
}

public class UnitLogEvent : DebugEvent
{
    [JsonProperty("logs")]
    public List<UnitLogEventItem>? Logs { get; set; }

    public UnitLogEvent() : base("unitLog") { }
}
