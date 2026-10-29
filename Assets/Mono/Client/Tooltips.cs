using System;
using System.Diagnostics.CodeAnalysis;
using UnityEngine;
using UnityEngine.UIElements;

public class Tooltips : Singleton<Tooltips>
{
    [SerializeField, NotNull] UIDocument? ui = null;
    [NotNull] Label? label = null;
    VisualElement? targetVisualElement = null;
    bool visible;

    void OnEnable()
    {
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
                SetTooltip(visualElement.tooltip, visualElement);
            }
        }

        foreach (VisualElement item in visualElement.Children())
        {
            Register(item, isChild: true);
        }
    }

    public void Unregister(VisualElement? visualElement)
    {
        if (visualElement is null) return;

        visualElement.UnregisterCallback<MouseEnterEvent>(OnElementMouseEnter);
        visualElement.UnregisterCallback<MouseLeaveEvent>(OnElementMouseLeave);

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

    public void OnDocumentHidden(UIElementReference uiDocument)
    {
        if (!visible) return;
        if (targetVisualElement is null) return;
        VisualElement e = targetVisualElement;
        while (e is not null)
        {
            if (e == uiDocument.Document.rootVisualElement)
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
        VisualElement v = visualElement;
        string? tooltip = null;
        while (v is not null && string.IsNullOrWhiteSpace(tooltip = v.tooltip))
        {
            v = v.parent;
        }
        SetTooltip(tooltip, visualElement);
    }

    void OnElementMouseLeave(MouseLeaveEvent e)
    {
        if (e.currentTarget is not VisualElement visualElement) return;
        SetTooltip(null, visualElement);
    }
}
