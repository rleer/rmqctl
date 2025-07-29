using RmqCli.Models;

namespace RmqCli.MessageFormatter;

public interface IMessageFormatterFactory
{
    IMessageFormatter CreateFormatter(OutputFormat format);
}

public class MessageFormatterFactory : IMessageFormatterFactory
{
    private readonly IEnumerable<IMessageFormatter> _formatters;

    public MessageFormatterFactory(IEnumerable<IMessageFormatter> formatters)
    {
        _formatters = formatters;
    }

    public IMessageFormatter CreateFormatter(OutputFormat format)
    {
        return format switch
        {
            OutputFormat.Text => _formatters.First(formatter => formatter is TextMessageFormatter),
            OutputFormat.Json => _formatters.First(formatter => formatter is JsonMessageFormatter),
            OutputFormat.Yaml => throw new NotImplementedException("YAML formatter is not yet implemented"),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown output format")
        };
    }
}