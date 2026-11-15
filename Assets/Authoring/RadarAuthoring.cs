using System;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

[AddComponentMenu("Authoring/Radar")]
class RadarAuthoring : MonoBehaviour
{
    [SerializeField] GameObject? Radar = default;
    [SerializeField] internal Vector2 apertureSize;
    [SerializeField, Min(0f)] internal float wavelength;
    [SerializeField, Min(0f)] internal float peakPower;
    [SerializeField, Min(0f)] internal float sensitivity;
    [SerializeField, Min(0f)] internal float rangeResolution;
    [SerializeField, Min(0f)] internal float dwellTime;
    [SerializeField] internal Vector2 slewRate;
    [SerializeField, Range(-1f, 1f)] internal float CosYawTravel;
    [SerializeField, Range(-1f, 1f)] internal float SinPitchLimit;
    [SerializeField, Range(-1f, 1f)] internal float CosElec;
    [SerializeField, Min(0f)] internal int maxContacts;
    [SerializeField] GameObject? RequestLED = default;
    [SerializeField] GameObject? ResponseLED = default;

    class Baker : Baker<RadarAuthoring>
    {
        public override void Bake(RadarAuthoring authoring)
        {
            //using BlobBuilder builder = new(Allocator.Temp);
            //ref RadarHardware hardware = ref builder.ConstructRoot<RadarHardware>();
            //hardware.ApertureSize = authoring.ApertureSize;
            //hardware.PeakPower = authoring.PeakPower;
            //hardware.Sensitivity = authoring.Sensitivity;
            //hardware.SlewRate = authoring.SlewRate;
            //hardware.MechCosTravel = authoring.MechCosTravel;
            //hardware.ElecCosFov = authoring.ElecCosFov;
            //hardware.BeamSwitchTime = authoring.BeamSwitchTime;
            //hardware.MaxContacts = authoring.MaxContacts;
            //hardware.MaxTracks = authoring.MaxTracks;
            //hardware.MinBeamTan = authoring.MinBeamTan;
            //hardware.PeakGain = authoring.PeakGain;
            float3 fwd = new(0, 0, 1);

            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent<Radar>(entity, new()
            {
                Transform = authoring.Radar != null ? GetEntity(authoring.Radar, TransformUsageFlags.Dynamic) : Entity.Null,
                //Hardware = builder.CreateBlobAssetReference<RadarHardware>(Allocator.Persistent),

                Owner = entity,
                MinBeamTan = 0.5f * authoring.wavelength / (float2)authoring.apertureSize,
                PeakPower = authoring.peakPower,
                Sensitivity = authoring.sensitivity,
                RangeResolution = authoring.rangeResolution,
                DwellTime = authoring.dwellTime,
                SlewRate = authoring.slewRate,
                CosYawTravel = authoring.CosYawTravel,
                SinPitchLimit = authoring.SinPitchLimit,
                CosElec = authoring.CosElec,
                MaxContacts = authoring.maxContacts,
                ReqCenter = fwd,
                ReqSector = MathF.PI,
                ReqHalvings = 0,
                ReqDirty = false,
                Aim = fwd,
                Beam = fwd,
                Center = fwd,
                BeamTan = new float2(RadarMath.OmniTan),
                //Rng = Unity.Mathematics.Random.CreateFromIndex((uint)Math.Max(1, entity.Index)),
                RequestLED = new(authoring.RequestLED != null ? GetEntity(authoring.RequestLED, TransformUsageFlags.Dynamic) : Entity.Null),
                ResponseLED = new(authoring.ResponseLED != null ? GetEntity(authoring.ResponseLED, TransformUsageFlags.Dynamic) : Entity.Null),
            });
            AddBuffer<RadarContact>(entity);
        }
    }

    void OnDrawGizmosSelected()
    {
        float range = RadarMath.MaxRange(new float2(RadarMath.OmniTan), peakPower, sensitivity, 1f);
        Gizmos.color = Color.white;
        Gizmos.DrawWireSphere(transform.position, range);
    }
}
