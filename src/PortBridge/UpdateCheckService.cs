using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace PortBridge;

public enum UpdateCheckStatus
{
    UpToDate,
    UpdateAvailable,
    Failed
}

public sealed record StableRelease(Version Version, string TagName, Uri ReleasePage, Uri? SetupAsset);

public sealed record UpdateCheckResult(UpdateCheckStatus Status, string Message, StableRelease? Release = null)
{
    public Uri? UpdateUri => Status == UpdateCheckStatus.UpdateAvailable ? Release?.SetupAsset ?? Release?.ReleasePage : null;
}

public sealed class UpdateCheckService : IDisposable
{
    public const string ReleasesApi = "https://api.github.com/repos/Nortea575/PortBridge/releases?per_page=30";

    private readonly HttpClient client;
    private readonly bool ownsClient;
    private readonly Uri releasesApi;
    private readonly TimeSpan requestTimeout;

    public UpdateCheckService(HttpClient? client = null, Uri? releasesApi = null, TimeSpan? requestTimeout = null)
    {
        ownsClient = client == null;
        this.client = client ?? new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        this.releasesApi = releasesApi ?? new Uri(ReleasesApi);
        this.requestTimeout = requestTimeout ?? TimeSpan.FromSeconds(8);
        if (!this.client.DefaultRequestHeaders.UserAgent.Any())
            this.client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("PortBridge", "0.3.0"));
        if (!this.client.DefaultRequestHeaders.Accept.Any(x => x.MediaType == "application/vnd.github+json"))
            this.client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    public async Task<UpdateCheckResult> CheckAsync(Version currentVersion, CancellationToken cancellationToken = default)
    {
        using var timeout = new CancellationTokenSource(requestTimeout);
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            using var response = await client.GetAsync(releasesApi, HttpCompletionOption.ResponseHeadersRead, linkedCancellation.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return HttpFailure(response.StatusCode);

            await using var stream = await response.Content.ReadAsStreamAsync(linkedCancellation.Token).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: linkedCancellation.Token).ConfigureAwait(false);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                return Failure("GitHub 返回的更新信息无法识别，请稍后重试。");

            var latest = FindLatestStable(document.RootElement);
            if (latest == null)
                return Failure("暂未找到可用的稳定版发布，请稍后重试。");

            return latest.Version > Normalize(currentVersion)
                ? new UpdateCheckResult(UpdateCheckStatus.UpdateAvailable, $"发现新版本 {DisplayVersion(latest.Version)}。", latest)
                : new UpdateCheckResult(UpdateCheckStatus.UpToDate, "当前已是最新稳定版。", latest);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Failure("连接 GitHub 超时，请检查网络后重试。");
        }
        catch (HttpRequestException)
        {
            return Failure("无法连接 GitHub，请检查网络后重试。");
        }
        catch (JsonException)
        {
            return Failure("GitHub 返回的更新信息无法识别，请稍后重试。");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return Failure("更新检查失败，请稍后重试。");
        }
    }

    private static StableRelease? FindLatestStable(JsonElement releases)
    {
        StableRelease? latest = null;
        foreach (var release in releases.EnumerateArray())
        {
            if (release.ValueKind != JsonValueKind.Object || IsTrue(release, "draft") || IsTrue(release, "prerelease")) continue;
            if (!TryGetString(release, "tag_name", out var tag) || !TryParseVersion(tag, out var version)) continue;
            if (!TryGetUri(release, "html_url", out var releasePage)) continue;

            Uri? setupAsset = null;
            if (release.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            {
                var expectedSetupName = $"PortBridge-{DisplayVersion(version)}-Setup-x64.exe";
                foreach (var asset in assets.EnumerateArray())
                {
                    if (!TryGetString(asset, "name", out var name)
                        || !name.Equals(expectedSetupName, StringComparison.OrdinalIgnoreCase)) continue;
                    if (TryGetUri(asset, "browser_download_url", out var assetUri))
                    {
                        setupAsset = assetUri;
                        break;
                    }
                }
            }

            var candidate = new StableRelease(version, tag, releasePage, setupAsset);
            if (latest == null || candidate.Version > latest.Version) latest = candidate;
        }
        return latest;
    }

    private static bool IsTrue(JsonElement value, string property) =>
        value.TryGetProperty(property, out var element) && element.ValueKind == JsonValueKind.True;

    private static bool TryGetString(JsonElement value, string property, out string text)
    {
        text = "";
        if (!value.TryGetProperty(property, out var element) || element.ValueKind != JsonValueKind.String) return false;
        text = element.GetString() ?? "";
        return !string.IsNullOrWhiteSpace(text);
    }

    private static bool TryGetUri(JsonElement value, string property, out Uri uri)
    {
        uri = null!;
        if (!TryGetString(value, property, out var text)
            || !Uri.TryCreate(text, UriKind.Absolute, out var candidate)
            || candidate == null
            || candidate.Scheme != Uri.UriSchemeHttps
            || !candidate.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)) return false;
        uri = candidate;
        return true;
    }

    public static string DisplayVersion(Version version) =>
        $"{version.Major}.{version.Minor}.{Math.Max(0, version.Build)}";

    private static bool TryParseVersion(string tag, out Version version)
    {
        version = new Version(0, 0);
        var number = tag.Trim();
        if (number.StartsWith('v') || number.StartsWith('V')) number = number[1..];
        if (!Version.TryParse(number, out var parsed) || parsed.Major < 0 || parsed.Minor < 0) return false;
        version = Normalize(parsed);
        return true;
    }

    private static Version Normalize(Version version) => new(
        Math.Max(0, version.Major), Math.Max(0, version.Minor), Math.Max(0, version.Build), Math.Max(0, version.Revision));

    private static UpdateCheckResult HttpFailure(HttpStatusCode statusCode) => statusCode switch
    {
        HttpStatusCode.NotFound => Failure("暂未找到稳定版发布，请稍后重试。"),
        HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests => Failure("GitHub 暂时限制了更新检查，请稍后重试。"),
        _ => Failure("更新检查失败，请稍后重试。")
    };

    private static UpdateCheckResult Failure(string message) => new(UpdateCheckStatus.Failed, message);

    public void Dispose()
    {
        if (ownsClient) client.Dispose();
    }
}
