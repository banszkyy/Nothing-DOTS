using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Entities;
using UnityEngine.UIElements;

public static class UIExtensions
{
    public static void SyncList<T>(
        this VisualElement container,
        DynamicBuffer<T> collection,
        VisualTreeAsset itemAsset,
        Action<T, VisualElement, bool> updater,
        Predicate<T>? predicate = null)
        where T : unmanaged
        => SyncList(
            container,
            collection.AsNativeArray(),
            itemAsset,
            updater,
            predicate
        );

    public static void SyncList<T>(
        this VisualElement container,
        NativeList<T> collection,
        VisualTreeAsset itemAsset,
        Action<T, VisualElement, bool> updater,
        Predicate<T>? predicate = null)
        where T : unmanaged
        => SyncList(
            container,
            collection.AsArray(),
            itemAsset,
            updater,
            predicate
        );

    public static void SyncList<T>(
        this VisualElement container,
        NativeArray<T> collection,
        VisualTreeAsset itemAsset,
        Action<T, VisualElement, bool> updater,
        Predicate<T>? predicate = null)
        where T : unmanaged
    {
        VisualElement[] childrenElement = container.Children().ToArray();
        int n = 0;

        for (int i = 0; i < collection.Length; i++)
        {
            if (predicate is not null && !predicate(collection[i])) continue;

            if (n < childrenElement.Length)
            {
                VisualElement element = childrenElement[n];
                updater.Invoke(collection[i], element, true);
            }
            else
            {
                VisualElement element = itemAsset.Instantiate();
                container.Add(element);
                updater.Invoke(collection[i], element, false);
            }

            n++;
        }

        for (int i = n; i < childrenElement.Length; i++)
        {
            container.Remove(childrenElement[i]);
        }
    }

    public static void SyncList<T>(
        this VisualElement container,
        IReadOnlyList<T> collection,
        VisualTreeAsset itemAsset,
        Action<T, VisualElement, bool> updater,
        Predicate<T>? predicate = null)
    {
        VisualElement[] childrenElement = container.Children().ToArray();
        int n = 0;

        for (int i = 0; i < collection.Count; i++)
        {
            if (predicate is not null && !predicate(collection[i])) continue;

            if (n < childrenElement.Length)
            {
                VisualElement element = childrenElement[n];
                updater.Invoke(collection[i], element, true);
            }
            else
            {
                VisualElement element = itemAsset.Instantiate();
                container.Add(element);
                updater.Invoke(collection[i], element, false);
            }

            n++;
        }

        for (int i = n; i < childrenElement.Length; i++)
        {
            container.Remove(childrenElement[i]);
        }
    }

    public static void SyncList<T>(
        this VisualElement container,
        IEnumerable<T> collection,
        VisualTreeAsset itemAsset,
        Action<T, VisualElement, bool> updater)
    {
        VisualElement[] childrenElement = container.Children().ToArray();
        int i = 0;

        foreach (T item in collection)
        {
            if (i < childrenElement.Length)
            {
                VisualElement element = childrenElement[i];
                updater.Invoke(item, element, true);
            }
            else
            {
                VisualElement element = itemAsset.Instantiate();
                container.Add(element);
                updater.Invoke(item, element, false);
            }

            i++;
        }

        for (; i < childrenElement.Length; i++)
        {
            container.Remove(childrenElement[i]);
        }
    }
}
