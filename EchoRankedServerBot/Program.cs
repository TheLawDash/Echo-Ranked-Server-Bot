using EchoRankedServerBot.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);

#if DEBUG
builder.Configuration.AddUserSecrets(typeof(Program).Assembly, optional: true);
#endif

EchoRankedServerBot.Extensions.ConfigurationExtensions.Initialize(builder.Configuration);

builder.Services.AddEchoRankedBot(builder.Configuration);

var host = builder.Build();
await host.RunAsync();
