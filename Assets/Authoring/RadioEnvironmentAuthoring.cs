using Unity.Entities;
using UnityEngine;

[AddComponentMenu("Authoring/Radio Environment")]
class RadioEnvironmentAuthoring : MonoBehaviour
{
    [SerializeField] float PathLossExp = 3.5f;
    [SerializeField] float RefLossDb = 54;
    [SerializeField] float ObstructionDb = 12;
    [SerializeField] float BandwidthHz = 25000;
    [SerializeField] float RequiredSnrDb = 10;

    class Baker : Baker<RadioEnvironmentAuthoring>
    {
        public override void Bake(RadioEnvironmentAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent<RadioEnvironment>(entity, new()
            {
                PathLossExp = authoring.PathLossExp,
                RefLossDb = authoring.RefLossDb,
                ObstructionDb = authoring.ObstructionDb,
                BandwidthHz = authoring.BandwidthHz,
                RequiredSnrDb = authoring.RequiredSnrDb,
            });
        }
    }
}
