namespace Tracker;

public static class ErrorText
{
    // Joins an exception's messages outer to inner: the useful detail (JSON line, socket error) is usually inside.
    public static string Chain(Exception ex)
    {
        var messages = new List<string>();
        for (var e = ex; e is not null; e = e.InnerException) messages.Add(e.Message);
        return string.Join(" -> ", messages.Distinct());
    }
}
