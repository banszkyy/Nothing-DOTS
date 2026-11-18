using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Unity.Entities;
using UnityEngine;

[AddComponentMenu("Authoring/Radio Environment")]
class RadioEnvironmentAuthoring : MonoBehaviour
{
    [SerializeField] internal float PathLossExp = 3.5f;
    [SerializeField] internal float RefLossDb = 54;
    [SerializeField] internal float ObstructionDb = 12;
    [SerializeField] internal float BandwidthHz = 25000;
    [SerializeField] internal float RequiredSnrDb = 10;

    [SerializeField, NotNull] internal AllPrefabs? AllPrefabs = null;

    class Baker : Baker<RadioEnvironmentAuthoring>
    {
        public override void Bake(RadioEnvironmentAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, authoring.Make());
        }
    }

    internal RadioEnvironment Make()
    {
        RadioAuthoring[] radios = AllPrefabs.EveryUnitLike.Select(v => v.TryGetComponent(out RadioAuthoring? radioAuthoring) ? radioAuthoring : null).Where(v => v != null).ToArray()!;
        RadioAuthoring? bestRadio = null;
        float bestAdvantage = float.NegativeInfinity;
        foreach (RadioAuthoring r in radios)
        {
            float v = r.RxGainDbi - r.NoiseFigureDb;
            if (bestRadio == null || v > bestAdvantage)
            {
                bestAdvantage = v;
                bestRadio = r;
            }
        }

        return new()
        {
            PathLossExp = PathLossExp,
            RefLossDb = RefLossDb,
            ObstructionDb = ObstructionDb,
            BandwidthHz = BandwidthHz,
            RequiredSnrDb = RequiredSnrDb,
            BestRadioReceiver = bestRadio == null ? default : bestRadio.Make(),
        };
    }
}
