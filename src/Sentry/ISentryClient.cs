using Sentry.Protocol.Envelopes;

namespace Sentry;

/// <summary>
/// Sentry Client interface.
/// </summary>
public interface ISentryClient
{
    /// <summary>
    /// Whether the client is enabled or not.
    /// </summary>
    public bool IsEnabled { get; }

    /// <summary>
    /// Capture an envelope and queue it.
    /// </summary>
    /// <param name="envelope">The envelope.</param>
    /// <returns>true if the enveloped was queued, false otherwise.</returns>
    public bool CaptureEnvelope(Envelope envelope);

    /// <summary>
    /// Capture the event
    /// </summary>
    /// <param name="evt">The event to be captured.</param>
    /// <param name="scope">An optional scope to be applied to the event.</param>
    /// <param name="hint">An optional hint providing high level context for the source of the event</param>
    /// <returns>The Id of the event.</returns>
    public SentryId CaptureEvent(SentryEvent evt, Scope? scope = null, SentryHint? hint = null);

    /// <summary>
    /// Captures feedback from the user.
    /// </summary>
    /// <param name="feedback">The feedback to send to Sentry.</param>
    /// <param name="scope">An optional scope to be applied to the event.</param>
    /// <param name="hint">An optional hint providing high level context for the source of the event</param>
    public void CaptureFeedback(SentryFeedback feedback, Scope? scope = null, SentryHint? hint = null);

    /// <summary>
    /// Captures a user feedback.
    /// </summary>
    /// <param name="userFeedback">The user feedback to send to Sentry.</param>
    [Obsolete("Use CaptureFeedback instead.")]
    public void CaptureUserFeedback(UserFeedback userFeedback);

    /// <summary>
    /// Captures a transaction.
    /// </summary>
    /// <remarks>
    /// Note: this method is NOT meant to be called from user code!
    /// Instead, call <see cref="ISpan.Finish()"/> on the transaction.
    /// </remarks>
    /// <param name="transaction">The transaction.</param>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public void CaptureTransaction(SentryTransaction transaction);

    /// <summary>
    /// Captures a transaction.
    /// </summary>
    /// <remarks>
    /// Note: this method is NOT meant to be called from user code!
    /// Instead, call <see cref="ISpan.Finish()"/> on the transaction.
    /// </remarks>
    /// <param name="transaction">The transaction.</param>
    /// <param name="scope">The scope to be applied to the transaction</param>
    /// <param name="hint">
    /// A hint providing extra context.
    /// This will be available in callbacks prior to processing the transaction.
    /// </param>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public void CaptureTransaction(SentryTransaction transaction, Scope? scope, SentryHint? hint);

    /// <summary>
    /// Captures a session update.
    /// </summary>
    /// <remarks>
    /// Note: this method is NOT meant to be called from user code!
    /// It will be called automatically by the SDK.
    /// </remarks>
    /// <param name="sessionUpdate">The update to send to Sentry.</param>
    public void CaptureSession(SessionUpdate sessionUpdate);

    /// <summary>
    /// Captures a Checkin.
    /// </summary>
    /// <param name="monitorSlug"></param>
    /// <param name="status"></param>
    /// <param name="sentryId"></param>
    /// <param name="duration"></param>
    /// <param name="scope"></param>
    /// <param name="configureMonitorOptions">The optional monitor config used to create a Check-In programmatically.</param>
    public SentryId CaptureCheckIn(string monitorSlug,
        CheckInStatus status,
        SentryId? sentryId = null,
        TimeSpan? duration = null,
        Scope? scope = null,
        Action<SentryMonitorOptions>? configureMonitorOptions = null);

    /// <summary>
    /// Flushes the queue of captured events until the timeout is reached.
    /// </summary>
    /// <param name="timeout">The amount of time allowed for flushing.</param>
    /// <returns>A task to await for the flush operation.</returns>
    public Task FlushAsync(TimeSpan timeout);
}
