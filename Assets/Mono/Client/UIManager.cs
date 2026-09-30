using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

[Serializable]
public struct UIElementReference : IEquatable<UIElementReference>
{
    [SerializeField, NotNull] public UIDocument? Document;
    [SerializeField, NotNull] public string? ElementName;

    public VisualElement? ElementOrNull
    {
        get
        {
            if (Document == null || !Document.isActiveAndEnabled || string.IsNullOrEmpty(ElementName)) return null;
            if (Document.rootVisualElement == null) return null;
            return Document.rootVisualElement.Q(ElementName) ?? throw new Exception($"Element #{ElementName} wasn't found on {Document}");
        }
    }

    public VisualElement Element
    {
        get
        {
            if (Document == null || !Document.isActiveAndEnabled || string.IsNullOrEmpty(ElementName)) throw new InvalidOperationException($"UI is null");
            if (Document.rootVisualElement == null) throw new InvalidOperationException($"UI wasn't opened yet");
            return Document.rootVisualElement.Q(ElementName) ?? throw new Exception($"Element #{ElementName} wasn't found on {Document}");
        }
    }

    public bool IsVisible
    {
        get => ElementOrNull?.resolvedStyle.display == DisplayStyle.Flex;
        set => Element.style.display = value ? DisplayStyle.Flex : DisplayStyle.None;
    }

    public override bool Equals(object? obj) => obj is UIElementReference other && Equals(other);
    public bool Equals(UIElementReference other) => other.Document == Document && other.ElementName == ElementName;

    public override int GetHashCode() => HashCode.Combine(Document, ElementName);

    public static bool operator ==(UIElementReference left, UIElementReference right) => left.Equals(right);
    public static bool operator !=(UIElementReference left, UIElementReference right) => !left.Equals(right);
}

public class UIManager : Singleton<UIManager>
{
    public readonly struct UISetup
    {
        public readonly UIElementReference UI;
        readonly UIManager Manager;

        public UISetup(UIElementReference ui, UIManager manager)
        {
            UI = ui;
            Manager = manager;
        }

        public UISetup Setup<TManager, TContext>(TManager manager, TContext context)
            where TManager : IUISetup<TContext>
        {
            manager.Setup(UI, context);
            if (manager is IUICleanup cleanup)
            {
                Manager.OpenedUIs.TryAdd(UI, new List<IUICleanup>());
                Manager.OpenedUIs[UI].Add(cleanup);
            }
            return this;
        }

        public UISetup Setup<TManager>(TManager manager)
            where TManager : IUISetup
        {
            manager.Setup(UI);
            if (manager is IUICleanup cleanup)
            {
                Manager.OpenedUIs.TryAdd(UI, new List<IUICleanup>());
                Manager.OpenedUIs[UI].Add(cleanup);
            }
            return this;
        }

        public UISetup Setup<TManager>()
            where TManager : Singleton<TManager>, IUISetup
            => Setup(Singleton<TManager>.Instance);

        public UISetup Setup<TManager, TContext>(TContext context)
            where TManager : Singleton<TManager>, IUISetup<TContext>
            => Setup(Singleton<TManager>.Instance, context);
    }

    [Header("Documents")]

    [SerializeField, NotNull] public UIElementReference MainMenu = default;
    [SerializeField, NotNull] public UIElementReference NetworkStatus = default;
    [SerializeField, NotNull] public UIElementReference Unit = default;
    [SerializeField, NotNull] public UIElementReference Factory = default;
    [SerializeField, NotNull] public UIElementReference Facility = default;
    [SerializeField, NotNull] public UIElementReference Pause = default;
    [SerializeField, NotNull] public UIElementReference Buildings = default;
    [SerializeField, NotNull] public UIElementReference Units = default;

    ImmutableArray<UIElementReference>? _uis = default;

    public ImmutableArray<UIElementReference> UIs => _uis ?? (_uis = ImmutableArray.Create(
        MainMenu,
        NetworkStatus,
        Unit,
        Factory,
        Facility,
        Pause,
        Buildings,
        Units
    )).Value;

    [NotNull] Dictionary<UIElementReference, List<IUICleanup>>? OpenedUIs = default;

    public bool AnyUIVisible
    {
        get
        {
            ImmutableArray<UIElementReference> uis = UIs;
            for (int i = 0; i < uis.Length; i++)
            {
                if (uis[i].IsVisible) return true;
            }
            return false;
        }
    }

    [Header("Debug")]
    [SerializeField, SaintsField.ReadOnly] bool _escPressed = false;
    [SerializeField, SaintsField.ReadOnly] bool _escGrabbed = false;

    void Start()
    {
        OpenedUIs = new();

        foreach (var ui in UIs)
        {
            if (ui.Document == null) Debug.LogError($"UI document is null");
            if (string.IsNullOrEmpty(ui.ElementName)) Debug.LogError($"Element name is null");
            CloseUI(ui);
        }
    }

    void Update()
    {
        _escPressed = Input.GetKeyDown(KeyCode.Escape);
        _escGrabbed = _escGrabbed && _escPressed;
    }

    // void LateUpdate()
    // {
    //     if (GrapESC()) CloseAllUI();
    // }

    public bool GrapESC()
    {
        if (_escGrabbed || !Input.GetKeyDown(KeyCode.Escape)) return false;
        _escGrabbed = true;
        return true;
    }

    public void CloseAllUI()
    {
        for (int i = 0; i < UIs.Length; i++)
        {
            CloseUI(UIs[i]);
        }
    }

    public void CloseAllUI(UIElementReference except)
    {
        for (int i = 0; i < UIs.Length; i++)
        {
            if (UIs[i] == except) continue;
            CloseUI(UIs[i]);
        }
    }

    public void CloseUI(UIElementReference ui)
    {
        Tooltips.Instance.OnDocumentHidden(ui);
        if (OpenedUIs.TryGetValue(ui, out List<IUICleanup>? cleanup))
        {
            foreach (IUICleanup item in cleanup)
            {
                item.Cleanup(ui);
            }
            OpenedUIs.Remove(ui);
        }
        ui.Document.rootVisualElement?.focusController.focusedElement?.Blur();
        ui.IsVisible = false;
    }

    public void CloseUI(IUICleanup ui)
    {
        foreach (KeyValuePair<UIElementReference, List<IUICleanup>> item in OpenedUIs.ToArray())
        {
            if (!item.Value.Contains(ui)) continue;
            CloseUI(item.Key);
        }
    }

    public UISetup OpenUI(UIElementReference ui)
    {
        CloseAllUI();
        ui.IsVisible = true;
        return new UISetup(ui, this);
    }
}
