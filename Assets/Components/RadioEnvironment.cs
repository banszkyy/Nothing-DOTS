using Unity.Entities;
using Unity.Mathematics;

struct RadioEnvironment : IComponentData
{
    public float PathLossExp;
    public float RefLossDb;
    public float ObstructionDb;
    public float BandwidthHz;
    public float RequiredSnrDb;

    public float SensitivityDbm(float noiseFigureDb) => -174f + 10f * math.log10(BandwidthHz) + noiseFigureDb + RequiredSnrDb;

    // TODO
    public static float Calibrate(float refBudgetDb, float refRange, float exp) => refBudgetDb - 10f * exp * math.log10(refRange);
}
