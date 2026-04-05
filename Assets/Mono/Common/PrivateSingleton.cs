using System;
using System.Diagnostics.CodeAnalysis;
using UnityEngine;

public class PrivateSingleton<T> : MonoBehaviour where T : UnityEngine.Object
{
    static T? _instance;

    [SuppressMessage("Style", "IDE0029")]
    protected static T Instance
    {
        get
        {
            if (_instance == null) _instance = FindAnyObjectByType<T>(FindObjectsInactive.Include);
            return _instance == null ? throw new NullReferenceException($"Singleton `{typeof(T).Name}` is null") : _instance;
        }
    }

    protected virtual void Awake()
    {
        if (_instance != null) throw new InvalidOperationException($"Singleton `{typeof(T).Name}` already exists");
        _instance = Instance;
    }
}
