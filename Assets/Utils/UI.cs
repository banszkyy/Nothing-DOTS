using System.Collections.Generic;
using SaintsField;
using UnityEngine;
using UnityEngine.UIElements;

public class UI : PrivateSingleton<UI>
{
#if UNITY_EDITOR
    [Header("Debug")]
    [SerializeField, ReadOnly] UIDocument? _focusedDocument;
    [SerializeField, ReadOnly] bool _isUiFocused;

    void Update()
    {
        _focusedDocument = null;
        foreach (UIDocument? uiDocument in UIDocuments)
        {
            if (uiDocument == null || uiDocument.rootVisualElement?.focusController?.focusedElement == null) continue;
            _focusedDocument = uiDocument;
            break;
        }
        _isUiFocused = IsUIFocused;
    }
#endif

    readonly HashSet<UIDocument?> _uiDocuments = new();

    static IReadOnlyCollection<UIDocument?> UIDocuments => Instance._uiDocuments;

    public static bool IsMouseHandled => IsUIFocused || IsPointerOverUI();

    public static bool IsUIFocused
    {
        get
        {
            if (GUIUtility.hotControl != 0) return true;

            foreach (UIDocument? uiDocument in UIDocuments)
            {
                if (uiDocument == null || uiDocument.rootVisualElement?.focusController?.focusedElement == null) continue;

                return true;
            }

            return false;
        }
    }

    static readonly List<VisualElement> _picked = new();
    public static bool IsPointerOverUI()
    {
        Vector2 pointerUiPos = new(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
        foreach (UIDocument? uiDocument in UIDocuments)
        {
            if (uiDocument == null || uiDocument.rootVisualElement?.panel == null) continue;
            _picked.Clear();
            uiDocument.rootVisualElement.panel.PickAll(pointerUiPos, _picked);
            foreach (VisualElement element in _picked)
            {
                if (element == null) continue;
                if (element.resolvedStyle.backgroundColor.a == 0f) continue;
                if (!element.enabledInHierarchy) continue;
                return true;
            }
            _picked.Clear();
        }
        return false;
    }

    public static bool Register(UIDocument document) => Instance._uiDocuments.Add(document);

    public static bool Deregister(UIDocument document) => Instance._uiDocuments.Remove(document);
}
