namespace EchoRankedServerBot.Configuration;

public class StreamingOptions
{
    public const string SectionName = "Streaming";

    public string BaseUrl { get; set; } = "https://g.echovrce.com";
    public int ReconnectSeconds { get; set; } = 2;
}
