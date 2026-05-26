using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DazContentInstaller.Database;
using Microsoft.EntityFrameworkCore;

namespace DazContentInstaller.Services;

public interface IInterruptedInstallRecoveryService
{
    Task RecoverInterruptedInstallsAsync(CancellationToken cancellationToken = default);
}

public sealed class InterruptedInstallRecoveryService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory) : IInterruptedInstallRecoveryService
{
    public const string InterruptedInstallMessage = "Installation was interrupted.";

    public async Task RecoverInterruptedInstallsAsync(CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var interruptedCount = await dbContext.Archives
            .CountAsync(x => x.Status == ArchiveStatus.Loading || x.Status == ArchiveStatus.Installing,
                cancellationToken)
            .ConfigureAwait(false);

        if (interruptedCount == 0)
            return;

        var completedAt = DateTime.Now;
        await dbContext.Archives
            .Where(x => x.Status == ArchiveStatus.Loading || x.Status == ArchiveStatus.Installing)
            .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.Status, ArchiveStatus.Error)
                    .SetProperty(x => x.ErrorMessage, InterruptedInstallMessage)
                    .SetProperty(x => x.InstallCompletedAt, completedAt),
                cancellationToken)
            .ConfigureAwait(false);
    }
}
