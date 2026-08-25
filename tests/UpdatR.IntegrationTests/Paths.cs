namespace UpdatR.IntegrationTests;

internal static class Paths
{
    public static string Packages =>
        Path.Combine(AppContext.BaseDirectory, "Resources", "Packages");

    public static class Temporary
    {
        public static string Root =>
            Path.Combine(Path.GetTempPath(), "dotnet-updatr", "integrationtests");

        public static string Packages => Path.Combine(Root, "Packages");
    }
}
