using System.Collections.Generic;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.LocalSimulation)]
public partial class WorldLabelSystemClientSystem : SystemBase
{
    Transform? _canvas;
    readonly List<WorldLabel> _instances = new();

    protected override void OnUpdate()
    {
        if (_canvas == null)
        {
            if (_canvas == null)
            {
                foreach (var canvas in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include))
                {
                    if (canvas.name != "UICanvas") continue;
                    _canvas = canvas.transform;
                    break;
                }
            }
        }

        if (!SystemAPI.TryGetSingleton(out NetworkId networkId)) return;
        if (!SystemAPI.ManagedAPI.TryGetSingleton(out WorldLabelSettings config)) return;

        EntityCommandBuffer commandBuffer = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(World.Unmanaged);

        foreach (var (player, labels) in
            SystemAPI.Query<RefRO<Player>, DynamicBuffer<BufferedWorldLabel>>())
        {
            if (player.ValueRO.ConnectionId != networkId.Value) continue;

            foreach (var (_, command, entity) in
                SystemAPI.Query<RefRO<ReceiveRpcCommandRequest>, RefRO<DebugLabelRpc>>()
                .WithEntityAccess())
            {
                commandBuffer.DestroyEntity(entity);
                labels.Add(new BufferedWorldLabel()
                {
                    Position = command.ValueRO.Position,
                    Color = command.ValueRO.Color,
                    Text = command.ValueRO.Text,
                    DieAt = (float)SystemAPI.Time.ElapsedTime + 0.5f,
                });
            }

            for (int i = System.Math.Min(labels.Length, _instances.Count) - 1; i >= 0; i--)
            {
                WorldLabel value = _instances[i];

                if (value.TextMeshPro.text != labels[i].Text) value.TextMeshPro.text = labels[i].Text.ToString();

                Vector3 screenPoint = MainCamera.Camera.WorldToScreenPoint(labels[i].Position);
                bool isVisible = screenPoint.z > 0f;

                if (value.gameObject.activeSelf != isVisible)
                {
                    value.gameObject.SetActive(isVisible);
                }
                if (!isVisible) continue;

                screenPoint.z = 0f;
                value.GetComponent<RectTransform>().anchoredPosition = screenPoint;
            }

            for (int i = labels.Length; i < _instances.Count; i++)
            {
                if (!_instances[i].gameObject.activeSelf) continue;
                _instances[i].gameObject.SetActive(false);
            }

            for (int i = _instances.Count; i < labels.Length; i++)
            {
                GameObject o = Object.Instantiate(config.Prefab, Vector3.zero, Quaternion.identity, _canvas);
                _instances.Add(o.GetComponent<WorldLabel>());
            }

            break;
        }
    }


    public void OnDisconnect()
    {
        Debug.Log($"{DebugEx.ClientPrefix} Destroying debug labels");

        foreach (WorldLabel item in _instances)
        {
            Object.Destroy(item.gameObject);
        }
        _instances.Clear();
    }
}
