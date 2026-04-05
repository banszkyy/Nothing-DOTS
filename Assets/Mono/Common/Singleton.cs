using System;
using System.Diagnostics.CodeAnalysis;
using UnityEngine;

public class Singleton<T> : MonoBehaviour where T : UnityEngine.Object
{
    static T? _instance;

    public static T? InstanceOrNull => _instance;

    [SuppressMessage("Style", "IDE0029")]
    public static T Instance
    {
        get
        {
            if (_instance == null) _instance = FindAnyObjectByType<T>(FindObjectsInactive.Include);
            return _instance == null ? throw new NullReferenceException($"Singleton `{typeof(T).Name}` is null") : _instance;
        }
    }

    protected virtual void Awake()
    {
        if (_instance != null)
        {
            if (_instance != this) Debug.LogError($"Singleton `{typeof(T).Name}` already exists (`{_instance}`)", gameObject);
            return;
        }
        _instance = Instance;
    }
}
