using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using Sentry.Extensibility;
using Sentry.Internal;
using Sentry.Internal.Http;
using Sentry.Internal.ScopeStack;
using Sentry.Protocol.Envelopes;
using Sentry.Testing;
using Xunit;
using Xunit.Abstractions;
using static Sentry.Internal.Constants;

namespace Sentry.Tests;

[Collection(nameof(SentrySdkCollection))]
public class SentrySdkTests : IDisposable
{
    private readonly IDiagnosticLogger _logger;

    public SentrySdkTests(ITestOutputHelper testOutputHelper)
    {
        _logger = Substitute.ForPartsOf<TestOutputDiagnosticLogger>(testOutputHelper);
    }

    [Fact]
    public void IsEnabled_StartsOffFalse()
    {
        Assert.False(SentrySdk.IsEnabled);
    }

    [Fact]
    public void LastEventId_NoEventsCaptured_IsEmpty()
    {
        Assert.Equal(default, SentrySdk.LastEventId);
    }

    [Fact]
    public void LastEventId_SetToEventId()
    {
        using var _ = SentrySdk.Init(o =>
        {
            o.Dsn = ValidDsn;
            o.AutoSessionTracking = false;
            o.BackgroundWorker = Substitute.For<IBackgroundWorker>();
            o.InitNativeSdks = false;
        });

        var id = SentrySdk.CaptureMessage("test");
        Assert.Equal(id, SentrySdk.LastEventId);
    }

    [Fact]
    public void LastEventId_Transaction_DoesNotReset()
    {
        using var _ = SentrySdk.Init(o =>
        {
            o.Dsn = ValidDsn;
            o.TracesSampleRate = 1.0;
            o.AutoSessionTracking = false;
            o.BackgroundWorker = Substitute.For<IBackgroundWorker>();
            o.InitNativeSdks = false;
        });

        var id = SentrySdk.CaptureMessage("test");
        var transaction = SentrySdk.StartTransaction("test", "test");
        transaction.Finish();
        Assert.Equal(id, SentrySdk.LastEventId);
    }

    [Fact]
    public void Init_BrokenDsn_Throws()
    {
        _ = Assert.Throws<UriFormatException>(() => SentrySdk.Init("invalid stuff"));
    }

    [Fact]
    public void Init_ValidDsn_EnablesSdk()
    {
        using var _ = SentrySdk.Init(o =>
        {
            o.Dsn = ValidDsn;
            o.AutoSessionTracking = false;
            o.BackgroundWorker = Substitute.For<IBackgroundWorker>();
            o.InitNativeSdks = false;
        });

        Assert.True(SentrySdk.IsEnabled);
    }

    [Fact]
    public void Init_ValidDsnEnvironmentVariable_EnablesSdk()
    {
        using var _ = SentrySdk.Init(o =>
        {
            o.FakeSettings().EnvironmentVariables[DsnEnvironmentVariable] = ValidDsn;
            o.AutoSessionTracking = false;
            o.BackgroundWorker = Substitute.For<IBackgroundWorker>();
            o.InitNativeSdks = false;
        });

        Assert.True(SentrySdk.IsEnabled);
    }

    [Fact]
    public void Init_InvalidDsnEnvironmentVariable_Throws()
    {
        // If the variable was set, to non empty string but value is broken, better crash than silently disable
        var ex = Assert.Throws<ArgumentException>(() =>
            SentrySdk.Init(o => o.FakeSettings().EnvironmentVariables[DsnEnvironmentVariable] = InvalidDsn));

        Assert.Equal("Invalid DSN: A Project Id is required.", ex.Message);
    }

    [Fact]
    public void Init_DisableDsnEnvironmentVariable_DisablesSdk()
    {
        using var _ = SentrySdk.Init(o => o.FakeSettings().EnvironmentVariables[DsnEnvironmentVariable] = SentryConstants.DisableSdkDsnValue);

        Assert.False(SentrySdk.IsEnabled);
    }

    [Fact]
    public void Init_EmptyDsn_DisabledSdk()
    {
        using (SentrySdk.Init(string.Empty))
        {
            Assert.False(SentrySdk.IsEnabled);
        }
    }

    [SkippableFact]
    public void Init_EmptyDsn_LogsWarning()
    {
#if SENTRY_DSN_DEFINED_IN_ENV
        Skip.If(true, "This test only works when the DSN is not configured as an environment variable.");
#endif
        var options = new SentryOptions
        {
            Dsn = SentryConstants.DisableSdkDsnValue,
            DiagnosticLogger = _logger,
            Debug = true,
            InitNativeSdks = false
        };

        using (SentrySdk.Init(options))
        {
            _logger.Received(1).Log(SentryLevel.Warning, "Init called with an empty string as the DSN. Sentry SDK will be disabled.");
        }
    }

    [Fact]
    public void Init_DsnWithSecret_LogsWarning()
    {
        var options = new SentryOptions
        {
            DiagnosticLogger = _logger,
            Debug = true,
            Dsn = "https://d4d82fc1c2c4032a83f3a29aa3a3aff:ed0a8589a0bb4d4793ac4c70375f3d65@fake-sentry.io:65535/2147483647",
            AutoSessionTracking = false,
            BackgroundWorker = Substitute.For<IBackgroundWorker>(),
            InitNativeSdks = false
        };

        using (SentrySdk.Init(options))
        {
            _logger.Received(1).Log(SentryLevel.Warning, "The provided DSN that contains a secret key. This is not required and will be ignored.");
        }
    }

    [Fact]
    public void Init_EmptyDsnDisabledDiagnostics_DoesNotLogWarning()
    {
        var options = new SentryOptions
        {
            Dsn = SentryConstants.DisableSdkDsnValue,
            DiagnosticLogger = _logger,
            Debug = false,
            InitNativeSdks = false,
        };

        using (SentrySdk.Init(options))
        {
            _logger.DidNotReceive().Log(Arg.Is(SentryLevel.Warning), Arg.Any<string>());
        }
    }

    [Fact]
    public void Init_MultipleCalls_ReplacesHubWithLatest()
    {
        var first = SentrySdk.Init(o =>
        {
            o.Dsn = ValidDsn;
            o.AutoSessionTracking = false;
            o.BackgroundWorker = Substitute.For<IBackgroundWorker>();
            o.InitNativeSdks = false;
        });
        SentrySdk.AddBreadcrumb("test", "category");
        var called = false;
        SentrySdk.ConfigureScope(p =>
        {
            called = true;
            _ = Assert.Single(p.Breadcrumbs);
        });
        Assert.True(called);
        called = false;

        var second = SentrySdk.Init(o =>
        {
            o.Dsn = ValidDsn;
            o.AutoSessionTracking = false;
            o.BackgroundWorker = Substitute.For<IBackgroundWorker>();
            o.InitNativeSdks = false;
        });
        SentrySdk.ConfigureScope(p =>
        {
            called = true;
            Assert.Empty(p.Breadcrumbs);
        });
        Assert.True(called);

        first.Dispose();
        second.Dispose();
    }

#if !__MOBILE__ && !CI_BUILD // Too slow on mobile. Too flaky in CI.
    [Theory]
    [InlineData(true)] // InitCacheFlushTimeout is more than enough time to process all messages
    [InlineData(false)] // InitCacheFlushTimeout is less time than needed to process all messages
    [InlineData(null)] // InitCacheFlushTimeout is not set
    public async Task Init_WithCache_BlocksUntilExistingCacheIsFlushed(bool? testDelayWorking)
    {
        // Note: We use a fake filesystem for this test, which uses only memory instead of disk.
        //       This keeps file IO access time out of the test.

        // Not too many, or this will be slow.  Not too few or this will be flaky.
        const int numEnvelopes = 5;

        // Set the delay for the transport here.  If the test becomes flaky, increase the timeout.
        var processingDelayPerEnvelope = TimeSpan.FromMilliseconds(200);

        // Arrange
        using var cacheDirectory = new TempDirectory();
        var cachePath = cacheDirectory.Path;

        // Pre-populate cache
        var initialInnerTransport = Substitute.For<ITransport>();
        var initialTransport = CachingTransport.Create(
            initialInnerTransport,
            new SentryOptions
            {
                Debug = true,
                DiagnosticLogger = _logger,
                Dsn = ValidDsn,
                CacheDirectoryPath = cachePath,
                AutoSessionTracking = false,
                InitNativeSdks = false,
            },
            startWorker: false);
        await using (initialTransport)
        {
            for (var i = 0; i < numEnvelopes; i++)
            {
                using var envelope = Envelope.FromEvent(new SentryEvent());
                await initialTransport.SendEnvelopeAsync(envelope);
            }
        }

        _logger.Log(SentryLevel.Debug, "Done adding to cache directory.");

        var countCompleted = 0;
        var transport = Substitute.For<ITransport>();
        transport.SendEnvelopeAsync(Arg.Any<Envelope>(), Arg.Any<CancellationToken>())
            .Returns(async callInfo =>
            {
                var token = callInfo.Arg<CancellationToken>();
                await Task.Delay(processingDelayPerEnvelope, token);
                Interlocked.Increment(ref countCompleted);
                _logger.Log(SentryLevel.Debug, $"Sent envelope {countCompleted}.");
            });

        // Set the timeout for the desired result
        var initFlushTimeout = testDelayWorking switch
        {
            // more than enough
            true => TimeSpan.FromTicks(processingDelayPerEnvelope.Ticks * (numEnvelopes * 10)),
            // enough for at least one, but not all
            false => TimeSpan.FromTicks(processingDelayPerEnvelope.Ticks * (numEnvelopes - 1)),
            // none at all
            null => TimeSpan.Zero
        };

        // Act
        SentryOptions options = null;
        try
        {
            var stopwatch = Stopwatch.StartNew();

            using var _ = SentrySdk.Init(o =>
            {
                // Disable process exit flush to resolve "There is no currently active test." errors.
                o.DisableAppDomainProcessExitFlush();

                o.Dsn = ValidDsn;
                o.Debug = true;
                o.DiagnosticLogger = _logger;
                o.CacheDirectoryPath = cachePath;
                o.InitCacheFlushTimeout = initFlushTimeout;
                o.Transport = transport;
                o.AutoSessionTracking = false;
                o.InitNativeSdks = false;
                options = o;
            });

            stopwatch.Stop();

            // Assert
            switch (testDelayWorking)
            {
                case true:
                    // We waited long enough to have them all
                    Assert.Equal(numEnvelopes, countCompleted);

                    // But we should not have waited longer than we needed to
                    Assert.True(stopwatch.Elapsed < initFlushTimeout, "Should not have waited for the entire timeout!");
                    break;
                case false:
                    // We only waited long enough to have at least one, but not all of them
                    Assert.InRange(countCompleted, 1, numEnvelopes - 1);
                    break;
                case null:
                    // We shouldn't have any, as we didn't ask to flush the cache on init
                    Assert.Equal(0, countCompleted);
                    break;
            }
        }
        finally
        {
            // cleanup to avoid disposing/deleting the temp directory while the cache worker is still running
            var cachingTransport = (CachingTransport)options!.Transport;
            await cachingTransport!.StopWorkerAsync();
        }
    }
#endif

    [Fact]
    public void Disposable_MultipleCalls_NoOp()
    {
        var disposable = SentrySdk.Init(o =>
        {
            o.Dsn = ValidDsn;
            o.AutoSessionTracking = false;
            o.BackgroundWorker = Substitute.For<IBackgroundWorker>();
            o.InitNativeSdks = false;
        });
        disposable.Dispose();
        disposable.Dispose();
        Assert.False(SentrySdk.IsEnabled);
    }

    [Fact]
    public void Dispose_DisposingFirst_DoesntAffectSecond()
    {
        var first = SentrySdk.Init(o =>
        {
            o.Dsn = ValidDsn;
            o.AutoSessionTracking = false;
            o.BackgroundWorker = Substitute.For<IBackgroundWorker>();
            o.InitNativeSdks = false;
        });
        var second = SentrySdk.Init(o =>
        {
            o.Dsn = ValidDsn;
            o.AutoSessionTracking = false;
            o.BackgroundWorker = Substitute.For<IBackgroundWorker>();
            o.InitNativeSdks = false;
        });
        SentrySdk.AddBreadcrumb("test", "category");
        first.Dispose();
        var called = false;
        SentrySdk.ConfigureScope(p =>
        {
            called = true;
            _ = Assert.Single(p.Breadcrumbs);
        });
        Assert.True(called);
        second.Dispose();
    }

    [Fact]
    public Task FlushAsync_NotInit_NoOp() => SentrySdk.FlushAsync();

    [Fact]
    public void PushScope_InstanceOf_DisabledClient()
    {
        Assert.Same(DisabledHub.Instance, SentrySdk.PushScope());
    }

    [Fact]
    public void PushScope_NullArgument_NoOp()
    {
        var scopeGuard = SentrySdk.PushScope(null as object);
        Assert.False(SentrySdk.IsEnabled);
        scopeGuard.Dispose();
    }

    [Fact]
    public void PushScope_Parameterless_NoOp()
    {
        var scopeGuard = SentrySdk.PushScope();
        Assert.False(SentrySdk.IsEnabled);
        scopeGuard.Dispose();
    }

    [Fact]
    public void PushScope_MultiCallState_SameDisposableInstance()
    {
        var state = new object();
        Assert.Same(SentrySdk.PushScope(state), SentrySdk.PushScope(state));
    }

    [Fact]
    public void PushScope_MultiCallParameterless_SameDisposableInstance() => Assert.Same(SentrySdk.PushScope(), SentrySdk.PushScope());

    [Fact]
    public void AddBreadcrumb_NoClock_NoOp() => SentrySdk.AddBreadcrumb(message: null!);

    [Fact]
    public void AddBreadcrumb_WithClock_NoOp() => SentrySdk.AddBreadcrumb(clock: null, null!);

    [Fact]
    public void ConfigureScope_Sync_CallbackNeverInvoked()
    {
        var invoked = false;
        SentrySdk.ConfigureScope(_ => invoked = true);
        Assert.False(invoked);
    }

    [Fact]
    public void ConfigureScope_SyncWithArg_CallbackNeverInvoked()
    {
        var invoked = false;
        SentrySdk.ConfigureScope((_, _) => invoked = true, "arg");
        Assert.False(invoked);
    }

    [Fact]
    public void ConfigureScope_SyncWithArg_ArgIsUsed()
    {
        using var _ = SentrySdk.Init(o =>
        {
            o.Dsn = ValidDsn;
            o.AutoSessionTracking = false;
            o.BackgroundWorker = Substitute.For<IBackgroundWorker>();
            o.InitNativeSdks = false;
        });

        const string key = "key";
        const string arg = "arg";

        SentrySdk.ConfigureScope((s, a) => s.SetTag(key, a), arg);

        string actual = null;
        SentrySdk.ConfigureScope(s => actual = s.Tags[key]);

        Assert.Equal(arg, actual);
    }

    [SkippableFact]
    public async Task ConfigureScope_OnTask_PropagatedToCaller()
    {
#if __ANDROID__
        Skip.If(true, "Flaky on Android");
#endif

        const string expected = "test";
        using var _ = SentrySdk.Init(o =>
        {
            o.Dsn = ValidDsn;
            o.AutoSessionTracking = false;
            o.BackgroundWorker = Substitute.For<IBackgroundWorker>();
            o.InitNativeSdks = false;
        });

        await ModifyScope().FailFastOnException();

        string actual = null;
        SentrySdk.ConfigureScope(s => actual = s.Breadcrumbs.First().Message);

        Assert.Equal(expected, actual);
        return;

        async Task ModifyScope()
        {
            await Task.Yield();
            SentrySdk.AddBreadcrumb(expected);
        }
    }

    [Fact]
    public void SetTag_SetsTagOnCurrentScope()
    {
        using var _ = SentrySdk.Init(o =>
        {
            o.Dsn = ValidDsn;
            o.AutoSessionTracking = false;
            o.BackgroundWorker = Substitute.For<IBackgroundWorker>();
            o.InitNativeSdks = false;
        });

        const string key = "key";
        const string value = "value";

        SentrySdk.SetTag(key, value);

        string actual = null;
        SentrySdk.ConfigureScope(s => actual = s.Tags[key]);

        Assert.Equal(value, actual);
    }

    [Fact]
    public void SetTag_NotInit_NoOp() => SentrySdk.SetTag("key", "value");

    [Fact]
    public void UnsetTag_UnsetsTagOnCurrentScope()
    {
        using var _ = SentrySdk.Init(o =>
        {
            o.Dsn = ValidDsn;
            o.AutoSessionTracking = false;
            o.BackgroundWorker = Substitute.For<IBackgroundWorker>();
            o.InitNativeSdks = false;
        });

        const string key = "key";
        const string value = "value";

        SentrySdk.SetTag(key, value);
        SentrySdk.UnsetTag(key);

        bool? containsKey = null;
        SentrySdk.ConfigureScope(s => containsKey = s.Tags.ContainsKey(key));

        Assert.True(containsKey is false);
    }

    [Fact]
    public void UnsetTag_NotInit_NoOp() => SentrySdk.UnsetTag("key");

    [Fact]
    public void CaptureEvent_WithConfiguredScope_ScopeAppliesToEvent()
    {
        const string expected = "test";
        var worker = Substitute.For<IBackgroundWorker>();

        using var _ = SentrySdk.Init(o =>
        {
            o.Dsn = ValidDsn;
            o.BackgroundWorker = worker;
            o.AutoSessionTracking = false;
            o.InitNativeSdks = false;
        });
        SentrySdk.CaptureEvent(new SentryEvent(), s => s.AddBreadcrumb(expected));

        worker.EnqueueEnvelope(
            Arg.Is<Envelope>(e => e.Items
                .Select(i => i.Payload)
                .OfType<JsonSerializable>()
                .Select(i => i.Source)
                .OfType<SentryEvent>()
                .Single()
                .Breadcrumbs
                .Single()
                .Message == expected));
    }

    [Fact]
    public void CaptureEvent_WithConfiguredScope_ScopeOnlyAppliesOnlyOnce()
    {
        using var _ = SentrySdk.Init(o =>
        {
            o.Dsn = ValidDsn;
            o.AutoSessionTracking = false;
            o.BackgroundWorker = Substitute.For<IBackgroundWorker>();
            o.InitNativeSdks = false;
        });

        var callbackCounter = 0;
        SentrySdk.CaptureEvent(new SentryEvent(), _ => callbackCounter++);
        SentrySdk.CaptureEvent(new SentryEvent());

        Assert.Equal(1, callbackCounter);
    }

    [Fact]
    public void CaptureEvent_WithConfiguredScopeNull_LogsError()
    {
        var logger = new InMemoryDiagnosticLogger();

        var options = new SentryOptions
        {
            Dsn = ValidDsn,
            DiagnosticLogger = logger,
            Debug = true,
            AutoSessionTracking = false,
            BackgroundWorker = Substitute.For<IBackgroundWorker>(),
            InitNativeSdks = false
        };

        using var _ = SentrySdk.Init(options);
        SentrySdk.CaptureEvent(new SentryEvent(), (null as Action<Scope>)!);

        logger.Entries.Any(e =>
                e.Level == SentryLevel.Error &&
                e.Message == "Failure to capture event: {0}")
            .Should()
            .BeTrue();
    }

    [Fact]
    public void CaptureEvent_WithConfiguredScope_ScopeCallbackGetsInvoked()
    {
        using var _ = SentrySdk.Init(o =>
        {
            o.Dsn = ValidDsn;
            o.AutoSessionTracking = false;
            o.BackgroundWorker = Substitute.For<IBackgroundWorker>();
            o.InitNativeSdks = false;
        });

        var scopeCallbackWasInvoked = false;
        SentrySdk.CaptureEvent(new SentryEvent(), _ => scopeCallbackWasInvoked = true);

        Assert.True(scopeCallbackWasInvoked);
    }

    [Fact]
    public void CaptureFeedback_WithConfiguredScope_ScopeCallbackGetsInvoked()
    {
        using var _ = SentrySdk.Init(o =>
        {
            o.Dsn = ValidDsn;
            o.AutoSessionTracking = false;
            o.BackgroundWorker = Substitute.For<IBackgroundWorker>();
            o.InitNativeSdks = false;
        });

        var scopeCallbackWasInvoked = false;
        SentrySdk.CaptureFeedback(new SentryFeedback("Foo"), _ => scopeCallbackWasInvoked = true);

        Assert.True(scopeCallbackWasInvoked);
    }

    [Fact]
    public void CaptureException_WithConfiguredScope_ScopeCallbackGetsInvoked()
    {
        using var _ = SentrySdk.Init(o =>
        {
            o.Dsn = ValidDsn;
            o.AutoSessionTracking = false;
            o.BackgroundWorker = Substitute.For<IBackgroundWorker>();
            o.InitNativeSdks = false;
        });

        var scopeCallbackWasInvoked = false;
        SentrySdk.CaptureException(new Exception(), _ => scopeCallbackWasInvoked = true);

        Assert.True(scopeCallbackWasInvoked);
    }

    [Fact]
    public void CaptureMessage_WithConfiguredScope_ScopeCallbackGetsInvoked()
    {
        using var _ = SentrySdk.Init(o =>
        {
            o.Dsn = ValidDsn;
            o.AutoSessionTracking = false;
            o.BackgroundWorker = Substitute.For<IBackgroundWorker>();
            o.InitNativeSdks = false;
        });

        var scopeCallbackWasInvoked = false;
        SentrySdk.CaptureMessage("TestMessage", _ => scopeCallbackWasInvoked = true);

        Assert.True(scopeCallbackWasInvoked);
    }

    [Fact]
    public async Task ConfigureScope_Async_CallbackNeverInvoked()
    {
        var invoked = false;
        await SentrySdk.ConfigureScopeAsync(_ =>
        {
            invoked = true;
            return Task.CompletedTask;
        });
        Assert.False(invoked);
    }

    [Fact]
    public async Task ConfigureScope_AsyncWithArg_CallbackNeverInvoked()
    {
        var invoked = false;
        await SentrySdk.ConfigureScopeAsync((_, _) =>
        {
            invoked = true;
            return Task.CompletedTask;
        }, "arg");
        Assert.False(invoked);
    }

    [Fact]
    public async Task ConfigureScope_AsyncWithArg_ArgIsUsed()
    {
        using var _ = SentrySdk.Init(o =>
        {
            o.Dsn = ValidDsn;
            o.AutoSessionTracking = false;
            o.BackgroundWorker = Substitute.For<IBackgroundWorker>();
            o.InitNativeSdks = false;
        });

        const string key = "key";
        const string arg = "arg";

        await SentrySdk.ConfigureScopeAsync((s, a) =>
        {
            s.SetTag(key, a);
            return Task.CompletedTask;
        }, arg);

        string actual = null;
        await SentrySdk.ConfigureScopeAsync(s =>
        {
            actual = s.Tags[key];
            return Task.CompletedTask;
        });

        Assert.Equal(arg, actual);
    }

    [Fact]
    public void CaptureEvent_Instance_NoOp() => SentrySdk.CaptureEvent(new SentryEvent());

    [Fact]
    public void CaptureException_Instance_NoOp() => SentrySdk.CaptureException(new Exception());

    [Fact]
    public void CaptureMessage_Message_NoOp() => SentrySdk.CaptureMessage("message");

    [Fact]
    public void CaptureMessage_MessageLevel_NoOp() => SentrySdk.CaptureMessage("message", SentryLevel.Debug);

    [Fact]
    public void CaptureMessage_SdkInitialized_IncludesScope()
    {
        var worker = Substitute.For<IBackgroundWorker>();
        using var _ = SentrySdk.Init(o =>
        {
            o.Dsn = ValidDsn;
            o.BackgroundWorker = worker;
            o.AutoSessionTracking = false;
            o.InitNativeSdks = false;
        });

        const string expected = "test";
        SentrySdk.AddBreadcrumb(expected);
        SentrySdk.CaptureMessage("message");

        worker.Received(1).EnqueueEnvelope(
            Arg.Is<Envelope>(e => e.Items
                .Select(i => i.Payload)
                .OfType<JsonSerializable>()
                .Select(i => i.Source)
                .OfType<SentryEvent>()
                .Single()
                .Breadcrumbs
                .Single()
                .Message == expected));
    }

    [Fact]
    public void Implements_Client()
    {
        var clientMembers = typeof(ISentryClient)
            .GetMembers(BindingFlags.Public | BindingFlags.Instance)
            .Select(m => m.ToString())
            .ToArray();

        var sentrySdkMembers = typeof(SentrySdk)
            .GetMembers(BindingFlags.Public | BindingFlags.Static)
            .Select(m => m.ToString())
            .ToArray();

        sentrySdkMembers.Should().Contain(clientMembers);
    }

    [Fact]
    public void Implements_ClientExtensions()
    {
        string[] excludedMembers = [nameof(SentryClientExtensions.GetSentryOptions), nameof(SentryClientExtensions.GetInternalSentryOptions)];
        var clientExtensions = typeof(SentryClientExtensions)
            .GetMembers(BindingFlags.Public | BindingFlags.Static)
            .Where(x => !excludedMembers.Contains(x.Name))
            // Remove the extension argument: Method(this ISentryClient client, ...
            .Select(m => m.ToString()!
                .Replace($"({typeof(ISentryClient).FullName}", "(")
                .Replace("(, ", "("));

        var sentrySdk = typeof(SentrySdk).GetMembers(BindingFlags.Public | BindingFlags.Static);
        var values = clientExtensions.Except(sentrySdk.Select(m => m.ToString()));
        Assert.Empty(values);
    }

    [Fact]
    public void Implements_ScopeManagement()
    {
        var scopeManagement = typeof(ISentryScopeManager).GetMembers(BindingFlags.Public | BindingFlags.Instance);
        var sentrySdk = typeof(SentrySdk).GetMembers(BindingFlags.Public | BindingFlags.Static);

        Assert.Empty(scopeManagement.Select(m => m.ToString()).Except(sentrySdk.Select(m => m.ToString())));
    }

    // Issue: https://github.com/getsentry/sentry-dotnet/issues/123
    [Fact]
    public void InitHub_NoDsn_DisposeDoesNotThrow()
    {
        var sut = SentrySdk.InitHub(new SentryOptions() { Dsn = SentryConstants.DisableSdkDsnValue }) as IDisposable;
        sut?.Dispose();
    }

    [Fact]
    public async Task InitHub_NoDsn_FlushAsyncDoesNotThrow()
    {
        var sut = SentrySdk.InitHub(new SentryOptions() { Dsn = SentryConstants.DisableSdkDsnValue });
        await sut.FlushAsync();
    }

    [Fact]
    public void InitHub_GlobalModeOff_AsyncLocalContainer()
    {
        // Arrange
        var options = new SentryOptions
        {
            Dsn = ValidDsn,
            IsGlobalModeEnabled = false,
            AutoSessionTracking = false,
            BackgroundWorker = Substitute.For<IBackgroundWorker>(),
            InitNativeSdks = false
        };

        // Act
        var sut = SentrySdk.InitHub(options);

        // Assert
        var hub = (Hub)sut;
        hub.ScopeManager.ScopeStackContainer.Should().BeOfType<AsyncLocalScopeStackContainer>();
    }

    [Fact]
    public void InitHub_GlobalModeOn_GlobalContainer()
    {
        // Arrange
        var options = new SentryOptions
        {
            Dsn = ValidDsn,
            IsGlobalModeEnabled = true,
            AutoSessionTracking = false,
            BackgroundWorker = Substitute.For<IBackgroundWorker>(),
            InitNativeSdks = false
        };

        // Act
        var sut = SentrySdk.InitHub(options);

        // Assert
        var hub = (Hub)sut;
        hub.ScopeManager.ScopeStackContainer.Should().BeOfType<GlobalScopeStackContainer>();
    }

    [Fact]
    public void InitHub_DebugInProductionEnvironment_LogsWarning()
    {
        var options = new SentryOptions
        {
            Dsn = ValidDsn,
            DiagnosticLogger = _logger,
            IsGlobalModeEnabled = true,
            AutoSessionTracking = false,
            BackgroundWorker = Substitute.For<IBackgroundWorker>(),
            InitNativeSdks = false,
            Debug = true,
            Environment = "production"
        };

        SentrySdk.InitHub(options);

        _logger.Received().Log(
            SentryLevel.Warning,
            "Sentry option 'Debug' is set to true while Environment is production. " +
            "Be aware this can cause performance degradation and is not advised. " +
            "See https://docs.sentry.io/platforms/dotnet/configuration/diagnostic-logger " +
            "for more information",
            Arg.Any<Exception>(),
            Arg.Any<object[]>());
    }

#if !__MOBILE__ // On mobile, we'll always have logs from the Android/iOS SDK, so we can't reliably test for silence.
    [Fact]
    public void InitHub_GlobalModeOn_NoWarningOrErrorLogged()
    {
        var options = new SentryOptions
        {
            Dsn = ValidDsn,
            DiagnosticLogger = _logger,
            IsGlobalModeEnabled = true,
            AutoSessionTracking = false,
            BackgroundWorker = Substitute.For<IBackgroundWorker>(),
            InitNativeSdks = false
        };

        SentrySdk.InitHub(options);

        _logger.DidNotReceive().Log(
            SentryLevel.Warning,
            Arg.Any<string>(),
            Arg.Any<Exception>(),
            Arg.Any<object[]>());

        _logger.DidNotReceive().Log(
            SentryLevel.Error,
            Arg.Any<string>(),
            Arg.Any<Exception>(),
            Arg.Any<object[]>());
    }

    [Fact]
    public void InitHub_GlobalModeOff_NoWarningOrErrorLogged()
    {
        var options = new SentryOptions
        {
            Dsn = ValidDsn,
            DiagnosticLogger = _logger,
            IsGlobalModeEnabled = false,
            AutoSessionTracking = false,
            BackgroundWorker = Substitute.For<IBackgroundWorker>(),
            InitNativeSdks = false
        };

        SentrySdk.InitHub(options);

        _logger.DidNotReceive().Log(
            SentryLevel.Warning,
            Arg.Any<string>(),
            Arg.Any<Exception>(),
            Arg.Any<object[]>());

        _logger.DidNotReceive().Log(
            SentryLevel.Error,
            Arg.Any<string>(),
            Arg.Any<Exception>(),
            Arg.Any<object[]>());
    }
#endif

    [Fact]
    public void InitHub_DebugEnabled_DebugLogsLogged()
    {
        var options = new SentryOptions
        {
            Dsn = ValidDsn,
            DiagnosticLogger = _logger,
            IsGlobalModeEnabled = true,
            Debug = true,
            AutoSessionTracking = false,
            BackgroundWorker = Substitute.For<IBackgroundWorker>(),
            InitNativeSdks = false
        };

        SentrySdk.InitHub(options);

        _logger.Received().Log(
            SentryLevel.Debug,
            Arg.Any<string>(),
            Arg.Any<Exception>(),
            Arg.Any<object[]>());
    }

#if __IOS__
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ProcessOnBeforeSend_NativeErrorSuppression(bool suppressNativeErrors)
    {
        // Arrange
        var options = new SentryOptions
        {
            Dsn = ValidDsn,
            DiagnosticLogger = _logger,
            IsGlobalModeEnabled = true,
            Debug = true,
            AutoSessionTracking = false,
            BackgroundWorker = Substitute.For<IBackgroundWorker>(),
            InitNativeSdks = false,
        };
        options.Native.SuppressExcBadAccess = suppressNativeErrors;

        var called = false;
        options.SetBeforeSend(e =>
        {
            called = true;
            return e;
        });

        var scope = new Scope(options);
        var hub = Substitute.For<IHub>();
        hub.When(h => hub.ConfigureScope(Arg.Any<Action<Scope>>()))
            .Do(callback => callback.Arg<Action<Scope>>().Invoke(scope));

        var evt = new Sentry.CocoaSdk.SentryEvent();
        var ex = new Sentry.CocoaSdk.SentryException("Not checked", "EXC_BAD_ACCESS");
        evt.Exceptions = [ex];

        // Act
        var result = SentrySdk.ProcessOnBeforeSend(options, evt, hub);

        // Assert
        if (suppressNativeErrors)
        {
            called.Should().BeFalse();
            result.Should().BeNull();
        }
        else
        {
            called.Should().BeTrue();
            result.Exceptions.First().Type.Should().Be("EXC_BAD_ACCESS");
        }
    }

    [Fact]
    public void ProcessOnBeforeSend_OptionsBeforeOnSendRuns()
    {
        // Arrange
        var options = new SentryOptions
        {
            Dsn = ValidDsn,
            DiagnosticLogger = _logger,
            IsGlobalModeEnabled = true,
            Debug = true,
            AutoSessionTracking = false,
            BackgroundWorker = Substitute.For<IBackgroundWorker>(),
            InitNativeSdks = false
        };

        var native = new Sentry.CocoaSdk.SentryEvent();
        native.ServerName = "server name";
        native.Dist = "dist";
        native.Logger = "logger";
        native.ReleaseName = "release name";
        native.Environment = "environment";
        native.Transaction = "transaction name";
        options.SetBeforeSend(e =>
        {
            e.TransactionName = "dotnet";
            return e;
        });

        var scope = new Scope(options);
        var hub = Substitute.For<IHub>();
        hub.When(h => hub.ConfigureScope(Arg.Any<Action<Scope>>()))
            .Do(callback => callback.Arg<Action<Scope>>().Invoke(scope));

        // Act
        var result = SentrySdk.ProcessOnBeforeSend(options, native, hub);

        // Assert
        result.Should().NotBeNull();
        result.Transaction.Should().Be("dotnet");
    }

    [Fact]
    public void ProcessOnBeforeSend_EventProcessorsInvoked()
    {
        // Arrange
        var options = new SentryOptions
        {
            Dsn = ValidDsn,
            DiagnosticLogger = _logger,
            IsGlobalModeEnabled = true,
            Debug = true,
            AutoSessionTracking = false,
            BackgroundWorker = Substitute.For<IBackgroundWorker>(),
            InitNativeSdks = false,
        };
        var eventProcessor = new TestEventProcessor();
        options.AddEventProcessor(eventProcessor);

        var scope = new Scope(options);
        var hub = Substitute.For<IHub>();
        hub.When(h => hub.ConfigureScope(Arg.Any<Action<Scope>>()))
            .Do(callback => callback.Arg<Action<Scope>>().Invoke(scope));

        var native = new Sentry.CocoaSdk.SentryEvent();

        // Act
        SentrySdk.ProcessOnBeforeSend(options, native, hub);

        // Assert
        eventProcessor.Invoked.Should().BeTrue();
    }
#endif

    public void Dispose()
    {
        SentrySdk.Close();
    }
}

file class TestEventProcessor : ISentryEventProcessor
{
    public bool Invoked { get; private set; }
    public SentryEvent Process(SentryEvent @event)
    {
        Invoked = true;
        return @event;
    }
}
