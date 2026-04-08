using System.Collections.Generic;
using System.Collections.Immutable;
using SaintsField;
using UnityEngine;
using UnityEngine.UIElements;

public class UI : PrivateSingleton<UI>
{
#if EDITOR_DEBUG
    [Header("Debug")]
    [SerializeField, ReadOnly] UIDocument?[]? _uiDocuments;
    [SerializeField, ReadOnly] UIDocument? _focusedDocument;
    [SerializeField, ReadOnly] bool _isUiFocused;
    float _uiDocumentsTime;

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

    static ImmutableArray<UIDocument?> UIDocuments
    {
        get
        {
            UI v = Instance;
            if (v._uiDocuments == null || Time.time - v._uiDocumentsTime > 10f)
            {
                v._uiDocumentsTime = Time.time;
                v._uiDocuments = FindObjectsByType<UIDocument?>(FindObjectsInactive.Include);
            }
            return v._uiDocuments.AsImmutableArrayUnsafe();
        }
    }

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
}
