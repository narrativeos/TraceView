// Copyright (c) 2025 BobLd
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Caly.Core.Utilities;

/// <summary>
/// Simple token-bucket rate limiter for external API calls.
///
/// Used by GeocodingService (Nominatim) and OrganizationDisambiguationService (Wikidata)
/// to avoid exceeding API rate limits. Thread-safe and supports async waiting.
///
/// Usage:
/// <code>
/// var limiter = new RateLimiter(maxRequestsPerSecond: 1);
/// await limiter.WaitAsync(ct);
/// // ... make API call ...
/// </code>
/// </summary>
public class RateLimiter
{
    private readonly double _minIntervalTicks;
    private long _lastRequestTicks;

    /// <summary>
    /// Create a rate limiter with the specified maximum requests per second.
    /// </summary>
    /// <param name="maxRequestsPerSecond">
    /// Maximum number of requests allowed per second.
    /// For Nominatim: 1 (one request per second is the default usage policy).
    /// For Wikidata REST API: typically 5–10 requests per second.
    /// </param>
    public RateLimiter(double maxRequestsPerSecond = 1.0)
    {
        if (maxRequestsPerSecond <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxRequestsPerSecond), "Must be greater than 0.");

        _minIntervalTicks = TimeSpan.TicksPerSecond / maxRequestsPerSecond;
        _lastRequestTicks = 0;
    }

    /// <summary>
    /// Wait until the rate limiter allows the next request.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    public async Task WaitAsync(CancellationToken ct = default)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var now = DateTime.UtcNow.Ticks;
            var last = Interlocked.Read(ref _lastRequestTicks);
            var elapsed = now - last;

            if (elapsed >= _minIntervalTicks)
            {
                // Try to claim this time slot
                var newLast = Interlocked.CompareExchange(ref _lastRequestTicks, now, last);
                if (newLast == last)
                {
                    // Successfully claimed
                    return;
                }
                // Another thread claimed it — yield to avoid busy waiting
                await Task.Yield();
                continue;
            }

            // Wait for the remaining interval
            var delayTicks = _minIntervalTicks - elapsed;
            var delayMs = (int)(delayTicks / TimeSpan.TicksPerMillisecond) + 1;
            if (delayMs > 0)
            {
                await Task.Delay(delayMs, ct);
            }
        }
    }
}