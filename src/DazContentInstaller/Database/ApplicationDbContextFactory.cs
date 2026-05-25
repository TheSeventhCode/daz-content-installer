using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DazContentInstaller.Database;

public class ApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
        var databasePath = args.Length > 0
            ? args[0]
            : System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "database.design.db");

        optionsBuilder.UseSqlite($"Data Source={databasePath}");
        return new ApplicationDbContext(optionsBuilder.Options);
    }
}
