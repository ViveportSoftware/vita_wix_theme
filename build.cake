#addin "nuget:?package=Cake.Git&version=0.22.0"

//////////////////////////////////////////////////////////////////////
// ARGUMENTS
//////////////////////////////////////////////////////////////////////

var configuration = Argument("configuration", "Debug");
var revision = EnvironmentVariable("BUILD_NUMBER") ?? Argument("revision", "9999");
var target = Argument("target", "Default");


//////////////////////////////////////////////////////////////////////
// PREPARATION
//////////////////////////////////////////////////////////////////////

// Define git commit id
var commitId = "SNAPSHOT";

// Define product name and version
var product = "Htc.Vita.Wix.Themes";
var companyName = "HTC";
var version = "0.9.0";
var semanticVersion = $"{version}.{revision}";
var ciVersion = $"{version}.0";
var buildVersion = "Release".Equals(configuration) ? semanticVersion : $"{ciVersion}-CI{revision}";
var nugetTags = new [] {"htc", "vita", "wix", "themes"};

// Define copyright
var copyright = $"Copyright © 2021 - {DateTime.Now.Year}";

// Define timestamp for signing
var lastSignTimestamp = DateTime.Now;
var signIntervalInMilli = 1000 * 5;

// Define path
var solutionFile = File($"./source/{product}.sln");
var testBundleProjectFile = File($"./source/{product}.TestBundle/{product}.TestBundle.wixproj");

// Define directories.
var distDir = Directory("./dist");
var tempDir = Directory("./temp");
var generatedDir = Directory("./source/generated");
var packagesDir = Directory("./source/packages");
var nugetDir = Directory("./dist") + Directory(configuration) + Directory("nuget");
var homeDir = Directory(EnvironmentVariable("USERPROFILE") ?? EnvironmentVariable("HOME"));

// Define signing key, password and timestamp server
var signKeyEnc = EnvironmentVariable("SIGNKEYENC") ?? "NOTSET";
var signPass = EnvironmentVariable("SIGNPASS") ?? "NOTSET";
var signSha1Uri = new Uri("http://timestamp.digicert.com");
var signSha256Uri = new Uri("http://timestamp.digicert.com");

// Define nuget push source and key
var nugetApiKey = EnvironmentVariable("NUGET_PUSH_TOKEN") ?? EnvironmentVariable("NUGET_APIKEY") ?? "NOTSET";
var nugetSource = EnvironmentVariable("NUGET_PUSH_PATH") ?? EnvironmentVariable("NUGET_SOURCE") ?? "NOTSET";


//////////////////////////////////////////////////////////////////////
// TASKS
//////////////////////////////////////////////////////////////////////

Task("Fetch-Git-Commit-ID")
    .ContinueOnError()
    .Does(() =>
{
    var lastCommit = GitLogTip(MakeAbsolute(Directory(".")));
    commitId = lastCommit.Sha;
});

Task("Display-Config")
    .IsDependentOn("Fetch-Git-Commit-ID")
    .Does(() =>
{
    Information($"Build target:        {target}");
    Information($"Build configuration: {configuration}");
    Information($"Build commitId:      {commitId}");
    Information($"Build version:       {buildVersion}");
});

Task("Clean-Workspace")
    .IsDependentOn("Display-Config")
    .Does(() =>
{
    CleanDirectory(distDir);
    CleanDirectory(tempDir);
    CleanDirectory(generatedDir);
    CleanDirectory(packagesDir);
});

Task("Restore-NuGet-Packages")
    .IsDependentOn("Clean-Workspace")
    .Does(() =>
{
    NuGetRestore(new FilePath($"./source/{product}.sln"));
});

Task("Generate-AssemblyInfo")
    .IsDependentOn("Restore-NuGet-Packages")
    .Does(() =>
{
    CreateDirectory(generatedDir);
    var assemblyVersion = "Release".Equals(configuration) ? semanticVersion : ciVersion;
    CreateAssemblyInfo(
            new FilePath("./source/generated/SharedAssemblyInfo.cs"),
            new AssemblyInfoSettings
            {
                    Company = companyName,
                    Copyright = copyright,
                    FileVersion = assemblyVersion,
                    InformationalVersion = assemblyVersion,
                    Product = $"{product} : {commitId}",
                    Version = version
            }
    );
});

Task("Build-Wix-Themes-Test")
    .IsDependentOn("Generate-AssemblyInfo")
    .Does(() =>
{
    // Use MSBuild
    MSBuild(
            testBundleProjectFile,
            settings => settings.SetConfiguration(configuration)
    );
});

Task("Build-NuGet-Package")
    .IsDependentOn("Build-Wix-Themes-Test")
    .Does(() =>
{
    CreateDirectory(nugetDir);
    var nuGetPackSettings = new NuGetPackSettings
    {
            Id = product,
            Version = buildVersion,
            Authors = new[] {"HTC"},
            Description = $"[CommitId: {commitId}]",
            Copyright = copyright,
            Tags = nugetTags,
            RequireLicenseAcceptance= false,
            Files = new []
            {
                    new NuSpecContent
                    {
                            Source = "**",
                            Target = "content"
                    }
            },
            Properties = new Dictionary<string, string>
            {
                    {"Configuration", configuration}
            },
            BasePath = tempDir + Directory(configuration) + Directory($"{product}/bin/net45/Resources"),
            OutputDirectory = nugetDir
    };

    NuGetPack(nuGetPackSettings);
});

Task("Publish-NuGet-Package")
    .WithCriteria(() => "Release".Equals(configuration) && !"NOTSET".Equals(nugetApiKey) && !"NOTSET".Equals(nugetSource))
    .IsDependentOn("Build-NuGet-Package")
    .Does(() =>
{
    NuGetPush(
            new FilePath($"./dist/{configuration}/nuget/{product}.{buildVersion}.nupkg"),
            new NuGetPushSettings
            {
                    ApiKey = nugetApiKey,
                    Source = nugetSource
            }
    );
});


//////////////////////////////////////////////////////////////////////
// TASK TARGETS
//////////////////////////////////////////////////////////////////////

Task("Default")
    .IsDependentOn("Build-NuGet-Package");

//////////////////////////////////////////////////////////////////////
// EXECUTION
//////////////////////////////////////////////////////////////////////

RunTarget(target);
