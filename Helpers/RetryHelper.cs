using System;
using System.Threading;
using System.Threading.Tasks;

namespace GPM_driver.Helpers;

public static class RetryHelper
{
    public static Task<T> ExecuteWithRetryAsync<T>(Func<Task<T>> operation, int maxAttempts = 3, TimeSpan? initialDelay = null, double backoffFactor = 2.0, TimeSpan? maxDelay = null, string? operationName = null, CancellationToken cancellationToken = default)
        => ExecuteWithRetryInternalAsync(operation, maxAttempts, initialDelay, backoffFactor, maxDelay, operationName, cancellationToken);

    public static Task ExecuteWithRetryAsync(Func<Task> operation, int maxAttempts = 3, TimeSpan? initialDelay = null, double backoffFactor = 2.0, TimeSpan? maxDelay = null, string? operationName = null, CancellationToken cancellationToken = default)
        => ExecuteWithRetryInternalAsync<object?>(async () =>
        {
            await operation().ConfigureAwait(false);
            return null;
        }, maxAttempts, initialDelay, backoffFactor, maxDelay, operationName, cancellationToken);

    private static async Task<T> ExecuteWithRetryInternalAsync<T>(Func<Task<T>> operation, int maxAttempts, TimeSpan? initialDelay, double backoffFactor, TimeSpan? maxDelay, string? operationName, CancellationToken cancellationToken)
    {
        if (operation == null) throw new ArgumentNullException(nameof(operation));
        if (maxAttempts <= 0) throw new ArgumentOutOfRangeException(nameof(maxAttempts));
        if (backoffFactor < 1.0) throw new ArgumentOutOfRangeException(nameof(backoffFactor));

        TimeSpan delay = initialDelay ?? TimeSpan.FromMilliseconds(500);
        TimeSpan maximumDelay = maxDelay ?? TimeSpan.FromSeconds(10);
        string name = string.IsNullOrWhiteSpace(operationName) ? "Operation" : operationName!;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return await operation().ConfigureAwait(false);
            }
            catch (Exception ex) when (attempt < maxAttempts)
            {
                var jitter = TimeSpan.FromMilliseconds(RandomProvider.Next(100, 400));
                var wait = delay + jitter;
                Console.WriteLine($"[{name}] attempt {attempt} failed: {ex.Message}. Retrying in {wait.TotalSeconds:F1}s...");
                await Task.Delay(wait, cancellationToken).ConfigureAwait(false);

                double nextDelayMs = Math.Min(maximumDelay.TotalMilliseconds, delay.TotalMilliseconds * backoffFactor);
                delay = TimeSpan.FromMilliseconds(nextDelayMs);
            }
        }

        // Final attempt without catching to allow exception to bubble up
        return await operation().ConfigureAwait(false);
    }
}
