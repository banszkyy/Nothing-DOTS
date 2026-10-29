using System.IO;
using UnityEngine;
using SaintsField.Playa;
using System.Collections.Generic;
using SaintsField;
using System.Linq;


#if UNITY_EDITOR
using UnityEditor;
#endif

public class ThumbnailGenerator : MonoBehaviour
{
    [SerializeField, Required] AllPrefabs Prefabs;

    [Header("Camera")]
    [SerializeField] Vector3 viewDirection = new(1f, 1.2f, -1f);
    [SerializeField, Range(10f, 90f)] float fieldOfView = 30f;
    [SerializeField] float padding = 1.1f;
    [SerializeField] Color background = new(0, 0, 0, 0);

    [Header("Output")]
    [SerializeField] int resolution = 256;

    // Hard-coded settings
    const string OutputFolder = "Assets/Thumbnails";
    const int CaptureLayer = 9;

    [Button("Generate Thumbnails")]
    public void GenerateThumbnails()
    {
        Directory.CreateDirectory(OutputFolder);

        List<(string ThumbnailPath, IHaveThumbnail Object)> generated = new();

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
            foreach (IHaveThumbnail? item in Prefabs.Buildings.Cast<IHaveThumbnail>().Concat(Prefabs.Units))
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

#if UNITY_EDITOR
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

        foreach ((string path, IHaveThumbnail item) in generated)
        {
            item.Thumbnail = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            EditorUtility.SetDirty((Object)item);
        }

        AssetDatabase.SaveAssets();
#endif
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

    /// <summary>
    /// Places the camera on the view-direction ray through the bounds center, at the smallest
    /// distance where all 8 bounds corners are inside the frustum.
    /// </summary>
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
}
