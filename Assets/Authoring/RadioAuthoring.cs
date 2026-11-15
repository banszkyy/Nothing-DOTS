using Unity.Entities;
using UnityEngine;

[AddComponentMenu("Authoring/Radio")]
public class RadioAuthoring : MonoBehaviour
{
    [SerializeField] Transform? TransmissionPoint;
    [SerializeField] internal float TxPowerDbm;
    [SerializeField] internal float RxGainDbi;
    [SerializeField] internal float SensitivityDbm;
    [SerializeField] internal byte MinBeamHalvings;
    [SerializeField] internal byte MaxBeamHalvings;
    [SerializeField] internal byte BearingSectors;

    class Baker : Baker<RadioAuthoring>
    {
        public override void Bake(RadioAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent<Radio>(entity, new()
            {
                WirelessTransmissionPoint = authoring.TransmissionPoint == null ? default : authoring.TransmissionPoint.position,
                TxPowerDbm = authoring.TxPowerDbm,
                RxGainDbi = authoring.RxGainDbi,
                SensitivityDbm = authoring.SensitivityDbm,
                MinBeamHalvings = authoring.MinBeamHalvings,
                MaxBeamHalvings = authoring.MaxBeamHalvings,
                BearingSectors = authoring.BearingSectors,
            });
        }
    }
}
