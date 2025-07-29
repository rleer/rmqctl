namespace RmqCli.Configuration;

public class FileConfig
{
    public string MessageDelimiter { get; set; } = Environment.NewLine;
    public int MessagesPerFile { get; set; }
}