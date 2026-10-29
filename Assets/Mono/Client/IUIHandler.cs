using UnityEngine.UIElements;

public interface IUISetup<TContext>
{
    void Setup(VisualElement ui, TContext context);
}

public interface IUISetup
{
    void Setup(VisualElement ui);
}

public interface IUICleanup
{
    void Cleanup();
}
