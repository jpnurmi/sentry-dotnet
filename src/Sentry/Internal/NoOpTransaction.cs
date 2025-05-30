namespace Sentry.Internal;

/// <summary>
/// Transaction class to use when we can't return null but a request to create a transaction couldn't be completed.
/// </summary>
internal class NoOpTransaction : NoOpSpan, ITransactionTracer
{
    public new static ITransactionTracer Instance { get; } = new NoOpTransaction();

    protected NoOpTransaction()
    {
    }

    public SdkVersion Sdk => SdkVersion.Instance;

    public virtual string Name
    {
        get => string.Empty;
        set { }
    }

    public bool? IsParentSampled
    {
        get => default;
        set { }
    }

    public TransactionNameSource NameSource => TransactionNameSource.Custom;

    public string? Distribution
    {
        get => string.Empty;
        set { }
    }

    public SentryLevel? Level
    {
        get => default;
        set { }
    }

    public SentryRequest Request
    {
        get => new();
        set { }
    }

    public SentryContexts Contexts
    {
        get => new();
        set { }
    }

    public SentryUser User
    {
        get => new();
        set { }
    }

    public string? Platform
    {
        get => default;
        set { }
    }

    public string? Release
    {
        get => default;
        set { }
    }

    public string? Environment
    {
        get => default;
        set { }
    }

    public string? TransactionName
    {
        get => default;
        set { }
    }

    public IReadOnlyList<string> Fingerprint
    {
        get => ImmutableList<string>.Empty;
        set { }
    }

    public virtual IReadOnlyCollection<ISpan> Spans => ImmutableList<ISpan>.Empty;

    public IReadOnlyCollection<Breadcrumb> Breadcrumbs => ImmutableList<Breadcrumb>.Empty;

    public ISpan? GetLastActiveSpan() => default;

    public void AddBreadcrumb(Breadcrumb breadcrumb) { }
}
