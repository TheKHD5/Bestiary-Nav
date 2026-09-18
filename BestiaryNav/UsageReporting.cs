using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BestiaryNav;

[Serializable]
public sealed class UsageReportingOptions
{
    // Existing installations must never be opted in by migration or upgrade.
    public bool Enabled { get; set; }
    public string InstallationId { get; set; } = "";

    public bool EnsureIdentity()
    {
        if (!Enabled) return false;
        if (Guid.TryParseExact(InstallationId, "D", out var id) && id != Guid.Empty && InstallationId[14] == '4' && id.ToString("D") == InstallationId) return false;
        ResetIdentity();
        return true;
    }

    public void ResetIdentity() => InstallationId = Enabled ? Guid.NewGuid().ToString("D") : "";
}

// No Dalamud/game references: only a user-approved random ID and the plugin version
// can enter this transport. All I/O runs asynchronously, outside the game update.
internal sealed class UsageReporter : IDisposable
{
    public const string PrivacyUrl = "https://bestiary-nav-reporting.khalidalsuwaidi68.chatgpt.site";
    private static readonly Uri Endpoint = new(PrivacyUrl + "/api/check-in");
    internal const long IntervalMs = 300_000;
    private readonly HttpClient http;
    private readonly string version;
    private readonly Func<long> now;
    private readonly object gate = new();
    private CancellationTokenSource lifetime = new();
    private Task? pending;
    private long nextAttempt;
    private bool enabled;
    private string identity = "";
    private bool disposed;
    private volatile string status = "Usage reporting is off.";
    public string Status => status;

    public UsageReporter(string version, HttpClient? client = null, Func<long>? clock = null)
    {
        this.version = version;
        now = clock ?? (() => Environment.TickCount64);
        http = client ?? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { Timeout = TimeSpan.FromSeconds(10) };
    }

    public void Configure(UsageReportingOptions options)
    {
        lock (gate)
        {
            if (disposed || enabled == options.Enabled && identity == options.InstallationId) return;
            lifetime.Cancel(); lifetime.Dispose(); lifetime = new();
            enabled = options.Enabled;
            identity = options.InstallationId;
            nextAttempt = 0;
            status = enabled ? "Ready to send optional usage reports." : "Usage reporting is off.";
        }
    }

    public void Tick()
    {
        lock (gate)
        {
            if (disposed || !enabled || !Guid.TryParseExact(identity, "D", out _) || now() < nextAttempt || pending is { IsCompleted: false }) return;
            // Reserve the interval before I/O. A failure never causes a tight retry loop.
            nextAttempt = now() + IntervalMs + Random.Shared.Next(0, 15_001);
            var token = lifetime.Token;
            var payload = JsonSerializer.Serialize(new { installationId = identity, pluginVersion = version });
            pending = Task.Run(() => Send(payload, token), CancellationToken.None);
        }
    }

    private async Task Send(string payload, CancellationToken token)
    {
        try
        {
            token.ThrowIfCancellationRequested();
            using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint);
            request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            lock (gate)
            {
                if (disposed || token.IsCancellationRequested) return;
                status = response.IsSuccessStatusCode ? "Usage report sent. Next check-in in about five minutes." : "Reporting unavailable; retrying later. Plugin features are unaffected.";
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or ObjectDisposedException)
        {
            lock (gate)
                if (!disposed && !token.IsCancellationRequested)
                    status = "Reporting unavailable; retrying later. Plugin features are unaffected.";
        }
    }

    internal Task FlushForChecks() { lock (gate) return pending ?? Task.CompletedTask; }
    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true; enabled = false; lifetime.Cancel(); lifetime.Dispose(); http.Dispose();
        }
    }
}
