using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace HfsIndicator;

internal sealed record HfsStatus(string Version, string? LanUrl);

internal sealed class HfsApi(int port)
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(3) };

    public async Task<HfsStatus?> GetStatusAsync()
    {
        try
        {
            using var document = await PostAsync("get_status");
            if (document is null) return null;

            var root = document.RootElement;
            var version = root.TryGetProperty("version", out var versionElement)
                          && versionElement.ValueKind == JsonValueKind.String
                ? versionElement.GetString() ?? ""
                : "";

            string? lanUrl = null;
            if (root.TryGetProperty("urls", out var urls)
                && urls.TryGetProperty("http", out var http)
                && http.ValueKind == JsonValueKind.Array
                && http.GetArrayLength() > 0)
            {
                lanUrl = http[0].GetString();
            }

            return new HfsStatus(version, lanUrl);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public async Task<int?> GetConnectionCountAsync()
    {
        try
        {
            using var document = await PostAsync("get_connections");
            if (document is not null
                && document.RootElement.TryGetProperty("list", out var list)
                && list.ValueKind == JsonValueKind.Array)
            {
                return list.GetArrayLength();
            }
        }
        catch (Exception)
        {
            // 接口改名或字段缺失时只丢连接数，不影响状态灯。
        }

        return null;
    }

    private async Task<JsonDocument?> PostAsync(string command)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{port}/~/api/{command}")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };
        request.Headers.Add("x-hfs-anti-csrf", "1");

        using var response = await Http.SendAsync(request);
        if (!response.IsSuccessStatusCode) return null;

        var body = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(body);
    }
}
