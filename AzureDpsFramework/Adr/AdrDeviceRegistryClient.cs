using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;
using Azure.Identity;

namespace AzureDpsFramework.Adr
{
    public sealed class AdrDeviceRegistryClient : IDisposable
    {
        private static readonly string ArmScope = "https://management.azure.com/.default";
        private readonly TokenCredential _credential;
        private readonly HttpClient _http;
        private readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };
        private bool _disposeHttp;

        public AdrDeviceRegistryClient(TokenCredential? credential = null, HttpClient? httpClient = null)
        {
            _credential = credential ?? new DefaultAzureCredential();
            _http = httpClient ?? new HttpClient();
            _disposeHttp = httpClient == null;
        }

        public async Task<IReadOnlyList<DeviceResource>> ListDevicesAsync(
            string subscriptionId,
            string resourceGroupName,
            string namespaceName,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(subscriptionId)) throw new ArgumentException("subscriptionId required");
            if (string.IsNullOrWhiteSpace(resourceGroupName)) throw new ArgumentException("resourceGroupName required");
            if (string.IsNullOrWhiteSpace(namespaceName)) throw new ArgumentException("namespaceName required");

            var results = new List<DeviceResource>();
            string? url =
                $"https://management.azure.com/subscriptions/{subscriptionId}/resourceGroups/{resourceGroupName}" +
                $"/providers/Microsoft.DeviceRegistry/namespaces/{namespaceName}/devices?api-version=2025-11-01-preview";

            while (!string.IsNullOrEmpty(url))
            {
                var token = await _credential.GetTokenAsync(new TokenRequestContext(new[] { ArmScope }), ct);
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);

                using var resp = await _http.SendAsync(req, ct);
                if (!resp.IsSuccessStatusCode)
                {
                    var body = await resp.Content.ReadAsStringAsync(ct);
                    throw new HttpRequestException($"ADR list failed: {(int)resp.StatusCode} {resp.ReasonPhrase}. Body: {body}");
                }

                await using var stream = await resp.Content.ReadAsStreamAsync(ct);
                var page = await JsonSerializer.DeserializeAsync<ArmListResponse<DeviceResource>>(stream, _json, ct)
                           ?? new ArmListResponse<DeviceResource>();
                if (page.Value is { Count: > 0 }) results.AddRange(page.Value);
                url = page.NextLink;
            }

            return results;
        }

        public void Dispose()
        {
            if (_disposeHttp)
            {
                _http.Dispose();
            }
        }
    }

    public sealed class ArmListResponse<T>
    {
        public List<T> Value { get; set; } = new();
        public string? NextLink { get; set; }
    }

    public sealed class DeviceResource
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public string? Type { get; set; }
        public JsonElement Properties { get; set; }
    }
}
