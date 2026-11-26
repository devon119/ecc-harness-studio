using ECCSELECTION.Web.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ECCSELECTION.Tests.Helpers;

public sealed class TestDbContextScope : IDisposable
{
    private readonly SqliteConnection _connection;
    public AppDbContext Context { get; }

    public TestDbContextScope()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        Context = new AppDbContext(options);
        Context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        Context.Dispose();
        _connection.Dispose();
    }
}
