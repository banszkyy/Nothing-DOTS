using System;
using UnityEngine.UIElements;

static class UISchemaExtensions
{
    public static TSchema AddNew<TSchema>(this VisualElement target, VisualTreeAsset visualTreeAsset) where TSchema : IDocumentSchema
    {
        VisualElement element = visualTreeAsset.Instantiate();
        target.Add(element);
        TSchema res = Activator.CreateInstance<TSchema>();
        res.Q(element);
        return res;
    }
}
