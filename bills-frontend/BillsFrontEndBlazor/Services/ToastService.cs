namespace BillsFrontEndBlazor.Services;

public enum ToastSeverity
{
    Success,
    Error,
    Warning,
    Info,
}

public sealed class Toast
{
    public required string Message { get; init; }

    public ToastSeverity Severity { get; init; }

    /// <summary>Identity for the @key on the rendered element — two toasts can
    /// carry the same text, and reusing the message as a key makes Blazor
    /// collapse them into one.</summary>
    public Guid Id { get; } = Guid.NewGuid();
}

/// <summary>
/// Drives Bootstrap's own toast markup from component state, with no
/// bootstrap.bundle.js involved. Scoped, like <see cref="BillEventService"/> —
/// a singleton would show one circuit's toasts to every other connected
/// browser.
/// </summary>
public sealed class ToastService : IDisposable
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(4);

    private readonly List<Toast> _toasts = [];
    private readonly CancellationTokenSource _cts = new();

    public event Action? OnToastsChanged;

    public IReadOnlyList<Toast> Toasts
    {
        get
        {
            lock (_toasts)
            {
                return _toasts.ToArray();
            }
        }
    }

    public void ShowSuccess(string message) => Show(message, ToastSeverity.Success);

    public void ShowError(string message) => Show(message, ToastSeverity.Error);

    public void ShowInfo(string message) => Show(message, ToastSeverity.Info);

    public void Show(string message, ToastSeverity severity)
    {
        var toast = new Toast { Message = message, Severity = severity };

        lock (_toasts)
        {
            _toasts.Add(toast);
        }

        OnToastsChanged?.Invoke();

        // Fire-and-forget on purpose: the caller is a UI event handler and must
        // not block for four seconds. Exceptions are swallowed by the catch
        // below rather than left to surface on a pooled thread with no handler.
        _ = DismissAfterDelayAsync(toast);
    }

    public void Remove(Toast toast)
    {
        bool removed;
        lock (_toasts)
        {
            removed = _toasts.Remove(toast);
        }

        if (removed)
        {
            OnToastsChanged?.Invoke();
        }
    }

    private async Task DismissAfterDelayAsync(Toast toast)
    {
        try
        {
            await Task.Delay(Lifetime, _cts.Token);
            Remove(toast);
        }
        catch (OperationCanceledException)
        {
            // The circuit ended before the toast expired. Nothing to clean up.
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
