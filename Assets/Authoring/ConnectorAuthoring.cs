using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

[AddComponentMenu("Authoring/Connector")]
class ConnectorAuthoring : MonoBehaviour
{
    [SerializeField] Transform[] PortPositions = System.Array.Empty<Transform>();

    class Baker : Baker<ConnectorAuthoring>
    {
        public override void Bake(ConnectorAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            FixedList64Bytes<float3> ports = new();
            foreach (Transform transform in authoring.PortPositions)
            {
                if (ports.Length >= ports.Capacity)
                {
                    Debug.LogError($"Too much port: max is {ports.Capacity}");
                    break;
                }
                ports.Add(transform.position);
            }

            if (ports.Length == 0)
            {
                Debug.LogError($"No ports defined for {authoring.gameObject}", authoring.gameObject);
            }

            AddComponent<Connector>(entity, new()
            {
                PortPositions = ports,
            });
            AddBuffer<BufferedWire>(entity);
        }
    }
}
