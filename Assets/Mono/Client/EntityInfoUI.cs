using System;
using System.Diagnostics.CodeAnalysis;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UI;

[Serializable]
public class EntityInfoUIBar
{
    [SerializeField, NotNull] public GameObject? Object = default;
    [SerializeField, NotNull] public Image? Foreground = default;
    [SerializeField, SaintsField.ReadOnly] public bool IsVisible;
}

[Serializable]
public class EntityInfoUIBar2
{
    [SerializeField, NotNull] public GameObject? Object = default;
    [SerializeField, NotNull] public Image? Foreground1 = default;
    [SerializeField, NotNull] public Image? Foreground2 = default;
    [SerializeField, SaintsField.ReadOnly] public bool IsVisible;
}

public class EntityInfoUI : MonoBehaviour
{
    [Header("UI")]

    [SerializeField, NotNull] public EntityInfoUIBar? HealthBar = default;
    [SerializeField, NotNull] public EntityInfoUIBar? BuildingProgress = default;
    [SerializeField, NotNull] public EntityInfoUIBar? TransporterLoad = default;
    [SerializeField, NotNull] public EntityInfoUIBar? TransporterProgress = default;
    [SerializeField, NotNull] public EntityInfoUIBar? ExtractorProgress = default;
    [SerializeField, NotNull] public EntityInfoUIBar2? ReloadProgress = default;
    [SerializeField, NotNull] public CanvasGroup? CanvasGroup = default;
    [SerializeField, NotNull] public Image? SelectionStatusIndicator = default;
    [SerializeField, NotNull] public Image? CandidateStatusIndicator = default;
    [SerializeField, NotNull] public Text? Label = default;

    [Header("Debug")]

    [SerializeField, SaintsField.ReadOnly] public float HealthPercent = default;
    [SerializeField, SaintsField.ReadOnly] public float BuildingProgressPercent = default;
    [SerializeField, SaintsField.ReadOnly] public float TransporterLoadPercent = default;
    [SerializeField, SaintsField.ReadOnly] public float TransporterProgressPercent = default;
    [SerializeField, SaintsField.ReadOnly] public float ExtractorProgressPercent = default;
    [SerializeField, SaintsField.ReadOnly] public float ReloadProgressPercent = default;
    [SerializeField, SaintsField.ReadOnly] public float MagazineProgressPercent = default;
    [SerializeField, SaintsField.ReadOnly] public float3 Position = default;
    [SerializeField, SaintsField.ReadOnly] public quaternion Rotation = default;
    [SerializeField, SaintsField.ReadOnly] public Bounds Bounds = default;
    [SerializeField, SaintsField.ReadOnly] public SelectionStatus SelectionStatus = default;

    [SerializeField, SaintsField.ReadOnly] public bool IsVisible = default;
    [SerializeField, SaintsField.ReadOnly] public int Team = default;

    void Start()
    {
        EntityInfoUIManager.Instance.UIs.Add(this);
    }

    void OnDestroy()
    {
        if (EntityInfoUIManager.InstanceOrNull == null) return;
        EntityInfoUIManager.InstanceOrNull.UIs.Remove(this);
    }
}
