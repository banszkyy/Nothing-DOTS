#pragma warning disable IDE0051 // Remove unused private members

using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using SaintsField;
using SaintsField.Playa;
using Unity.EditorCoroutines.Editor;
using Unity.Entities.Hybrid.Baking;
using Unity.NetCode;


#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;

[CreateAssetMenu(fileName = "Prefabs", menuName = "Game/Prefabs")]
class AllPrefabs : ScriptableObject
{
    [Header("Abstract")]
    [SerializeField, NotNull] public GameObject? PlayerPrefab = default;
    [SerializeField, NotNull] public GameObject? VirtualPlayerPrefab = default;

    [Header("Items")]
    [SerializeField, NotNull] public GameObject? Resource = default;

    [Header("Startkit")]
    [SerializeField, NotNull] public BuildingPrefab? PlayerInterface = default;
    [SerializeField, NotNull] public UnitPrefab? Builder = default;

    [Header("All")]
    [SerializeField, NotNull, ReadOnly] public UnitPrefab[]? Units = default;
    [SerializeField, NotNull, ReadOnly] public BuildingPrefab[]? Buildings = default;
    [SerializeField, NotNull, ReadOnly] public ProjectilePrefab[]? Projectiles = default;
    [SerializeField, NotNull, ReadOnly] public ResearchMetadata[]? Researches = default;

#if UNITY_EDITOR
    [Button("Load Files")]
    void LoadFiles()
    {
        static TAsset[] LoadFromDirectory<TAsset>(string directory) where TAsset : Object
        {
            List<TAsset> result = new();
            foreach (string path in Directory.GetFiles(directory, "*.asset"))
            {
                TAsset? asset = AssetDatabase.LoadAssetAtPath<TAsset>(path.Replace(Application.dataPath, "Assets"));
                if (asset == null) continue;
                result.Add(asset);
            }
            return result.ToArray();
        }

        Units = LoadFromDirectory<UnitPrefab>(Path.Combine(Application.dataPath, "Game", "Units"));
        Buildings = LoadFromDirectory<BuildingPrefab>(Path.Combine(Application.dataPath, "Game", "Buildings"));
        Projectiles = LoadFromDirectory<ProjectilePrefab>(Path.Combine(Application.dataPath, "Game", "Projectiles"));
        Researches = LoadFromDirectory<ResearchMetadata>(Path.Combine(Application.dataPath, "Game", "Researches"));

        EditorUtility.SetDirty(this);
        AssetDatabase.SaveAssetIfDirty(this);
    }

    [Header("Thumbnail Generator")]
    [SerializeField] Vector3 viewDirection = new(1f, 1.2f, -1f);
    [SerializeField, Range(10f, 90f)] float fieldOfView = 30f;
    [SerializeField] float padding = 1.1f;
    [SerializeField] Color background = new(0, 0, 0, 0);
    [SerializeField] int resolution = 256;

    const string OutputFolder = "Assets/Generated";
    const int CaptureLayer = 9;

    [Button("Generate Thumbnails")]
    public void GenerateThumbnails()
    {
        Directory.CreateDirectory(OutputFolder);

        List<(string ThumbnailPath, IGeneralUnit Object)> generated = new();

        GameObject camGO = new("~ThumbnailCamera") { hideFlags = HideFlags.HideAndDontSave };
        Camera cam = camGO.AddComponent<Camera>();
        cam.orthographic = false;
        cam.fieldOfView = fieldOfView;
        cam.aspect = 1f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = background;
        cam.cullingMask = 1 << CaptureLayer;
        cam.enabled = false;

        RenderTexture rt = new(resolution, resolution, 24, RenderTextureFormat.ARGB32) { antiAliasing = 8 };
        Texture2D tex = new(resolution, resolution, TextureFormat.RGBA32, false);
        RenderTexture prevActive = RenderTexture.active;

        try
        {
            foreach (IGeneralUnit item in Buildings.Cast<IGeneralUnit>().Concat(Units))
            {
                if (item == null) continue;

                GameObject instance = Instantiate(item.Prefab, Vector3.zero, Quaternion.identity);
                try
                {
                    SetLayerRecursive(instance, CaptureLayer);

                    if (!TryGetBounds(instance, out Bounds bounds))
                    {
                        Debug.LogWarning($"'{item.Prefab.name}' has no renderers, skipped.", item.Prefab);
                        continue;
                    }

                    FrameBounds(cam, bounds);

                    cam.targetTexture = rt;
                    cam.Render();

                    RenderTexture.active = rt;
                    tex.ReadPixels(new Rect(0, 0, resolution, resolution), 0, 0);
                    tex.Apply();

                    string path = Path.Combine(OutputFolder, item.Prefab.name + ".png").Replace('\\', '/');
                    File.WriteAllBytes(path, tex.EncodeToPNG());
                    generated.Add((path, item));
                }
                finally
                {
                    DestroyImmediate(instance);
                }
            }
        }
        finally
        {
            cam.targetTexture = null;
            RenderTexture.active = prevActive;
            rt.Release();
            DestroyImmediate(rt);
            DestroyImmediate(tex);
            DestroyImmediate(camGO);
        }

        AssetDatabase.Refresh();

        foreach ((string path, _) in generated)
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);

            if (AssetImporter.GetAtPath(path) is TextureImporter importer)
            {
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.alphaIsTransparency = true;
                importer.SaveAndReimport();
            }
        }

        foreach ((string path, IGeneralUnit item) in generated)
        {
            item.Thumbnail = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            EditorUtility.SetDirty((Object)item);
        }

        AssetDatabase.SaveAssets();
    }

    static void SetLayerRecursive(GameObject go, int layer)
    {
        foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
            t.gameObject.layer = layer;
    }

    static bool TryGetBounds(GameObject go, out Bounds bounds)
    {
        bounds = default;
        Renderer[] renderers = go.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return false;

        bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);
        return true;
    }

    void FrameBounds(Camera cam, Bounds b)
    {
        Vector3 dir = viewDirection.normalized;
        Transform ct = cam.transform;
        ct.rotation = Quaternion.LookRotation(-dir, Vector3.up);

        // Aspect is 1, so horizontal and vertical half-extent slopes are equal.
        float tanHalf = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);

        // For a corner at camera-space offset (x, y, z) from the center, with the camera at distance d
        // behind the center: depth = d + z, and it's visible when |x| <= depth * tanHalf
        // -> d >= |x| / tanHalf - z. Take the max over all corners and both axes.
        float dist = 0f;
        Vector3 e = b.extents;
        for (int i = 0; i < 8; i++)
        {
            Vector3 corner = new(
                (i & 1) == 0 ? -e.x : e.x,
                (i & 2) == 0 ? -e.y : e.y,
                (i & 4) == 0 ? -e.z : e.z);

            Vector3 local = ct.InverseTransformDirection(corner);
            float needed = (Mathf.Max(Mathf.Abs(local.x), Mathf.Abs(local.y)) / tanHalf) - local.z;
            dist = Mathf.Max(dist, needed);
        }
        dist *= padding;

        ct.position = b.center + (dir * dist);

        float radius = e.magnitude;
        cam.nearClipPlane = Mathf.Max(0.01f, dist - (radius * 1.5f));
        cam.farClipPlane = dist + (radius * 1.5f);
    }

    [Header("Holograms")]
    [SerializeField, NotNull] Material? HologramMaterial = default;

    [Button("Generate Holograms")]
    void GenerateHologramsWrapper() => EditorCoroutineUtility.StartCoroutine(GenerateHolograms(), this);
    IEnumerator GenerateHolograms()
    {
        Directory.CreateDirectory(OutputFolder);

        foreach (IGeneralUnit item in Buildings.Cast<IGeneralUnit>().Concat(Units))
        {
            GameObject hologramObject = GenerateHologram(item.Prefab, $"{item.Prefab.name}Hologram");
            string hologramPrefabPath = Path.Combine(OutputFolder, $"{hologramObject.name}.prefab");
            if (File.Exists(hologramPrefabPath))
            {
                AssetDatabase.DeleteAsset(hologramPrefabPath);
                yield return null;
            }
            item.HologramPrefab = PrefabUtility.SaveAsPrefabAsset(hologramObject, hologramPrefabPath);
            EditorUtility.SetDirty((Object)item);
            yield return null;

            DestroyImmediate(hologramObject);
            yield return null;
        }

        AssetDatabase.SaveAssets();
    }

    GameObject GenerateHologram(GameObject prefab, string name)
    {
        GameObject result = new(name);

        if (prefab.TryGetComponent(out BoxCollider boxCollider))
        {
            BoxCollider newBoxCollider = result.AddComponent<BoxCollider>();
            newBoxCollider.center = boxCollider.center;
            newBoxCollider.size = boxCollider.size;
        }
        else if (prefab.TryGetComponent(out UnityEngine.SphereCollider sphereCollider))
        {
            UnityEngine.SphereCollider newSphereCollider = result.AddComponent<UnityEngine.SphereCollider>();
            newSphereCollider.center = sphereCollider.center;
            newSphereCollider.radius = sphereCollider.radius;
        }

        foreach (MeshRenderer renderer in prefab.GetComponentsInChildren<MeshRenderer>(false))
        {
            MeshFilter meshFilter = renderer.GetComponent<MeshFilter>();

            GameObject newObject = new(renderer.name, typeof(MeshRenderer), typeof(MeshFilter));
            newObject.transform.SetParent(result.transform);

            MeshFilter newMeshFilter = newObject.GetComponent<MeshFilter>();
            MeshRenderer newRenderer = newObject.GetComponent<MeshRenderer>();

            newMeshFilter.sharedMesh = meshFilter.sharedMesh;
            newRenderer.sharedMaterial = HologramMaterial;

            newObject.transform.SetPositionAndRotation(renderer.transform.position, renderer.transform.rotation);
            newObject.transform.localScale = renderer.transform.lossyScale;
        }

        return result;
    }

    [Button("Generate Placeholders")]
    void GeneratePlaceholdersWrapper() => EditorCoroutineUtility.StartCoroutine(GeneratePlaceholders(), this);

    IEnumerator GeneratePlaceholders()
    {
        Directory.CreateDirectory(OutputFolder);

        foreach (BuildingPrefab item in Buildings)
        {
            GameObject placeholderObject = GenerateHologram(item.Prefab, $"{item.Prefab.name}Placeholder");

            placeholderObject.AddComponent<BuildingPlaceholderAuthoring>();
            placeholderObject.AddComponent<LinkedEntityGroupAuthoring>();
            GhostAuthoringComponent ghostAuthoring = placeholderObject.AddComponent<GhostAuthoringComponent>();
            ghostAuthoring.OptimizationMode = GhostOptimizationMode.Static;
            placeholderObject.AddComponent<EntityInQuadrantAuthoring>();
            ColliderAuthoring colliderAuthoring = placeholderObject.AddComponent<ColliderAuthoring>();
            colliderAuthoring.IsStatic = true;
            placeholderObject.AddComponent<TerrainBuildingAuthoring>();
            placeholderObject.AddComponent<SaveTransformAuthoring>();

            if (item.Prefab.TryGetComponent(out ConnectorAuthoring connectorAuthoring))
            {
                WirePlaceholderAuthoring connectorPlaceholderAuthoring = placeholderObject.AddComponent<WirePlaceholderAuthoring>();
                connectorPlaceholderAuthoring.PortPositions = new Unity.Mathematics.float3[connectorAuthoring.PortPositions.Length];
                for (int i = 0; i < connectorAuthoring.PortPositions.Length; i++)
                {
                    connectorPlaceholderAuthoring.PortPositions[i] = connectorAuthoring.PortPositions[i].position;
                }
            }

            string hologramPrefabPath = Path.Combine(OutputFolder, $"{placeholderObject.name}.prefab");
            if (File.Exists(hologramPrefabPath))
            {
                AssetDatabase.DeleteAsset(hologramPrefabPath);
                yield return null;
            }
            item.PlaceholderPrefab = PrefabUtility.SaveAsPrefabAsset(placeholderObject, hologramPrefabPath);
            EditorUtility.SetDirty(item);
            yield return null;

            DestroyImmediate(placeholderObject);
            yield return null;
        }

        AssetDatabase.SaveAssets();
    }
#endif
}
