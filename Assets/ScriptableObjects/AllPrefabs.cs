using System.Diagnostics.CodeAnalysis;
using UnityEngine;

[CreateAssetMenu(fileName = "Prefabs", menuName = "Game/Prefabs")]
class AllPrefabs : ScriptableObject
{
    [SerializeField, NotNull] public GameObject? PlayerPrefab = default;
    [SerializeField, NotNull] public GameObject? VirtualPlayerPrefab = default;
    [SerializeField, NotNull] public GameObject? CoreComputerPrefab = default;
    [SerializeField, NotNull] public GameObject? Resource = default;
    [SerializeField, NotNull] public UnitPrefab? Builder = default;

    [SerializeField, NotNull] public UnitPrefab[]? Units = default;
    [SerializeField, NotNull] public BuildingPrefab[]? Buildings = default;
    [SerializeField, NotNull] public ProjectilePrefab[]? Projectiles = default;
}
