using System.Text.Json;
using EchoRankedImageProcessingTest.Utilities;
using EchoRankedServerBot.Configuration;
using EchoRankedServerBot.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

internal class Program
{
    public static void Main(string[] args)
    {
        var projectDirectory = FindProjectDirectory();
        var builder = Host.CreateApplicationBuilder(args);
        builder.Configuration.AddUserSecrets(typeof(Program).Assembly, optional: true);
        // Test-specific settings override the shared bot secrets.
        builder.Configuration
            .AddJsonFile(Path.Combine(projectDirectory, "scoreboard-settings.json"), optional: false)
            .AddEnvironmentVariables()
            .AddCommandLine(args);
        var imagePath = Path.GetFullPath(
            builder.Configuration["ImageTest:TemplatePath"] ?? "Assets/original2.png", projectDirectory);
        var outputPath = GetNumberedOutputPath(Path.GetFullPath(
            builder.Configuration["ImageTest:OutputPath"] ?? "Assets/output.png", projectDirectory));
        EchoRankedServerBot.Extensions.ConfigurationExtensions.Initialize(builder.Configuration);
        builder.Services.Configure<BotOptions>(builder.Configuration.GetSection(BotOptions.SectionName));
        builder.Services.PostConfigure<BotOptions>(options =>
        {
            if (!string.IsNullOrWhiteSpace(options.ScoreboardFontPath))
                options.ScoreboardFontPath = Path.GetFullPath(options.ScoreboardFontPath, projectDirectory);
        });
        builder.Services.AddSingleton<ScoreboardImageService>();
        var host = builder.Build();
        var imageService = host.Services.GetRequiredService<ScoreboardImageService>();
        var echoMatchData = MockMatchService.CreateMockEchoVrApiSession();
        echoMatchData.OrangeRoundScore = builder.Configuration.GetValue<int?>("ImageTest:OrangeRoundScore") ?? echoMatchData.OrangeRoundScore;
        echoMatchData.BlueRoundScore = builder.Configuration.GetValue<int?>("ImageTest:BlueRoundScore") ?? echoMatchData.BlueRoundScore;
        var playerList = echoMatchData.Teams!.SelectMany(x => x.Players!).ToList();
        var memoryStream = imageService.GenerateScoreboardAsync(imagePath, echoMatchData, playerList.GenerateMockPlayerScores(), "TEST-IMAGE-MATCH");
        if (memoryStream == null)
        {
            Console.WriteLine("Failed to create scoreboard image");
            return;
        }
        memoryStream.Position = 0;
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        using var filestream = File.Create(outputPath);
        memoryStream.CopyTo(filestream);
        filestream.Close();
        memoryStream.Close();
        Console.WriteLine("Image has been saved to " + outputPath);
    }

    private static string FindProjectDirectory()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "EchoRankedImageProcessingTest.csproj")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("Run the image test from a build inside the EchoRankedImageProcessingTest project.");
    }

    private static string GetNumberedOutputPath(string outputPath)
    {
        var directory = Path.GetDirectoryName(outputPath)!;
        var name = Path.GetFileNameWithoutExtension(outputPath);
        var extension = Path.GetExtension(outputPath);
        var currentVersion = Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, $"{name}-*{extension}")
                .Select(file => int.TryParse(Path.GetFileNameWithoutExtension(file)[(name.Length + 1)..], out var version) ? version : 0)
                .DefaultIfEmpty(0)
                .Max()
            : 0;

        return Path.Combine(directory, $"{name}-{currentVersion + 1}{extension}");
    }
}
