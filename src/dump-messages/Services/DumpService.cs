namespace dump_messages.Services;

public interface IDumpService
{
    Task DumpMessages(string queue, string fileName);
}

public class DumpService : IDumpService
{
    public Task DumpMessages(string queue, string fileName)
    {
        throw new NotImplementedException();
    }
}