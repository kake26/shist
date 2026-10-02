namespace Shist;

internal static class Ansi
{
    private static readonly bool On = string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR"));

    private static string C(string code) => On ? code : "";

    public static readonly string R = C("\e[0m");
    public static readonly string Bold = C("\e[1m");
    public static readonly string Dim = C("\e[2m");
    public static readonly string Cyan = C("\e[36m");
    public static readonly string Green = C("\e[32m");
    public static readonly string Yellow = C("\e[33m");
    public static readonly string Magenta = C("\e[35m");
    public static readonly string Blue = C("\e[34m");
}