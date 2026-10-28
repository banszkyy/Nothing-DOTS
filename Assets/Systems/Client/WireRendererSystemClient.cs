using Unity.Mathematics;
using Unity.Entities;
using Unity.Transforms;
using Unity.NetCode;
using System.Collections.Generic;
using UnityEngine;
using System;
using UnityEngine.Pool;
using System.Diagnostics.CodeAnalysis;

[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.LocalSimulation)]
public partial class WireRendererSystemClient : SystemBase
{
    /*
    Entity _segments;

    protected override void OnCreate()
    {
        RequireForUpdate<WiresSettings>();
    }

    protected override void OnStopRunning()
    {
        Core.Destroy(EntityManager, _segments);
    }

    protected override void OnUpdate()
    {
        if (_segments == Entity.Null)
        {
            WiresSettings settings = SystemAPI.ManagedAPI.GetSingleton<WiresSettings>();
            Core.Create(EntityManager, out _segments, settings.Material);
        }

        Segment segment = Core.GetSegment(EntityManager, _segments);

        segment.Buffer.Clear();

        foreach (var (connector, connectorPos1, wires, ghost) in SystemAPI.Query<RefRO<Connector>, RefRO<LocalTransform>, DynamicBuffer<BufferedWire>, RefRO<GhostInstance>>().WithAll<Connector>())
        {
            foreach (BufferedWire connector2 in wires)
            {
                if (!connector2.GhostA.Equals(ghost.ValueRO)) continue;

                float3 connectorPos2 = default;
                foreach (var (_connector2, _connectorPos2, ghost2) in SystemAPI.Query<RefRO<Connector>, RefRO<LocalTransform>, RefRO<GhostInstance>>())
                {
                    if (!connector2.GhostB.Equals(ghost2.ValueRO)) continue;
                    connectorPos2 = _connectorPos2.ValueRO.TransformPoint(_connector2.ValueRO.ConnectorPosition);
                    break;
                }

                if (connectorPos2.Equals(default)) continue;

                float3 startPosition = connectorPos1.ValueRO.TransformPoint(connector.ValueRO.ConnectorPosition);
                float3 endPosition = connectorPos2;

                float l = math.distance(startPosition, endPosition);

                float3 prevPosition = startPosition;
                for (float i = 1f; i < l; i++)
                {
                    float3 p = math.lerp(startPosition, endPosition, i / l);
                    segment.Buffer.Add(new float3x2(prevPosition, p));
                    prevPosition = p;
                }
                segment.Buffer.Add(new float3x2(prevPosition, endPosition));
            }
        }

        Core.SetSegmentChanged(EntityManager, _segments);
    }
    */

    readonly struct WireId : IEquatable<WireId>
    {
        public readonly SpawnedGhost EntityA;
        public readonly SpawnedGhost EntityB;
        public readonly int ConnectorA;
        public readonly int ConnectorB;

        public WireId(BufferedWire v)
        {
            EntityA = v.GhostA;
            EntityB = v.GhostB;
            ConnectorA = v.PortA;
            ConnectorB = v.PortB;
        }

        public static implicit operator WireId(BufferedWire v) => new(v);

        public bool Equals(WireId other) =>
            EntityA.Equals(other.EntityA)
            && EntityB.Equals(other.EntityB)
            && ConnectorA == other.ConnectorA
            && ConnectorB == other.ConnectorB;
        public override bool Equals(object? obj) => obj is WireId other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(EntityA, EntityB);
    }

    [NotNull] WiresSettings? Settings = null;
    readonly Dictionary<WireId, LineRenderer> Lines = new();
    readonly Dictionary<WireId, LineRenderer> PlaceholderLines = new();
    [NotNull] ObjectPool<LineRenderer>? LinesPool = null;

    protected override void OnCreate()
    {
        RequireForUpdate<WiresSettings>();
        LinesPool = new ObjectPool<LineRenderer>(
            createFunc: () =>
            {
                LineRenderer line = new GameObject("Wire").AddComponent<LineRenderer>();
                line.widthCurve = AnimationCurve.Constant(0f, 1f, 0.05f);
                return line;
            },
            actionOnGet: static v => v.gameObject.SetActive(true),
            actionOnRelease: static v => v.gameObject.SetActive(false),
            actionOnDestroy: static v => UnityEngine.Object.Destroy(v.gameObject)
        );
    }

    const float WireResolution = 1f;
    const float WireCatenary = 20f;

    public static Vector3[] GenerateWire(float3 startPosition, float3 endPosition)
    {
        float distance = Vector3.Distance(startPosition, endPosition);
        int nPoints = (int)((distance / WireResolution) + 1);

        if (nPoints <= 2)
        {
            return new Vector3[] { startPosition, endPosition };
        }

        Vector3[] wirePoints = new Vector3[nPoints];
        GenerateWire(startPosition, endPosition, wirePoints);
        return wirePoints;
    }

    static float Catenary(float a, float t) => a * (float)Math.Cosh(t / a);

    public static void GenerateWire(float3 startPosition, float3 endPosition, Span<Vector3> wirePoints)
    {
        float distance = math.distance(startPosition, endPosition);
        float wireResolution = distance / (wirePoints.Length - 1);

        wirePoints[0] = startPosition;
        wirePoints[^1] = endPosition;

        float3 dir = math.normalize(endPosition - startPosition);
        float offset = Catenary(WireCatenary, -distance / 2);

        for (int i = 1; i < wirePoints.Length - 1; ++i)
        {
            float3 wirePoint = startPosition + (i * wireResolution * dir);

            float x = (i * wireResolution) - (distance / 2);
            wirePoint.y -= offset - Catenary(WireCatenary, x);

            wirePoints[i] = wirePoint;
        }
    }

    public static void DrawWire(float3 startPosition, float3 endPosition, Color color, float duration, bool depthTest)
    {
        float wireResolution = WireResolution;
        float distance = math.distance(startPosition, endPosition);
        int nPoints = (int)((distance / wireResolution) + 1);
        wireResolution = distance / (nPoints - 1);

        Vector3 p = startPosition;

        float3 dir = math.normalize(endPosition - startPosition);
        float offset = Catenary(WireCatenary, -distance / 2);

        for (int i = 1; i < nPoints - 1; ++i)
        {
            float3 wirePoint = startPosition + (i * wireResolution * dir);

            float t = (i * wireResolution) - (distance / 2);
            wirePoint.y -= offset - Catenary(WireCatenary, t);

            DebugEx.DrawLine(p, wirePoint, color, duration, depthTest);
            p = wirePoint;
        }

        DebugEx.DrawLine(p, endPosition, color, duration, depthTest);
    }

    protected override void OnUpdate()
    {
        Settings = SystemAPI.ManagedAPI.GetSingleton<WiresSettings>();

        foreach (KeyValuePair<WireId, LineRenderer> line in Lines)
        {
            bool a = false;
            bool b = false;

            foreach (var (ghost, wires) in SystemAPI.Query<RefRO<GhostInstance>, DynamicBuffer<BufferedWire>>())
            {
                if (line.Key.EntityA.Equals(ghost.ValueRO))
                {
                    foreach (var wire in wires)
                    {
                        if (line.Key.Equals(wire))
                        {
                            a = true;
                            if (b) goto ok;
                        }
                    }
                }
                else if (line.Key.EntityB.Equals(ghost.ValueRO))
                {
                    foreach (var wire in wires)
                    {
                        if (line.Key.Equals(wire))
                        {
                            b = true;
                            if (a) goto ok;
                        }
                    }
                }
            }

            if (!a || !b)
            {
                LinesPool.Release(line.Value);
                Lines.Remove(line.Key);
                break;
            }

        ok:;
        }

        foreach (KeyValuePair<WireId, LineRenderer> line in PlaceholderLines)
        {
            bool a = false;
            bool b = false;

            foreach (var (ghost, wires) in SystemAPI.Query<RefRO<GhostInstance>, DynamicBuffer<BufferedWire>>())
            {
                if (line.Key.EntityA.Equals(ghost.ValueRO))
                {
                    foreach (var wire in wires)
                    {
                        if (line.Key.Equals(wire))
                        {
                            a = true;
                            if (b) goto ok;
                        }
                    }
                }
                else if (line.Key.EntityB.Equals(ghost.ValueRO))
                {
                    foreach (var wire in wires)
                    {
                        if (line.Key.Equals(wire))
                        {
                            b = true;
                            if (a) goto ok;
                        }
                    }
                }
            }

            if (!a || !b)
            {
                LinesPool.Release(line.Value);
                PlaceholderLines.Remove(line.Key);
                break;
            }

        ok:;
        }

        foreach (var (connectorA, connectorPosA, wires, ghostA, entityA) in SystemAPI.Query<RefRO<Connector>, RefRO<LocalTransform>, DynamicBuffer<BufferedWire>, RefRO<GhostInstance>>().WithEntityAccess())
        {
            foreach (BufferedWire wire in wires)
            {
                if (!wire.GhostA.Equals(ghostA.ValueRO)) continue;

                bool isPlaceholder = SystemAPI.HasComponent<WirePlaceholder>(entityA);

                float3 startPosition = connectorPosA.ValueRO.TransformPoint(connectorA.ValueRO.PortPositions[wire.PortA]);
                float3 endPosition = default;

                foreach (var (connectorB, connectorPosB, ghostB, entityB) in SystemAPI.Query<RefRO<Connector>, RefRO<LocalTransform>, RefRO<GhostInstance>>().WithEntityAccess())
                {
                    if (!wire.GhostB.Equals(ghostB.ValueRO)) continue;
                    isPlaceholder = isPlaceholder || SystemAPI.HasComponent<WirePlaceholder>(entityB);
                    endPosition = connectorPosB.ValueRO.TransformPoint(connectorB.ValueRO.PortPositions[wire.PortB]);
                    break;
                }

                if (endPosition.Equals(default)) continue;

                WireId wireId = wire;

                Dictionary<WireId, LineRenderer> _lines = isPlaceholder ? PlaceholderLines : Lines;
                if (!_lines.TryGetValue(wireId, out LineRenderer? line) || line == null)
                {
                    line = _lines[wireId] = LinesPool.Get();
                    line.material = isPlaceholder ? Settings.PlaceholderMaterial : Settings.Material;
                }

                Vector3[] points = GenerateWire(startPosition, endPosition);
                line.positionCount = points.Length;
                line.SetPositions(points);
            }
        }
    }

    public void OnDisconnect()
    {
        Debug.Log($"{DebugEx.ClientPrefix} Destroying wires");

        Lines.Clear();
        PlaceholderLines.Clear();
        LinesPool.Clear();
    }
}
