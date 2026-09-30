using System;
using System.Diagnostics.CodeAnalysis;
using Unity.Entities;
using UnityEngine;

[AddComponentMenu("Authoring/Virtual Player Settings")]
public class VirtualPlayerSettingsAuthoring : MonoBehaviour
{
    [Serializable]
    class Entry
    {
        [SerializeField, NotNull] public string? Nickname = null;
    }

    [SerializeField] Entry[] Entries = Array.Empty<Entry>();

    class Baker : Baker<VirtualPlayerSettingsAuthoring>
    {
        public override void Bake(VirtualPlayerSettingsAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            var buffer = AddBuffer<BufferedVirtualPlayerSettings>(entity);
            foreach (var entry in authoring.Entries)
            {
                buffer.Add(new()
                {
                    Nickname = entry.Nickname,
                });
            }
        }
    }
}
