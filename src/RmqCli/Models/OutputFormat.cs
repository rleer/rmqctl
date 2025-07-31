namespace RmqCli.Models;

public enum OutputFormat
{
    /// <summary>
    /// Output results in plain text format.
    /// </summary>
    Plain,
    
    /// <summary>
    /// Output results in a table format.
    /// </summary>
    Table,

    /// <summary>
    /// Output results in JSON format.
    /// </summary>
    Json,
    
    /// <summary>
    /// To be implemented: Output results in JSON based on a JSONPath expression.
    /// This format allows for filtering and selecting specific parts of the JSON output.
    /// </summary>
    JsonPath
}