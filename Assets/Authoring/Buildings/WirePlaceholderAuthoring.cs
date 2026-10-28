using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

class WirePlaceholderAuthoring : MonoBehaviour
{
    [SerializeField, SaintsField.ReadOnly] public float3[] PortPositions = System.Array.Empty<float3>();

    class Baker : Baker<WirePlaceholderAuthoring>
    {
        public override void Bake(WirePlaceholderAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent<WirePlaceholder>(entity);
            FixedList64Bytes<float3> ports = new();
            foreach (float3 port in authoring.PortPositions) ports.Add(port);

            AddComponent<Connector>(entity, new()
            {
                PortPositions = ports,
            });
            AddBuffer<BufferedWire>(entity);
        }
    }
}
