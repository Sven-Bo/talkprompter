using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Teleprompter.Core.Languages;
using Teleprompter.Speech;

namespace Teleprompter.Core.Tests;

public sealed class VoicePackInstallerTests : IDisposable
{
    private const string VoskFolder = "vosk-model-small-pl-test";
    private const string SherpaFolder = "sherpa-onnx-test";

    private static readonly byte[] Tokens = "a 0\nb 1\n"u8.ToArray();
    private static readonly byte[] Encoder = Enumerable.Range(0, 4096).Select(i => (byte)i).ToArray();

    private readonly string _models = Path.Combine(Path.GetTempPath(), "tp-install-" + Guid.NewGuid().ToString("N"));

    private static readonly VoicePackInstallerOptions Fast = new()
    {
        RetryDelay = _ => TimeSpan.Zero,
        MoveRetryDelay = TimeSpan.FromMilliseconds(50),
        FreeSpace = _ => long.MaxValue
    };

    public void Dispose()
    {
        try
        {
            Directory.Delete(_models, recursive: true);
        }
        catch (IOException)
        {
            // Temp folder cleanup is best effort.
        }
    }

    // ----- fixtures -----

    private static VoiceLanguage SherpaPack(long? encoderBytes = null) => new(
        "id", "Indonesian", "Bahasa Indonesia", VoicePackKind.SherpaOnnx, SherpaFolder,
        new[]
        {
            new VoicePackFile("encoder-test.onnx", "https://models.test/encoder-test.onnx", encoderBytes ?? Encoder.Length),
            new VoicePackFile("tokens.txt", "https://models.test/tokens.txt", Tokens.Length)
        });

    private static VoiceLanguage VoskPack(byte[] zip) => new(
        "pl", "Polish", "Polski", VoicePackKind.Vosk, VoskFolder,
        new[] { new VoicePackFile(VoskFolder + ".zip", $"https://models.test/{VoskFolder}.zip", zip.Length) });

    private static byte[] VoskZip(bool complete = true)
    {
        string[] files = complete
            ? new[] { "am/final.mdl", "conf/mfcc.conf", "graph/HCLr.fst", "graph/Gr.fst" }
            : new[] { "am/final.mdl" };

        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (string file in files)
            {
                using Stream entry = zip.CreateEntry($"{VoskFolder}/{file}").Open();
                entry.Write(new byte[] { 1, 2, 3 });
            }
        }

        return buffer.ToArray();
    }

    private static HttpResponseMessage Ok(byte[] body) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };

    private static HttpResponseMessage Serve(string url, byte[]? zip = null) => url switch
    {
        _ when url.EndsWith("tokens.txt", StringComparison.Ordinal) => Ok(Tokens),
        _ when url.EndsWith(".onnx", StringComparison.Ordinal) => Ok(Encoder),
        _ when url.EndsWith(".zip", StringComparison.Ordinal) && zip is not null => Ok(zip),
        _ => new HttpResponseMessage(HttpStatusCode.NotFound)
    };

    private VoicePackInstaller Installer(FakeServer server, VoicePackInstallerOptions? options = null)
        => new(_models, server, options ?? Fast);

    private static readonly IProgress<(double Percent, string Status)> NoProgress = new SyncProgress(_ => { });

    private string[] StagingFolders()
        => Directory.Exists(_models)
            ? Directory.GetDirectories(_models, ".unpack-*")
            : Array.Empty<string>();

    // ----- installing -----

    [Fact]
    public async Task LooseFiles_AreInstalledAsTheModelFolder()
    {
        var server = new FakeServer((url, _) => Serve(url));

        string dir = await Installer(server).InstallAsync(SherpaPack(), NoProgress, CancellationToken.None);

        dir.Should().Be(Path.Combine(_models, SherpaFolder));
        ModelScanner.IsSherpaModel(dir).Should().BeTrue();
        File.ReadAllBytes(Path.Combine(dir, "encoder-test.onnx")).Should().Equal(Encoder);
        StagingFolders().Should().BeEmpty();
    }

    [Fact]
    public async Task Zip_IsUnpackedIntoTheModelFolder_AndRemoved()
    {
        byte[] zip = VoskZip();
        var server = new FakeServer((url, _) => Serve(url, zip));

        string dir = await Installer(server).InstallAsync(VoskPack(zip), NoProgress, CancellationToken.None);

        ModelScanner.IsVoskModel(dir).Should().BeTrue();
        Directory.GetFiles(_models, "*.zip", SearchOption.AllDirectories).Should().BeEmpty();
        StagingFolders().Should().BeEmpty();
    }

    [Fact]
    public async Task IncompleteZip_IsRejected_AndNothingIsInstalled()
    {
        byte[] zip = VoskZip(complete: false);
        var server = new FakeServer((url, _) => Serve(url, zip));

        Func<Task> install = () => Installer(server).InstallAsync(VoskPack(zip), NoProgress, CancellationToken.None);

        await install.Should().ThrowAsync<VoicePackException>();
        Directory.Exists(Path.Combine(_models, VoskFolder)).Should().BeFalse();
        StagingFolders().Should().BeEmpty();
    }

    [Fact]
    public async Task FileOfTheWrongSize_IsRejected()
    {
        var server = new FakeServer((url, _) => Serve(url));

        Func<Task> install = () => Installer(server).InstallAsync(SherpaPack(encoderBytes: 9999), NoProgress, CancellationToken.None);

        await install.Should().ThrowAsync<VoicePackException>();
        Directory.Exists(Path.Combine(_models, SherpaFolder)).Should().BeFalse();
    }

    [Fact]
    public async Task BrokenExistingFolder_IsReplaced()
    {
        string existing = Path.Combine(_models, VoskFolder);
        Directory.CreateDirectory(Path.Combine(existing, "am"));
        File.WriteAllText(Path.Combine(existing, "am", "final.mdl"), "half");
        File.WriteAllText(Path.Combine(existing, "stale.txt"), "old");
        byte[] zip = VoskZip();
        var server = new FakeServer((url, _) => Serve(url, zip));

        string dir = await Installer(server).InstallAsync(VoskPack(zip), NoProgress, CancellationToken.None);

        ModelScanner.IsVoskModel(dir).Should().BeTrue();
        File.Exists(Path.Combine(dir, "stale.txt")).Should().BeFalse();
    }

    [Fact]
    public async Task LeftoverStagingFolders_AreCleanedUp()
    {
        Directory.CreateDirectory(Path.Combine(_models, ".unpack-vosk-model-small-de-0.15", "junk"));
        var server = new FakeServer((url, _) => Serve(url));

        await Installer(server).InstallAsync(SherpaPack(), NoProgress, CancellationToken.None);

        StagingFolders().Should().BeEmpty();
    }

    // ----- network failures -----

    [Fact]
    public async Task TlsReset_IsRetried()
    {
        var server = new FakeServer((url, attempt) => url.EndsWith(".onnx", StringComparison.Ordinal) && attempt == 1
            ? throw new HttpRequestException("The SSL connection could not be established.", new IOException("reset"))
            : Serve(url));

        string dir = await Installer(server).InstallAsync(SherpaPack(), NoProgress, CancellationToken.None);

        server.Calls("encoder-test.onnx").Should().Be(2);
        ModelScanner.IsSherpaModel(dir).Should().BeTrue();
    }

    [Fact]
    public async Task ResetMidStream_RestartsTheFile_WithoutKeepingPartialBytes()
    {
        var server = new FakeServer((url, attempt) => url.EndsWith(".onnx", StringComparison.Ordinal) && attempt == 1
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new BrokenStream(Encoder.Take(1000).ToArray())) }
            : Serve(url));
        var progress = new SyncProgress(_ => { });

        string dir = await Installer(server).InstallAsync(SherpaPack(), progress, CancellationToken.None);

        server.Calls("encoder-test.onnx").Should().Be(2);
        File.ReadAllBytes(Path.Combine(dir, "encoder-test.onnx")).Should().Equal(Encoder);
        progress.Statuses.Should().Contain(s => s.StartsWith("Connection dropped", StringComparison.Ordinal));
    }

    [Fact]
    public async Task NotFound_IsNotRetried()
    {
        var server = new FakeServer((url, _) => url.EndsWith("tokens.txt", StringComparison.Ordinal)
            ? new HttpResponseMessage(HttpStatusCode.NotFound)
            : Serve(url));

        Func<Task> install = () => Installer(server).InstallAsync(SherpaPack(), NoProgress, CancellationToken.None);

        (await install.Should().ThrowAsync<HttpRequestException>()).Which.StatusCode.Should().Be(HttpStatusCode.NotFound);
        server.Calls("tokens.txt").Should().Be(1);
        Directory.Exists(Path.Combine(_models, SherpaFolder)).Should().BeFalse();
        StagingFolders().Should().BeEmpty();
    }

    [Fact]
    public async Task PersistentConnectionFailure_GivesUpAfterTheLastAttempt()
    {
        var server = new FakeServer((_, _) => throw new HttpRequestException("No such host is known."));

        Func<Task> install = () => Installer(server).InstallAsync(SherpaPack(), NoProgress, CancellationToken.None);

        await install.Should().ThrowAsync<HttpRequestException>();
        server.Calls("encoder-test.onnx").Should().Be(new VoicePackInstallerOptions().MaxAttempts);
    }

    [Fact]
    public async Task Stall_IsReportedAsATimeout_AfterRetrying()
    {
        var server = new FakeServer((url, _) => url.EndsWith(".onnx", StringComparison.Ordinal)
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new StallingStream()) }
            : Serve(url));
        var options = Fast with { StallTimeout = TimeSpan.FromMilliseconds(150), MaxAttempts = 2 };

        Func<Task> install = () => Installer(server, options).InstallAsync(SherpaPack(), NoProgress, CancellationToken.None);

        await install.Should().ThrowAsync<TimeoutException>();
        server.Calls("encoder-test.onnx").Should().Be(2);
    }

    [Fact]
    public async Task CancelDuringAStall_IsACancellation_NotARetry()
    {
        var server = new FakeServer((url, _) => url.EndsWith(".onnx", StringComparison.Ordinal)
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new StallingStream()) }
            : Serve(url));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));

        Func<Task> install = () => Installer(server).InstallAsync(SherpaPack(), NoProgress, cts.Token);

        await install.Should().ThrowAsync<OperationCanceledException>();
        server.Calls("encoder-test.onnx").Should().Be(1);
        StagingFolders().Should().BeEmpty();
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(HttpStatusCode.ServiceUnavailable, true)]
    [InlineData(HttpStatusCode.TooManyRequests, true)]
    [InlineData(HttpStatusCode.NotFound, false)]
    [InlineData(HttpStatusCode.Forbidden, false)]
    public void HttpFailures_AreTransientOnlyWhenRetryingCanHelp(HttpStatusCode? status, bool expected)
    {
        VoicePackInstaller.IsTransientNetworkFailure(new HttpRequestException("x", null, status))
            .Should().Be(expected);
    }

    [Fact]
    public void ConnectionResetsAndStalls_AreTransient_ButDiskErrorsAreNot()
    {
        VoicePackInstaller.IsTransientNetworkFailure(
            new IOException("reset", new SocketException((int)SocketError.ConnectionReset))).Should().BeTrue();
        VoicePackInstaller.IsTransientNetworkFailure(new TimeoutException()).Should().BeTrue();
        VoicePackInstaller.IsTransientNetworkFailure(new IOException("There is not enough space on the disk.")).Should().BeFalse();
        VoicePackInstaller.IsTransientNetworkFailure(new InvalidDataException()).Should().BeFalse();
    }

    private static readonly IPAddress V6A = IPAddress.Parse("2600:9000::1");
    private static readonly IPAddress V4A = IPAddress.Parse("54.230.41.129");
    private static readonly IPAddress V6B = IPAddress.Parse("2600:9000::2");
    private static readonly IPAddress V4B = IPAddress.Parse("54.230.41.130");

    [Fact]
    public void Connections_KeepTheSystemAddressOrder_ByDefault()
    {
        VoicePackInstaller.ConnectOrder(new[] { V6A, V4A, V6B, V4B }, preferIPv4: false)
            .Should().Equal(V6A, V4A, V6B, V4B);
    }

    [Fact]
    public void Connections_CanTryIPv4AddressesFirst()
    {
        VoicePackInstaller.ConnectOrder(new[] { V6A, V4A, V6B, V4B }, preferIPv4: true)
            .Should().Equal(V4A, V4B, V6A, V6B);
    }

    [Fact]
    public async Task ConnectionFailure_MakesTheRetryTryTheOtherIPFamilyFirst()
    {
        // Real networks differ per host: one resets TLS over IPv6 (Hugging
        // Face here) while another is far slower over IPv4 (alphacephei), so
        // only a failure switches families.
        var server = new FakeServer((url, attempt) => url.EndsWith(".onnx", StringComparison.Ordinal) && attempt == 1
            ? throw new HttpRequestException("The SSL connection could not be established.", new IOException("reset"))
            : Serve(url));
        VoicePackInstaller installer = Installer(server);

        await installer.InstallAsync(SherpaPack(), NoProgress, CancellationToken.None);

        installer.PrefersIPv4.Should().BeTrue();
    }

    [Fact]
    public async Task SuccessfulDownloads_KeepTheSystemAddressOrder()
    {
        VoicePackInstaller installer = Installer(new FakeServer((url, _) => Serve(url)));

        await installer.InstallAsync(SherpaPack(), NoProgress, CancellationToken.None);

        installer.PrefersIPv4.Should().BeFalse();
    }

    // ----- disk and cancellation -----

    [Fact]
    public async Task NotEnoughDiskSpace_FailsBeforeDownloading()
    {
        var server = new FakeServer((url, _) => Serve(url));
        var options = Fast with { FreeSpace = _ => 10 };

        Func<Task> install = () => Installer(server, options).InstallAsync(SherpaPack(), NoProgress, CancellationToken.None);

        (await install.Should().ThrowAsync<VoicePackException>()).Which.Message.Should().Contain("disk space");
        server.TotalCalls.Should().Be(0);
    }

    [Fact]
    public async Task CancelWhileInstalling_InstallsNothing()
    {
        var server = new FakeServer((url, _) => Serve(url));
        using var cts = new CancellationTokenSource();
        var progress = new SyncProgress(p =>
        {
            if (p.Status.StartsWith("Installing", StringComparison.Ordinal))
            {
                cts.Cancel();
            }
        });

        Func<Task> install = () => Installer(server).InstallAsync(SherpaPack(), progress, cts.Token);

        await install.Should().ThrowAsync<OperationCanceledException>();
        Directory.Exists(Path.Combine(_models, SherpaFolder)).Should().BeFalse();
        StagingFolders().Should().BeEmpty();
    }

    [Fact]
    public async Task BriefFileLock_OnTheOldFolder_IsWaitedOut()
    {
        // Virus scanners briefly hold files open; replacing the folder must wait, not fail.
        string existing = Path.Combine(_models, SherpaFolder);
        Directory.CreateDirectory(existing);
        var locked = new FileStream(Path.Combine(existing, "locked.bin"), FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        _ = Task.Delay(300).ContinueWith(_ => locked.Dispose(), TaskScheduler.Default);
        var server = new FakeServer((url, _) => Serve(url));
        var options = Fast with { MoveAttempts = 40 };

        string dir = await Installer(server, options).InstallAsync(SherpaPack(), NoProgress, CancellationToken.None);

        ModelScanner.IsSherpaModel(dir).Should().BeTrue();
        File.Exists(Path.Combine(dir, "locked.bin")).Should().BeFalse();
    }

    [Fact]
    public async Task PersistentFileLock_FailsWithAClearMessage()
    {
        string existing = Path.Combine(_models, SherpaFolder);
        Directory.CreateDirectory(existing);
        using var locked = new FileStream(Path.Combine(existing, "locked.bin"), FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        var server = new FakeServer((url, _) => Serve(url));
        var options = Fast with { MoveAttempts = 3 };

        Func<Task> install = () => Installer(server, options).InstallAsync(SherpaPack(), NoProgress, CancellationToken.None);

        await install.Should().ThrowAsync<VoicePackException>();
        StagingFolders().Should().BeEmpty();
    }

    // ----- test doubles -----

    private sealed class FakeServer : HttpMessageHandler
    {
        private readonly Func<string, int, HttpResponseMessage> _respond;
        private readonly ConcurrentDictionary<string, int> _calls = new();

        public FakeServer(Func<string, int, HttpResponseMessage> respond) => _respond = respond;

        public int TotalCalls => _calls.Values.Sum();

        public int Calls(string fileName)
            => _calls.Where(c => c.Key.EndsWith("/" + fileName, StringComparison.Ordinal)).Sum(c => c.Value);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string url = request.RequestUri!.ToString();
            int attempt = _calls.AddOrUpdate(url, 1, (_, n) => n + 1);
            return Task.FromResult(_respond(url, attempt));
        }
    }

    private sealed class SyncProgress : IProgress<(double Percent, string Status)>
    {
        private readonly Action<(double Percent, string Status)> _onReport;

        public SyncProgress(Action<(double Percent, string Status)> onReport) => _onReport = onReport;

        public List<string> Statuses { get; } = new();

        public void Report((double Percent, string Status) value)
        {
            Statuses.Add(value.Status);
            _onReport(value);
        }
    }

    /// <summary>Serves some bytes, then fails like a connection reset.</summary>
    private sealed class BrokenStream : Stream
    {
        private readonly byte[] _prefix;
        private int _position;

        public BrokenStream(byte[] prefix) => _prefix = prefix;

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_position >= _prefix.Length)
            {
                throw new IOException(
                    "Unable to read data from the transport connection.",
                    new SocketException((int)SocketError.ConnectionReset));
            }

            int n = Math.Min(count, _prefix.Length - _position);
            Array.Copy(_prefix, _position, buffer, offset, n);
            _position += n;
            return n;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>A connection that stays open but never sends data.</summary>
    private sealed class StallingStream : Stream
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
