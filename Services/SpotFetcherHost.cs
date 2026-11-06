using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace dndhelper.Services
{
    public class SpotFetcherHost : BackgroundService
    {
        private static readonly TimeSpan RestartDelay = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan BusyPortDelay = TimeSpan.FromSeconds(30);

        private readonly ILogger<SpotFetcherHost> _logger;
        private readonly string _dir;
        private readonly string _venv;
        private readonly string _python;
        private readonly Uri _url;

        public SpotFetcherHost(IConfiguration config, IHostEnvironment env, ILogger<SpotFetcherHost> logger)
        {
            _logger = logger;
            _dir = config.GetValue<string>("SpotFetcher:Dir") ?? Path.Combine(env.ContentRootPath, "spotfetcher");
            _venv = config.GetValue<string>("SpotFetcher:Venv")
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SpotFetcher", "venv");
            _python = config.GetValue<string>("SpotFetcher:Python") ?? (OperatingSystem.IsWindows() ? "python" : "python3");
            _url = new Uri(config.GetValue<string>("SpotFetcher:Url") ?? "http://127.0.0.1:47821");
        }

        private string VenvPython => OperatingSystem.IsWindows()
            ? Path.Combine(_venv, "Scripts", "python.exe")
            : Path.Combine(_venv, "bin", "python");

        protected override async Task ExecuteAsync(CancellationToken ct)
        {
            if (!File.Exists(Path.Combine(_dir, "requirements.txt")))
            {
                _logger.LogWarning("SpotFetcher is off: no helper found in {Dir}", _dir);
                return;
            }

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    if (await PortTakenAsync(ct))
                    {
                        await Task.Delay(BusyPortDelay, ct);
                        continue;
                    }
                    await PrepareAsync(ct);
                    _logger.LogInformation("Starting SpotFetcher");
                    var code = await RunAsync(VenvPython, "-m spotfetcher", ct);
                    _logger.LogWarning("SpotFetcher stopped with exit code {Code}; restarting", code);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "SpotFetcher could not start");
                }
                await Task.Delay(RestartDelay, ct);
            }
        }

        private async Task<bool> PortTakenAsync(CancellationToken ct)
        {
            using var tcp = new TcpClient();
            try
            {
                await tcp.ConnectAsync(_url.Host, _url.Port, ct).AsTask().WaitAsync(TimeSpan.FromSeconds(1), ct);
                return true;
            }
            catch (Exception ex) when (ex is SocketException or TimeoutException)
            {
                return false;
            }
        }

        private async Task PrepareAsync(CancellationToken ct)
        {
            if (!File.Exists(VenvPython))
            {
                _logger.LogInformation("Setting up SpotFetcher's Python environment in {Venv}", _venv);
                if (await RunAsync(_python, $"-m venv \"{_venv}\"", ct) != 0) throw new InvalidOperationException($"'{_python} -m venv' failed. Is Python 3.10+ installed?");
            }
            var pip = await RunAsync(VenvPython, "-m pip install --quiet --disable-pip-version-check --upgrade -r requirements.txt", ct);
            if (pip != 0) _logger.LogWarning("Updating SpotFetcher's packages failed (exit {Code}); starting with what is installed", pip);
        }

        private async Task<int> RunAsync(string file, string args, CancellationToken ct)
        {
            var info = new ProcessStartInfo(file, args)
            {
                WorkingDirectory = _dir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            info.Environment["PYTHONUNBUFFERED"] = "1";
            using var process = new Process { StartInfo = info };
            process.OutputDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) _logger.LogInformation("SpotFetcher: {Line}", e.Data); };
            process.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) _logger.LogWarning("SpotFetcher: {Line}", e.Data); };
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            try
            {
                await process.WaitForExitAsync(ct);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                throw;
            }
            return process.ExitCode;
        }
    }
}
