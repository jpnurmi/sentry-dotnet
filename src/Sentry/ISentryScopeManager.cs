namespace Sentry;

/// <summary>
/// Scope management.
/// </summary>
/// <remarks>
/// An implementation shall create new scopes and allow consumers
/// modify the current scope.
/// </remarks>
public interface ISentryScopeManager
{
    /// <summary>
    /// Configures the current scope through the callback.
    /// </summary>
    /// <param name="configureScope">The configure scope callback.</param>
    public void ConfigureScope(Action<Scope> configureScope);

    /// <summary>
    /// Configures the current scope through the callback.
    /// </summary>
    /// <param name="configureScope">The configure scope callback.</param>
    /// <param name="arg">The argument to pass to the configure scope callback.</param>
    public void ConfigureScope<TArg>(Action<Scope, TArg> configureScope, TArg arg);

    /// <summary>
    /// Configures the current scope through the callback asynchronously.
    /// </summary>
    /// <param name="configureScope">The configure scope callback.</param>
    /// <returns>A task that completes when the callback is done or a completed task if the SDK is disabled.</returns>
    public Task ConfigureScopeAsync(Func<Scope, Task> configureScope);

    /// <summary>
    /// Configures the current scope through the callback asynchronously.
    /// </summary>
    /// <param name="configureScope">The configure scope callback.</param>
    /// <param name="arg">The argument to pass to the configure scope callback.</param>
    /// <returns>A task that completes when the callback is done or a completed task if the SDK is disabled.</returns>
    public Task ConfigureScopeAsync<TArg>(Func<Scope, TArg, Task> configureScope, TArg arg);

    /// <summary>
    /// Sets a tag on the current scope.
    /// </summary>
    public void SetTag(string key, string value);

    /// <summary>
    /// Removes a tag from the current scope.
    /// </summary>
    public void UnsetTag(string key);

    /// <summary>
    /// Binds the client to the current scope.
    /// </summary>
    /// <param name="client">The client.</param>
    public void BindClient(ISentryClient client);

    /// <summary>
    /// Pushes a new scope into the stack which is removed upon Dispose.
    /// </summary>
    /// <returns>A disposable which removes the scope
    /// from the environment when invoked.</returns>
    public IDisposable PushScope();

    /// <summary>
    /// Pushes a new scope into the stack which is removed upon Dispose.
    /// </summary>
    /// <param name="state">A state to associate with the scope.</param>
    /// <returns>A disposable which removes the scope
    /// from the environment when invoked.</returns>
    public IDisposable PushScope<TState>(TState state);
}
