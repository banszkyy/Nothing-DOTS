using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections;
#if UNITY_EDITOR
using Unity.EditorCoroutines.Editor;
#endif

#if UNITY_EDITOR
[UnityEditor.InitializeOnLoad]
#endif
public class ThreadedDataRequester : Singleton<ThreadedDataRequester>
{
    static readonly ConcurrentQueue<TaskInfo> DataQueue = new();
    static readonly List<Task> Tasks = new();

    public static void RequestData<T>(Func<T> task, Action<T> callback)
    {
        Task? _task = null;
        _task = Task.Run(() =>
        {
            DataQueue.Enqueue(new TaskInfo(v => callback.Invoke((T)v!), task.Invoke(), _task!));
        });
        Tasks.Add(_task);
    }

    public static void RequestData<T>(Func<Task<T>> task, Action<T> callback)
    {
        Task? _task = null;
        _task = task.Invoke().ContinueWith((task) =>
        {
            if (task.IsFaulted) Debug.LogError(task.Exception);
            else if (!task.IsCanceled) DataQueue.Enqueue(new TaskInfo(v => callback.Invoke((T)v!), task.Result, _task!));
        });
        Tasks.Add(_task);
    }

    public static void RequestDataCoroutine<T>(Func<Action<T>, IEnumerator> task, Action<T> callback)
    {
        void CallbackWrapper(T v) => DataQueue.Enqueue(new TaskInfo(v => callback.Invoke((T)v!), v, null));

#if UNITY_EDITOR
        if (InstanceOrNull == null)
        {
            EditorCoroutineUtility.StartCoroutineOwnerless(task.Invoke(CallbackWrapper));
        }
        else
        {
            InstanceOrNull.StartCoroutine(task.Invoke(CallbackWrapper));
        }
#else
        Instance.StartCoroutine(task.Invoke(CallbackWrapper));
#endif
    }

#if UNITY_EDITOR
    static ThreadedDataRequester()
    {
        UnityEditor.EditorApplication.update += DequeueResults;
    }
#endif

    static void DequeueResults()
    {
        int l = DataQueue.Count;
        for (int i = 0; i < l; i++)
        {
            if (!DataQueue.TryDequeue(out TaskInfo taskInfo)) break;
            taskInfo.Callback(taskInfo.Parameter);
            if (taskInfo.Task != null) Tasks.Remove(taskInfo.Task);
        }

        for (int i = 0; i < Tasks.Count; i++)
        {
            Task task = Tasks[i];
            if (task.IsFaulted)
            {
                Debug.LogError(task.Exception);
                Tasks.RemoveAt(i--);
            }
            else if (task.IsCanceled)
            {
                Debug.LogError("Task cancelled");
                Tasks.RemoveAt(i--);
            }
        }
    }

    void Update() => DequeueResults();

    void OnDestroy()
    {
        Task.WaitAll(Tasks.ToArray(), 10000);
    }

    readonly struct TaskInfo
    {
        public readonly Action<object?> Callback;
        public readonly object? Parameter;
        public readonly Task? Task;

        public TaskInfo(Action<object?> callback, object? parameter, Task? task)
        {
            Callback = callback;
            Parameter = parameter;
            Task = task;
        }
    }
}
