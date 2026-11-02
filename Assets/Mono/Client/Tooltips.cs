using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using UnityEngine;
using UnityEngine.UIElements;

public class Tooltips : Singleton<Tooltips>
{
    [SerializeField, NotNull] UIDocument? ui = null;
    [NotNull] Label? label = null;
    VisualElement? custom = null;
    bool isCustom;
    VisualElement? targetVisualElement = null;
    bool visible;

    readonly Dictionary<VisualElement, (VisualTreeAsset Asset, Action<VisualElement> Callback)> customTooltips = new();

    void OnEnable()
    {
        label = ui.rootVisualElement.Q<Label>("tooltip");
    }

    void Update()
    {
        label.style.visibility = ((!isCustom || custom == null) && visible) ? Visibility.Visible : Visibility.Hidden;
        if (custom != null) custom.style.visibility = (isCustom && visible) ? Visibility.Visible : Visibility.Hidden;

        if (!visible) return;

        VisualElement e = (custom != null && isCustom) ? custom : label;
        e.style.left = Math.Max(0, Input.mousePosition.x);
        e.style.bottom = Math.Max(0, Input.mousePosition.y);
    }

    public void Reregister(VisualElement? visualElement)
    {
        if (visualElement is null) return;
        Unregister(visualElement);
        Register(visualElement);
    }

    public void Register(VisualElement? visualElement, bool isChild = false)
    {
        if (visualElement is null) return;

        if (isChild || !string.IsNullOrEmpty(visualElement.tooltip))
        {
            visualElement.RegisterCallback<MouseEnterEvent>(OnElementMouseEnter);
            visualElement.RegisterCallback<MouseLeaveEvent>(OnElementMouseLeave);

            if (targetVisualElement == visualElement)
            {
                SetTooltip(visualElement, visualElement.tooltip);
            }
        }

        foreach (VisualElement item in visualElement.Children())
        {
            Register(item, isChild: true);
        }
    }

    public void Reregister(VisualElement? visualElement, VisualTreeAsset content, Action<VisualElement> onShow)
    {
        if (visualElement is null) return;
        Unregister(visualElement);
        Register(visualElement, content, onShow);
    }

    public void Register(VisualElement? visualElement, VisualTreeAsset content, Action<VisualElement> onShow)
    {
        if (visualElement is null) return;

        visualElement.RegisterCallback<MouseEnterEvent>(OnElementMouseEnter);
        visualElement.RegisterCallback<MouseLeaveEvent>(OnElementMouseLeave);

        if (targetVisualElement == visualElement)
        {
            SetTooltip(visualElement, content, onShow);
        }

        customTooltips[visualElement] = (content, onShow);

        foreach (VisualElement item in visualElement.Children())
        {
            Register(item, isChild: true);
        }
    }

    public void Unregister(VisualElement? visualElement)
    {
        if (visualElement is null) return;

        customTooltips.Remove(visualElement);

        visualElement.UnregisterCallback<MouseEnterEvent>(OnElementMouseEnter);
        visualElement.UnregisterCallback<MouseLeaveEvent>(OnElementMouseLeave);

        foreach (VisualElement item in visualElement.Children())
        {
            Unregister(item);
        }
    }

    void ClearTooltip(VisualElement? visualElement)
    {
        visible = false;
        isCustom = false;
        label.text = string.Empty;
        targetVisualElement = visualElement;
    }

    void SetTooltip(VisualElement visualElement, string content)
    {
        visible = true;
        isCustom = false;
        label.text = content ?? string.Empty;
        targetVisualElement = visualElement;
    }

    void SetTooltip(VisualElement visualElement, VisualTreeAsset asset, Action<VisualElement> callback)
    {
        visible = true;
        isCustom = true;
        targetVisualElement = visualElement;

        if (custom != null)
        {
            if (custom.userData != visualElement)
            {
                custom?.RemoveFromHierarchy();
                custom = null;
            }
        }

        if (custom == null)
        {
            custom = asset.Instantiate();
            custom.style.position = Position.Absolute;
            custom.userData = visualElement;
            custom.pickingMode = PickingMode.Ignore;
            custom.style.left = Math.Max(0, Input.mousePosition.x);
            custom.style.bottom = Math.Max(0, Input.mousePosition.y);
            ui.rootVisualElement.Add(custom);
        }

        callback(custom);
    }

    public void OnDocumentHidden(UIElementReference uiDocument)
    {
        if (!visible) return;
        if (targetVisualElement is null) return;
        VisualElement e = targetVisualElement;
        while (e is not null)
        {
            DebugEx.Assert(uiDocument.Document != null);

            if (e == uiDocument.Document.rootVisualElement)
            {
                ClearTooltip(targetVisualElement);
                break;
            }
            e = e.hierarchy.parent;
        }
    }

    void OnElementMouseEnter(MouseEnterEvent e)
    {
        if (e.currentTarget is not VisualElement visualElement) return;
        VisualElement v = visualElement;

        while (v is not null)
        {
            if (v == custom) return;

            if (customTooltips.TryGetValue(v, out var content))
            {
                SetTooltip(v, content.Asset, content.Callback);
                return;
            }

            if (!string.IsNullOrWhiteSpace(v.tooltip))
            {
                SetTooltip(v, v.tooltip);
                return;
            }

            v = v.parent;
        }

        ClearTooltip(v);
    }

    void OnElementMouseLeave(MouseLeaveEvent e)
    {
        if (e.currentTarget is not VisualElement visualElement) return;
        if (custom != null && (e.currentTarget == custom || e.target == custom)) return;

        ClearTooltip(visualElement);
    }
}
