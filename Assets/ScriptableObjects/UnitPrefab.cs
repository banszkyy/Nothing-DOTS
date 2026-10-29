
using System;
using System.Diagnostics.CodeAnalysis;
using UnityEngine;

[CreateAssetMenu(fileName = "Unit", menuName = "Game/Unit")]
class UnitPrefab : ScriptableObject, IHaveThumbnail
{
    [SerializeField, NotNull] public GameObject? Prefab = default;
    [SerializeField, NotNull] public Texture2D? Thumbnail = default;
    [SerializeField, NotNull] public GameObject? HologramPrefab = default;
    [SerializeField, Min(0f)] public float ProductionTime = default;
    [SerializeField, Min(0f)] public float RequiredResources = default;
    [SerializeField] public ResearchMetadata? RequiredResearch = default;

    GameObject IHaveThumbnail.Prefab => Prefab != null ? Prefab : throw new NullReferenceException();
    Texture2D IHaveThumbnail.Thumbnail { get => Thumbnail != null ? Thumbnail : throw new NullReferenceException(); set => Thumbnail = value; }
}
