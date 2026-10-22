namespace Archiver;

public static class ErrorText
{
    // Joins an exception's messages outer to inner: config errors keep the useful detail (JSON line/position) inside.
    public static string Chain(Exception ex)
    {
        var messages = new List<string>();
        for (var e = ex; e is not null; e = e.InnerException) messages.Add(e.Message);
        return string.Join(" -> ", messages.Distinct());
    }
}
