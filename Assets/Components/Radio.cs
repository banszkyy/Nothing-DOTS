using Unity.Entities;

struct Radio : IComponentData
{
    public float TxPowerDbm;
    public float RxGainDbi;
    public float SensitivityDbm;
    public byte MinBeamHalvings;
    public byte MaxBeamHalvings;
    public byte BearingSectors;
}
