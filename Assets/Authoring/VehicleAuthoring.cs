using Unity.Entities;
using UnityEngine;

[AddComponentMenu("Authoring/Vehicle")]
public class VehicleAuthoring : MonoBehaviour
{
    [SerializeField, Min(0f)] internal float MaxSpeed;
    [SerializeField, Min(0f)] internal float Acceleration;
    [SerializeField, Min(0f)] internal float Steering;

    class Baker : Baker<VehicleAuthoring>
    {
        public override void Bake(VehicleAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new Vehicle()
            {
                MaxSpeed = authoring.MaxSpeed,
                Acceleration = authoring.Acceleration,
                Steering = authoring.Steering,
            });
        }
    }
}
