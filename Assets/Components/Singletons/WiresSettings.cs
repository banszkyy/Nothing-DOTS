using Unity.Entities;
using UnityEngine;

public class WiresSettings : IComponentData
{
    public required Material Material;
    public required Material PlaceholderMaterial;
}
