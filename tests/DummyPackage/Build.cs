using System.Reflection;
using Bullseye;
using McMaster.Extensions.CommandLineUtils;
using NuGet.Versioning;
using static Bullseye.Targets;
using static SimpleExec.Command;

using var app = new CommandLineApplication() { UsePagerForHelpText = false };
app.HelpOption();
var version = app.Option<string>("-v|--version <version>", "Version of the package.", CommandOptionType.SingleValue);
var packageIdOption = app.Option<string>("-id|--packageId <package-id>", "Name of the package.", CommandOptionType.SingleValue);
var targetFrameworkOption = app.Option<string>("-tfm|--target-framework <tfm>", "Target framework of the package.", CommandOptionType.SingleValue, conf => conf.DefaultValue = "net6.0");

// translate from Bullseye to McMaster.Extensions.CommandLineUtils
app.Argument("targets", "A list of targets to run or list. If not specified, the \"default\" target will be run, or all targets will be listed.", true);
foreach (var (aliases, description) in Options.Definitions)
{
    _ = app.Option(string.Join("|", aliases), description, CommandOptionType.NoValue);
}

app.OnExecuteAsync(async _ =>
{
     var msbuild = Assembly
        .GetEntryAssembly()!
        .GetCustomAttribute<MsBuildConfigurationAttribute>()!;

    // translate from McMaster.Extensions.CommandLineUtils to Bullseye
    var targets = app.Arguments[0].Values.OfType<string>();
    var options = new Options(Options.Definitions.Select(d => (d.Aliases[0], app.Options.Single(o => d.Aliases.Contains($"--{o.LongName}")).HasValue())));

    var nuGetVersion = NuGetVersion.Parse(version.Value());
    var packageId = packageIdOption.Value();
    var tfm = targetFrameworkOption.Value();

    Target("default", async () =>
    {
        await Console.Out.WriteLineAsync($"version:   {nuGetVersion}");
        await Console.Out.WriteLineAsync($"packageId: {packageId}");
        await Console.Out.WriteLineAsync($"tfm:       {tfm}");

        await RunAsync(
            "dotnet",
            $"pack --configuration Release -p:version=\"{nuGetVersion}\" -p:packageId=\"{packageId}\" -p:targetFramework=\"{tfm}\" {msbuild.ProjectDir} --output ."
        );
    });

    Target("push", DependsOn("default"), async () =>
    {
        await RunAsync(
            "dotnet",
            $"nuget push .\\{packageId}.{nuGetVersion}.nupkg --api-key AzureDevOps --source https://pkgs.dev.azure.com/curium/0329093f-bb7c-41c0-9e4c-893f37483900/_packaging/broken-parts/nuget/v3/index.json --interactive"
        );
    });

    await RunTargetsAndExitAsync(targets, options);
});

return await app.ExecuteAsync(args);

[AttributeUsage(AttributeTargets.Assembly, Inherited = false, AllowMultiple = false)]
sealed class MsBuildConfigurationAttribute : Attribute
{
    public string ProjectDir { get; }

    public MsBuildConfigurationAttribute(string projectDir)
    {
        ProjectDir = projectDir;
    }
}