using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using System.Web.Http;
using Newtonsoft.Json.Linq;
using Xunit;

namespace MyDmsVn.Server.HttpHost.Tests;

// VPS-01: pin existing legacy attribute routes and Newtonsoft envelope before refactoring.
public sealed class Vps01ContractFreezeTests
{
    // Scan the host assembly so a newly introduced attribute-route controller cannot bypass the freeze.
    private static readonly Type[] ControllerTypes = typeof(HealthController).Assembly.GetTypes()
        .Where(type => !type.IsAbstract &&
            typeof(ApiController).IsAssignableFrom(type) &&
            type.GetCustomAttributes(typeof(RoutePrefixAttribute), false).Length != 0)
        .OrderBy(type => type.FullName, StringComparer.Ordinal).ToArray();

    [Fact]
    public void Legacy_controller_routes_match_independent_frozen_inventory()
    {
        var file = Path.Combine(ContractsDirectory(), "vps-01-route-inventory.json");
        var endpoints = (JArray)JObject.Parse(File.ReadAllText(file))["endpoints"]!;
        var expected = endpoints.Select(e => Signature((string)e["httpMethod"]!,
            (string)e["path"]!, (string)e["controller"]!, (string)e["action"]!))
            .OrderBy(e => e, StringComparer.Ordinal).ToArray();
        Assert.Equal(9, ControllerTypes.Length);
        Assert.Equal(29, expected.Length);
        Assert.Equal(29, expected.Distinct(StringComparer.Ordinal).Count());

        var actual = new List<string>();
        foreach (var type in ControllerTypes)
        {
            var prefix = ((RoutePrefixAttribute)type.GetCustomAttributes(
                typeof(RoutePrefixAttribute), false).Single()).Prefix;
            foreach (var method in type.GetMethods(BindingFlags.Public |
                BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                var route = method.GetCustomAttributes(typeof(RouteAttribute), false)
                    .Cast<RouteAttribute>().SingleOrDefault();
                if (route == null) continue;
                var verbs = method.GetCustomAttributes(false).Select(a => a.GetType().Name)
                    .Where(n => n == "HttpGetAttribute" || n == "HttpPostAttribute" ||
                        n == "HttpPutAttribute" || n == "HttpDeleteAttribute").ToArray();
                Assert.Single(verbs);
                var verb = verbs[0].Replace("Http", "").Replace("Attribute", "").ToUpperInvariant();
                var path = "/" + string.Join("/", new[] { prefix.Trim('/'), route.Template.Trim('/') }
                    .Where(part => part.Length != 0));
                actual.Add(Signature(verb, path, type.Name, method.Name));
            }
        }
        Assert.Equal(expected, actual.OrderBy(e => e, StringComparer.Ordinal).ToArray());
    }

    [Theory]
    [InlineData("web2-v1", 34)]
    [InlineData("web3-v1", 38)]
    [InlineData("web4-v1", 48)]
    [InlineData("web5-resolution-v1", 41)]
    [InlineData("web6-resend-eligibility-v1", 17)]
    public void Frozen_corpus_files_are_available_locally(string folder, int count)
    {
        var root = ContractsDirectory();
        var lines = File.ReadAllLines(Path.Combine(root, "corpus-blobs.sha1"));
        Assert.Equal(178, lines.Length);
        var matches = lines.Where(line => line.Contains("  docs/contracts/" + folder + "/")).ToArray();
        Assert.Equal(count, matches.Length);
        foreach (var line in matches)
        {
            var parts = line.Split(new[] { "  " }, 2, StringSplitOptions.None);
            Assert.Equal(2, parts.Length);
            Assert.Equal(40, parts[0].Length);
            var relative = parts[1].Substring("docs/contracts/".Length)
                .Replace('/', Path.DirectorySeparatorChar);
            Assert.True(File.Exists(Path.Combine(root, relative)), "Missing fixture: " + relative);
        }
    }

    [Fact]
    public async Task Success_envelope_freezes_int64_decimal_and_missing_null()
    {
        using var response = HttpJson.Success(new
        {
            receiptId = "9007199254740993",
            quantity = "1.2500",
            missing = (string?)null
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("{\"data\":{\"receiptId\":\"9007199254740993\",\"quantity\":\"1.2500\"}}",
            await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Error_envelope_freezes_status_code_correlation_and_body()
    {
        using var response = HttpJson.Error(401, "Auth.Unauthorized",
            "Authentication is required.", "vps01-correlation");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("{\"error\":{\"code\":\"Auth.Unauthorized\",\"message\":\"Authentication is required.\",\"details\":[],\"correlationId\":\"vps01-correlation\"}}",
            await response.Content.ReadAsStringAsync());
    }

    private static string Signature(string verb, string path, string controller, string action) =>
        verb + " " + path + " [" + controller + "." + action + "]";

    private static string ContractsDirectory()
    {
        for (DirectoryInfo? dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            dir != null; dir = dir.Parent)
        {
            var result = Path.Combine(dir.FullName, "docs", "contracts");
            if (File.Exists(Path.Combine(result, "corpus-blobs.sha1"))) return result;
        }
        throw new DirectoryNotFoundException("Expected checked-in docs/contracts fixture corpus.");
    }
}
