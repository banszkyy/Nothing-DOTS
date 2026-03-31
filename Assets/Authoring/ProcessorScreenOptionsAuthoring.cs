using System.Diagnostics.CodeAnalysis;
using Unity.Entities;
using UnityEngine;

[AddComponentMenu("Authoring/Processor Screen Options")]
class ProcessorScreenOptionsAuthoring : MonoBehaviour
{
    [SerializeField, NotNull] GameObject? ScreenPrefab = null;

    class Baker : Baker<ProcessorScreenOptionsAuthoring>
    {
        public override void Bake(ProcessorScreenOptionsAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponentObject<ProcessorScreenOptions>(entity, new()
            {
                ScreenPrefab = authoring.ScreenPrefab,
            });
        }
    }
}
