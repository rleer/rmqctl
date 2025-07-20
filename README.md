# rmqctl - Developer Tool for RabbitMQ

`rmqctl` is a command line tool for RabbitMQ focused on developers working with RabbitMQ. 


## Installation

Build and install the tool:

```bash
dotnet build
dotnet pack
dotnet tool install --global --add-source ./nupkg rmqctl
```

## Configuration

`rmqctl` uses TOML configuration files following standard CLI tool conventions. Configuration is loaded in the following priority order (highest priority wins):

1. Environment variables (prefixed with `RMQCTL_`)
2. Custom config file (via `--config` flag)
3. User config file: `~/.config/rmqctl/config.toml`
4. System-wide config file: `/etc/rmqctl/config.toml`

### Default Configuration

On first run, `rmqctl` automatically creates a default configuration file at `~/.config/rmqctl/config.toml`.

### Configuration File Locations

- **Linux/macOS**: `~/.config/rmqctl/config.toml`
- **Windows**: `%APPDATA%/rmqctl/config.toml`
- **System-wide (Linux/macOS)**: `/etc/rmqctl/config.toml`
- **System-wide (Windows)**: `%PROGRAMDATA%/rmqctl/config.toml`

### Configuration Management

Use the built-in configuration commands to manage your settings:

```bash
# Show configuration file location
rmqctl config path

# Display current configuration
rmqctl config show

# Open configuration in default editor
rmqctl config edit

# Reset configuration to defaults
rmqctl config reset

# Use a custom configuration file
rmqctl --config /path/to/custom-config.toml <command>
```

### Environment Variables

Override any configuration setting using environment variables with the `RMQCTL_` prefix:

```bash
# Override RabbitMQ host
export RMQCTL_RabbitMqConfig__Host=production-rabbit

# Override port
export RMQCTL_RabbitMqConfig__Port=5673

# Override user credentials
export RMQCTL_RabbitMqConfig__User=myuser
export RMQCTL_RabbitMqConfig__Password=mypassword
```

Note: Use double underscores (`__`) to represent nested configuration sections.

## Usage

### Publishing Messages

```bash
# Publish a simple text message
rmqctl publish --queue myqueue --message "Hello, World!"

# Publish from a file
rmqctl publish --queue myqueue --file message.txt

# Use custom RabbitMQ settings
rmqctl --config production-config.toml publish --queue myqueue --message "Hello"
```

### Consuming Messages

```bash
# Consume messages and display to console
rmqctl consume --queue myqueue

# Consume messages and save to file
rmqctl consume --queue myqueue --output file --file messages.txt
```

## Development

To start a RabbitMQ server with the management plugin, run the following command:

```bash
docker run -d --hostname rmq --name rabbit-server -p 8080:15672 -p 5672:5672 rabbitmq:4-management
```

You can open the RabbitMQ management interface at [http://localhost:8080](http://localhost:8080) with the default username and password both set to `guest`.

### Building

```bash
# Restore dependencies
dotnet restore

# Build the project
dotnet build

# Run tests
dotnet test

# Run the tool locally
dotnet run -- <command>
```

