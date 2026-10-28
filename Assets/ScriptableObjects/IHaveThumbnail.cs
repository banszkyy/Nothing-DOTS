using UnityEngine;

interface IHaveThumbnail
{
    GameObject Prefab { get; }
    Texture2D Thumbnail { get; set; }
}
