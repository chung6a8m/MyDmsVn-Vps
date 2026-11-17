using System;
using System.IO;
using System.Text;
using System.Threading;
using MyDmsVn.Desktop.Infrastructure.Http;
using Newtonsoft.Json;

namespace MyDmsVn.Vps03.PendingStore.CrashHost;

internal static class Program
{
    private const int MaximumExchangeBytes = 4096;
    private static readonly UTF8Encoding Utf8 = new(false, true);

    private static int Main(string[] args)
    {
        if (args.Length != 3 || !AllowedMode(args[0]) ||
            !Path.IsPathRooted(args[2]) || !Directory.Exists(args[2])) return 64;
        try
        {
            var options = new HttpDesktopOptions(args[1]);
            var requestPath = Path.Combine(args[2], "request.json");
            var record = ReadRecord(requestPath);
            var store = new WindowsHttpPendingWriteStore(options);
            switch (args[0])
            {
                case "before-save":
                    Write(args[2], "stage.json", new Exchange { Stage = "before-save-ready" });
                    Thread.Sleep(Timeout.Infinite);
                    return 0;
                case "after-save":
                    store.Save(record);
                    Write(args[2], "stage.json", new Exchange { Stage = "after-save-complete" });
                    Thread.Sleep(Timeout.Infinite);
                    return 0;
                case "load":
                    var loaded = store.Load();
                    Write(args[2], "result.json", loaded == null
                        ? new Exchange { Stage = "load-complete", FailureCode = "Desktop.PendingIntentAbsent" }
                        : new Exchange
                        {
                            Stage = "load-complete",
                            OperationId = loaded.OperationId,
                            Fingerprint = loaded.Fingerprint,
                        });
                    return 0;
                default:
                    return 64;
            }
        }
        catch (Exception error)
        {
            TryWriteFailure(args[2], Category(error));
            return 2;
        }
    }

    private static bool AllowedMode(string mode) =>
        mode == "before-save" || mode == "after-save" || mode == "load";

    private static HttpPendingWriteRecord ReadRecord(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length <= 0 || info.Length > MaximumExchangeBytes)
            throw new InvalidDataException();
        var record = JsonConvert.DeserializeObject<HttpPendingWriteRecord>(
            File.ReadAllText(path, Utf8), new JsonSerializerSettings
            {
                MissingMemberHandling = MissingMemberHandling.Error,
                TypeNameHandling = TypeNameHandling.None,
            }) ?? throw new InvalidDataException();
        record.Validate();
        return record;
    }

    private static void Write(string directory, string name, Exchange exchange)
    {
        var json = JsonConvert.SerializeObject(exchange, Formatting.None);
        var bytes = Utf8.GetBytes(json);
        if (bytes.Length > MaximumExchangeBytes) throw new InvalidDataException();
        var target = Path.Combine(directory, name);
        var temp = target + ".tmp";
        File.WriteAllBytes(temp, bytes);
        if (File.Exists(target)) File.Delete(target);
        File.Move(temp, target);
    }

    private static void TryWriteFailure(string directory, string category)
    {
        try { Write(directory, "result.json", new Exchange { Stage = "failed", FailureCode = category }); }
        catch { }
    }

    private static string Category(Exception error)
    {
        if (error is InvalidOperationException invalid &&
            invalid.Message.StartsWith("Desktop.", StringComparison.Ordinal)) return invalid.Message;
        return error is JsonException || error is InvalidDataException ||
            error is DecoderFallbackException || error is ArgumentException
            ? "Desktop.PendingExchangeInvalid"
            : "Desktop.PendingStoreFailure";
    }

    private sealed class Exchange
    {
        public string? Stage { get; set; }
        public string? OperationId { get; set; }
        public string? Fingerprint { get; set; }
        public string? FailureCode { get; set; }
    }
}
