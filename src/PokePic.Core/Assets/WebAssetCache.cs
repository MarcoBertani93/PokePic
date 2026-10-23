using System.Collections.Concurrent;
using System.Net;

namespace PokePic.Core.Assets;

/// <summary>
/// Downloads http(s) assets and keeps them in memory, so a file is fetched once per session (the editor renders
/// the same template many times). "Not found" (404) and failures are kept too, so a broken link does not slow
/// down every render: <see cref="ForgetFailures"/> retries them.
/// </summary>
public sealed class WebAssetCache(HttpClient? client = null)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    private readonly HttpClient _client = client ?? CreateClient();
    private readonly ConcurrentDictionary<string, Lazy<Entry>> _entries = new(StringComparer.Ordinal);

    public static bool IsWebUrl(string location) =>
        location.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || location.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    /// <summary>The file, or null if the server says it does not exist (404/410).</summary>
    /// <exception cref="WebAssetException">The download failed (network, timeout, other HTTP status).</exception>
    public byte[]? TryGet(string url)
    {
        var entry = _entries.GetOrAdd(url, u => new Lazy<Entry>(() => Download(u))).Value;
        return entry.Error is { } error ? throw new WebAssetException(error) : entry.Bytes;
    }

    /// <summary>Forgets failed and missing downloads, so they are tried again.</summary>
    public void ForgetFailures()
    {
        foreach (var (url, entry) in _entries)
        {
            if (entry.IsValueCreated && entry.Value.Bytes is null)
                _entries.TryRemove(url, out _);
        }
    }

    private Entry Download(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return new Entry(null, $"Invalid address \"{url}\".");
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            using var response = _client.Send(request);
            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
                return new Entry(null, null);
            if (!response.IsSuccessStatusCode)
                return new Entry(null, $"Download of {url} failed: HTTP {(int)response.StatusCode} {response.ReasonPhrase}.");
            using var stream = response.Content.ReadAsStream();
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            return new Entry(buffer.ToArray(), null);
        }
        catch (TaskCanceledException)
        {
            return new Entry(null, $"Download of {url} failed: no answer within {Timeout.TotalSeconds:0} seconds.");
        }
        catch (HttpRequestException ex)
        {
            return new Entry(null, $"Download of {url} failed: {ex.Message}");
        }
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = Timeout };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("PokePic/1.0");
        return client;
    }

    /// <param name="Bytes">The file; null when missing or failed.</param>
    /// <param name="Error">Why the download failed; null when it worked or the file does not exist.</param>
    private sealed record Entry(byte[]? Bytes, string? Error);
}

/// <summary>A web asset could not be downloaded. It is an <see cref="IOException"/>, like a file that cannot be read.</summary>
public sealed class WebAssetException(string message) : IOException(message);
