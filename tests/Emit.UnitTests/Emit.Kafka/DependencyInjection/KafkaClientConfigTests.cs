namespace Emit.Kafka.Tests.DependencyInjection;

using global::Emit.Kafka.DependencyInjection;
using Xunit;
using ConfluentKafka = Confluent.Kafka;

public sealed class KafkaClientConfigTests
{
    [Fact]
    public void GivenBootstrapServersSet_WhenApplyTo_ThenConfigBootstrapServersIsSet()
    {
        // Arrange
        var clientConfig = new KafkaClientConfig { BootstrapServers = "localhost:9092" };
        var config = new ConfluentKafka.ClientConfig();

        // Act
        clientConfig.ApplyTo(config);

        // Assert
        Assert.Equal("localhost:9092", config.BootstrapServers);
    }

    [Fact]
    public void GivenClientIdSet_WhenApplyTo_ThenConfigClientIdIsSet()
    {
        // Arrange
        var clientConfig = new KafkaClientConfig { ClientId = "my-app" };
        var config = new ConfluentKafka.ClientConfig();

        // Act
        clientConfig.ApplyTo(config);

        // Assert
        Assert.Equal("my-app", config.ClientId);
    }

    [Fact]
    public void GivenSecurityProtocolSet_WhenApplyTo_ThenConfigSecurityProtocolIsSet()
    {
        // Arrange
        var clientConfig = new KafkaClientConfig { SecurityProtocol = ConfluentKafka.SecurityProtocol.SaslSsl };
        var config = new ConfluentKafka.ClientConfig();

        // Act
        clientConfig.ApplyTo(config);

        // Assert
        Assert.Equal(ConfluentKafka.SecurityProtocol.SaslSsl, config.SecurityProtocol);
    }

    [Fact]
    public void GivenSaslMechanismSet_WhenApplyTo_ThenConfigSaslMechanismIsSet()
    {
        // Arrange
        var clientConfig = new KafkaClientConfig { SaslMechanism = ConfluentKafka.SaslMechanism.Plain };
        var config = new ConfluentKafka.ClientConfig();

        // Act
        clientConfig.ApplyTo(config);

        // Assert
        Assert.Equal(ConfluentKafka.SaslMechanism.Plain, config.SaslMechanism);
    }

    [Fact]
    public void GivenSaslCredentialsSet_WhenApplyTo_ThenConfigSaslCredentialsAreSet()
    {
        // Arrange
        var clientConfig = new KafkaClientConfig
        {
            SaslUsername = "user",
            SaslPassword = "pass"
        };
        var config = new ConfluentKafka.ClientConfig();

        // Act
        clientConfig.ApplyTo(config);

        // Assert
        Assert.Equal("user", config.SaslUsername);
        Assert.Equal("pass", config.SaslPassword);
    }

    [Fact]
    public void GivenSslLocationsSet_WhenApplyTo_ThenConfigSslLocationsAreSet()
    {
        // Arrange
        var clientConfig = new KafkaClientConfig
        {
            SslCaLocation = "/certs/ca.pem",
            SslCertificateLocation = "/certs/cert.pem",
            SslKeyLocation = "/certs/key.pem",
            SslKeyPassword = "secret"
        };
        var config = new ConfluentKafka.ClientConfig();

        // Act
        clientConfig.ApplyTo(config);

        // Assert
        Assert.Equal("/certs/ca.pem", config.SslCaLocation);
        Assert.Equal("/certs/cert.pem", config.SslCertificateLocation);
        Assert.Equal("/certs/key.pem", config.SslKeyLocation);
        Assert.Equal("secret", config.SslKeyPassword);
    }

    [Fact]
    public void GivenSocketTimeoutSet_WhenApplyTo_ThenConfigSocketTimeoutMsIsSet()
    {
        // Arrange
        var clientConfig = new KafkaClientConfig { SocketTimeout = TimeSpan.FromSeconds(30) };
        var config = new ConfluentKafka.ClientConfig();

        // Act
        clientConfig.ApplyTo(config);

        // Assert
        Assert.Equal(30000, config.SocketTimeoutMs);
    }

    [Fact]
    public void GivenConnectionsMaxIdleSet_WhenApplyTo_ThenConfigConnectionsMaxIdleMsIsSet()
    {
        // Arrange
        var clientConfig = new KafkaClientConfig { ConnectionsMaxIdle = TimeSpan.FromMinutes(9) };
        var config = new ConfluentKafka.ClientConfig();

        // Act
        clientConfig.ApplyTo(config);

        // Assert
        Assert.Equal(540000, config.ConnectionsMaxIdleMs);
    }

    [Fact]
    public void GivenMetadataMaxAgeSet_WhenApplyTo_ThenConfigMetadataMaxAgeMsIsSet()
    {
        // Arrange
        var clientConfig = new KafkaClientConfig { MetadataMaxAge = TimeSpan.FromMinutes(5) };
        var config = new ConfluentKafka.ClientConfig();

        // Act
        clientConfig.ApplyTo(config);

        // Assert
        Assert.Equal(300000, config.MetadataMaxAgeMs);
    }

    [Fact]
    public void GivenReconnectBackoffSet_WhenApplyTo_ThenConfigReconnectBackoffMsIsSet()
    {
        // Arrange
        var clientConfig = new KafkaClientConfig { ReconnectBackoff = TimeSpan.FromMilliseconds(100) };
        var config = new ConfluentKafka.ClientConfig();

        // Act
        clientConfig.ApplyTo(config);

        // Assert
        Assert.Equal(100, config.ReconnectBackoffMs);
    }

    [Fact]
    public void GivenReconnectBackoffMaxSet_WhenApplyTo_ThenConfigReconnectBackoffMaxMsIsSet()
    {
        // Arrange
        var clientConfig = new KafkaClientConfig { ReconnectBackoffMax = TimeSpan.FromSeconds(10) };
        var config = new ConfluentKafka.ClientConfig();

        // Act
        clientConfig.ApplyTo(config);

        // Assert
        Assert.Equal(10000, config.ReconnectBackoffMaxMs);
    }

    [Fact]
    public void GivenStatisticsIntervalSet_WhenApplyTo_ThenConfigStatisticsIntervalMsIsSet()
    {
        // Arrange
        var clientConfig = new KafkaClientConfig { StatisticsInterval = TimeSpan.FromSeconds(5) };
        var config = new ConfluentKafka.ClientConfig();

        // Act
        clientConfig.ApplyTo(config);

        // Assert
        Assert.Equal(5000, config.StatisticsIntervalMs);
    }

    [Fact]
    public void GivenSocketKeepaliveEnableSet_WhenApplyTo_ThenConfigSocketKeepaliveEnableIsSet()
    {
        // Arrange
        var clientConfig = new KafkaClientConfig { SocketKeepaliveEnable = true };
        var config = new ConfluentKafka.ClientConfig();

        // Act
        clientConfig.ApplyTo(config);

        // Assert
        Assert.True(config.SocketKeepaliveEnable);
    }

    [Fact]
    public void GivenMessageMaxBytesSet_WhenApplyTo_ThenConfigMessageMaxBytesIsSet()
    {
        // Arrange
        var clientConfig = new KafkaClientConfig { MessageMaxBytes = 2097152 };
        var config = new ConfluentKafka.ClientConfig();

        // Act
        clientConfig.ApplyTo(config);

        // Assert
        Assert.Equal(2097152, config.MessageMaxBytes);
    }

    [Fact]
    public void GivenNoPropertiesSet_WhenApplyTo_ThenConfigUnchanged()
    {
        // Arrange
        var clientConfig = new KafkaClientConfig();
        var config = new ConfluentKafka.ClientConfig
        {
            BootstrapServers = "existing:9092",
            SocketTimeoutMs = 5000
        };

        // Act
        clientConfig.ApplyTo(config);

        // Assert
        Assert.Equal("existing:9092", config.BootstrapServers);
        Assert.Equal(5000, config.SocketTimeoutMs);
    }

    [Fact]
    public void GivenAllPropertiesSet_WhenApplyTo_ThenAllApplied()
    {
        // Arrange
        var clientConfig = new KafkaClientConfig
        {
            BootstrapServers = "broker1:9092,broker2:9092",
            ClientId = "my-app",
            SecurityProtocol = ConfluentKafka.SecurityProtocol.SaslSsl,
            SaslMechanism = ConfluentKafka.SaslMechanism.Plain,
            SaslUsername = "user",
            SaslPassword = "pass",
            SslCaLocation = "/certs/ca.pem",
            SslCertificateLocation = "/certs/cert.pem",
            SslKeyLocation = "/certs/key.pem",
            SslKeyPassword = "secret",
            SocketTimeout = TimeSpan.FromSeconds(30),
            ConnectionsMaxIdle = TimeSpan.FromMinutes(9),
            MetadataMaxAge = TimeSpan.FromMinutes(5),
            ReconnectBackoff = TimeSpan.FromMilliseconds(100),
            ReconnectBackoffMax = TimeSpan.FromSeconds(10),
            StatisticsInterval = TimeSpan.FromSeconds(5),
            SocketKeepaliveEnable = true,
            MessageMaxBytes = 2097152
        };
        var config = new ConfluentKafka.ClientConfig();

        // Act
        clientConfig.ApplyTo(config);

        // Assert
        Assert.Equal("broker1:9092,broker2:9092", config.BootstrapServers);
        Assert.Equal("my-app", config.ClientId);
        Assert.Equal(ConfluentKafka.SecurityProtocol.SaslSsl, config.SecurityProtocol);
        Assert.Equal(ConfluentKafka.SaslMechanism.Plain, config.SaslMechanism);
        Assert.Equal("user", config.SaslUsername);
        Assert.Equal("pass", config.SaslPassword);
        Assert.Equal("/certs/ca.pem", config.SslCaLocation);
        Assert.Equal("/certs/cert.pem", config.SslCertificateLocation);
        Assert.Equal("/certs/key.pem", config.SslKeyLocation);
        Assert.Equal("secret", config.SslKeyPassword);
        Assert.Equal(30000, config.SocketTimeoutMs);
        Assert.Equal(540000, config.ConnectionsMaxIdleMs);
        Assert.Equal(300000, config.MetadataMaxAgeMs);
        Assert.Equal(100, config.ReconnectBackoffMs);
        Assert.Equal(10000, config.ReconnectBackoffMaxMs);
        Assert.Equal(5000, config.StatisticsIntervalMs);
        Assert.True(config.SocketKeepaliveEnable);
        Assert.Equal(2097152, config.MessageMaxBytes);
    }

    [Fact]
    public void GivenApplyToProducerConfig_WhenCalled_ThenProducerConfigInheritsSettings()
    {
        // Arrange
        var clientConfig = new KafkaClientConfig { BootstrapServers = "localhost:9092" };
        var producerConfig = new ConfluentKafka.ProducerConfig();

        // Act
        clientConfig.ApplyTo(producerConfig);

        // Assert
        Assert.Equal("localhost:9092", producerConfig.BootstrapServers);
    }

    [Fact]
    public void GivenApplyToConsumerConfig_WhenCalled_ThenConsumerConfigInheritsSettings()
    {
        // Arrange
        var clientConfig = new KafkaClientConfig { BootstrapServers = "localhost:9092" };
        var consumerConfig = new ConfluentKafka.ConsumerConfig();

        // Act
        clientConfig.ApplyTo(consumerConfig);

        // Assert
        Assert.Equal("localhost:9092", consumerConfig.BootstrapServers);
    }
}
