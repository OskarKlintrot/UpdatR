using System.Diagnostics.CodeAnalysis;
using System.Xml.Linq;

namespace UpdatR.Update.IntegrationTests;

[UsesVerify]
[SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "Test methods")]
public class UpdateTests
{
    [Theory]
    [InlineData("0.0.1")]
    [InlineData("0.0.2")]
    public async Task Given_UpToDate_When_Update_Then_DoNothing(string version)
    {
        // Arrange
        var temp = Path.Combine(Paths.Temporary.Root, nameof(Given_UpToDate_When_Update_Then_DoNothing));
        var tempCsproj = Path.Combine(temp, "Dummy.App.csproj");
        var tempNuget = Path.Combine(temp, "nuget.config");

        Directory.CreateDirectory(temp);

        var csprojOriginal = await CreateTempCsprojAsync(
            tempCsproj,
            new KeyValuePair<string, string>("Dummy", version));

        CreateNuGetConfig(tempNuget);

        var update = new Update();

        // Act
        var summary = await update.UpdateAsync(tempCsproj);

        // Assert
        await Verify(GetVerifyObjects()).UseParameters(version);

        async IAsyncEnumerable<object> GetVerifyObjects()
        {
            yield return summary.UpdatedPackages;
            yield return csprojOriginal;
            yield return await File.ReadAllTextAsync(tempCsproj);
        }
    }

    private void CreateNuGetConfig(string nuget)
    {
        var nugetContent = GetResource("UpdatR.Update.IntegrationTests.Resources.DummyProject.nuget.config");

        var doc = XDocument.Parse(nugetContent);

        var packageSources = doc
            .Element("configuration")!
            .Element("packageSources")!;

        var add = new XElement("add");

        add.Add(
            new XAttribute("key", "local"),
            new XAttribute("value", Paths.Temporary.Packages));

        packageSources.Add(add);

        doc.Save(nuget);
    }

    private async Task<string> CreateTempCsprojAsync(string tempCsproj, params KeyValuePair<string, string>[] packages)
    {
        var csprojStr = GetResource("UpdatR.Update.IntegrationTests.Resources.DummyProject.Dummy.App.csproj");

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

        csproj.Save(tempCsproj);

        return await File.ReadAllTextAsync(tempCsproj)!;
    }

    private string GetResource(string resourceName)
    {
        using var stream = GetType().Assembly.GetManifestResourceStream(resourceName);

        if (stream is null)
        {
            throw new InvalidOperationException($"'{resourceName} is not an embedded resource.");
        }

        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }
}
