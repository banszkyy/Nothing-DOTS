using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

struct Radio : IComponentData
{
    public float3 WirelessTransmissionPoint;
    public float TxPowerDbm;
    public float RxGainDbi;
    public float NoiseFigureDb;
    public byte MinBeamHalvings;
    public byte MaxBeamHalvings;
    public byte BearingSectors;

    public FixedList128Bytes<BufferedUnitTransmission> IncomingTransmissions;
    public FixedList128Bytes<BufferedUnitTransmissionOutgoing> OutgoingTransmissions;
}
