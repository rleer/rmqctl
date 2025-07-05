namespace rmqctl.Configuration;

public class FileConfig
{
    public string MessageDelimiter { get; set; } = "---";
    public int MessagesPerFile { get; set; }
}