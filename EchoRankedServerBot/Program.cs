using EchoRankedServerBot.Extensions;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);

ConfigurationExtensions.Initialize(builder.Configuration);

builder.Services.AddEchoRankedBot(builder.Configuration);

var host = builder.Build();
await host.RunAsync();
