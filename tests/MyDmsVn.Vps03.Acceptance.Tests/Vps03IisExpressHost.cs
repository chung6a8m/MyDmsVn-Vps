using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

namespace MyDmsVn.Vps03.Acceptance.Tests;

internal sealed class Vps03IisExpressHost
{
    private const string SiteName = "Vps03Issue8";
    private readonly Process _process;
    private readonly string _runDirectory;
    private readonly int _port;
    private bool _stopped;

    private Vps03IisExpressHost(Process process, string runDirectory, int port)
    {
        _process = process;
        _runDirectory = runDirectory;
        _port = port;
    }

    internal static async Task<Vps03IisExpressHost> StartAsync(
        Uri origin, string tlsThumbprint, string packageDirectory,
        IReadOnlyDictionary<string, string?> environment, CancellationToken token)
    {
        if (origin.Scheme != Uri.UriSchemeHttps || origin.AbsolutePath != "/")
            throw new InvalidDataException("VPS-03 IIS Express origin must be an HTTPS authority.");
        if (!Directory.Exists(packageDirectory) || !File.Exists(Path.Combine(packageDirectory, "Web.config")))
            throw new InvalidDataException("VPS-03 HttpHost package is missing Web.config.");
        ValidateCertificate(tlsThumbprint);
        if (IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(endpoint => endpoint.Port == origin.Port))
            throw new InvalidDataException("VPS-03 HTTPS port is already in use.");

        var executable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "IIS Express", "iisexpress.exe");
        if (!File.Exists(executable)) throw new FileNotFoundException("Installed IIS Express was not found.", executable);
        var runDirectory = Path.Combine(Path.GetTempPath(), "mydmsvn-vps03-iis-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(runDirectory);
        var configuration = CreateConfiguration(runDirectory, packageDirectory, origin);
        var start = new ProcessStartInfo(executable,
            "/config:\"" + configuration + "\" /site:" + SiteName + " /systray:false")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = runDirectory,
        };
        foreach (var key in start.EnvironmentVariables.Keys.Cast<string>().Where(IsSensitiveInheritedKey).ToArray())
            start.EnvironmentVariables.Remove(key);
        foreach (var item in environment)
        {
            if (item.Value == null) start.EnvironmentVariables.Remove(item.Key);
            else start.EnvironmentVariables[item.Key] = item.Value;
        }

        var process = new Process { StartInfo = start, EnableRaisingEvents = true };
        try
        {
            if (!process.Start()) throw new IOException("Owned IIS Express process did not start.");
            var host = new Vps03IisExpressHost(process, runDirectory, origin.Port);
            await host.WaitForHttpsAsync(origin, token).ConfigureAwait(false);
            return host;
        }
        catch
        {
            if (!process.HasExited)
            {
                process.Kill();
                process.WaitForExit(10000);
            }
            process.Dispose();
            throw;
        }
    }

    internal async Task StopAndVerifyAbsentAsync(CancellationToken token)
    {
        if (_stopped) return;
        if (!_process.HasExited)
        {
            _process.Kill();
            if (!await WaitUntilAsync(() => _process.HasExited, TimeSpan.FromSeconds(15), token).ConfigureAwait(false))
                throw new IOException("Owned IIS Express process did not stop; diagnostics retained.");
        }
        if (!await WaitUntilAsync(() => !IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners()
                .Any(endpoint => endpoint.Port == _port), TimeSpan.FromSeconds(15), token).ConfigureAwait(false))
            throw new IOException("Owned IIS Express HTTPS listener did not drain; diagnostics retained.");
        _process.Dispose();
        Directory.Delete(_runDirectory, true);
        _stopped = true;
    }

    internal static void ValidateTls(Uri expectedOrigin, string expectedThumbprint,
        Uri boundOrigin, string boundThumbprint)
    {
        if (expectedOrigin.GetLeftPart(UriPartial.Authority) != boundOrigin.GetLeftPart(UriPartial.Authority))
            throw new InvalidDataException("IIS Express HTTPS authority does not match the configured VPS-03 origin.");
        if (!string.Equals(Normalize(expectedThumbprint), Normalize(boundThumbprint), StringComparison.Ordinal))
            throw new InvalidDataException("IIS Express TLS certificate does not match the pinned VPS-03 thumbprint.");
    }

    private async Task WaitForHttpsAsync(Uri origin, CancellationToken token)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        Exception? last = null;
        var deadline = DateTime.UtcNow.AddSeconds(25);
        while (DateTime.UtcNow < deadline)
        {
            token.ThrowIfCancellationRequested();
            if (_process.HasExited) throw new IOException("Owned IIS Express exited during HTTPS admission.");
            try
            {
                using var response = await client.GetAsync(new Uri(origin, "/api/v1/auth/me"), token).ConfigureAwait(false);
                return;
            }
            catch (Exception error) when (error is HttpRequestException || error is TaskCanceledException)
            {
                last = error;
                await Task.Delay(200, token).ConfigureAwait(false);
            }
        }
        throw new TimeoutException("Owned IIS Express did not admit the trusted HTTPS origin.", last);
    }

    private static string CreateConfiguration(string runDirectory, string packageDirectory, Uri origin)
    {
        var source = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "IISExpress", "config", "applicationhost.config");
        if (!File.Exists(source)) throw new FileNotFoundException("IIS Express template configuration is absent.", source);
        var xml = new XmlDocument { XmlResolver = null };
        xml.Load(source);
        var sites = xml.SelectSingleNode("/configuration/system.applicationHost/sites")
            ?? throw new InvalidDataException("IIS Express sites configuration is absent.");
        foreach (XmlNode existing in sites.SelectNodes("site")!) sites.RemoveChild(existing);
        var site = xml.CreateElement("site");
        site.SetAttribute("name", SiteName);
        site.SetAttribute("id", "1");
        var application = xml.CreateElement("application");
        application.SetAttribute("path", "/");
        application.SetAttribute("applicationPool", "Clr4IntegratedAppPool");
        var directory = xml.CreateElement("virtualDirectory");
        directory.SetAttribute("path", "/");
        directory.SetAttribute("physicalPath", Path.GetFullPath(packageDirectory));
        application.AppendChild(directory);
        site.AppendChild(application);
        var bindings = xml.CreateElement("bindings");
        var binding = xml.CreateElement("binding");
        binding.SetAttribute("protocol", "https");
        binding.SetAttribute("bindingInformation", "*:" + origin.Port + ":" + origin.Host);
        bindings.AppendChild(binding);
        site.AppendChild(bindings);
        sites.AppendChild(site);
        var path = Path.Combine(runDirectory, "applicationhost.config");
        xml.Save(path);
        return path;
    }

    private static void ValidateCertificate(string expectedThumbprint)
    {
        var normalized = Normalize(expectedThumbprint);
        foreach (var location in new[] { StoreLocation.CurrentUser, StoreLocation.LocalMachine })
        {
            using var store = new X509Store(StoreName.My, location);
            store.Open(OpenFlags.ReadOnly);
            if (store.Certificates.Find(X509FindType.FindByThumbprint, normalized, validOnly: false).Count > 0)
                return;
        }
        throw new InvalidDataException("Pinned VPS-03 TLS certificate was not found in a Windows MY store.");
    }

    private static bool IsSensitiveInheritedKey(string key) =>
        key.IndexOf("SQL", StringComparison.OrdinalIgnoreCase) >= 0 ||
        key.IndexOf("PASSWORD", StringComparison.OrdinalIgnoreCase) >= 0 ||
        key.IndexOf("TOKEN", StringComparison.OrdinalIgnoreCase) >= 0 ||
        key.IndexOf("SECRET", StringComparison.OrdinalIgnoreCase) >= 0 ||
        key.IndexOf("JWT", StringComparison.OrdinalIgnoreCase) >= 0;

    private static string Normalize(string value) => value.Replace(" ", string.Empty).ToUpperInvariant();

    private static async Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan budget, CancellationToken token)
    {
        var deadline = DateTime.UtcNow.Add(budget);
        while (DateTime.UtcNow < deadline)
        {
            token.ThrowIfCancellationRequested();
            if (condition()) return true;
            await Task.Delay(100, token).ConfigureAwait(false);
        }
        return condition();
    }
}
