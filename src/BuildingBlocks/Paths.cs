namespace BuildingBlocks;

internal static class Paths
{
    public static string Temporary => Path.Combine(Path.GetTempPath(), "dotnet-updatr");
}
