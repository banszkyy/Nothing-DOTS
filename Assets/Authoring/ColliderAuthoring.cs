using Unity.Entities;
using UnityEngine;

[AddComponentMenu("Authoring/Collider")]
public class ColliderAuthoring : MonoBehaviour
{
    [SerializeField] internal bool IsStatic = default;

    class Baker : Baker<ColliderAuthoring>
    {
        public override void Bake(ColliderAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);

            if (!authoring.gameObject.TryGetComponent<UnityEngine.Collider>(out UnityEngine.Collider? collider))
            {
                Debug.LogWarning($"{DebugEx.BakingPrefix} No collider attached to Game Object", authoring.gameObject);
                return;
            }

            Bounds bounds = default;
            static void ExtendBounds(ref Bounds bounds, Transform root, GameObject gameObject)
            {
                foreach (MeshRenderer renderer in gameObject.GetComponents<MeshRenderer>())
                {
                    bounds.Encapsulate(new Bounds(root.InverseTransformPoint(renderer.bounds.center), renderer.bounds.extents));
                }
                foreach (Transform child in gameObject.transform)
                {
                    ExtendBounds(ref bounds, root, child.gameObject);
                }
            }
            ExtendBounds(ref bounds, authoring.transform, authoring.gameObject);

            AddComponent<MeshBounds>(entity, new()
            {
                Bounds = bounds,
            });

            switch (collider)
            {
                case UnityEngine.SphereCollider v:
                {
                    AddComponent<Collider>(entity, new SphereCollider(
                        authoring.IsStatic,
                        v.radius < 0.01f ? 0f : v.radius,
                        v.center
                    ));
                    break;
                }
                case BoxCollider v:
                {
                    AddComponent<Collider>(entity, new AABBCollider(
                        authoring.IsStatic,
                        new()
                        {
                            Center = v.center,
                            Extents = v.size / 2f,
                        }
                    ));
                    break;
                }
                default:
                    Debug.LogError($"Collider \"{collider.GetType().Name}\" not implemented", authoring.gameObject);
                    break;
            }
        }
    }
}
