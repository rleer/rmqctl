using rmqctl.Models;

namespace rmqctl.Configuration;

public class DaemonConfig
{
    public bool IsDaemonMode { get; set; }
    public string? Queue { get; set; }
    public AckModes? AckMode { get; set; }
    public int MessageCount { get; set; } = -1;
}