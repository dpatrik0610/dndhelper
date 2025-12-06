using dndhelper.Database;
using dndhelper.Services.Interfaces;
using Serilog;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace dndhelper.Services
{
    public class BackupService : IBackupService
    {
        private readonly MongoDbContext _dbContext;
        private readonly ILogger _logger;
        private readonly string _mongodumpPath;
        private readonly string _mongorestorePath;

        public BackupService(MongoDbContext dbContext, ILogger logger)
        {
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _mongodumpPath = ResolveMongoToolPath("MONGODUMP_PATH", "mongodump");
            _mongorestorePath = ResolveMongoToolPath("MONGORESTORE_PATH", "mongorestore", _mongodumpPath);
        }

        public async Task<BackupResult> ExportCollectionAsync(string collectionName, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(collectionName))
                throw new ArgumentNullException(nameof(collectionName));

            var archiveStream = new MemoryStream();
            var fileName = $"{collectionName}-dump-{DateTime.UtcNow:yyyyMMddHHmmss}.gz";

            var psi = new ProcessStartInfo
            {
                FileName = _mongodumpPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };

            psi.ArgumentList.Add($"--uri={_dbContext.ConnectionString}");
            psi.ArgumentList.Add($"--db={_dbContext.DatabaseName}");
            psi.ArgumentList.Add($"--collection={collectionName}");
            psi.ArgumentList.Add("--archive");
            psi.ArgumentList.Add("--gzip");

            using var process = new Process { StartInfo = psi };

            try
            {
                _logger.Information("Starting mongodump for collection {Collection} on database {Database} using {Executable}", collectionName, _dbContext.DatabaseName, _mongodumpPath);
                process.Start();
            }
            catch (Exception ex)
            {
                _logger.Error(ex,
                    "Failed to start mongodump process. FileName={FileName}, PATH={Path}",
                    _mongodumpPath, Environment.GetEnvironmentVariable("PATH"));
                throw new InvalidOperationException("mongodump is not available on the host. Install MongoDB Database Tools or set MONGODUMP_PATH.", ex);
            }

            await using var _ = cancellationToken.Register(() =>
            {
                if (!process.HasExited)
                    process.Kill();
            });

            var readStdErrTask = process.StandardError.ReadToEndAsync();
            var copyStdOutTask = process.StandardOutput.BaseStream.CopyToAsync(archiveStream, cancellationToken);

            await Task.WhenAll(copyStdOutTask, process.WaitForExitAsync(cancellationToken));
            var stdErr = await readStdErrTask;

            if (process.ExitCode != 0)
            {
                _logger.Error("mongodump failed for collection {Collection}. ExitCode: {ExitCode}. Error: {Error}", collectionName, process.ExitCode, stdErr);
                var detail = string.IsNullOrWhiteSpace(stdErr) ? $"mongodump failed for collection '{collectionName}'." : stdErr.Trim();
                throw new InvalidOperationException(detail);
            }

            archiveStream.Position = 0;
            _logger.Information("mongodump completed for collection {Collection} ({Bytes} bytes)", collectionName, archiveStream.Length);

            return new BackupResult(archiveStream, fileName, "application/gzip");
        }

        public async Task RestoreCollectionAsync(string collectionName, Stream archiveStream, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(collectionName))
                throw new ArgumentNullException(nameof(collectionName));

            if (archiveStream == null || !archiveStream.CanRead)
                throw new ArgumentException("Archive stream is not readable.", nameof(archiveStream));

            var psi = new ProcessStartInfo
            {
                FileName = _mongorestorePath,
                RedirectStandardInput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };

            psi.ArgumentList.Add($"--uri={_dbContext.ConnectionString}");
            psi.ArgumentList.Add($"--nsInclude={_dbContext.DatabaseName}.{collectionName}");
            psi.ArgumentList.Add("--archive");
            psi.ArgumentList.Add("--gzip");
            psi.ArgumentList.Add("--drop");

            using var process = new Process { StartInfo = psi };

            try
            {
                _logger.Information("Starting mongorestore for collection {Collection} on database {Database} using {Executable}", collectionName, _dbContext.DatabaseName, _mongorestorePath);
                process.Start();
            }
            catch (Exception ex)
            {
                _logger.Error(ex,
                    "Failed to start mongorestore process. FileName={FileName}, PATH={Path}",
                    _mongorestorePath, Environment.GetEnvironmentVariable("PATH"));
                throw new InvalidOperationException("mongorestore is not available on the host. Install MongoDB Database Tools or set MONGORESTORE_PATH.", ex);
            }

            await using var _ = cancellationToken.Register(() =>
            {
                if (!process.HasExited)
                    process.Kill();
            });

            var stdErrTask = process.StandardError.ReadToEndAsync();

            try
            {
                await archiveStream.CopyToAsync(process.StandardInput.BaseStream, cancellationToken);
                await process.StandardInput.FlushAsync();
                process.StandardInput.Close();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error writing archive stream to mongorestore stdin for collection {Collection}", collectionName);
                throw;
            }

            await process.WaitForExitAsync(cancellationToken);
            var stdErr = await stdErrTask;

            if (process.ExitCode != 0)
            {
                _logger.Error("mongorestore failed for collection {Collection}. ExitCode: {ExitCode}. Error: {Error}", collectionName, process.ExitCode, stdErr);
                var detail = string.IsNullOrWhiteSpace(stdErr) ? $"mongorestore failed for collection '{collectionName}'." : stdErr.Trim();
                throw new InvalidOperationException(detail);
            }

            _logger.Information("mongorestore completed for collection {Collection}", collectionName);
        }

        private static string ResolveMongoToolPath(string envVarName, string toolName, string? siblingToolPath = null)
        {
            var envPath = Environment.GetEnvironmentVariable(envVarName);
            if (!string.IsNullOrWhiteSpace(envPath) && File.Exists(envPath))
            {
                return envPath;
            }

            if (!string.IsNullOrWhiteSpace(siblingToolPath) && Path.IsPathRooted(siblingToolPath))
            {
                var dir = Path.GetDirectoryName(siblingToolPath);
                if (!string.IsNullOrWhiteSpace(dir))
                {
                    var ext = Path.GetExtension(siblingToolPath);
                    var candidate = Path.Combine(dir, string.IsNullOrWhiteSpace(ext) ? toolName : $"{toolName}{ext}");
                    if (File.Exists(candidate))
                        return candidate;
                }
            }

            return toolName;
        }
    }
}
