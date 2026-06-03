using StrongInject;
using StrongInject.Internal;
using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;

[EditorBrowsable(EditorBrowsableState.Never)]
[SuppressMessage("Design", "CA1050:Declare types in namespaces", Justification = "Intentionally in the global namespace so the Run/RunAsync/Resolve extension methods are available without an explicit 'using StrongInject'; moving them into a namespace would break existing call sites.")]
public static class StrongInjectContainerExtensions
{
    public static TResult Run<T, TResult, TParam>(this IContainer<T> container, Func<T, TParam, TResult> func, TParam param)
    {
        if (container is null) throw new ArgumentNullException(nameof(container));
        return container.Run(func, param);
    }

    public static TResult Run<T, TResult>(this IContainer<T> container, Func<T, TResult> func)
    {
        if (container is null) throw new ArgumentNullException(nameof(container));
        return container.Run(static (t, func) => func(t), func);
    }

    public static void Run<T>(this IContainer<T> container, Action<T> action)
    {
        if (container is null) throw new ArgumentNullException(nameof(container));
        container.Run(static (t, action) =>
        {
            action(t);
            return default(object);
        }, action);
    }

    public static Owned<T> Resolve<T>(this IContainer<T> container)
    {
        if (container is null) throw new ArgumentNullException(nameof(container));
        return container.Resolve();
    }

    public static ValueTask<TResult> RunAsync<T, TResult, TParam>(this IAsyncContainer<T> container, Func<T, TParam, ValueTask<TResult>> func, TParam param)
    {
        if (container is null) throw new ArgumentNullException(nameof(container));
        return container.RunAsync(func, param);
    }

    public static ValueTask<TResult> RunAsync<T, TResult>(this IAsyncContainer<T> container, Func<T, ValueTask<TResult>> func)
    {
        if (container is null) throw new ArgumentNullException(nameof(container));
        return container.RunAsync(static (t, func) => func(t), func);
    }

    /// <param name="container"></param>
    /// <param name="func"></param>
    /// <param name="_">Ignore this parameter. Used to prefer overload <see cref="RunAsync{T, TResult}(IAsyncContainer{T}, Func{T, ValueTask{TResult}})"/> to this overload.</param>
    /// <returns></returns>
    public static ValueTask<TResult> RunAsync<T, TResult>(this IAsyncContainer<T> container, Func<T, Task<TResult>> func, DummyParameter? _ = null)
    {
        if (container is null) throw new ArgumentNullException(nameof(container));
        return container.RunAsync(static (t, func) => new ValueTask<TResult>(func(t)), func);
    }

    /// <param name="container"></param>
    /// <param name="func"></param>
    /// <param name="_">Ignore this parameter. Used to prefer overload <see cref="RunAsync{T, TResult}(IAsyncContainer{T}, Func{T, Task{TResult}}, DummyParameter?)"/> to this overload.</param>
    /// <returns></returns>
    public static ValueTask<TResult> RunAsync<T, TResult>(this IAsyncContainer<T> container, Func<T, TResult> func, DummyParameter? _ = null)
    {
        if (container is null) throw new ArgumentNullException(nameof(container));
        return container.RunAsync(static (t, func) => new ValueTask<TResult>(func(t)), func);
    }

    public static ValueTask RunAsync<T>(this IAsyncContainer<T> container, Func<T, ValueTask> action)
    {
        if (container is null) throw new ArgumentNullException(nameof(container));
        return container.RunAsync(static async (t, action) =>
        {
            await action(t);
            return default(object?);
        }, action).AsValueTask();
    }

    /// <param name="container"></param>
    /// <param name="action"></param>
    /// <param name="_">Ignore this parameter. Used to prefer overload <see cref="RunAsync{T}(IAsyncContainer{T}, Func{T, ValueTask})"/> to this overload.</param>
    /// <returns></returns>
    public static ValueTask RunAsync<T>(this IAsyncContainer<T> container, Func<T, Task> action, DummyParameter? _ = null)
    {
        if (container is null) throw new ArgumentNullException(nameof(container));
        return container.RunAsync(static async (t, action) =>
        {
            await action(t);
            return default(object?);
        }, action).AsValueTask();
    }

    /// <param name="container"></param>
    /// <param name="action"></param>
    /// <param name="_">Ignore this parameter. Used to prefer overload <see cref="RunAsync{T}(IAsyncContainer{T}, Func{T, Task}, DummyParameter?)"/> to this overload.</param>
    /// <returns></returns>
    public static ValueTask RunAsync<T>(this IAsyncContainer<T> container, Action<T> action, DummyParameter? _ = null)
    {
        if (container is null) throw new ArgumentNullException(nameof(container));
        return container.RunAsync(static (t, action) =>
        {
            action(t);
            return new ValueTask<object?>(default(object));
        }, action).AsValueTask();
    }

    public static ValueTask<AsyncOwned<T>> ResolveAsync<T>(this IAsyncContainer<T> container)
    {
        if (container is null) throw new ArgumentNullException(nameof(container));
        return container.ResolveAsync();
    }
}
