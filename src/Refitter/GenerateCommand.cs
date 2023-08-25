using Refitter.Core;
using Spectre.Console;
using Spectre.Console.Cli;
using System.Diagnostics;
using System.Text.Json;

namespace Refitter;

public sealed class GenerateCommand : AsyncCommand<Settings>
{
    public override ValidationResult Validate(CommandContext context, Settings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.OpenApiPath))
            return ValidationResult.Error("Input file is required");

        if (IsUrl(settings.OpenApiPath))
            return base.Validate(context, settings);

        return File.Exists(settings.OpenApiPath)
            ? base.Validate(context, settings)
            : ValidationResult.Error($"File not found - {Path.GetFullPath(settings.OpenApiPath)}");
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var refitGeneratorSettings = new RefitGeneratorSettings
        {
            OpenApiPath = settings.OpenApiPath!,
            Namespace = settings.Namespace ?? "GeneratedCode",
            AddAutoGeneratedHeader = !settings.NoAutoGeneratedHeader,
            AddAcceptHeaders = !settings.NoAcceptHeaders,
            GenerateContracts = !settings.InterfaceOnly,
            ReturnIApiResponse = settings.ReturnIApiResponse,
            UseCancellationTokens = settings.UseCancellationTokens,
            GenerateOperationHeaders = !settings.NoOperationHeaders,
            UseIsoDateFormat = settings.UseIsoDateFormat,
            TypeAccessibility = settings.InternalTypeAccessibility
                ? TypeAccessibility.Internal
                : TypeAccessibility.Public,
            AdditionalNamespaces = settings.AdditionalNamespaces!,
            MultipleInterfaces = settings.MultipleInterfaces,
        };

        var crlf = Environment.NewLine;
        try
        {
            var stopwatch = Stopwatch.StartNew();
            AnsiConsole.MarkupLine($"[green]Refitter v{GetType().Assembly.GetName().Version!}[/]");
            AnsiConsole.MarkupLine($"[green]Support key: {SupportInformation.GetSupportKey()}[/]");
            
            if (!string.IsNullOrWhiteSpace(settings.SettingsFilePath))
            {
                var json = await File.ReadAllTextAsync(settings.SettingsFilePath);
                refitGeneratorSettings = JsonSerializer.Deserialize<RefitGeneratorSettings>(json)!;
                refitGeneratorSettings.OpenApiPath = settings.OpenApiPath!;
            }

            var generator = await RefitGenerator.CreateAsync(refitGeneratorSettings);
            var code = generator.Generate().ReplaceLineEndings();
            AnsiConsole.MarkupLine($"[green]Length: {code.Length} bytes[/]");

            if (!string.IsNullOrWhiteSpace(settings.OutputPath))
            {
                var directory = Path.GetDirectoryName(settings.OutputPath);
                if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
                    Directory.CreateDirectory(directory);
            }

            var outputPath = settings.OutputPath ?? "Output.cs";
            AnsiConsole.MarkupLine($"[green]Output: {Path.GetFullPath(outputPath)}[/]");
            await File.WriteAllTextAsync(outputPath, code);
            await Analytics.LogFeatureUsage(settings);

            AnsiConsole.MarkupLine($"[green]Duration: {stopwatch.Elapsed}{crlf}[/]");
            return 0;
        }
        catch (Exception exception)
        {
            AnsiConsole.MarkupLine($"[red]Error:{crlf}{exception.Message}[/]");
            AnsiConsole.MarkupLine($"[yellow]Stack Trace:{crlf}{exception.StackTrace}[/]");
            await Analytics.LogError(exception, settings);
            return exception.HResult;
        }
    }

    private static bool IsUrl(string openApiPath)
    {
        return Uri.TryCreate(openApiPath, UriKind.Absolute, out var uriResult) &&
               (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps);
    }
}