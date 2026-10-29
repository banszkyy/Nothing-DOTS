using System.Collections.Generic;
using Unity.Entities;
using UnityEngine.UIElements;

public class UnitMenuManager : Singleton<UnitMenuManager>, IUISetup<Entity>, IUICleanup
{
    UnitSchema? _ui;
    Entity _entity = Entity.Null;
    readonly List<(IUICleanup Manager, VisualElement Element)> _views = new();

    void Update()
    {
        if (!_ui.IsVisible()) return;

        if (UIManager.Instance.GrapESC())
        {
            UIManager.Instance.CloseUI(this);
            return;
        }

        EntityManager entityManager = ConnectionManager.ClientOrDefaultWorld.EntityManager;
        if (!entityManager.Exists(_entity))
        {
            UIManager.Instance.CloseUI(this);
            return;
        }
    }

    static TemplateContainer MakeView(UnitSchema ui, VisualTreeAsset viewAsset)
    {
        TemplateContainer view = viewAsset.Instantiate();
        view.style.flexGrow = 1;
        view.style.flexBasis = new Length(50, LengthUnit.Percent);
        ui.ViewsContainer.Add(view);
        return view;
    }

    public void Setup(VisualElement ui, Entity entity)
    {
        _ui = new(ui);
        _entity = entity;
        _views.Clear();
        _ui.ViewsContainer.Clear();

        EntityManager entityManager = ConnectionManager.ClientOrDefaultWorld.EntityManager;

        if (entityManager.HasComponent<Factory>(entity))
        {
            TemplateContainer view = MakeView(_ui, UIManager.Instance.Factory);
            FactoryManager.Instance.Setup(view, entity);
            Tooltips.Instance.Reregister(view);
            _views.Add((FactoryManager.Instance, view));
        }

        if (entityManager.HasComponent<Facility>(entity))
        {
            TemplateContainer view = MakeView(_ui, UIManager.Instance.Facility);
            FacilityManager.Instance.Setup(view, entity);
            Tooltips.Instance.Reregister(view);
            _views.Add((FacilityManager.Instance, view));
        }

        if (entityManager.HasComponent<Processor>(entity))
        {
            TemplateContainer view = MakeView(_ui, UIManager.Instance.Processor);
            TerminalManager.Instance.Setup(view, entity);
            Tooltips.Instance.Reregister(view);
            _views.Add((TerminalManager.Instance, view));
        }
    }

    public void Cleanup()
    {
        _entity = Entity.Null;
        foreach ((IUICleanup viewManager, _) in _views)
        {
            viewManager.Cleanup();
        }
        _views.Clear();
        _ui?.ViewsContainer.Clear();
        _ui = null;
    }
}
