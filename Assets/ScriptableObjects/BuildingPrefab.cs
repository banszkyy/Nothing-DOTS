
using System.Diagnostics.CodeAnalysis;
using UnityEngine;

[CreateAssetMenu(fileName = "Building", menuName = "Game/Building")]
class BuildingPrefab : ScriptableObject, IHaveThumbnail
{
    [SerializeField, NotNull] public GameObject? Prefab = default;
    [SerializeField, NotNull] public Texture2D? Thumbnail = default;
    [SerializeField, NotNull] public GameObject? PlaceholderPrefab = default;
    [SerializeField, NotNull] public GameObject? HologramPrefab = default;
    [SerializeField, Min(0f)] public float ConstructionTime = default;
    [SerializeField, Min(0f)] public float RequiredResources = default;
    [SerializeField] public ResearchMetadata? RequiredResearch = default;

    GameObject? IHaveThumbnail.Prefab => Prefab;
    Texture2D? IHaveThumbnail.Thumbnail { get => Thumbnail; set => Thumbnail = value; }
}
