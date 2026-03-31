using System.Collections;
using System.Diagnostics.CodeAnalysis;
using SaintsField.Playa;
#if UNITY_EDITOR
using Unity.EditorCoroutines.Editor;
#endif
using UnityEngine;

class HologramGenerator : MonoBehaviour
{
#if UNITY_EDITOR
    [SerializeField, NotNull] GameObject? Prefab = default;
    [SerializeField, NotNull] Material? Material = default;

    [Button]
    public void Generate() => EditorCoroutineUtility.StartCoroutine(GenerateImpl(), this);

    IEnumerator GenerateImpl()
    {
        transform.position = default;

        WaitForEndOfFrame wait = new();

        yield return wait;
        yield return wait;

        GameObject[] children = new GameObject[transform.childCount];
        for (int i = 0; i < transform.childCount; i++)
        {
            children[i] = transform.GetChild(i).gameObject;
        }

        foreach (GameObject child in children)
        {
            DestroyImmediate(child, false);
            yield return wait;
        }

        yield return wait;
        yield return wait;

        foreach (UnityEngine.Collider collider in GetComponents<UnityEngine.Collider>())
        {
            DestroyImmediate(collider, false);
            yield return wait;
        }

        yield return wait;
        yield return wait;

        if (Prefab.TryGetComponent(out BoxCollider boxCollider))
        {
            BoxCollider newBoxCollider = gameObject.AddComponent<BoxCollider>();
            newBoxCollider.center = boxCollider.center;
            newBoxCollider.size = boxCollider.size;
        }
        else if (Prefab.TryGetComponent(out UnityEngine.SphereCollider sphereCollider))
        {
            UnityEngine.SphereCollider newSphereCollider = gameObject.AddComponent<UnityEngine.SphereCollider>();
            newSphereCollider.center = sphereCollider.center;
            newSphereCollider.radius = sphereCollider.radius;
        }

        yield return wait;
        yield return wait;

        MeshRenderer[] renderers = Prefab.GetComponentsInChildren<MeshRenderer>(false);
        foreach (MeshRenderer renderer in renderers)
        {
            MeshFilter meshFilter = renderer.GetComponent<MeshFilter>();

            GameObject newObject = new(renderer.name, typeof(MeshRenderer), typeof(MeshFilter));
            newObject.transform.SetParent(transform);

            MeshFilter newMeshFilter = newObject.GetComponent<MeshFilter>();
            MeshRenderer newRenderer = newObject.GetComponent<MeshRenderer>();

            newMeshFilter.sharedMesh = meshFilter.sharedMesh;
            newRenderer.sharedMaterial = Material;

            newObject.transform.SetPositionAndRotation(renderer.transform.position, renderer.transform.rotation);
            newObject.transform.localScale = renderer.transform.localScale;
            yield return wait;
        }
    }
#endif
}
