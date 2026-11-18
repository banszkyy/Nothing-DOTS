using Unity.Entities;
using Unity.Mathematics;

struct RadioEnvironment : IComponentData
{
    public float PathLossExp;
    public float RefLossDb;
    public float ObstructionDb;
    public float BandwidthHz;
    public float RequiredSnrDb;
    public Radio BestRadioReceiver;

    const float ThermalNoiseDbmPerHz = -174f; // kT at ~290 K
    public readonly float NoiseFloorDbm => ThermalNoiseDbmPerHz + (10f * math.log10(BandwidthHz));
    public readonly float SensitivityDbm(in Radio rx) => NoiseFloorDbm + rx.NoiseFigureDb + RequiredSnrDb;
}
