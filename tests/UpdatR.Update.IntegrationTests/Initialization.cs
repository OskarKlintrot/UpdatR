using System.Runtime.CompilerServices;

namespace UpdatR.Update.IntegrationTests;

public static class Initialization
{
    [ModuleInitializer]
    public static void Run()
    {
        if (Directory.Exists(Paths.Temporary.Root))
        {
            Directory.Delete(Paths.Temporary.Root, true);
        }

        Directory.CreateDirectory(Paths.Temporary.Root);
        Directory.CreateDirectory(Paths.Temporary.Packages);

        CopyPackages(Paths.Temporary.Packages);
    }

    private static void CopyPackages(string source)
    {
        if (!Directory.Exists(source))
        {
            throw new ArgumentException("Path not found.", nameof(source));
        }

        foreach (var package in Directory.EnumerateFiles(Paths.Packages, "*.nupkg"))
        {
            File.Copy(
                package,
                Path.Combine(source, new FileInfo(package).Name),
                overwrite: true);
        }
    }
}
