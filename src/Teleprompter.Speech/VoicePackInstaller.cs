using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Teleprompter.Core.Languages;

namespace Teleprompter.Speech;

/// <summary>Tunables for <see cref="VoicePackInstaller"/>; tests shrink the waits.</summary>
public sealed record VoicePackInstallerOptions
{
    /// <summary>Tries per file before a network failure is reported.</summary>
    public int MaxAttempts { get; init; } = 4;

    /// <summary>Wait before retry number n + 1 (1 s, 2 s, 4 s).</summary>
    public Func<int, TimeSpan> RetryDelay { get; init; } = attempt => TimeSpan.FromSeconds(1 << (attempt - 1));

    /// <summary>Give up on a connection that sends nothing for this long.</summary>
    public TimeSpan StallTimeout { get; init; } = TimeSpan.FromSeconds(45);

    /// <summary>Tries to put the pack in place while something (a virus scanner) holds its files.</summary>
    public int MoveAttempts { get; init; } = 10;

    public TimeSpan MoveRetryDelay { get; init; } = TimeSpan.FromMilliseconds(500);

    /// <summary>Free bytes on the drive of a folder.</summary>
    public Func<string, long> FreeSpace { get; init; } =
        path => new DriveInfo(Path.GetPathRoot(Path.GetFullPath(path))!).AvailableFreeSpace;
}

/// <summary>A voice pack could not be installed, for a reason the user can act on.</summary>
public sealed class VoicePackException : Exception
{
    public VoicePackException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Downloads a language's voice pack into a models folder. Everything lands in
/// a staging folder first and moves into place only when complete, so a failed
/// or cancelled download never leaves a partial model that would look installed.
/// </summary>
public sealed class VoicePackInstaller
{
    private const string StagingPrefix = ".unpack-";
    private const int BufferSize = 81920;
    private const double BytesPerMegabyte = 1024d * 1024d;

    /// <summary>Per-address TCP connect limit, so a dead route falls through to the next address quickly.</summary>
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(8);

    // Unpacked Vosk models are about twice their zip, and the zip is still on
    // disk while unpacking; loose files only need their own size.
    private const double ZipSpaceFactor = 3.0;
    private const double FilesSpaceFactor = 1.25;

    private readonly string _modelsDirectory;
    private readonly HttpMessageHandler? _handler;
    private readonly VoicePackInstallerOptions _options;

    // Flipped after every retryable failure so the next try starts with the
    // other IP family. Networks break one family per host: here Hugging Face
    // reset TLS over IPv6, while alphacephei ran ten times slower over IPv4.
    private volatile bool _preferIPv4;

    public VoicePackInstaller(
        string modelsDirectory,
        HttpMessageHandler? handler = null,
        VoicePackInstallerOptions? options = null)
    {
        _modelsDirectory = modelsDirectory;
        _handler = handler;
        _options = options ?? new VoicePackInstallerOptions();
    }

    /// <summary>
    /// Downloads and installs the pack, reporting (percent, status). Returns the
    /// installed model folder. Cancelling at any point installs nothing.
    /// </summary>
    public async Task<string> InstallAsync(
        VoiceLanguage language,
        IProgress<(double Percent, string Status)> progress,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_modelsDirectory);
        RemoveStagingFolders();
        EnsureFreeSpace(language);

        string stagingDir = Path.Combine(_modelsDirectory, StagingPrefix + language.ModelFolder);
        try
        {
            // Zips unpack their own model folder; loose files go straight into it.
            string downloadDir = language.Kind == VoicePackKind.Vosk
                ? Directory.CreateDirectory(stagingDir).FullName
                : Directory.CreateDirectory(Path.Combine(stagingDir, language.ModelFolder)).FullName;

            long done = 0;
            foreach (VoicePackFile file in language.Files)
            {
                string target = Path.Combine(downloadDir, file.Name);
                done += await DownloadWithRetryAsync(language, file, target, done, progress, cancellationToken);
            }

            progress.Report((99.5, "Installing…"));
            string staged = await Task.Run(() => Unpack(language, stagingDir), cancellationToken);

            string modelDir = Path.Combine(_modelsDirectory, language.ModelFolder);
            await MoveIntoPlaceAsync(staged, modelDir, cancellationToken);

            progress.Report((100, "Ready."));
            return modelDir;
        }
        finally
        {
            TryDeleteDirectory(stagingDir);
        }
    }

    /// <summary>True after an odd number of network failures: connect over IPv4 first.</summary>
    internal bool PrefersIPv4 => _preferIPv4;

    /// <summary>
    /// The order to try a host's addresses in: the system's own order (usually
    /// IPv6 first), or IPv4 first. .NET never switches families by itself when
    /// a connection resets after connecting.
    /// </summary>
    internal static IReadOnlyList<IPAddress> ConnectOrder(IEnumerable<IPAddress> addresses, bool preferIPv4)
        => preferIPv4
            ? addresses.OrderBy(a => a.AddressFamily == AddressFamily.InterNetwork ? 0 : 1).ToArray()
            : addresses.ToArray();

    private HttpMessageHandler CreateDefaultHandler() => new SocketsHttpHandler
    {
        ConnectCallback = async (context, cancellationToken) =>
        {
            IPAddress[] resolved = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken);
            Exception? lastError = null;
            foreach (IPAddress address in ConnectOrder(resolved, _preferIPv4))
            {
                var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                try
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    timeout.CancelAfter(ConnectTimeout);
                    await socket.ConnectAsync(address, context.DnsEndPoint.Port, timeout.Token);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch (Exception ex) when (ex is SocketException or OperationCanceledException
                                           && !cancellationToken.IsCancellationRequested)
                {
                    socket.Dispose();
                    lastError = ex;
                }
            }

            throw lastError ?? new SocketException((int)SocketError.HostNotFound);
        }
    };

    /// <summary>
    /// Whether retrying can help: connection and TLS resets, stalls, and server
    /// overload. "Not found" and local disk errors fail at once.
    /// </summary>
    internal static bool IsTransientNetworkFailure(Exception ex) => ex switch
    {
        HttpRequestException { StatusCode: { } status } =>
            (int)status >= 500 || status is HttpStatusCode.TooManyRequests or HttpStatusCode.RequestTimeout,
        HttpRequestException => true,
        HttpIOException => true,
        IOException { InnerException: SocketException } => true,
        TimeoutException => true,
        _ => false
    };

    private async Task<long> DownloadWithRetryAsync(
        VoiceLanguage language,
        VoicePackFile file,
        string target,
        long doneBefore,
        IProgress<(double Percent, string Status)> progress,
        CancellationToken cancellationToken)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                return await DownloadFileAsync(language, file, target, doneBefore, progress, cancellationToken);
            }
            catch (Exception ex) when (attempt < _options.MaxAttempts
                                       && !cancellationToken.IsCancellationRequested
                                       && IsTransientNetworkFailure(ex))
            {
                _preferIPv4 = !_preferIPv4;
                double percent = Math.Min(99.0, doneBefore * 100.0 / Math.Max(1, language.DownloadBytes));
                progress.Report((percent, $"Connection dropped. Retrying ({attempt + 1} of {_options.MaxAttempts})…"));
                await Task.Delay(_options.RetryDelay(attempt), cancellationToken);
            }
        }
    }

    /// <summary>Streams one file to disk (replacing any partial copy); returns the bytes written.</summary>
    private async Task<long> DownloadFileAsync(
        VoiceLanguage language,
        VoicePackFile file,
        string target,
        long doneBefore,
        IProgress<(double Percent, string Status)> progress,
        CancellationToken cancellationToken)
    {
        using var stall = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        stall.CancelAfter(_options.StallTimeout);

        try
        {
            // A fresh client per attempt means a fresh connection, which
            // usually reaches a healthy server after a reset.
            using HttpClient http = _handler is null
                ? new HttpClient(CreateDefaultHandler(), disposeHandler: true)
                : new HttpClient(_handler, disposeHandler: false);
            http.Timeout = Timeout.InfiniteTimeSpan;

            using HttpResponseMessage response = await http.GetAsync(
                file.Url, HttpCompletionOption.ResponseHeadersRead, stall.Token);
            response.EnsureSuccessStatusCode();

            await using Stream source = await response.Content.ReadAsStreamAsync(stall.Token);
            await using FileStream output = File.Create(target);

            double total = Math.Max(1, language.DownloadBytes);
            var buffer = new byte[BufferSize];
            long written = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, stall.Token)) > 0)
            {
                stall.CancelAfter(_options.StallTimeout); // data arrived: restart the stall clock
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                written += read;

                long done = doneBefore + written;
                progress.Report((
                    Math.Min(99.0, done * 100.0 / total),
                    $"Downloading {language.EnglishName}… {(int)(done / BytesPerMegabyte)} of {language.DownloadMegabytes} MB"));
            }

            return written;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("The download stopped responding.");
        }
    }

    private static string Unpack(VoiceLanguage language, string stagingDir)
    {
        string staged = Path.Combine(stagingDir, language.ModelFolder);
        bool complete;
        if (language.Kind == VoicePackKind.Vosk)
        {
            try
            {
                // The official zips contain the model folder at their root.
                ZipFile.ExtractToDirectory(Path.Combine(stagingDir, language.Files[0].Name), stagingDir);
            }
            catch (InvalidDataException ex)
            {
                throw new VoicePackException("The downloaded voice pack is damaged. Please try again.", ex);
            }

            complete = ModelScanner.IsVoskModel(staged);
        }
        else
        {
            // Loose files are pinned to one revision, so their sizes are exact.
            complete = ModelScanner.IsSherpaModel(staged)
                       && language.Files.All(f => new FileInfo(Path.Combine(staged, f.Name)).Length == f.Bytes);
        }

        return complete
            ? staged
            : throw new VoicePackException("The downloaded voice pack is incomplete. Please try again.");
    }

    private async Task MoveIntoPlaceAsync(string staged, string modelDir, CancellationToken cancellationToken)
    {
        for (int attempt = 1; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (Directory.Exists(modelDir))
                {
                    Directory.Delete(modelDir, recursive: true);
                }

                Directory.Move(staged, modelDir);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (attempt >= _options.MoveAttempts)
                {
                    throw new VoicePackException(
                        "Windows would not let TalkPrompter put the voice pack in place. Another program, "
                        + "such as a virus scanner, may be using the files. Please try again in a moment.", ex);
                }

                await Task.Delay(_options.MoveRetryDelay, cancellationToken);
            }
        }
    }

    private void EnsureFreeSpace(VoiceLanguage language)
    {
        double factor = language.Kind == VoicePackKind.Vosk ? ZipSpaceFactor : FilesSpaceFactor;
        long needed = (long)(language.DownloadBytes * factor);

        long available;
        try
        {
            available = _options.FreeSpace(_modelsDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return; // unknown free space: let the download try
        }

        if (available < needed)
        {
            throw new VoicePackException(
                $"Not enough disk space. The {language.EnglishName} voice pack needs about {needed / BytesPerMegabyte:F0} MB free.");
        }
    }

    /// <summary>Staging folders are only left behind by a crash or power loss.</summary>
    private void RemoveStagingFolders()
    {
        foreach (string dir in Directory.GetDirectories(_modelsDirectory, StagingPrefix + "*"))
        {
            TryDeleteDirectory(dir);
        }
    }

    private static void TryDeleteDirectory(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A leftover staging folder is harmless and removed next time.
        }
    }
}
