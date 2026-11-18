using System;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

/// <summary>
/// <seealso href="https://assetstore.unity.com/packages/vfx/shaders/fullscreen-camera-effects/aos-fog-of-war-249249">Source</seealso>
/// </summary>
public class FogOfWarSettings : IComponentData
{
    public required float3 LevelMidPoint;
    public required float FogRefreshRate;
    public required float FogPlaneHeight;
    public required Material FogPlaneMaterial;
    public required Color FogColor;
    public required float FogPlaneAlpha;
    public required float FogLerpSpeed;
    public required bool KeepRevealedTiles;
    public required float RevealedTileOpacity;
    public required int LevelDimensionX;
    public required int LevelDimensionY;
    public required float UnitScale;
    public required float ScanSpacingPerUnit;
    public required float RayStartHeight;
    public required float RayMaxDistance;
    public required LayerMask ObstacleLayers;
    public required bool IgnoreTriggers;

    /// <summary>
    /// Checks if the given level coordinates are within level dimension range.
    /// </summary>
    public bool CheckLevelGridRange(int2 levelCoordinates) =>
        levelCoordinates.x >= 0 &&
        levelCoordinates.x < LevelDimensionX &&
        levelCoordinates.y >= 0 &&
        levelCoordinates.y < LevelDimensionY;

    /// <summary>
    /// Converts level coordinates into world coordinates.
    /// </summary>
    public float3 GetWorld(int2 worldCoordinates) => new(
        GetWorldX(worldCoordinates.x + (LevelDimensionX / 2)),
        0,
        GetWorldY(worldCoordinates.y + (LevelDimensionY / 2))
    );

    /// <summary>
    /// Converts level coordinate to corresponding unit world coordinates.
    /// </summary>
    public float GetWorldY(int y)
    {
        if (LevelDimensionY % 2 == 0)
        {
            return LevelMidPoint.z - ((LevelDimensionY / 2f) - y) * UnitScale;
        }

        return LevelMidPoint.z - ((LevelDimensionY / 2f) - (y + 0.5f)) * UnitScale;
    }

    /// <summary>
    /// Converts level coordinate to corresponding unit world coordinates.
    /// </summary>
    public float GetWorldX(int x)
    {
        if (LevelDimensionX % 2 == 0)
        {
            return LevelMidPoint.x - ((LevelDimensionX / 2f) - x) * UnitScale;
        }

        return LevelMidPoint.x - ((LevelDimensionX / 2f) - (x + 0.5f)) * UnitScale;
    }

    /// <summary>
    /// Converts unit (divided by unitScale, then rounded) world coordinates to level coordinates.
    /// </summary>
    public int2 WorldToLevel(float3 worldCoordinates)
    {
        int2 unitWorldCoordinates = GetUnit(worldCoordinates);

        return new int2(
            unitWorldCoordinates.x + (LevelDimensionX / 2),
            unitWorldCoordinates.y + (LevelDimensionY / 2));
    }

    /// <summary>
    /// Converts "pure" world coordinates into unit world coordinates.
    /// </summary>
    public int2 GetUnit(float3 worldCoordinates) => new(
        GetUnitX(worldCoordinates.x),
        GetUnitY(worldCoordinates.z)
    );

    /// <summary>
    /// Converts world coordinate to unit world coordinates.
    /// </summary>
    public int GetUnitX(float x) => (int)math.round((x - LevelMidPoint.x) / UnitScale);

    /// <summary>
    /// Converts world coordinate to unit world coordinates.
    /// </summary>
    public int GetUnitY(float y) => (int)math.round((y - LevelMidPoint.z) / UnitScale);
}
