using System;
using System.Collections.Generic;
using Unity.Mathematics;
using Unity.Serialization;
using UnityEngine;

public class EntityInfoUIManager : Singleton<EntityInfoUIManager>
{
    [SerializeField] float StartFadeDistance = default;
    [SerializeField] float DisappearDistance = default;

    [DontSerialize, HideInInspector] public List<EntityInfoUI> UIs = new();

    void OnValidate()
    {
        DisappearDistance = math.max(StartFadeDistance, DisappearDistance);
    }

    void LateUpdate()
    {
        Span<Vector3> worldCorners = stackalloc Vector3[8];

        foreach (EntityInfoUI item in UIs)
        {
            Vector3 screenPoint = MainCamera.Camera.WorldToScreenPoint(item.Position);
            bool isVisible = screenPoint.z > 0f && screenPoint.z < DisappearDistance;

            if (isVisible != item.IsVisible)
            {
                item.IsVisible = isVisible;
                item.gameObject.SetActive(isVisible);
            }

            if (!isVisible)
            {
                item.CanvasGroup.alpha = 0f;
                continue;
            }

            static void HandleBar(EntityInfoUIBar bar, float percent)
            {
                bar.Foreground.fillAmount = percent;
                bool barVisible = percent is > 0f and < 1f;
                if (bar.IsVisible != barVisible)
                {
                    bar.IsVisible = barVisible;
                    bar.Object.SetActive(barVisible);
                }
            }

            static void HandleBar2(EntityInfoUIBar2 bar, float percent1, float percent2)
            {
                bar.Foreground1.fillAmount = percent1;
                bar.Foreground2.fillAmount = percent2;
                bool barVisible = percent1 is > 0f and < 1f || percent2 is > 0f and < 1f;
                if (bar.IsVisible != barVisible)
                {
                    bar.IsVisible = barVisible;
                    bar.Object.SetActive(barVisible);
                }
            }

            HandleBar(item.HealthBar, item.HealthPercent);
            HandleBar(item.BuildingProgress, item.BuildingProgressPercent);
            HandleBar(item.TransporterLoad, item.TransporterLoadPercent);
            HandleBar(item.TransporterProgress, item.TransporterProgressPercent);
            HandleBar(item.ExtractorProgress, item.ExtractorProgressPercent);
            HandleBar2(item.ReloadProgress, item.ReloadProgressPercent, item.MagazineProgressPercent);

            item.CanvasGroup.alpha = math.clamp(1f - ((screenPoint.z - StartFadeDistance) / (DisappearDistance - StartFadeDistance)), 0f, 1f);

            if (item.SelectionStatusIndicator.enabled = item.SelectionStatus is SelectionStatus.Selected or SelectionStatus.Candidate)
            {
                Vector3 c = item.Bounds.center;
                Vector3 e = item.Bounds.extents;

                worldCorners[0] = MainCamera.Camera.WorldToScreenPoint(item.Position + math.rotate(item.Rotation, c + new Vector3(+e.x, +e.y, +e.z)));
                worldCorners[1] = MainCamera.Camera.WorldToScreenPoint(item.Position + math.rotate(item.Rotation, c + new Vector3(+e.x, +e.y, -e.z)));
                worldCorners[2] = MainCamera.Camera.WorldToScreenPoint(item.Position + math.rotate(item.Rotation, c + new Vector3(+e.x, -e.y, +e.z)));
                worldCorners[3] = MainCamera.Camera.WorldToScreenPoint(item.Position + math.rotate(item.Rotation, c + new Vector3(+e.x, -e.y, -e.z)));
                worldCorners[4] = MainCamera.Camera.WorldToScreenPoint(item.Position + math.rotate(item.Rotation, c + new Vector3(-e.x, +e.y, +e.z)));
                worldCorners[5] = MainCamera.Camera.WorldToScreenPoint(item.Position + math.rotate(item.Rotation, c + new Vector3(-e.x, +e.y, -e.z)));
                worldCorners[6] = MainCamera.Camera.WorldToScreenPoint(item.Position + math.rotate(item.Rotation, c + new Vector3(-e.x, -e.y, +e.z)));
                worldCorners[7] = MainCamera.Camera.WorldToScreenPoint(item.Position + math.rotate(item.Rotation, c + new Vector3(-e.x, -e.y, -e.z)));

                Vector2 min = worldCorners[0];
                Vector2 max = worldCorners[0];
                for (int i = 1; i < worldCorners.Length; i++)
                {
                    min.x = math.min(min.x, worldCorners[i].x);
                    min.y = math.min(min.y, worldCorners[i].y);
                    max.x = math.max(max.x, worldCorners[i].x);
                    max.y = math.max(max.y, worldCorners[i].y);
                }

                Vector2 a = min;
                Vector2 b = max;
                Vector2 center = (b + a) * 0.5f;
                item.SelectionStatusIndicator.rectTransform.sizeDelta = b - a;
                item.SelectionStatusIndicator.rectTransform.anchoredPosition = center - (Vector2)screenPoint;
            }

            // item.Label.text = item.Team.ToString();

            screenPoint.z = 0f;
            RectTransform transform = item.GetComponent<RectTransform>();
            transform.anchoredPosition = screenPoint;
        }
    }
}
