using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

public static partial class DebugEx
{
    public static void Label(float3 position, string text, float lifetimeSec)
    {
#if UNITY_EDITOR && EDITOR_DEBUG
        DebugDrawer? drawer = DebugDrawer.InstanceOrNull;
        if (drawer == null) return;
        drawer._labels.Add(new DebugDrawer.DebugLabel(
            text,
            Color.white,
            position,
            MonoTime.Now,
            MonoTime.Now + lifetimeSec,
            false
        ));
#endif
    }

    public static void Label(float3 position, string text, int lifetimeTicks = 1)
    {
#if UNITY_EDITOR && EDITOR_DEBUG
        DebugDrawer? drawer = DebugDrawer.InstanceOrNull;
        if (drawer == null) return;
        drawer._labels.Add(new DebugDrawer.DebugLabel(
            text,
            Color.white,
            position,
            Time.frameCount,
            Time.frameCount + lifetimeTicks,
            true
        ));
#endif
    }
}

public class DebugDrawer : Singleton<DebugDrawer>
{
#if UNITY_EDITOR && EDITOR_DEBUG
    public readonly struct DebugLabel
    {
        public readonly string Text;
        public readonly Color Color;
        public readonly Vector3 Position;

        public readonly float BornTime;
        public readonly float DestroyTime;
        public readonly bool InFrames;

        public DebugLabel(string text, Color color, Vector3 position, float bornTime, float destroyTime, bool inFrames)
        {
            Text = text;
            Color = color;
            Position = position;
            BornTime = bornTime;
            DestroyTime = destroyTime;
            InFrames = inFrames;
        }
    }

    internal readonly List<DebugLabel> _labels = new();

    void OnDrawGizmos()
    {
        float now = MonoTime.Now;
        int nowFrames = Time.frameCount;
        for (int i = _labels.Count - 1; i >= 0; i--)
        {
            DebugLabel label = _labels[i];
            if ((label.InFrames ? nowFrames : now) >= label.DestroyTime)
            {
                _labels.RemoveAt(i);
                continue;
            }

            RenderLabel(label.Position, label.Color, label.Text);
        }
    }
#endif

    static readonly Dictionary<Color, GUIStyle> Skins = new();

    static void RenderLabel(in Vector3 position, in Color color, string text)
    {
#if UNITY_EDITOR
        Vector3 screenPosition = MainCamera.Camera.WorldToScreenPoint(position);
        if (screenPosition.y < 0
            || screenPosition.y > MainCamera.Camera.pixelHeight
            || screenPosition.x < 0
            || screenPosition.x > MainCamera.Camera.pixelWidth
            || screenPosition.z < 0)
        {
            return;
        }

        float pixelRatio = UnityEditor.HandleUtility.GUIPointToScreenPixelCoordinate(Vector2.right).x - UnityEditor.HandleUtility.GUIPointToScreenPixelCoordinate(Vector2.zero).x;
        UnityEditor.Handles.BeginGUI();
        if (!Skins.TryGetValue(color, out GUIStyle? style))
        {
            if (Skins.Count > 64) Skins.Clear();
            Skins[color] = style = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                normal = new GUIStyleState() { textColor = color }
            };
        }
        Vector2 size = style.CalcSize(new GUIContent(text)) * pixelRatio;
        Vector2 alignedPosition =
            (((Vector2)screenPosition +
            (size * ((Vector2.left + Vector2.up) / 2f))) * (Vector2.right + Vector2.down)) +
            (Vector2.up * MainCamera.Camera.pixelHeight);
        GUI.Label(new Rect(alignedPosition / pixelRatio, size / pixelRatio), text, style);
        UnityEditor.Handles.EndGUI();
#endif
    }
}
