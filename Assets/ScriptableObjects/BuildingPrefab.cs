
using System;
using System.Diagnostics.CodeAnalysis;
using UnityEngine;

[CreateAssetMenu(fileName = "Building", menuName = "Game/Building")]
class BuildingPrefab : ScriptableObject, IGeneralUnit
{
    [SerializeField, NotNull] public GameObject? Prefab = default;
    [SerializeField, NotNull] public Texture2D? Thumbnail = default;
    [SerializeField, NotNull] public GameObject? PlaceholderPrefab = default;
    [SerializeField, NotNull] public GameObject? HologramPrefab = default;
    [SerializeField, Min(0f)] public float ConstructionTime = default;
    [SerializeField, Min(0f)] public float RequiredResources = default;
    [SerializeField] public ResearchMetadata? RequiredResearch = default;

    GameObject IGeneralUnit.Prefab => Prefab != null ? Prefab : throw new NullReferenceException();
    string IGeneralUnit.Name => Prefab != null ? Prefab.name : throw new NullReferenceException();
    Texture2D IGeneralUnit.Thumbnail { get => Thumbnail != null ? Thumbnail : throw new NullReferenceException(); set => Thumbnail = value; }
    GameObject IGeneralUnit.HologramPrefab { get => HologramPrefab != null ? HologramPrefab : throw new NullReferenceException(); set => HologramPrefab = value; }
}
