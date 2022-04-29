using System.Xml.Linq;

namespace UpdatR.IntegrationTests;

internal static class FileCreationUtils
{
    public static async Task<string> CreateSlnAsync(
        string path,
        string projectName,
        string projectPath
    )
    {
        var content = GetResource("UpdatR.IntegrationTests.Resources.Templates.Dummy.sln");

        content = content
            .Replace("<PROJECTNAME>", projectName)
            .Replace(
                "<PROJECTPATH>",
                Path.GetRelativePath(new FileInfo(path).DirectoryName!, projectPath)
            );

        await File.WriteAllTextAsync(path, content);

        return await File.ReadAllTextAsync(path)!;
    }

    public static async Task<string> CreateToolsConfigAsync(
        string path,
        string packageId,
        string version,
        string command
    )
    {
        var content = GetResource(
            "UpdatR.IntegrationTests.Resources.Templates..config.dotnet-tools.json"
        );

        content = content
            .Replace("<PACKAGEID>", packageId)
            .Replace("<VERSION>", version)
            .Replace("<COMMAND>", command);

        await File.WriteAllTextAsync(path, content);

        return await File.ReadAllTextAsync(path)!;
    }

    // TODO: Generate json on-the-fly instead
    public static async Task<string> CreateToolsConfigAsync(
        string path,
        string packageId,
        string version,
        string command,
        string packageId2,
        string version2,
        string command2
    )
    {
        var content = GetResource(
            "UpdatR.IntegrationTests.Resources.Templates..config.dotnet-tools2.json"
        );

        content = content
            .Replace("<PACKAGEID>", packageId)
            .Replace("<VERSION>", version)
            .Replace("<COMMAND>", command);

        content = content
            .Replace("<PACKAGEID2>", packageId2)
            .Replace("<VERSION2>", version2)
            .Replace("<COMMAND2>", command2);

        await File.WriteAllTextAsync(path, content);

        return await File.ReadAllTextAsync(path)!;
    }

    public static void CreateNuGetConfig(string path, bool addNuGetOrg = false)
    {
        var nugetContent = GetResource("UpdatR.IntegrationTests.Resources.Templates.nuget.config");

        var doc = XDocument.Parse(nugetContent);

        var packageSources = doc.Element("configuration")!.Element("packageSources")!;

        if (addNuGetOrg)
        {
            var nugetOrg = new XElement("add");

            nugetOrg.Add(
                new XAttribute("key", "nuget.org"),
                new XAttribute("value", "https://api.nuget.org/v3/index.json")
            );

            packageSources.Add(nugetOrg);
        }

        var local = new XElement("add");

        local.Add(
            new XAttribute("key", "local"),
            new XAttribute("value", Paths.Temporary.Packages)
        );

        packageSources.Add(local);

        doc.Save(path);
    }

    public static Task<string> CreateTempCsprojAsync(
        string path,
        params KeyValuePair<string, string>[] packages
    )
    {
        return CreateTempCsprojAsync(path, "net6.0", packages);
    }

    public static async Task<string> CreateTempCsprojAsync(
        string path,
        string tfm = "net6.0",
        params KeyValuePair<string, string>[] packages
    )
    {
        var csprojStr = GetResource("UpdatR.IntegrationTests.Resources.Templates.Dummy.App.csproj");

        var csproj = XDocument.Parse(csprojStr);

        csproj
            .Element("Project")!
            .Element("PropertyGroup")!
            .Element("TargetFramework")!
            .SetValue(tfm);

        var itemGroup = csproj.Element("Project")!.Element("ItemGroup")!;

        foreach (var package in packages)
        {
            var packageReference = new XElement("PackageReference");

            packageReference.Add(
                new XAttribute("Include", package.Key),
                new XAttribute("Version", package.Value)
            );

            itemGroup.Add(packageReference);
        }

        csproj.Save(path);

        return await File.ReadAllTextAsync(path)!;
    }

    private static string GetResource(string resourceName)
    {
        using var stream = typeof(FileCreationUtils).Assembly.GetManifestResourceStream(
            resourceName
        );

        if (stream is null)
        {
            throw new InvalidOperationException($"'{resourceName} is not an embedded resource.");
        }

        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }
}
