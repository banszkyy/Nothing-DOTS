
using System.Diagnostics.CodeAnalysis;
using UnityEngine;

[CreateAssetMenu(fileName = "Unit", menuName = "Game/Unit")]
class UnitPrefab : ScriptableObject
{
    [SerializeField, NotNull] public GameObject? Prefab = default;
    [SerializeField, NotNull] public GameObject? HologramPrefab = default;
    [SerializeField, Min(0f)] public float ProductionTime = default;
    [SerializeField, Min(0f)] public float RequiredResources = default;
    [SerializeField] public ResearchMetadata? RequiredResearch = default;
}
