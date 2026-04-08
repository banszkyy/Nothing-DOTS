using System;
using System.Diagnostics.CodeAnalysis;
using UnityEngine;
using UnityEngine.UIElements;

public class Tooltips : Singleton<Tooltips>
{
    [NotNull] UIDocument? ui = null;
    [NotNull] Label? label = null;
    VisualElement? targetVisualElement = null;
    bool visible;

    void OnEnable()
    {
        ui = GetComponent<UIDocument>();
        label = ui.rootVisualElement.Q<Label>("tooltip");
    }

    void Update()
    {
        if (!visible) return;

        label.style.left = Math.Max(0, Input.mousePosition.x);
        label.style.bottom = Math.Max(0, Input.mousePosition.y);
        label.style.visibility = visible ? Visibility.Visible : Visibility.Hidden;
    }

    public void Reregister(VisualElement? visualElement)
    {
        if (visualElement is null) return;

        if (visualElement.tooltip is not null)
        {
            visualElement.UnregisterCallback<MouseEnterEvent>(OnElementMouseEnter);
            visualElement.UnregisterCallback<MouseLeaveEvent>(OnElementMouseLeave);
            visualElement.RegisterCallback<MouseEnterEvent>(OnElementMouseEnter);
            visualElement.RegisterCallback<MouseLeaveEvent>(OnElementMouseLeave);
        }

        foreach (VisualElement item in visualElement.Children())
        {
            Reregister(item);
        }
    }

    public void Register(VisualElement? visualElement)
    {
        if (visualElement is null) return;

        if (visualElement.tooltip is not null)
        {
            visualElement.RegisterCallback<MouseEnterEvent>(OnElementMouseEnter);
            visualElement.RegisterCallback<MouseLeaveEvent>(OnElementMouseLeave);
        }

        foreach (VisualElement item in visualElement.Children())
        {
            Register(item);
        }
    }

    public void Unregister(VisualElement? visualElement)
    {
        if (visualElement is null) return;

        if (visualElement.tooltip is not null)
        {
            visualElement.UnregisterCallback<MouseEnterEvent>(OnElementMouseEnter);
            visualElement.UnregisterCallback<MouseLeaveEvent>(OnElementMouseLeave);
        }

        foreach (VisualElement item in visualElement.Children())
        {
            Unregister(item);
        }
    }

    void SetTooltip(string? tooltip, VisualElement? visualElement)
    {
        visible = !string.IsNullOrWhiteSpace(tooltip);
        label.text = tooltip;
        targetVisualElement = visualElement;
    }

    public void OnDocumentHidden(UIDocument uiDocument)
    {
        if (!visible) return;
        if (targetVisualElement is null) return;
        VisualElement e = targetVisualElement;
        while (e is not null)
        {
            if (e == uiDocument.rootVisualElement)
            {
                SetTooltip(null, targetVisualElement);
                break;
            }
            e = e.hierarchy.parent;
        }
    }

    void OnElementMouseEnter(MouseEnterEvent e)
    {
        if (e.currentTarget is not VisualElement visualElement) return;
        SetTooltip(visualElement.tooltip, visualElement);
    }

    void OnElementMouseLeave(MouseLeaveEvent e)
    {
        if (e.currentTarget is not VisualElement visualElement) return;
        SetTooltip(null, visualElement);
    }
}
