using Renci.SshNet;

namespace PortBridge;

public sealed class TunnelSession : IDisposable
{
    private SshClient? client;
    private PrivateKeyFile? key;
    private readonly List<ForwardedPortLocal> forwards = new();
    public bool IsConnected => client?.IsConnected == true && forwards.Count > 0 && forwards.All(f => f.IsStarted);
    public event Action<string>? Error;

    public async Task ConnectAsync(ServerProfile profile, string secret,
        Func<string, string, bool> trustHost, CancellationToken cancellationToken = default)
    {
        profile.Validate();
        try
        {
            AuthenticationMethod auth;
            if (profile.Auth == "key")
            {
                key = string.IsNullOrEmpty(secret) ? new PrivateKeyFile(profile.KeyPath) : new PrivateKeyFile(profile.KeyPath, secret);
                auth = new PrivateKeyAuthenticationMethod(profile.User, key);
            }
            else auth = new PasswordAuthenticationMethod(profile.User, secret);
            var info = new ConnectionInfo(profile.Host, profile.Port, profile.User, auth) { Timeout = TimeSpan.FromSeconds(15) };
            client = new SshClient(info) { KeepAliveInterval = TimeSpan.FromSeconds(20) };
            client.HostKeyReceived += (_, e) => e.CanTrust = trustHost(profile.Host + ":" + profile.Port, "SHA256:" + e.FingerPrintSHA256);
            client.ErrorOccurred += (_, e) => Error?.Invoke(e.Exception.Message);
            await client.ConnectAsync(cancellationToken).ConfigureAwait(false);
            foreach (var rule in profile.Rules.Where(r => r.Enabled))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var forward = new ForwardedPortLocal("127.0.0.1", (uint)rule.LocalPort, rule.TargetHost, (uint)rule.TargetPort);
                forwards.Add(forward);
                forward.Exception += (_, e) => Error?.Invoke(rule.Name + ": " + e.Exception.Message);
                client.AddForwardedPort(forward);
                forward.Start();
            }
        }
        catch { Dispose(); throw; }
    }

    public void Dispose()
    {
        foreach (var forward in forwards) { try { forward.Dispose(); } catch { /* Continue closing the remaining ports. */ } }
        forwards.Clear();
        try { client?.Dispose(); } finally { client = null; key?.Dispose(); key = null; }
    }
}
