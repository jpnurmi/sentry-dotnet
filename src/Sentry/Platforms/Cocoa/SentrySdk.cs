using Sentry.Cocoa;
using Sentry.Cocoa.Extensions;
using Sentry.Extensibility;
using Sentry.Internal;

// ReSharper disable once CheckNamespace
namespace Sentry;

public static partial class SentrySdk
{
    private static void InitSentryCocoaSdk(SentryOptions options)
    {
        options.LogDebug("Initializing native SDK");

        // Set default release and distribution
        options.Release ??= GetDefaultReleaseString();
        options.Distribution ??= GetDefaultDistributionString();

        // Set options for the Cocoa SDK
        var nativeOptions = new SentryCocoaSdkOptions();

        // These options are copied over from our SentryOptions
        nativeOptions.AttachStacktrace = options.AttachStacktrace;
        nativeOptions.Debug = options.Debug;
        nativeOptions.DiagnosticLevel = options.DiagnosticLevel.ToCocoaSentryLevel();
        nativeOptions.Dsn = options.Dsn;
        // Hardcode to false because .NET SDK manages sessions itself
        nativeOptions.EnableAutoSessionTracking = false;
        nativeOptions.EnableCaptureFailedRequests = options.CaptureFailedRequests;
        nativeOptions.FailedRequestStatusCodes = GetFailedRequestStatusCodes(options.FailedRequestStatusCodes);
        nativeOptions.MaxAttachmentSize = (nuint)options.MaxAttachmentSize;
        nativeOptions.MaxBreadcrumbs = (nuint)options.MaxBreadcrumbs;
        nativeOptions.MaxCacheItems = (nuint)options.MaxCacheItems;
        nativeOptions.ReleaseName = options.Release;
        nativeOptions.SampleRate = options.SampleRate;
        nativeOptions.SendClientReports = options.SendClientReports;
        nativeOptions.SendDefaultPii = options.SendDefaultPii;
        nativeOptions.SessionTrackingIntervalMillis = (nuint)options.AutoSessionTrackingInterval.TotalMilliseconds;

        if (options.Environment is { } environment)
        {
            nativeOptions.Environment = environment;
        }

        // These options are not available in the Sentry Cocoa SDK
        // nativeOptions.? = options.InitCacheFlushTimeout;
        // nativeOptions.? = options.MaxQueueItems;
        // nativeOptions.? = options.ShutdownTimeout;

        // NOTE: options.CacheDirectoryPath - No option for this in Sentry Cocoa, but caching is still enabled
        // https://github.com/getsentry/sentry-cocoa/issues/1051

        // NOTE: Tags in options.DefaultTags should not be passed down, because we already call SetTag on each
        //       one when sending events, which is relayed through the scope observer.

        if (options.BeforeBreadcrumbInternal is { } beforeBreadcrumb)
        {
            nativeOptions.BeforeBreadcrumb = b =>
            {
                // Note: The Cocoa SDK doesn't yet support hints.
                // See https://github.com/getsentry/sentry-cocoa/issues/2325
                var hint = new SentryHint();
                var breadcrumb = b.ToBreadcrumb(options.DiagnosticLogger);
                var result = beforeBreadcrumb(breadcrumb, hint)?.ToCocoaBreadcrumb();

                // Note: Nullable result is allowed but delegate is generated incorrectly
                // See https://github.com/xamarin/xamarin-macios/issues/15299#issuecomment-1201863294
                return result!;
            };
        }

        // These options we have behind feature flags
        if (options is { IsPerformanceMonitoringEnabled: true, Native.EnableTracing: true })
        {
            nativeOptions.TracesSampleRate = options.TracesSampleRate;

            if (options.TracesSampler is { } tracesSampler)
            {
                nativeOptions.TracesSampler = cocoaContext =>
                {
                    var context = cocoaContext.ToTransactionSamplingContext();
                    var result = tracesSampler(context);

                    // Note: Nullable result is allowed but delegate is generated incorrectly
                    // See https://github.com/xamarin/xamarin-macios/issues/15299#issuecomment-1201863294
                    return result!;
                };
            }
        }

        nativeOptions.BeforeSend = evt => ProcessOnBeforeSend(options, evt)!;

        if (options.OnCrashedLastRun is { } onCrashedLastRun)
        {
            nativeOptions.OnCrashedLastRun = evt =>
            {
                // because we delegate to user code, we need to protect anything that could happen in this event
                try
                {
                    var sentryEvent = evt.ToSentryEvent();
                    if (sentryEvent != null)
                    {
                        onCrashedLastRun(sentryEvent);
                    }
                }
                catch (Exception ex)
                {
                    options.LogError(ex, "Crashed Last Run Error");
                }
            };
        }

        // These options are from Cocoa's SentryOptions
        nativeOptions.AttachScreenshot = options.Native.AttachScreenshot;
        nativeOptions.AppHangTimeoutInterval = options.Native.AppHangTimeoutInterval.TotalSeconds;
        nativeOptions.IdleTimeout = options.Native.IdleTimeout.TotalSeconds;
        nativeOptions.Dist = options.Distribution;
        nativeOptions.EnableAppHangTracking = options.Native.EnableAppHangTracking;
        nativeOptions.EnableAppHangTrackingV2 = options.Native.EnableAppHangTrackingV2;
        nativeOptions.EnableAutoBreadcrumbTracking = options.Native.EnableAutoBreadcrumbTracking;
        nativeOptions.EnableAutoPerformanceTracing = options.Native.EnableAutoPerformanceTracing;
        nativeOptions.EnableCoreDataTracing = options.Native.EnableCoreDataTracing;
        nativeOptions.EnableFileIOTracing = options.Native.EnableFileIOTracing;
        nativeOptions.EnableNetworkBreadcrumbs = options.Native.EnableNetworkBreadcrumbs;
        nativeOptions.EnableNetworkTracking = options.Native.EnableNetworkTracking;
#pragma warning disable CS0618 // Type or member is obsolete
        nativeOptions.EnableWatchdogTerminationTracking = options.Native.EnableWatchdogTerminationTracking;
#pragma warning restore CS0618 // Type or member is obsolete
        nativeOptions.EnableSwizzling = options.Native.EnableSwizzling;
        nativeOptions.EnableUIViewControllerTracing = options.Native.EnableUIViewControllerTracing;
        nativeOptions.EnableUserInteractionTracing = options.Native.EnableUserInteractionTracing;
        nativeOptions.UrlSessionDelegate = options.Native.UrlSessionDelegate;

        // StitchAsyncCode removed from Cocoa SDK in 8.6.0 with https://github.com/getsentry/sentry-cocoa/pull/2973
        // nativeOptions.StitchAsyncCode = options.Native.StitchAsyncCode;

        // In-App Excludes and Includes to be passed to the Cocoa SDK
        options.Native.InAppExcludes?.ForEach(x => nativeOptions.AddInAppExclude(x));
        options.Native.InAppIncludes?.ForEach(x => nativeOptions.AddInAppInclude(x));

        // These options are intentionally not expose or modified
        // nativeOptions.Enabled
        // nativeOptions.SdkInfo
        // nativeOptions.Integrations
        // nativeOptions.DefaultIntegrations
        // nativeOptions.EnableProfiling  (deprecated)

        // Set hybrid SDK name
        SentryCocoaHybridSdk.SetSdkName("sentry.cocoa.dotnet");

        // Now initialize the Cocoa SDK
        SentryCocoaSdk.StartWithOptions(nativeOptions);

        // Set options for the managed SDK that depend on the Cocoa SDK. (The user will not be able to modify these.)
        options.AddEventProcessor(new CocoaEventProcessor());
        options.CrashedLastRun = () => SentryCocoaSdk.CrashedLastRun;
        options.EnableScopeSync = true;
        options.ScopeObserver = new CocoaScopeObserver(options);

        // Note: don't use AddProfilingIntegration as it would print a warning if user used it too.
        if (!options.HasIntegration<ProfilingIntegration>())
        {
            options.AddIntegration(new ProfilingIntegration());
        }
        // TODO: Pause/Resume
    }

    private static string GetDefaultReleaseString()
    {
        var packageName = GetBundleValue("CFBundleIdentifier");
        var packageVersion = GetBundleValue("CFBundleShortVersionString");
        var buildVersion = GetBundleValue("CFBundleVersion");

        return $"{packageName}@{packageVersion}+{buildVersion}";
    }

    private static string GetDefaultDistributionString() => GetBundleValue("CFBundleVersion");

    private static string GetBundleValue(string key) => NSBundle.MainBundle.ObjectForInfoDictionary(key).ToString();

    private static CocoaSdk.SentryHttpStatusCodeRange[] GetFailedRequestStatusCodes(IList<HttpStatusCodeRange> httpStatusCodeRanges)
    {
        var nativeRanges = new CocoaSdk.SentryHttpStatusCodeRange[httpStatusCodeRanges.Count];
        for (var i = 0; i < httpStatusCodeRanges.Count; i++)
        {
            var range = httpStatusCodeRanges[i];
            nativeRanges[i] = new CocoaSdk.SentryHttpStatusCodeRange(range.Start, range.End);
        }

        return nativeRanges;
    }

    [DebuggerStepThrough]
    internal static CocoaSdk.SentryEvent? ProcessOnBeforeSend(SentryOptions options, CocoaSdk.SentryEvent evt)
        => ProcessOnBeforeSend(options, evt, CurrentHub);

    /// <summary>
    /// This overload allows us to inject an IHub for testing. During normal execution, the CurrentHub is used.
    /// However, since this class is static, there's no easy alternative way to inject this when executing tests.
    /// </summary>
    internal static CocoaSdk.SentryEvent? ProcessOnBeforeSend(SentryOptions options, CocoaSdk.SentryEvent evt, IHub hub)
    {
        if (hub is DisabledHub)
        {
            return evt;
        }

        // When we have an unhandled managed exception, we send that to Sentry twice - once managed and once native.
        // The managed exception is what a .NET developer would expect, and it is sent by the Sentry.NET SDK
        // But we also get a native SIGABRT since it crashed the application, which is sent by the Sentry Cocoa SDK.

        // There should only be one exception on the event in this case
        if ((options.Native.SuppressSignalAborts || options.Native.SuppressExcBadAccess) && evt.Exceptions?.Length == 1)
        {
            // It will match the following characteristics
            var ex = evt.Exceptions[0];

            // Thankfully, sometimes we can see Xamarin's unhandled exception handler on the stack trace, so we can filter
            // them out. Here is the function that calls abort(), which we will use as a filter:
            // https://github.com/xamarin/xamarin-macios/blob/c55fbdfef95028ba03d0f7a35aebca03bd76f852/runtime/runtime.m#L1114-L1122
            if (options.Native.SuppressSignalAborts && ex.Type == "SIGABRT" && ex.Value == "Signal 6, Code 0" &&
                ex.Stacktrace?.Frames.Any(f => f.Function == "xamarin_unhandled_exception_handler") is true)
            {
                // Don't send it
                options.LogDebug("Discarded {0} error ({1}). Captured as  managed exception instead.", ex.Type,
                    ex.Value);
                return null!;
            }

            // Similar workaround for NullReferenceExceptions. We don't have any easy way to know whether the
            // exception is managed code (compiled to native) or original native code though.
            // See: https://github.com/getsentry/sentry-dotnet/issues/3776
            if (options.Native.SuppressExcBadAccess && ex.Type == "EXC_BAD_ACCESS")
            {
                // Don't send it
                options.LogDebug("Discarded {0} error ({1}). Captured as  managed exception instead.", ex.Type,
                    ex.Value);
                return null!;
            }
        }

        // We run our SIGABRT checks first before running managed processors.
        // Because we delegate to user code, we need to catch/log exceptions.
        try
        {
            // Normally the event processors would be invoked by the SentryClient, but the Cocoa SDK has its own client,
            // so we need to manually invoke any managed event processors here to apply them to Native events.
            var manualProcessors = GetEventProcessors(hub)
                .Where(p => p is not MainSentryEventProcessor)
                .ToArray();
            if (manualProcessors.Length == 0 && options.BeforeSendInternal is null)
            {
                return evt;
            }

            var sentryEvent = evt.ToSentryEvent();
            if (SentryEventHelper.ProcessEvent(sentryEvent, manualProcessors, null, options) is not { } processedEvent)
            {
                return null;
            }

            processedEvent = SentryEventHelper.DoBeforeSend(processedEvent, new SentryHint(), options);
            if (processedEvent == null)
            {
                return null;
            }

            // we only support a subset of mutated data to be passed back to the native SDK at this time
            processedEvent.CopyToCocoaSentryEvent(evt);

            return evt;
        }
        catch (Exception ex)
        {
            options.LogError(ex, "Error running managed event processors for native event");
            return evt;
        }

        static IEnumerable<ISentryEventProcessor> GetEventProcessors(IHub hub)
        {
            if (hub is Hub fullHub)
            {
                return fullHub.ScopeManager.GetCurrent().Key.GetAllEventProcessors();
            }
            IEnumerable<ISentryEventProcessor>? eventProcessors = null;
            hub.ConfigureScope(scope => eventProcessors = scope.GetAllEventProcessors());
            return eventProcessors ?? [];
        }
    }
}
