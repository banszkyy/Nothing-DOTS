using System.Diagnostics.CodeAnalysis;
using TMPro;
using Unity.Entities;
using UnityEngine;

[AddComponentMenu("Authoring/Processor Screen")]
class ProcessorScreenAuthoring : MonoBehaviour
{
    [SerializeField, NotNull] Canvas? Canvas = default;

    class Baker : Baker<ProcessorScreenAuthoring>
    {
        public override void Bake(ProcessorScreenAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent<ProcessorScreen>(entity, new()
            {
                Size = authoring.Canvas.GetComponent<RectTransform>().sizeDelta,
                Position = authoring.Canvas.transform.position,
                Rotation = authoring.Canvas.transform.rotation,
                FontSize = authoring.Canvas.gameObject.GetComponentInChildren<TextMeshProUGUI>().fontSize,
            });
        }
    }
}
