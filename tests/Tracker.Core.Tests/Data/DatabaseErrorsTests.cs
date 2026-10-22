using System.Net.Sockets;
using Npgsql;
using Tracker.Data;

namespace Tracker.Core.Tests.Data;

public class DatabaseErrorsTests
{
    private static PostgresException Pg(string state) => new("x", "ERROR", "ERROR", state);

    [Theory]
    [InlineData("28P01")] // wrong password
    [InlineData("3D000")] // database does not exist
    [InlineData("57P03")] // starting up
    public void Login_missing_database_and_startup_are_outages(string state) => Assert.True(DatabaseErrors.IsUnavailable(Pg(state)));

    [Theory]
    [InlineData("42P01")] // undefined table
    [InlineData("42P07")] // duplicate table
    [InlineData("23505")] // unique violation
    public void Other_server_answers_are_not(string state) =>
        Assert.False(DatabaseErrors.IsUnavailable(new InvalidOperationException("wrapped", Pg(state))));

    [Fact]
    public void Connection_failures_are_outages()
    {
        Assert.True(DatabaseErrors.IsUnavailable(new NpgsqlException("refused", new SocketException())));
        Assert.True(DatabaseErrors.IsUnavailable(new TimeoutException()));
        Assert.False(DatabaseErrors.IsUnavailable(new InvalidOperationException()));
    }
}
