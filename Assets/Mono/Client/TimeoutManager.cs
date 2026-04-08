using System;
using System.Diagnostics.CodeAnalysis;
using SaintsField;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;
using UnityEngine.UIElements;

class TimeoutManager : MonoBehaviour
{
    [Header("UI")]
    [SerializeField, NotNull] UIDocument? UI = default;

    [Header("Debug")]
    [SerializeField, ReadOnly] uint Snapshot;
    [SerializeField, ReadOnly] double Time;

    void Start()
    {
        Time = DateTimeOffset.UtcNow.TimeOfDay.TotalSeconds;
    }

    void Update()
    {
        if (ConnectionManager.ClientWorld == null) return;

        double now = DateTimeOffset.UtcNow.TimeOfDay.TotalSeconds;

        using EntityQuery q = ConnectionManager.ClientWorld.EntityManager.CreateEntityQuery(typeof(NetworkSnapshotAck));
        NetworkSnapshotAck ack = q.GetSingleton<NetworkSnapshotAck>();
        uint t = ack.LastReceiveTimestamp;
        if (t != Snapshot)
        {
            Snapshot = t;
            Time = now;
        }

        UI.ForceSetActive(Snapshot != 0 && now > Time + 1);
    }
}
