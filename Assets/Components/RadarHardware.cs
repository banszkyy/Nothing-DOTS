using Unity.Mathematics;

public struct RadarHardware
{
    public float2 ApertureSize;     // m, antenna local X (width) and Y (height)
    public float PeakPower;        // weak effect on range, strong on ESM signature
    public float Sensitivity;      // receiver noise floor
    public float SlewRate;         // rad/s, 0 = cannot move mechanically
    public float MechCosTravel;    // cos(max deflection from mount forward); 1 = none
    public float ElecCosFov;       // cos(electronic steering half-angle); 1 = none
    public float BeamSwitchTime;   // s between dwells (~0 for electronic)
    public byte MaxContacts;
    public byte MaxTracks;        // 0 = no track capability (baker omits RadarTrack buffer)
    public float2 MinBeamTan;       // derived at bake: ~λ / ApertureSize, per antenna axis
    public float PeakGain;         // derived at bake: ∝ 1 / (MinBeamTan.x * MinBeamTan.y)
}

