using System.Diagnostics.CodeAnalysis;
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
    [SerializeField, NotNull] public UnitPrefab[]? Units = default;
    [SerializeField, NotNull] public BuildingPrefab[]? Buildings = default;
    [SerializeField, NotNull] public ProjectilePrefab[]? Projectiles = default;
    [SerializeField, NotNull] public ResearchMetadata[]? Researches = default;
}
