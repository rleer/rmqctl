using System.CommandLine;
using System.CommandLine.Binding;

namespace dump_messages.Models;

public class DestinationBinder : BinderBase<Destination>
{
    private readonly Option<string> _queueOption;
    private readonly Option<string> _exchangeOption;
    private readonly Option<string> _routingKeyOption;

    public DestinationBinder(Option<string> queueOption, Option<string> exchangeOption, Option<string> routingKeyOption)
    {
        _queueOption = queueOption;
        _exchangeOption = exchangeOption;
        _routingKeyOption = routingKeyOption;
    }
    
    protected override Destination GetBoundValue(BindingContext bindingContext)
    {
        return new Destination
        {
            Queue = bindingContext.ParseResult.GetValueForOption(_queueOption),
            Exchange = bindingContext.ParseResult.GetValueForOption(_exchangeOption)!,
            RoutingKey = bindingContext.ParseResult.GetValueForOption(_routingKeyOption)
        };
    }
}