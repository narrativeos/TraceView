using System.Net;
using System.Text;

namespace Caly.Tests;

/// <summary>
/// Binds a throwaway loopback port for the service tests that stub an HTTP server.
/// </summary>
internal static class LoopbackListener
{
    /// <summary>
    /// Starts an <see cref="HttpListener"/> on a random loopback port and returns its base
    /// url without a trailing slash. Retries because the port may already be taken.
    /// </summary>
    internal static HttpListener Start(out string baseUrl)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var port = 17800 + Random.Shared.Next(2000);
            var prefix = $"http://127.0.0.1:{port}/";
            var listener = new HttpListener();
            listener.Prefixes.Add(prefix);
            try
            {
                listener.Start();
                baseUrl = prefix.TrimEnd('/');
                return listener;
            }
            catch (HttpListenerException)
            {
                listener.Close();
            }
        }

        throw new InvalidOperationException("Could not bind a loopback port for the service tests.");
    }

    /// <summary>
    /// Writes a small JSON body with the given status and closes the response.
    /// </summary>
    internal static async Task RespondAsync(HttpListenerContext context, HttpStatusCode status, string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        context.Response.StatusCode = (int)status;
        context.Response.ContentType = "application/json";
        await context.Response.OutputStream.WriteAsync(bytes);
        context.Response.Close();
    }
}
