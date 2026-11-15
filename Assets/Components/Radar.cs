using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Random = Unity.Mathematics.Random;

public struct Radar : IComponentData
{
    public const float RadarRadius = 80f;

    public Entity Transform;
    //public BlobAssetReference<RadarHardware> Hardware;

    // ---------------- hardware (constant after baking) ----------------
    public Entity Owner;            // unit carrying the radar; never detected by itself
    public float2 MinBeamTan;       // narrowest beam HALF-width as a tangent (x = mount right, y = mount up)
    public float PeakPower;         // transmit power (arbitrary units, tune together with Sensitivity)
    public float Sensitivity;       // receiver noise floor, same units
    public float RangeResolution;   // metres; range error scale (c*tau/2 in real life)
    public float DwellTime;         // seconds spent on one beam position
    public float2 SlewRate;          // rad/s of the physical dish; 0 = cannot move
    public float CosYawTravel;         // cos(max dish deflection from mount forward); -1 = free rotation, 1 = fixed
    public float SinPitchLimit;
    public float CosElec;           // cos(electronic steering half-angle around the dish axis); 1 = none
    public int MaxContacts;         // contact buffer cap

    // ---------------- request (written by the interpreter bridge) ----------------
    public float3 ReqCenter;        // sector centre (any non-zero vector, zero = mount forward)
    public float ReqSector;         // sector HALF-angle in radians, PI = everything
    public int ReqHalvings;         // 0 = omni, each +1 halves the beam in both axes
    public bool ReqDirty;           // bridge sets true after writing; control system clears it

    // ---------------- state (written by the two radar systems only) ----------------
    public float3 Aim;              // physical dish axis (drive your dish visual from this)
    public float3 Beam;             // actual beam direction = Aim, plus electronic offset
    public float2 BeamTan;          // effective beam half-widths after clamping to MinBeamTan
    public float3 Center;           // normalized sector centre
    public float Sector;            // clamped sector half-angle
    public float ScanTheta;         // spiral position: angle from Center
    public float ScanPhi;           // spiral position: rotation around Center
    public float DwellLeft;
    public bool PreparingPulse;     // control -> sweep handoff: "illuminate Beam now"
    public bool PulseReady;         // control -> sweep handoff: "illuminate Beam now"
    public bool SinglePosition;     // beam already covers the whole sector: stare at Center, no spiral
    public bool RingScan;           // only a yaw rotor exists: the sweep is a ring around mount Y, not a spiral
    public float3 RingCenter;       // horizontal direction the yaw sweep is centred on
    public float YawHalf;           // half-extent of the yaw sweep in radians; PI = full 360 degrees
    public float Revisit;           // estimate after a request, measured sweep time afterwards
    public float ScanSign;          // yaw sweep direction, +1 / -1
    public double SweepStart;
    public uint SweepCount;
    //public Random Rng;              // per-radar noise source (state must be non-zero)

    [GhostField] public BlinkingLED RequestLED;
    [GhostField] public BlinkingLED ResponseLED;
}

// ---- components on things that can be detected (add to anything: units, buildings, props) ----
public struct RadarSignature : IComponentData { public float RadarCrossSection; }       // radar cross-section

// Placeholder: replace with your own world-space velocity component (or PhysicsVelocity.Linear).
public struct Velocity : IComponentData { public float3 Value; }

public static class RadarMath
{
    public const float OmniTan = 4f;        // halvings = 0 -> beam half-width tangent of 4 (gain 1, isotropic)
    public const float Overlap = 1.6f;      // beam spacing in beam half-widths (2.0 = beams just touch at -3 dB)
    public const float AlignCos = 0.9999f;  // beam counts as "on position" within ~0.8 degrees

    // Rough radar_set() answer: beam positions needed * time per position.
    public static float EstimateRevisit(in Radar radar)
    {
        if (radar.SinglePosition) return radar.DwellTime;
        if (radar.RingScan)
        {
            float yawStep = math.atan(radar.BeamTan.x) * Overlap;
            float n = math.max(1f, math.ceil(2f * radar.YawHalf / yawStep));
            return n * (radar.DwellTime + yawStep / radar.SlewRate.x);
        }
        float sectorSolid = 2f * math.PI * (1f - math.cos(radar.Sector));
        float cell = Overlap * Overlap * radar.BeamTan.x * radar.BeamTan.y;
        float beams = math.max(1f, math.ceil(sectorSolid / cell));
        float maxSlew = math.cmax(radar.SlewRate);
        float slew = maxSlew > 0f ? math.atan(Overlap * math.sqrt(radar.BeamTan.x * radar.BeamTan.y)) / maxSlew : 0f;
        return beams * (radar.DwellTime + slew);
    }

    // Range at which a target of the given RCS is detected 63% of the time on boresight (snr = 1). For tuning/overlays.
    public static float MaxRange(float2 beamTan, float peakPower, float sensitivity, float radarCrossSection)
    {
        float g = Gain(beamTan);
        return math.pow(peakPower * g * g * radarCrossSection / sensitivity, 0.25f);
    }

    // Antenna gain relative to omni. Halving both beam axes = 4x gain.
    public static float Gain(float2 beamTan) => math.max(1f, OmniTan * OmniTan / (beamTan.x * beamTan.y));

    // Isotropic beam: no direction exists, so the dish must not move and the sweep skips the beam test.
    public static bool IsOmni(float2 beamTan) => Gain(beamTan) <= 1.0001f;

    // Orthonormal beam frame for a boresight: right = cross(up, fwd) (Unity is left-handed).
    // Keeps the beam's "tall" axis aligned with mount up, so fan beams stay upright.
    public static void BeamFrame(float3 forward, out float3 right, out float3 up)
    {
        float3 r = math.cross(new float3(0, 1, 0), forward);
        float l = math.length(r);
        right = l > 1e-4f ? r / l : new float3(1, 0, 0);
        up = math.cross(forward, right);
    }

    // Any stable pair of axes perpendicular to c.
    public static void PerpBasis(float3 c, out float3 u, out float3 v)
    {
        BeamFrame(c, out u, out v);
    }

    // Radius of the beam ellipse (in tangent-plane units) along unit direction t, which must lie in the plane of right/up.
    public static float EllipseRadius(float3 t, float3 right, float3 up, float2 tan)
    {
        float a = math.dot(t, right) / tan.x;
        float b = math.dot(t, up) / tan.y;
        return math.rsqrt(a * a + b * b);
    }

    public static float3 ClampToCone(float3 d, float3 axis, float cosHalf)
    {
        float c = math.dot(d, axis);
        if (c >= cosHalf) return d;
        float3 perp = d - axis * c;
        float l = math.length(perp);
        if (l > 1e-5f) perp /= l;
        else { BeamFrame(axis, out perp, out _); }
        float s = math.sqrt(math.max(0f, 1f - cosHalf * cosHalf));
        return axis * cosHalf + perp * s;
    }

    public static float3 RotateToward(float3 from, float3 to, float maxAngle)
    {
        float c = math.clamp(math.dot(from, to), -1f, 1f);
        if (math.acos(c) <= maxAngle) return to;
        float3 perp = to - from * c;
        float l = math.length(perp);
        if (l > 1e-5f) perp /= l;
        else { BeamFrame(from, out perp, out _); }
        math.sincos(maxAngle, out float s, out float cs);
        return from * cs + perp * s;
    }

    // cos(a + b) from cos(a), cos(b) without trig; -1 when the cones together cover the sphere.
    public static float CombineCos(float ca, float cb)
    {
        if (ca + cb < 0f) return -1f;       // a + b > PI
        float sa = math.sqrt(math.max(0f, 1f - ca * ca));
        float sb = math.sqrt(math.max(0f, 1f - cb * cb));
        return ca * cb - sa * sb;
    }

    // Horizontal (mount Y = up) component of v as a unit vector; fallback if v is vertical.
    static float3 Horizontal(float3 v, float3 fallback)
    {
        float3 h = new(v.x, 0f, v.z);
        float l = math.length(h);
        return l > 1e-4f ? h / l : fallback;
    }

    // Dish pose closest to `want` that the rotors can reach. A missing rotor keeps its current value.
    //   yaw   : horizontal part of the direction, clamped to the yaw travel cone around mount forward
    //   pitch : the Y component itself (sin of elevation), clamped to +-SinPitchLimit
    public static float3 ClampToTravel(in Radar radar, float3 want)
    {
        var fwd = new float3(0, 0, 1);
        float3 aimH = Horizontal(radar.Aim, fwd);
        float3 h = Horizontal(want, aimH);
        float3 yawDir = radar.SlewRate.x > 0f ? ClampToCone(h, fwd, radar.CosYawTravel) : aimH;
        float sy = radar.SlewRate.y > 0f ? math.clamp(want.y, -radar.SinPitchLimit, radar.SinPitchLimit) : radar.Aim.y;
        return yawDir * math.sqrt(math.max(0f, 1f - sy * sy)) + new float3(0, 1, 0) * sy;
    }

    // Each rotor moves independently at its own rate: yaw rotates the horizontal direction about mount Y,
    // pitch changes the elevation. Angles appear only here, as scalar speeds.
    public static float3 MoveDish(in Radar radar, float3 target, float deltaTime)
    {
        var fwd = new float3(0, 0, 1);
        float3 aimH = Horizontal(radar.Aim, fwd);
        float3 newH = RotateToward(aimH, Horizontal(target, aimH), radar.SlewRate.x * deltaTime);

        float e = math.asin(math.clamp(radar.Aim.y, -1f, 1f));
        float et = math.asin(math.clamp(target.y, -1f, 1f));
        float step = radar.SlewRate.y * deltaTime;
        e += math.clamp(et - e, -step, step);
        math.sincos(e, out float se, out float ce);
        return newH * ce + new float3(0, 1, 0) * se;
    }

    public static float Gaussian(ref Random random)
    {
        float u1 = math.max(random.NextFloat(), 1e-6f);
        float u2 = random.NextFloat();
        return math.sqrt(-2f * math.log(u1)) * math.cos(2f * math.PI * u2);
    }

    // Unit-mean exponential: models target echo fluctuation (Swerling I).
    public static float Fluctuation(ref Random random) => -math.log(math.max(random.NextFloat(), 1e-6f));

    public static byte ToLevel(float snr) => (byte)math.clamp(40f * math.log10(snr), 0f, 255f);   // 4 steps per dB, 0 = below noise

    // Keeps buffer sorted by range, one entry per source, capped.
    public static void InsertContact(DynamicBuffer<RadarContact> buffer, in RadarContact contact, int max)
    {
        for (int i = 0; i < buffer.Length; i++)
            if (buffer[i].Source == contact.Source) { buffer.RemoveAt(i); break; }

        float d2 = math.lengthsq(contact.Pos);
        int idx = buffer.Length;
        for (int i = 0; i < buffer.Length; i++)
            if (math.lengthsq(buffer[i].Pos) > d2) { idx = i; break; }

        if (idx >= max) return;
        buffer.Insert(idx, contact);
        if (buffer.Length > max) buffer.RemoveAt(buffer.Length - 1);
    }
}
