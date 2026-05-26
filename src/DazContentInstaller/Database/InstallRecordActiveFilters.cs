using System;
using System.Linq.Expressions;

namespace DazContentInstaller.Database;

public static class InstallRecordActiveFilters
{
    public static bool IsOnDiskActiveStatus(InstallRecordStatus status) =>
        status is InstallRecordStatus.Installed
            or InstallRecordStatus.Replaced
            or InstallRecordStatus.Skipped;

    public static bool IsActiveOnDiskOwner(bool hasBeenOverriden, InstallRecordStatus status) =>
        !hasBeenOverriden && IsOnDiskActiveStatus(status);

    public static Expression<Func<InstallRecord, bool>> IsActiveOnDiskOwnerExpression { get; } = record =>
        !record.HasBeenOverriden &&
        (record.InstallRecordStatus == InstallRecordStatus.Installed ||
         record.InstallRecordStatus == InstallRecordStatus.Replaced ||
         record.InstallRecordStatus == InstallRecordStatus.Skipped);
}
