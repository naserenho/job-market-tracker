using System.Net.Sockets;
using Npgsql;

namespace Tracker.Data;

public static class DatabaseErrors
{
    // Not running, wrong port, timed out, starting up or refusing the login: the "start Docker" message applies.
    // Other server errors (bad SQL, a missing column, a unique violation) are not outages.
    public static bool IsUnavailable(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
            switch (e)
            {
                case PostgresException pg when pg.SqlState.StartsWith("28") || pg.SqlState.StartsWith("3D") || pg.SqlState.StartsWith("57P"):
                    return true;
                case PostgresException:
                    return false;
                case NpgsqlException or SocketException or TimeoutException:
                    return true;
            }
        return false;
    }
}
