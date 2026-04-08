using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

public class UIManager : Singleton<UIManager>
{
    public readonly struct UISetup
    {
        readonly UIDocument _ui;
        readonly UIManager _manager;

        public UISetup(UIDocument ui, UIManager manager)
        {
            _ui = ui;
            _manager = manager;
        }

        public UISetup Setup<TManager, TContext>(TManager manager, TContext context)
            where TManager : IUISetup<TContext>, IUICleanup
        {
            manager.Setup(_ui, context);
            _manager.OpenedUIs.TryAdd(_ui, new List<IUICleanup>());
            _manager.OpenedUIs[_ui].Add(manager);
            return this;
        }

        public UISetup Setup<TManager>(TManager manager)
            where TManager : IUISetup, IUICleanup
        {
            manager.Setup(_ui);
            _manager.OpenedUIs.TryAdd(_ui, new List<IUICleanup>());
            _manager.OpenedUIs[_ui].Add(manager);
            return this;
        }

        public UISetup Setup<TManager>()
            where TManager : Singleton<TManager>, IUISetup, IUICleanup
            => Setup(Singleton<TManager>.Instance);

        public UISetup Setup<TManager, TContext>(TContext context)
            where TManager : Singleton<TManager>, IUISetup<TContext>, IUICleanup
            => Setup(Singleton<TManager>.Instance, context);
    }

    [Header("Documents")]

    [SerializeField, NotNull] public UIDocument? MainMenu = default;
    [SerializeField, NotNull] public UIDocument? NetworkStatus = default;
    [SerializeField, NotNull] public UIDocument? Unit = default;
    [SerializeField, NotNull] public UIDocument? Factory = default;
    [SerializeField, NotNull] public UIDocument? Facility = default;
    [SerializeField, NotNull] public UIDocument? Pause = default;
    [SerializeField, NotNull] public UIDocument? Buildings = default;
    [SerializeField, NotNull] public UIDocument? Units = default;
    [SerializeField, NotNull] public UIDocument? DiskDrive = default;

    ImmutableArray<UIDocument>? _uis = default;

    public ImmutableArray<UIDocument> UIs => _uis ?? (_uis = ImmutableArray.Create(
        MainMenu,
        NetworkStatus,
        Unit,
        Factory,
        Facility,
        Pause,
        Buildings,
        Units,
        DiskDrive
    )).Value;

    [NotNull] Dictionary<UIDocument, List<IUICleanup>>? OpenedUIs = default;

    public bool AnyUIVisible
    {
        get
        {
            ImmutableArray<UIDocument> uis = UIs;
            for (int i = 0; i < uis.Length; i++)
            {
                if (uis[i].enabled && uis[i].gameObject.activeSelf) return true;
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

    public void CloseAllUI(UIDocument except)
    {
        for (int i = 0; i < UIs.Length; i++)
        {
            if (UIs[i] == except) continue;
            CloseUI(UIs[i]);
        }
    }

    public void CloseUI(UIDocument ui)
    {
        Tooltips.Instance.OnDocumentHidden(ui);
        if (OpenedUIs.TryGetValue(ui, out List<IUICleanup>? cleanup))
        {
            foreach (IUICleanup item in cleanup) item.Cleanup(ui);
            OpenedUIs.Remove(ui);
        }
        ui.rootVisualElement?.focusController.focusedElement?.Blur();
        ui.ForceSetActive(false);
    }

    public void CloseUI(IUICleanup ui)
    {
        foreach (KeyValuePair<UIDocument, List<IUICleanup>> item in OpenedUIs.ToArray())
        {
            if (!item.Value.Contains(ui)) continue;
            CloseUI(item.Key);
        }
    }

    public UISetup OpenUI(UIDocument ui)
    {
        CloseAllUI();
        ui.ForceSetActive(true);
        return new UISetup(ui, this);
    }
}
