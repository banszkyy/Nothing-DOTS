using Unity.Entities;
using UnityEngine;

[AddComponentMenu("Authoring/Radio")]
public class RadioAuthoring : MonoBehaviour
{
    [SerializeField] Transform? TransmissionPoint;
    [SerializeField, Range(-10f, 50f)] internal float TxPowerDbm = 30f;
    [SerializeField, Range(-5f, 20f)] internal float RxGainDbi = 0f;
    [SerializeField, Range(1f, 20f), Tooltip("lower = better receiver")] internal float NoiseFigureDb = 6f;
    [SerializeField] internal byte MinBeamHalvings;
    [SerializeField] internal byte MaxBeamHalvings;
    [SerializeField] internal byte BearingSectors;

    class Baker : Baker<RadioAuthoring>
    {
        public override void Bake(RadioAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, authoring.Make());
        }
    }

    internal Radio Make() => new()
    {
        WirelessTransmissionPoint = TransmissionPoint == null ? default : TransmissionPoint.localPosition,
        TxPowerDbm = TxPowerDbm,
        RxGainDbi = RxGainDbi,
        NoiseFigureDb = NoiseFigureDb,
        MinBeamHalvings = MinBeamHalvings,
        MaxBeamHalvings = MaxBeamHalvings,
        BearingSectors = BearingSectors,
    };
}
