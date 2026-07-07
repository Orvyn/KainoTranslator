using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;

namespace ScreenTranslator.Translation;

/// <summary>
/// Holds the user-configured proxy list and hands out an HttpClient per translator that
/// rotates through the list (round-robin) so heavy usage of the free/unofficial endpoints
/// (mainly Google's) is less likely to get IP-blocked. Proxies are optional; with an empty
/// list, a normal direct HttpClient is returned.
/// Proxy string formats accepted: "host:port", "http://host:port", "http://user:pass@host:port".
/// </summary>
public sealed class ProxyManager
{
    private readonly List<string> _proxies;
    private readonly bool _rotate;
    private int _index = -1;
    private readonly ConcurrentDictionary<string, HttpClient> _clientCache = new();

    public ProxyManager(IEnumerable<string> proxies, bool rotate)
    {
        _proxies = new List<string>(proxies);
        _rotate = rotate;
    }

    public bool HasProxies => _proxies.Count > 0;

    /// <summary>Get an HttpClient to use for the next outgoing request.</summary>
    public HttpClient GetClient()
    {
        if (_proxies.Count == 0)
            return _clientCache.GetOrAdd("__direct__", _ => CreateClient(null));

        var proxyString = _rotate ? NextProxy() : _proxies[0];
        return _clientCache.GetOrAdd(proxyString, CreateClient);
    }

    /// <summary>Call this when a request fails, so the caller can retry with a different proxy.</summary>
    public HttpClient GetNextClientAfterFailure(HttpClient failedClient)
    {
        if (_proxies.Count <= 1) return failedClient;
        return GetClient();
    }

    private string NextProxy()
    {
        var i = Interlocked.Increment(ref _index);
        return _proxies[((i % _proxies.Count) + _proxies.Count) % _proxies.Count];
    }

    private static HttpClient CreateClient(string? proxyString)
    {
        var handler = new HttpClientHandler();
        if (!string.IsNullOrWhiteSpace(proxyString))
        {
            var uriString = proxyString.Contains("://") ? proxyString : "http://" + proxyString;
            var uri = new Uri(uriString);
            var webProxy = new WebProxy(new Uri($"{uri.Scheme}://{uri.Host}:{uri.Port}"));
            if (!string.IsNullOrEmpty(uri.UserInfo))
            {
                var parts = uri.UserInfo.Split(':', 2);
                webProxy.Credentials = new NetworkCredential(parts[0], parts.Length > 1 ? parts[1] : "");
            }
            handler.Proxy = webProxy;
            handler.UseProxy = true;
        }
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
        return client;
    }
}
