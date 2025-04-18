# rmqctl - RabbitMQ Control

RabbitMQ Control is a command line tool for RabbitMQ.
It allows you to publish messages to a queue and consume messages from a queue.
It follows the pattern of tools like `kubectl`.

## Development

To start a RabbitMQ server with the management plugin, run the following command:

```bash
docker run -d --hostname rmq --name rabbit-server -p 8080:15672 -p 5672:5672 rabbitmq:4-management
```

You can open the RabbitMQ management interface at [http://localhost:8080](http://localhost:8080) with the default username and password both set to `guest`.
