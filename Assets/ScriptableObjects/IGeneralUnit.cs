using UnityEngine;

interface IGeneralUnit
{
    GameObject Prefab { get; }
    Texture2D Thumbnail { get; set; }
    string Name { get; }
    GameObject HologramPrefab { get; set; }
}
