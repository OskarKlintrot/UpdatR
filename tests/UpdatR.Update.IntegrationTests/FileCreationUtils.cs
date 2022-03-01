using System.Xml.Linq;

namespace UpdatR.Update.IntegrationTests;

internal static class FileCreationUtils
{
    public static async Task<string> CreateSlnAsync(string path, string projectName, string projectPath)
    {
        var content = GetResource("UpdatR.Update.IntegrationTests.Resources.Templates.Dummy.sln");

        content = content
            .Replace("<PROJECTNAME>", projectName)
            .Replace("<PROJECTPATH>", Path.GetRelativePath(new FileInfo(path).DirectoryName!, projectPath));

        await File.WriteAllTextAsync(path, content);

        return await File.ReadAllTextAsync(path)!;
    }

    public static async Task<string> CreateToolsConfigAsync(string path, string packageId, string version, string command)
    {
        var content = GetResource("UpdatR.Update.IntegrationTests.Resources.Templates..config.dotnet-tools.json");

        content = content
            .Replace("<PACKAGEID>", packageId)
            .Replace("<VERSION>", version)
            .Replace("<COMMAND>", command);

        await File.WriteAllTextAsync(path, content);

        return await File.ReadAllTextAsync(path)!;
    }

    public static void CreateNuGetConfig(string path)
    {
        var nugetContent = GetResource("UpdatR.Update.IntegrationTests.Resources.Templates.nuget.config");

        var doc = XDocument.Parse(nugetContent);

        var packageSources = doc
            .Element("configuration")!
            .Element("packageSources")!;

        var add = new XElement("add");

        add.Add(
            new XAttribute("key", "local"),
            new XAttribute("value", Paths.Temporary.Packages));

        packageSources.Add(add);

        doc.Save(path);
    }

    public static async Task<string> CreateTempCsprojAsync(string path, params KeyValuePair<string, string>[] packages)
    {
        var csprojStr = GetResource("UpdatR.Update.IntegrationTests.Resources.Templates.Dummy.App.csproj");

        var csproj = XDocument.Parse(csprojStr);

        var itemGroup = csproj.Element("Project")!.Element("ItemGroup")!;

        foreach (var package in packages)
        {
            var packageReference = new XElement("PackageReference");

            packageReference.Add(
                new XAttribute("Include", package.Key),
                new XAttribute("Version", package.Value));

            itemGroup.Add(packageReference);
        }

        csproj.Save(path);

        return await File.ReadAllTextAsync(path)!;
    }

    private static string GetResource(string resourceName)
    {
        using var stream = typeof(FileCreationUtils).Assembly.GetManifestResourceStream(resourceName);

        if (stream is null)
        {
            throw new InvalidOperationException($"'{resourceName} is not an embedded resource.");
        }

        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }
}
