using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Unity.Collections;
using UnityEngine;
using UnityEngine.UIElements;

public class ProcessorGUIManager : MonoBehaviour
{
    [SerializeField, NotNull] UIDocument? _ui = default;

    readonly Dictionary<int, Texture2D> _textures = new();
    readonly HashSet<int> _ids = new();
    UQueryState<VisualElement>? _userIdQ;

    static void UpdateElement(VisualElement visualElement, in UserUIElement userElement)
    {
        visualElement.style.flexDirection = userElement.Direction switch
        {
            UserUIDirection.Horizontal => FlexDirection.Row,
            UserUIDirection.Vertical => FlexDirection.Column,
            UserUIDirection.HorizontalReverse => FlexDirection.RowReverse,
            UserUIDirection.VerticalReverse => FlexDirection.ColumnReverse,
            _ => throw new UnreachableException(),
        };

        visualElement.style.marginTop =
        visualElement.style.marginRight =
        visualElement.style.marginBottom =
        visualElement.style.marginLeft = userElement.Margin;

        visualElement.style.paddingTop =
        visualElement.style.paddingRight =
        visualElement.style.paddingBottom =
        visualElement.style.paddingLeft = userElement.Padding;

        visualElement.style.width = userElement.Size.x == 0 ? new StyleLength(StyleKeyword.Auto) : new StyleLength(new Length(userElement.Size.x, LengthUnit.Pixel));
        visualElement.style.height = userElement.Size.y == 0 ? new StyleLength(StyleKeyword.Auto) : new StyleLength(new Length(userElement.Size.y, LengthUnit.Pixel));
    }

    unsafe void Update()
    {
        NativeList<UserUIElement> uiElements = ConnectionManager.ClientOrDefaultWorld.Unmanaged.GetSystem<ProcessorSystemClient>().uiElements;
        _userIdQ ??= _ui.rootVisualElement.Query().Class("user-ui").Build();

        _ids.Clear();

        for (int i = 0; i < uiElements.Length; i++)
        {
            ref UserUIElement uiElement = ref uiElements.GetUnsafeList()->Ptr[i];
            VisualElement? e = _ui.rootVisualElement.Q(uiElement.Id.ToString());

            _ids.Add(uiElement.Id);

            VisualElement desiredParent = uiElement.Parent == 0 ? _ui.rootVisualElement.Q("root") : _ui.rootVisualElement.Q(uiElement.Parent.ToString());
            if (desiredParent is null)
            {
                Debug.LogWarning($"Desired parent is null");
                continue;
            }

            if (e is null)
            {
                switch (uiElement.Type)
                {
                    case UserUIElementType.Box:
                    {
                        VisualElement l = new();

                        l.AddToClassList("user-ui");

                        e = l;
                        break;
                    }
                    case UserUIElementType.Label:
                    {
                        Label l = new();

                        l.AddToClassList("user-ui");
                        l.style.color = new Color(uiElement.Meta.Label.Color.x, uiElement.Meta.Label.Color.y, uiElement.Meta.Label.Color.z);
                        l.text = uiElement.Meta.Label.Text.AsString().ToString();

                        e = l;
                        break;
                    }
                    case UserUIElementType.Image:
                    {
                        Image l = new();

                        l.AddToClassList("user-ui");

                        if (!_textures.TryGetValue(uiElement.Id, out Texture2D? img))
                        {
                            img = _textures[uiElement.Id] = new Texture2D(uiElement.Meta.Image.Width, uiElement.Meta.Image.Height);
                            img.filterMode = FilterMode.Point;
                            img.wrapMode = TextureWrapMode.Clamp;

                            img.SetPixels32(new Color32[img.width * img.height]);
                            img.Apply();
                        }
                        l.image = img;

                        e = l;
                        break;
                    }
                    case UserUIElementType.MIN:
                    case UserUIElementType.MAX:
                    default:
                        throw new UnreachableException();
                }

                UpdateElement(e, in uiElement);
                e.name = uiElement.Id.ToString();
                desiredParent.Add(e);
            }
            else
            {
                if (!uiElement.IsDirty) continue;

                if (e.parent != desiredParent)
                {
                    e.RemoveFromHierarchy();
                    desiredParent.Add(e);
                }

                UpdateElement(e, in uiElement);
                switch (uiElement.Type)
                {
                    case UserUIElementType.Box: break;
                    case UserUIElementType.Label:
                    {
                        if (e is not Label l) break;

                        l.style.color = new Color(uiElement.Meta.Label.Color.x, uiElement.Meta.Label.Color.y, uiElement.Meta.Label.Color.z);
                        l.text = uiElement.Meta.Label.Text.AsString().ToString();

                        break;
                    }
                    case UserUIElementType.Image:
                    {
                        if (e is not Image l) break;

                        if (!_textures.TryGetValue(uiElement.Id, out Texture2D? img))
                        {
                            img = _textures[uiElement.Id] = new Texture2D(uiElement.Meta.Image.Width, uiElement.Meta.Image.Height);
                            img.filterMode = FilterMode.Point;
                            img.wrapMode = TextureWrapMode.Clamp;
                        }
                        else if (img.width != uiElement.Meta.Image.Width || img.height != uiElement.Meta.Image.Height)
                        {
                            img.width = uiElement.Meta.Image.Width;
                            img.height = uiElement.Meta.Image.Height;
                        }

                        for (int y = 0; y < uiElement.Meta.Image.Height; y++)
                        {
                            for (int x = 0; x < uiElement.Meta.Image.Width; x++)
                            {
                                unsafe
                                {
                                    byte p = ((byte*)Unsafe.AsPointer(ref uiElement.Meta.Image.Image))[x + (y * uiElement.Meta.Image.Width)];
                                    img.SetPixel(x, y, new Color(
                                        (float)((p >> 5) & 0b111) / (float)0b111,
                                        (float)((p >> 2) & 0b111) / (float)0b111,
                                        (float)((p >> 0) & 0b011) / (float)0b011,
                                        1f
                                    ));
                                }
                            }
                        }

                        img.Apply();

                        l.image = img;
                        break;
                    }
                    case UserUIElementType.MIN:
                    case UserUIElementType.MAX:
                    default:
                        break;
                }
            }

            uiElement.IsDirty = false;
        }

        foreach (int id in _textures.Keys)
        {
            if (_ids.Contains(id)) continue;

            _textures.Remove(id);
            break;
        }

        foreach (VisualElement item in _userIdQ.Value)
        {
            if (item.parent is null) continue;
            if (!int.TryParse(item.name ?? string.Empty, out int id)) continue;
            if (_ids.Contains(id)) continue;

            item.parent.Remove(item);
            break;
        }
    }
}
