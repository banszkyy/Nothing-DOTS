using System;
using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using SaintsField.Playa;
#if UNITY_EDITOR
using Unity.EditorCoroutines.Editor;
using UnityEditor;
#endif
using UnityEngine;

class PlaceholderGenerator : MonoBehaviour
{
#if UNITY_EDITOR
    [SerializeField, NotNull] GameObject? Prefab = default;

    [Button]
    public void Generate()
    {
        Wrapper(v => EditorCoroutineUtility.StartCoroutine(GenerateImpl(v), this));
    }

    void Wrapper(Action<GameObject> callback)
    {
        if (PrefabUtility.IsPartOfPrefabAsset(gameObject))
        {
            callback.Invoke(gameObject);
            EditorUtility.SetDirty(gameObject);
        }
        else
        {
            Undo.RecordObject(gameObject, "Modify Object");

            callback.Invoke(gameObject);

            EditorUtility.SetDirty(gameObject);

            if (PrefabUtility.IsPartOfPrefabInstance(gameObject))
            {
                PrefabUtility.RecordPrefabInstancePropertyModifications(gameObject);
            }
        }
    }

    IEnumerator GenerateImpl(GameObject o)
    {
        yield return null;

        foreach (Component component in o.GetComponents<WirePlaceholderAuthoring>().Skip(1))
        {
            DestroyImmediate(component, false);
            yield return null;
        }

        if (!Prefab.TryGetComponent(out ConnectorAuthoring connectorAuthoring))
        {
            foreach (Component component in o.GetComponents<WirePlaceholderAuthoring>())
            {
                DestroyImmediate(component, false);
                yield return null;
            }
        }
        else
        {
            if (!o.TryGetComponent(out WirePlaceholderAuthoring newConnectorAuthoring))
            {
                newConnectorAuthoring = o.AddComponent<WirePlaceholderAuthoring>();
                yield return null;
            }

            newConnectorAuthoring.PortPositions = new Unity.Mathematics.float3[connectorAuthoring.PortPositions.Length];
            for (int i = 0; i < connectorAuthoring.PortPositions.Length; i++)
            {
                newConnectorAuthoring.PortPositions[i] = connectorAuthoring.PortPositions[i].position;
            }
        }

        yield return null;
    }
#endif
}
