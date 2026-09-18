using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BestiaryNav;

internal static class UsageReportingChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        var options = new UsageReportingOptions();
        check(!options.Enabled && !options.EnsureIdentity() && options.InstallationId == "", "no consent means no identifier creation");
        long time = 100;
        var handler = new RecordingHandler();
        using var reporter = new UsageReporter("0.6.15.0", new HttpClient(handler), () => time);
        reporter.Configure(options); reporter.Tick(); await reporter.FlushForChecks();
        check(handler.Payloads.Count == 0, "reporting sends nothing by default");
        options.Enabled = true;
        check(options.EnsureIdentity() && options.InstallationId[14] == '4', "opt-in creates random UUID v4");
        var id = options.InstallationId;
        check(!options.EnsureIdentity() && options.InstallationId == id, "stable identity survives repeated saves");
        reporter.Configure(options); reporter.Tick(); await reporter.FlushForChecks();
        check(handler.Payloads.Count == 1, "opt-in sends one check-in");
        using (var payload = JsonDocument.Parse(handler.Payloads[0]))
        {
            check(payload.RootElement.GetProperty("installationId").GetString() == id, "only random identity used");
            check(payload.RootElement.GetProperty("pluginVersion").GetString() == "0.6.15.0", "version is reported");
            var count = 0; foreach (var _ in payload.RootElement.EnumerateObject()) count++;
            check(count == 2, "payload excludes game and personal data");
        }
        reporter.Tick(); await reporter.FlushForChecks();
        check(handler.Payloads.Count == 1, "frames do not flood the endpoint");
        time += UsageReporter.IntervalMs + 15001;
        handler.Fail = true; reporter.Tick(); await reporter.FlushForChecks();
        check(reporter.Status.Contains("unavailable"), "network failure is contained");
        reporter.Tick(); await reporter.FlushForChecks();
        check(handler.Payloads.Count == 2, "failed requests also wait before retrying");
        options.Enabled = false; reporter.Configure(options); time += UsageReporter.IntervalMs * 2; reporter.Tick(); await reporter.FlushForChecks();
        check(handler.Payloads.Count == 2 && reporter.Status.Contains("off"), "opt-out stops requests");
        options.ResetIdentity(); check(options.InstallationId == "", "reset while off forgets the local ID");
        options.Enabled = true; options.EnsureIdentity(); check(options.InstallationId != id, "reset is not linked to previous identity");
        handler.Fail = false; reporter.Configure(options); reporter.Tick(); await reporter.FlushForChecks();
        check(handler.Payloads.Count == 3, "reporting can resume with fresh identity");
        reporter.Dispose(); time += UsageReporter.IntervalMs * 2; reporter.Tick();
        check(handler.Payloads.Count == 3, "unload stops all future reporting");
        check(handler.AllHttps, "all reports use HTTPS");
    }
    private sealed class RecordingHandler : HttpMessageHandler
    {
        public readonly List<string> Payloads = new();
        public bool Fail;
        public bool AllHttps = true;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            AllHttps &= request.RequestUri?.Scheme == "https" && request.Method == HttpMethod.Post;
            Payloads.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            if (Fail) throw new HttpRequestException("Test outage");
            return new(HttpStatusCode.NoContent);
        }
    }
}
