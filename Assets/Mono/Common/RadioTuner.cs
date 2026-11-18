using SaintsField;
using Unity.Mathematics;
using UnityEngine;

class RadioTuner : MonoBehaviour
{
    [SerializeField, Required] RadioAuthoring? Transmitter;
    [SerializeField, Required] RadioAuthoring[]? Receivers;
    [SerializeField, Required] RadioEnvironmentAuthoring? RadioEnvironment;
    [SerializeField] bool Aim;
    [SerializeField] Transform? AimPosition;
    [SerializeField] byte BeamHalvings;

    void OnDrawGizmos()
    {
        if (Transmitter == null) return;
        if (Receivers == null) return;
        if (RadioEnvironment == null) return;

        Radio tx = Transmitter.Make();
        RadioEnvironment env = RadioEnvironment.Make();

        foreach (RadioAuthoring receiver in Receivers)
        {
            if (receiver == null) continue;
            Radio rx = receiver.Make();
            float d = WirelessTransmissionSystemServer.MaxRange(tx, tx.MinBeamHalvings, rx, env, 0, 0f);
            DebugEx.DrawSphere(transform.position, d, Color.cyan);

            d = WirelessTransmissionSystemServer.MaxRange(tx, tx.MaxBeamHalvings, rx, env, 0, 0f);
            DebugEx.DrawSphere(transform.position, d, Color.green);
        }

        if (!Aim || AimPosition == null) return;

        float maxRadius = WirelessTransmissionSystemServer.MaxRange(tx, BeamHalvings, env.BestRadioReceiver, env);
        DebugEx.DrawSphere(transform.position, maxRadius, Color.blue);

        const float range = 100f;
        const float step = 2f;

        float3 sourcePoint = transform.position;

        for (float x = -range; x <= range; x += step)
        {
            for (float z = -range; z <= range; z += step)
            {
                float3 receiverPoint = new(x, AimPosition.position.y, z);

                float3 aim = math.normalizesafe(AimPosition.position - transform.position, new float3(0f, -1f, 0f));

                SendRequest req = new()
                {
                    Aim = aim,
                    BeamHalvings = BeamHalvings,
                };

                float n = 0;

                foreach (RadioAuthoring receiver in Receivers)
                {
                    if (receiver == null) continue;
                    Radio rx = receiver.Make();
                    if (WirelessTransmissionSystemServer.TryDeliver(tx, transform.InverseTransformPoint(sourcePoint), req, rx, transform.InverseTransformPoint(receiverPoint), env, 0, 0f, out _))
                    {
                        n++;
                    }
                }

                if (n == 0) continue;

                DebugEx.DrawBox(receiverPoint, step - 0.1f, Color.Lerp(Color.black, Color.green, n / Receivers.Length));
            }
        }

        float rangeMain = WirelessTransmissionSystemServer.MaxRange(tx, BeamHalvings, env.BestRadioReceiver, env, offAxisDeg: 0f);
        float rangeFloor = WirelessTransmissionSystemServer.MaxRange(tx, BeamHalvings, env.BestRadioReceiver, env, offAxisDeg: 180f);

        int diff = (int)Mathf.Ceil(maxRadius / Cell.Size);
        for (int x = -diff; x <= diff; x++)
        {
            for (int z = -diff; z <= diff; z++)
            {
                Cell p = Cell.ToGrid(transform.position) + new Cell(x, z);
                if (!WirelessTransmissionSystemServer.IsCellCovered(transform.position, new SendRequest() { BeamHalvings = BeamHalvings, Aim = math.normalizesafe(AimPosition.position - transform.position, new float3(0f, -1f, 0f)) }, p, tx, env, rangeMain, rangeFloor))
                {
                    continue;
                }

                Cell.Draw(p, Color.gray);
            }
        }
    }
}
