using Sentry.Extensibility;
using Sentry.Infrastructure;
using Sentry.Internal;
using Sentry.Internal.Extensions;
using Sentry.Protocol.Envelopes;

namespace Sentry;

#if !SENTRY_UNITY
/// <summary>
/// Sentry SDK entrypoint.
/// </summary>
/// <remarks>
/// This is a facade to the SDK instance.
/// It allows safe static access to a client and scope management.
/// When the SDK is uninitialized, calls to this class result in no-op so no callbacks are invoked.
/// </remarks>
public
#endif
static partial class SentrySdk
{
    internal static IHub CurrentHub = DisabledHub.Instance;

    internal static SentryOptions? CurrentOptions => CurrentHub.GetSentryOptions();

    /// <summary>
    /// Last event id recorded in the current scope.
    /// </summary>
    public static SentryId LastEventId { [DebuggerStepThrough] get => CurrentHub.LastEventId; }

    internal static IHub InitHub(SentryOptions options)
    {
        options.SetupLogging();

        ProcessInfo.Instance ??= new ProcessInfo(options);

        // Locate the DSN
        var dsnString = options.SettingLocator.GetDsn();

        // If it's either explicitly disabled or we couldn't resolve the DSN
        // from anywhere else, return a disabled hub.
        if (Dsn.IsDisabled(dsnString))
        {
            options.LogWarning("Init called with an empty string as the DSN. Sentry SDK will be disabled.");
            return DisabledHub.Instance;
        }

        // Validate DSN for an early exception in case it's malformed
        var dsn = Dsn.Parse(dsnString);
        if (dsn.SecretKey != null)
        {
            options.LogWarning("The provided DSN that contains a secret key. This is not required and will be ignored.");
        }

#pragma warning disable CS0162 // Unreachable code detected
#pragma warning disable 0162 // Unreachable code on old .NET frameworks
        AotHelper.CheckIsTrimmed(options.DiagnosticLogger);
        options.LogDebug(AotHelper.IsTrimmed
            ? "This looks like a Native AOT application build."
            : "This doesn't look like a Native AOT application build."
        );
#pragma warning restore 0162
#pragma warning restore CS0162 // Unreachable code detected

        // Initialize native platform SDKs here
        if (options.InitNativeSdks)
        {
#if __IOS__
            InitSentryCocoaSdk(options);
#elif ANDROID
            InitSentryAndroidSdk(options);
#elif NET8_0_OR_GREATER
            // TODO: Is this working properly? Currently we don't have any way to check if the app is being compiled AOT
            // All we know is whether trimming has been enabled or not. I think at the moment we'll be initialising
            // SentryNative for managed applications when they've been trimmed!
            if (SentryNative.IsAvailable)
            {
                InitNativeSdk(options);
            }
#endif
        }

        // We init the hub after native SDK in case the native init needs to adapt some options.
        var hub = new Hub(options);

        // Run all post-init callbacks set up by native integrations.
        foreach (var callback in options.PostInitCallbacks)
        {
            callback.Invoke(hub);
        }
        options.PostInitCallbacks.Clear();

        // Platform specific check for profiler misconfiguration.
#if __IOS__
        // No user-facing warning necessary - the integration is part of InitSentryCocoaSdk().
        Debug.Assert(options.IsProfilingEnabled == (options.TransactionProfilerFactory is not null));
#elif ANDROID
        LogWarningIfProfilingMisconfigured(options, " on Android");
#else
#if NET8_0_OR_GREATER
        if (AotHelper.IsTrimmed)
        {
            LogWarningIfProfilingMisconfigured(options, " for NativeAOT");
        }
        else
#endif
        {
            LogWarningIfProfilingMisconfigured(options, ", because ProfilingIntegration from package Sentry.Profiling" +
            " hasn't been registered. You can do that by calling 'options.AddProfilingIntegration()'");
        }
#endif

        return hub;
    }

    private static void LogWarningIfProfilingMisconfigured(SentryOptions options, string info)
    {
        if (options.IsProfilingEnabled && (options.TransactionProfilerFactory is null))
        {
            options.LogWarning("You've tried to enable profiling in options, but it is not available{0}.", info);
        }
    }

    /// <summary>
    /// Initializes the SDK while attempting to locate the DSN.
    /// </summary>
    /// <remarks>
    /// If the DSN is not found, the SDK will not change state.
    /// </remarks>
    /// <returns>An object that can be disposed to disable the Sentry SDK, if desired.</returns>
    /// <remarks>
    /// Disposing the result will flush previously-captured events and disable the SDK.
    /// In most cases there's no need to dispose the result.  There are only a few exceptions where it makes
    /// sense to dispose:
    /// <list type="bullet">
    /// <item>You have additional work to perform that you don't want Sentry to monitor.</item>
    /// <item>You have used <see cref="SentryOptions.DisableAppDomainProcessExitFlush"/>.</item>
    /// <item>You are integrating Sentry into an environment that has custom application lifetime events.</item>
    /// </list>
    /// </remarks>
    public static IDisposable Init() => Init((string?)null);

    /// <summary>
    /// Initializes the SDK with the specified DSN.
    /// </summary>
    /// <remarks>
    /// An empty string is interpreted as a disabled SDK.
    /// </remarks>
    /// <seealso href="https://develop.sentry.dev/sdk/overview/#usage-for-end-users"/>
    /// <param name="dsn">The dsn.</param>
    /// <returns>An object that can be disposed to disable the Sentry SDK, if desired.</returns>
    /// <remarks>
    /// Disposing the result will flush previously-captured events and disable the SDK.
    /// In most cases there's no need to dispose the result.  There are only a few exceptions where it makes
    /// sense to dispose:
    /// <list type="bullet">
    /// <item>You have additional work to perform that you don't want Sentry to monitor.</item>
    /// <item>You have used <see cref="SentryOptions.DisableAppDomainProcessExitFlush"/>.</item>
    /// <item>You are integrating Sentry into an environment that has custom application lifetime events.</item>
    /// </list>
    /// </remarks>
    public static IDisposable Init(string? dsn) => !Dsn.IsDisabled(dsn)
        ? Init(c => c.Dsn = dsn)
        : DisabledHub.Instance;

    /// <summary>
    /// Initializes the SDK with an optional configuration options callback.
    /// </summary>
    /// <param name="configureOptions">The configuration options callback.</param>
    /// <returns>An object that can be disposed to disable the Sentry SDK, if desired.</returns>
    /// <remarks>
    /// Disposing the result will flush previously-captured events and disable the SDK.
    /// In most cases there's no need to dispose the result.  There are only a few exceptions where it makes
    /// sense to dispose:
    /// <list type="bullet">
    /// <item>You have additional work to perform that you don't want Sentry to monitor.</item>
    /// <item>You have used <see cref="SentryOptions.DisableAppDomainProcessExitFlush"/>.</item>
    /// <item>You are integrating Sentry into an environment that has custom application lifetime events.</item>
    /// </list>
    /// </remarks>
    public static IDisposable Init(Action<SentryOptions>? configureOptions)
    {
        var options = new SentryOptions();
        configureOptions?.Invoke(options);

        return Init(options);
    }

    /// <summary>
    /// Initializes the SDK with the specified options instance.
    /// </summary>
    /// <param name="options">The options instance</param>
    /// <remarks>
    /// Used by integrations which have their own delegates.
    /// </remarks>
    /// <returns>An object that can be disposed to disable the Sentry SDK, if desired.</returns>
    /// <remarks>
    /// Disposing the result will flush previously-captured events and disable the SDK.
    /// In most cases there's no need to dispose the result.  There are only a few exceptions where it makes
    /// sense to dispose:
    /// <list type="bullet">
    /// <item>You have additional work to perform that you don't want Sentry to monitor.</item>
    /// <item>You have used <see cref="SentryOptions.DisableAppDomainProcessExitFlush"/>.</item>
    /// <item>You are integrating Sentry into an environment that has custom application lifetime events.</item>
    /// </list>
    /// </remarks>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static IDisposable Init(SentryOptions options) => UseHub(InitHub(options));

    internal static IDisposable UseHub(IHub hub)
    {
        var oldHub = Interlocked.Exchange(ref CurrentHub, hub);
        (oldHub as IDisposable)?.Dispose();
        return new DisposeHandle(hub);
    }

    /// <summary>
    /// Flushes the queue of captured events until the timeout set in <see cref="SentryOptions.FlushTimeout"/>
    /// is reached.
    /// </summary>
    /// <remarks>
    /// Blocks synchronously. Prefer <see cref="FlushAsync()"/> in async code.
    /// </remarks>
    [DebuggerStepThrough]
    public static void Flush() => CurrentHub.Flush();

    /// <summary>
    /// Flushes the queue of captured events until the timeout is reached.
    /// </summary>
    /// <param name="timeout">The amount of time allowed for flushing.</param>
    /// <remarks>
    /// Blocks synchronously. Prefer <see cref="FlushAsync(TimeSpan)"/> in async code.
    /// </remarks>
    [DebuggerStepThrough]
    public static void Flush(TimeSpan timeout) => CurrentHub.Flush(timeout);

    /// <summary>
    /// Flushes the queue of captured events until the timeout set in <see cref="SentryOptions.FlushTimeout"/>
    /// is reached.
    /// </summary>
    /// <returns>A task to await for the flush operation.</returns>
    [DebuggerStepThrough]
    public static Task FlushAsync() => CurrentHub.FlushAsync();

    /// <summary>
    /// Flushes the queue of captured events until the timeout is reached.
    /// </summary>
    /// <param name="timeout">The amount of time allowed for flushing.</param>
    /// <returns>A task to await for the flush operation.</returns>
    [DebuggerStepThrough]
    public static Task FlushAsync(TimeSpan timeout) => CurrentHub.FlushAsync(timeout);

    /// <summary>
    /// Close the SDK.
    /// </summary>
    /// <remarks>
    /// Flushes the events and disables the SDK.
    /// This method is mostly used for testing the library since
    /// Init returns a IDisposable that can be used to shutdown the SDK.
    /// </remarks>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static void Close()
    {
        var oldHub = Interlocked.Exchange(ref CurrentHub, DisabledHub.Instance);
        (oldHub as IDisposable)?.Dispose();
        ProcessInfo.Instance = null;
    }

    private class DisposeHandle : IDisposable
    {
        private IHub _localHub;
        public DisposeHandle(IHub hub) => _localHub = hub;

        public void Dispose()
        {
            _ = Interlocked.CompareExchange(ref CurrentHub, DisabledHub.Instance, _localHub);
            (_localHub as IDisposable)?.Dispose();
            _localHub = null!;
        }
    }

    /// <summary>
    /// Whether the SDK is enabled or not.
    /// </summary>
    public static bool IsEnabled { [DebuggerStepThrough] get => CurrentHub.IsEnabled; }

    /// <summary>
    /// Creates a new scope that will terminate when disposed.
    /// </summary>
    /// <remarks>
    /// Pushes a new scope while inheriting the current scope's data.
    /// </remarks>
    /// <param name="state">A state object to be added to the scope.</param>
    /// <returns>A disposable that when disposed, ends the created scope.</returns>
    [DebuggerStepThrough]
    public static IDisposable PushScope<TState>(TState state) => CurrentHub.PushScope(state);

    /// <summary>
    /// Creates a new scope that will terminate when disposed.
    /// </summary>
    /// <returns>A disposable that when disposed, ends the created scope.</returns>
    [DebuggerStepThrough]
    public static IDisposable PushScope() => CurrentHub.PushScope();

    /// <summary>
    /// Binds the client to the current scope.
    /// </summary>
    /// <param name="client">The client.</param>
    [DebuggerStepThrough]
    public static void BindClient(ISentryClient client) => CurrentHub.BindClient(client);

    /// <summary>
    /// Adds a breadcrumb to the current Scope.
    /// </summary>
    /// <param name="message">
    /// If a message is provided it’s rendered as text and the whitespace is preserved.
    /// Very long text might be abbreviated in the UI.</param>
    /// <param name="category">
    /// Categories are dotted strings that indicate what the crumb is or where it comes from.
    /// Typically it’s a module name or a descriptive string.
    /// For instance ui.click could be used to indicate that a click happened in the UI or flask could be used to indicate that the event originated in the Flask framework.
    /// </param>
    /// <param name="type">
    /// The type of breadcrumb.
    /// The default type is default which indicates no specific handling.
    /// Other types are currently http for HTTP requests and navigation for navigation events.
    /// <seealso href="https://develop.sentry.dev/sdk/event-payloads/breadcrumbs/#breadcrumb-types"/>
    /// </param>
    /// <param name="data">
    /// Data associated with this breadcrumb.
    /// Contains a sub-object whose contents depend on the breadcrumb type.
    /// Additional parameters that are unsupported by the type are rendered as a key/value table.
    /// </param>
    /// <param name="level">Breadcrumb level.</param>
    /// <seealso href="https://develop.sentry.dev/sdk/event-payloads/breadcrumbs/"/>
    [DebuggerStepThrough]
    public static void AddBreadcrumb(
        string message,
        string? category = null,
        string? type = null,
        IDictionary<string, string>? data = null,
        BreadcrumbLevel level = default)
        => CurrentHub.AddBreadcrumb(message, category, type, data, level);

    /// <summary>
    /// Adds a breadcrumb to the current scope.
    /// </summary>
    /// <remarks>
    /// This overload is intended to be used by integrations only.
    /// The objective is to allow better testability by allowing control of the timestamp set to the breadcrumb.
    /// </remarks>
    /// <param name="clock">An optional <see cref="ISystemClock"/>.</param>
    /// <param name="message">The message.</param>
    /// <param name="category">The category.</param>
    /// <param name="type">The type.</param>
    /// <param name="data">The data.</param>
    /// <param name="level">The level.</param>
    [DebuggerStepThrough]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static void AddBreadcrumb(
        ISystemClock? clock,
        string message,
        string? category = null,
        string? type = null,
        IDictionary<string, string>? data = null,
        BreadcrumbLevel level = default)
        => CurrentHub.AddBreadcrumb(clock, message, category, type, data, level);

    /// <summary>
    /// Adds a breadcrumb to the current Scope.
    /// </summary>
    /// <param name="breadcrumb">The breadcrumb to be added</param>
    /// <param name="hint">A hint providing additional context that can be used in the BeforeBreadcrumb callback</param>
    /// <see cref="AddBreadcrumb(string, string?, string?, IDictionary{string, string}?, BreadcrumbLevel)"/>
    [DebuggerStepThrough]
    public static void AddBreadcrumb(Breadcrumb breadcrumb, SentryHint? hint = null)
        => CurrentHub.AddBreadcrumb(breadcrumb, hint);

    /// <summary>
    /// Configures the scope through the callback.
    /// </summary>
    /// <param name="configureScope">The configure scope callback.</param>
    [DebuggerStepThrough]
    public static void ConfigureScope(Action<Scope> configureScope)
        => CurrentHub.ConfigureScope(configureScope);

    /// <summary>
    /// Configures the scope through the callback.
    /// <example>
    /// <code>
    /// object someValue = ...;
    /// SentrySdk.ConfigureScope(static (scope, arg) => scope.SetExtra("key", arg), someValue);
    /// </code>
    /// </example>
    /// </summary>
    /// <param name="configureScope">The configure scope callback.</param>
    /// <param name="arg">The argument to pass to the configure scope callback.</param>
    public static void ConfigureScope<TArg>(Action<Scope, TArg> configureScope, TArg arg)
        => CurrentHub.ConfigureScope(configureScope, arg);

    /// <summary>
    /// Configures the scope through the callback asynchronously.
    /// </summary>
    /// <param name="configureScope">The configure scope callback.</param>
    /// <returns>A task that completes when the callback is done or a completed task if the SDK is disabled.</returns>
    [DebuggerStepThrough]
    public static Task ConfigureScopeAsync(Func<Scope, Task> configureScope)
        => CurrentHub.ConfigureScopeAsync(configureScope);

    /// <summary>
    /// Configures the scope through the callback asynchronously.
    /// <example>
    /// <code>
    /// object someValue = ...;
    /// SentrySdk.ConfigureScopeAsync(static async (scope, arg) =>
    /// {
    ///     scope.SetExtra("key", arg);
    /// }, someValue);
    /// </code>
    /// </example>
    /// </summary>
    /// <param name="configureScope">The configure scope callback.</param>
    /// <param name="arg">The argument to pass to the configure scope callback.</param>
    /// <returns>A task that completes when the callback is done or a completed task if the SDK is disabled.</returns>
    [DebuggerStepThrough]
    public static Task ConfigureScopeAsync<TArg>(Func<Scope, TArg, Task> configureScope, TArg arg)
        => CurrentHub.ConfigureScopeAsync(configureScope, arg);

    /// <summary>
    /// Sets a tag on the current scope.
    /// </summary>
    [DebuggerStepThrough]
    public static void SetTag(string key, string value)
        => CurrentHub.SetTag(key, value);

    /// <summary>
    /// Removes a tag from the current scope.
    /// </summary>
    [DebuggerStepThrough]
    public static void UnsetTag(string key)
        => CurrentHub.UnsetTag(key);

    /// <inheritdoc cref="ISentryClient.CaptureEnvelope"/>
    [DebuggerStepThrough]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static bool CaptureEnvelope(Envelope envelope)
        => CurrentHub.CaptureEnvelope(envelope);

    /// <summary>
    /// Captures the event, passing a hint, using the specified scope.
    /// </summary>
    /// <param name="evt">The event.</param>
    /// <param name="scope">The scope.</param>
    /// <param name="hint">a hint for the event.</param>
    /// <returns>The Id of the event.</returns>
    [DebuggerStepThrough]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static SentryId CaptureEvent(SentryEvent evt, Scope? scope = null, SentryHint? hint = null)
        => CurrentHub.CaptureEvent(evt, scope, hint);

    /// <summary>
    /// Captures an event with a configurable scope.
    /// </summary>
    /// <remarks>
    /// This allows modifying a scope without affecting other events.
    /// </remarks>
    /// <param name="evt">The event.</param>
    /// <param name="configureScope">The callback to configure the scope.</param>
    /// <returns>The Id of the event.</returns>
    [DebuggerStepThrough]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static SentryId CaptureEvent(SentryEvent evt, Action<Scope> configureScope)
        => CurrentHub.CaptureEvent(evt, null, configureScope);

    /// <summary>
    /// Captures an event with a configurable scope.
    /// </summary>
    /// <remarks>
    /// This allows modifying a scope without affecting other events.
    /// </remarks>
    /// <param name="evt">The event.</param>
    /// <param name="hint">An optional hint to be provided with the event</param>
    /// <param name="configureScope">The callback to configure the scope.</param>
    /// <returns>The Id of the event.</returns>
    [DebuggerStepThrough]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static SentryId CaptureEvent(SentryEvent evt, SentryHint? hint, Action<Scope> configureScope)
        => CurrentHub.CaptureEvent(evt, hint, configureScope);

    /// <summary>
    /// Captures the exception.
    /// </summary>
    /// <param name="exception">The exception.</param>
    /// <returns>The Id of the event.</returns>
    [DebuggerStepThrough]
    public static SentryId CaptureException(Exception exception)
        => CurrentHub.CaptureException(exception);

    /// <summary>
    /// Captures the exception with a configurable scope.
    /// </summary>
    /// <remarks>
    /// This allows modifying a scope without affecting other events.
    /// </remarks>
    /// <param name="exception">The exception.</param>
    /// <param name="configureScope">The callback to configure the scope.</param>
    /// <returns>The Id of the event.</returns>
    [DebuggerStepThrough]
    public static SentryId CaptureException(Exception exception, Action<Scope> configureScope)
        => CurrentHub.CaptureException(exception, configureScope);

    /// <summary>
    /// Captures the message.
    /// </summary>
    /// <param name="message">The message to send.</param>
    /// <param name="level">The message level.</param>
    /// <returns>The Id of the event.</returns>
    [DebuggerStepThrough]
    public static SentryId CaptureMessage(string message, SentryLevel level = SentryLevel.Info)
        => CurrentHub.CaptureMessage(message, level);

    /// <summary>
    /// Captures the message with a configurable scope.
    /// </summary>
    /// <remarks>
    /// This allows modifying a scope without affecting other events.
    /// </remarks>
    /// <param name="message">The message to send.</param>
    /// <param name="configureScope">The callback to configure the scope.</param>
    /// <param name="level">The message level.</param>
    /// <returns>The Id of the event.</returns>
    [DebuggerStepThrough]
    public static SentryId CaptureMessage(string message, Action<Scope> configureScope, SentryLevel level = SentryLevel.Info)
        => CurrentHub.CaptureMessage(message, configureScope, level);

    /// <summary>
    /// Captures feedback from the user.
    /// </summary>
    [DebuggerStepThrough]
    public static void CaptureFeedback(SentryFeedback feedback, Action<Scope> configureScope, SentryHint? hint = null)
        => CurrentHub.CaptureFeedback(feedback, configureScope, hint);

    /// <summary>
    /// Captures feedback from the user.
    /// </summary>
    [DebuggerStepThrough]
    public static void CaptureFeedback(SentryFeedback feedback, Scope? scope = null, SentryHint? hint = null)
        => CurrentHub.CaptureFeedback(feedback, scope, hint);

    /// <summary>
    /// Captures feedback from the user.
    /// </summary>
    [DebuggerStepThrough]
    public static void CaptureFeedback(string message, string? contactEmail = null, string? name = null,
        string? replayId = null, string? url = null, SentryId? associatedEventId = null, Scope? scope = null,
        SentryHint? hint = null)
        => CurrentHub.CaptureFeedback(new SentryFeedback(message, contactEmail, name, replayId, url, associatedEventId),
            scope, hint);

    /// <summary>
    /// Captures a user feedback.
    /// </summary>
    /// <param name="userFeedback">The user feedback to send to Sentry.</param>
    [DebuggerStepThrough]
    [Obsolete("Use CaptureFeedback instead.")]
    public static void CaptureUserFeedback(UserFeedback userFeedback)
        => CurrentHub.CaptureUserFeedback(userFeedback);

    /// <summary>
    /// Captures a user feedback.
    /// </summary>
    /// <param name="eventId">The event Id.</param>
    /// <param name="email">The user email.</param>
    /// <param name="comments">The user comments.</param>
    /// <param name="name">The optional username.</param>
    [DebuggerStepThrough]
    [Obsolete("Use CaptureFeedback instead.")]
    public static void CaptureUserFeedback(SentryId eventId, string email, string comments, string? name = null)
        => CurrentHub.CaptureUserFeedback(new UserFeedback(eventId, name, email, comments));

    /// <summary>
    /// Captures a transaction.
    /// </summary>
    /// <remarks>
    /// Note: this method is NOT meant to be called from user code!
    /// Instead, call <see cref="ISpan.Finish()"/> on the transaction.
    /// </remarks>
    [DebuggerStepThrough]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static void CaptureTransaction(SentryTransaction transaction)
        => CurrentHub.CaptureTransaction(transaction);

    /// <summary>
    /// Captures a transaction.
    /// </summary>
    /// <remarks>
    /// Note: this method is NOT meant to be called from user code!
    /// Instead, call <see cref="ISpan.Finish()"/> on the transaction.
    /// </remarks>
    [DebuggerStepThrough]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static void CaptureTransaction(SentryTransaction transaction, Scope? scope, SentryHint? hint)
        => CurrentHub.CaptureTransaction(transaction, scope, hint);

    /// <summary>
    /// Captures a session update.
    /// </summary>
    [DebuggerStepThrough]
    public static void CaptureSession(SessionUpdate sessionUpdate)
        => CurrentHub.CaptureSession(sessionUpdate);

    /// <summary>
    /// Captures a check-in.
    /// </summary>
    /// <remarks>
    /// Capturing a check-in returns an ID. The ID can be used to update the status. I.e. to update a check-in you
    /// captured from `CheckInStatus.InProgress` to `CheckInStatus.Ok`.
    /// </remarks>
    /// <param name="monitorSlug">The monitor slug of the check-in.</param>
    /// <param name="status">The status of the check-in.</param>
    /// <param name="sentryId">The <see cref="SentryId"/> associated with the check-in.</param>
    /// <param name="duration">The duration of the check-in.</param>
    /// <param name="scope">The scope of the check-in.</param>
    /// <param name="configureMonitorOptions">The optional monitor config used to create a Check-In programmatically.</param>
    /// <returns>The ID of the check-in.</returns>
    [DebuggerStepThrough]
    public static SentryId CaptureCheckIn(string monitorSlug,
        CheckInStatus status,
        SentryId? sentryId = null,
        TimeSpan? duration = null,
        Scope? scope = null,
        Action<SentryMonitorOptions>? configureMonitorOptions = null)
        => CurrentHub.CaptureCheckIn(
            monitorSlug,
            status,
            sentryId,
            duration,
            scope,
            configureMonitorOptions);

    /// <summary>
    /// Starts a transaction if there is not already one active on the scope, otherwise starts a new child span on the
    /// currently active transaction.
    /// </summary>
    public static ISpan StartSpan(string operation, string description) => CurrentHub.StartSpan(operation, description);

    /// <summary>
    /// Starts a transaction.
    /// </summary>
    [DebuggerStepThrough]
    public static ITransactionTracer StartTransaction(
        ITransactionContext context,
        IReadOnlyDictionary<string, object?> customSamplingContext)
        => CurrentHub.StartTransaction(context, customSamplingContext);

    /// <summary>
    /// Starts a transaction.
    /// </summary>
    [DebuggerStepThrough]
    internal static ITransactionTracer StartTransaction(
        ITransactionContext context,
        IReadOnlyDictionary<string, object?> customSamplingContext,
        DynamicSamplingContext? dynamicSamplingContext)
        => CurrentHub.StartTransaction(context, customSamplingContext, dynamicSamplingContext);

    /// <summary>
    /// Starts a transaction.
    /// </summary>
    [DebuggerStepThrough]
    public static ITransactionTracer StartTransaction(ITransactionContext context)
        => CurrentHub.StartTransaction(context);

    /// <summary>
    /// Starts a transaction.
    /// </summary>
    [DebuggerStepThrough]
    public static ITransactionTracer StartTransaction(string name, string operation)
        => CurrentHub.StartTransaction(name, operation);

    /// <summary>
    /// Starts a transaction.
    /// </summary>
    [DebuggerStepThrough]
    public static ITransactionTracer StartTransaction(string name, string operation, string? description)
        => CurrentHub.StartTransaction(name, operation, description);

    /// <summary>
    /// Starts a transaction.
    /// </summary>
    [DebuggerStepThrough]
    public static ITransactionTracer StartTransaction(string name, string operation, SentryTraceHeader traceHeader)
        => CurrentHub.StartTransaction(name, operation, traceHeader);

    /// <summary>
    /// Binds specified exception the specified span.
    /// </summary>
    /// <remarks>
    /// This method is used internally and is not meant for public use.
    /// </remarks>
    [DebuggerStepThrough]
    public static void BindException(Exception exception, ISpan span)
        => CurrentHub.BindException(exception, span);

    /// <summary>
    /// Gets the currently active transaction.
    /// </summary>
    [DebuggerStepThrough]
    public static ITransactionTracer? GetTransaction() => CurrentHub.GetTransaction();

    /// <summary>
    /// Gets the last active span
    /// </summary>
    [DebuggerStepThrough]
    public static ISpan? GetSpan()
        => CurrentHub.GetSpan();

    /// <summary>
    /// Gets the Sentry trace header of the parent that allows tracing across services
    /// </summary>
    [DebuggerStepThrough]
    public static SentryTraceHeader? GetTraceHeader()
        => CurrentHub.GetTraceHeader();

    /// <summary>
    /// Gets the Sentry "baggage" header that allows tracing across services
    /// </summary>
    [DebuggerStepThrough]
    public static BaggageHeader? GetBaggage()
        => CurrentHub.GetBaggage();

    /// <summary>
    /// Continues a trace based on HTTP header values provided as strings.
    /// </summary>
    /// <remarks>
    /// If no "sentry-trace" header is provided a random trace ID and span ID is created.
    /// </remarks>
    [DebuggerStepThrough]
    public static TransactionContext ContinueTrace(
        string? traceHeader,
        string? baggageHeader,
        string? name = null,
        string? operation = null)
        => CurrentHub.ContinueTrace(traceHeader, baggageHeader, name, operation);

    /// <summary>
    /// Continues a trace based on HTTP header values.
    /// </summary>
    /// <remarks>
    /// If no "sentry-trace" header is provided a random trace ID and span ID is created.
    /// </remarks>
    [DebuggerStepThrough]
    public static TransactionContext ContinueTrace(
        SentryTraceHeader? traceHeader,
        BaggageHeader? baggageHeader,
        string? name = null,
        string? operation = null)
        => CurrentHub.ContinueTrace(traceHeader, baggageHeader, name, operation);

    /// <inheritdoc cref="IHub.StartSession"/>
    [DebuggerStepThrough]
    public static void StartSession()
        => CurrentHub.StartSession();

    /// <inheritdoc cref="IHub.EndSession"/>
    [DebuggerStepThrough]
    public static void EndSession(SessionEndStatus status = SessionEndStatus.Exited)
        => CurrentHub.EndSession(status);

    /// <inheritdoc cref="IHub.PauseSession"/>
    [DebuggerStepThrough]
    public static void PauseSession()
        => CurrentHub.PauseSession();

    /// <inheritdoc cref="IHub.ResumeSession"/>
    [DebuggerStepThrough]
    public static void ResumeSession()
        => CurrentHub.ResumeSession();

    /// <summary>
    /// Deliberately crashes an application, which is useful for testing and demonstration purposes.
    /// </summary>
    /// <remarks>
    /// The method is marked obsolete only to discourage accidental misuse.
    /// We do not intend to remove it.
    /// </remarks>
    [Obsolete("WARNING: This method deliberately causes a crash, and should not be used in a real application.")]
    public static void CauseCrash(CrashType crashType)
    {
        var info = $"{nameof(SentrySdk)}.{nameof(CauseCrash)}({nameof(CrashType)}.{crashType})";
        var msg = $"This exception was caused deliberately by {info}.";
        CurrentOptions?.LogDebug("Triggering a deliberate exception because {0} was called", info);

        switch (crashType)
        {
            case CrashType.Managed:
                throw new ApplicationException(msg);

            case CrashType.ManagedBackgroundThread:
                var thread = new Thread(() => throw new ApplicationException(msg));
                thread.Start();
                break;

#if ANDROID
            case CrashType.Java:
                JavaSdk.Android.Supplemental.Buggy.ThrowRuntimeException(msg);
                break;

            case CrashType.JavaBackgroundThread:
                JavaSdk.Android.Supplemental.Buggy.ThrowRuntimeExceptionOnBackgroundThread(msg);
                break;

            case CrashType.Native:
                NativeCrash();
                break;
#elif __IOS__
            case CrashType.Native:
                SentryCocoaSdk.Crash();
                break;
#elif NET8_0_OR_GREATER
            case CrashType.Native:
                if (!SentryNative.IsAvailable)
                {
                    CurrentOptions?.LogError("The support for capturing native crashes is limited to AOT compilation on platforms with SentryNative support.");
                    return;
                }

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    NativeStrlenMSVCRT(IntPtr.Zero);
                }
                else
                {
                    NativeStrlenLibC(IntPtr.Zero);
                }
                break;
#endif
            default:
                throw new ArgumentOutOfRangeException(nameof(crashType), crashType, null);
        }
        CurrentOptions?.LogWarning("Something went wrong in {0}, execution should never reach this.", info);
    }

#if ANDROID
    [System.Runtime.InteropServices.DllImport("libsentrysupplemental.so", EntryPoint = "crash")]
    private static extern void NativeCrash();
#elif NET8_0_OR_GREATER
    [DllImport("msvcrt", EntryPoint = "strlen")]
    private static extern IntPtr NativeStrlenMSVCRT(IntPtr str);

    [DllImport("libc", EntryPoint = "strlen")]
    private static extern IntPtr NativeStrlenLibC(IntPtr strt);
#endif
}
