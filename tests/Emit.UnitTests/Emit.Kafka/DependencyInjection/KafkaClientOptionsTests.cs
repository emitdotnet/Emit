namespace Emit.Kafka.Tests.DependencyInjection;

using global::Emit.Kafka.DependencyInjection;
using Xunit;
using ConfluentKafka = Confluent.Kafka;

public sealed class KafkaClientOptionsTests
{
    [Fact]
    public void GivenBootstrapServersSet_WhenApplyTo_ThenConfigBootstrapServersIsSet()
    {
        // Arrange
        var clientConfig = new KafkaClientOptions { BootstrapServers = "localhost:9092" };
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
        var clientConfig = new KafkaClientOptions { ClientId = "my-app" };
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
        var clientConfig = new KafkaClientOptions { SecurityProtocol = ConfluentKafka.SecurityProtocol.SaslSsl };
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
        var clientConfig = new KafkaClientOptions { SaslMechanism = ConfluentKafka.SaslMechanism.Plain };
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
        var clientConfig = new KafkaClientOptions
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
        var clientConfig = new KafkaClientOptions
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
        var clientConfig = new KafkaClientOptions { SocketTimeout = TimeSpan.FromSeconds(30) };
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
        var clientConfig = new KafkaClientOptions { ConnectionsMaxIdle = TimeSpan.FromMinutes(9) };
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
        var clientConfig = new KafkaClientOptions { MetadataMaxAge = TimeSpan.FromMinutes(5) };
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
        var clientConfig = new KafkaClientOptions { ReconnectBackoff = TimeSpan.FromMilliseconds(100) };
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
        var clientConfig = new KafkaClientOptions { ReconnectBackoffMax = TimeSpan.FromSeconds(10) };
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
        var clientConfig = new KafkaClientOptions { StatisticsInterval = TimeSpan.FromSeconds(5) };
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
        var clientConfig = new KafkaClientOptions { SocketKeepaliveEnable = true };
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
        var clientConfig = new KafkaClientOptions { MessageMaxBytes = 2097152 };
        var config = new ConfluentKafka.ClientConfig();

        // Act
        clientConfig.ApplyTo(config);

        // Assert
        Assert.Equal(2097152, config.MessageMaxBytes);
    }

    [Fact]
    public void GivenClientRackSet_WhenApplyTo_ThenConfigClientRackIsSet()
    {
        // Arrange
        var clientConfig = new KafkaClientOptions { ClientRack = "rack-1" };
        var config = new ConfluentKafka.ClientConfig();

        // Act
        clientConfig.ApplyTo(config);

        // Assert
        Assert.Equal("rack-1", config.ClientRack);
    }

    [Fact]
    public void GivenSaslOauthbearerMethodSet_WhenApplyTo_ThenConfigSaslOauthbearerMethodIsSet()
    {
        // Arrange
        var clientConfig = new KafkaClientOptions { SaslOauthbearerMethod = ConfluentKafka.SaslOauthbearerMethod.Oidc };
        var config = new ConfluentKafka.ClientConfig();

        // Act
        clientConfig.ApplyTo(config);

        // Assert
        Assert.Equal(ConfluentKafka.SaslOauthbearerMethod.Oidc, config.SaslOauthbearerMethod);
    }

    [Fact]
    public void GivenSaslOauthbearerClientIdSet_WhenApplyTo_ThenConfigSaslOauthbearerClientIdIsSet()
    {
        // Arrange
        var clientConfig = new KafkaClientOptions { SaslOauthbearerClientId = "my-client-id" };
        var config = new ConfluentKafka.ClientConfig();

        // Act
        clientConfig.ApplyTo(config);

        // Assert
        Assert.Equal("my-client-id", config.SaslOauthbearerClientId);
    }

    [Fact]
    public void GivenSaslOauthbearerClientSecretSet_WhenApplyTo_ThenConfigSaslOauthbearerClientSecretIsSet()
    {
        // Arrange
        var clientConfig = new KafkaClientOptions { SaslOauthbearerClientSecret = "my-secret" };
        var config = new ConfluentKafka.ClientConfig();

        // Act
        clientConfig.ApplyTo(config);

        // Assert
        Assert.Equal("my-secret", config.SaslOauthbearerClientSecret);
    }

    [Fact]
    public void GivenSaslOauthbearerTokenEndpointUrlSet_WhenApplyTo_ThenConfigSaslOauthbearerTokenEndpointUrlIsSet()
    {
        // Arrange
        var clientConfig = new KafkaClientOptions { SaslOauthbearerTokenEndpointUrl = "https://idp.example.com/token" };
        var config = new ConfluentKafka.ClientConfig();

        // Act
        clientConfig.ApplyTo(config);

        // Assert
        Assert.Equal("https://idp.example.com/token", config.SaslOauthbearerTokenEndpointUrl);
    }

    [Fact]
    public void GivenSaslOauthbearerScopeSet_WhenApplyTo_ThenConfigSaslOauthbearerScopeIsSet()
    {
        // Arrange
        var clientConfig = new KafkaClientOptions { SaslOauthbearerScope = "openid profile" };
        var config = new ConfluentKafka.ClientConfig();

        // Act
        clientConfig.ApplyTo(config);

        // Assert
        Assert.Equal("openid profile", config.SaslOauthbearerScope);
    }

    [Fact]
    public void GivenSaslOauthbearerExtensionsSet_WhenApplyTo_ThenConfigSaslOauthbearerExtensionsIsSet()
    {
        // Arrange
        var clientConfig = new KafkaClientOptions { SaslOauthbearerExtensions = "key1=val1,key2=val2" };
        var config = new ConfluentKafka.ClientConfig();

        // Act
        clientConfig.ApplyTo(config);

        // Assert
        Assert.Equal("key1=val1,key2=val2", config.SaslOauthbearerExtensions);
    }

    [Fact]
    public void GivenSslEndpointIdentificationAlgorithmSet_WhenApplyTo_ThenConfigSslEndpointIdentificationAlgorithmIsSet()
    {
        // Arrange
        var clientConfig = new KafkaClientOptions { SslEndpointIdentificationAlgorithm = ConfluentKafka.SslEndpointIdentificationAlgorithm.Https };
        var config = new ConfluentKafka.ClientConfig();

        // Act
        clientConfig.ApplyTo(config);

        // Assert
        Assert.Equal(ConfluentKafka.SslEndpointIdentificationAlgorithm.Https, config.SslEndpointIdentificationAlgorithm);
    }

    [Fact]
    public void GivenEnableSslCertificateVerificationSet_WhenApplyTo_ThenConfigEnableSslCertificateVerificationIsSet()
    {
        // Arrange
        var clientConfig = new KafkaClientOptions { EnableSslCertificateVerification = true };
        var config = new ConfluentKafka.ClientConfig();

        // Act
        clientConfig.ApplyTo(config);

        // Assert
        Assert.True(config.EnableSslCertificateVerification);
    }

    [Fact]
    public void GivenSocketConnectionSetupTimeoutSet_WhenApplyTo_ThenConfigSocketConnectionSetupTimeoutMsIsSet()
    {
        // Arrange
        var clientConfig = new KafkaClientOptions { SocketConnectionSetupTimeout = TimeSpan.FromSeconds(15) };
        var config = new ConfluentKafka.ClientConfig();

        // Act
        clientConfig.ApplyTo(config);

        // Assert
        Assert.Equal(15000, config.SocketConnectionSetupTimeoutMs);
    }

    [Fact]
    public void GivenTopicMetadataRefreshIntervalSet_WhenApplyTo_ThenConfigTopicMetadataRefreshIntervalMsIsSet()
    {
        // Arrange
        var clientConfig = new KafkaClientOptions { TopicMetadataRefreshInterval = TimeSpan.FromMinutes(5) };
        var config = new ConfluentKafka.ClientConfig();

        // Act
        clientConfig.ApplyTo(config);

        // Assert
        Assert.Equal(300000, config.TopicMetadataRefreshIntervalMs);
    }

    [Fact]
    public void GivenRetryBackoffSet_WhenApplyTo_ThenConfigRetryBackoffMsIsSet()
    {
        // Arrange
        var clientConfig = new KafkaClientOptions { RetryBackoff = TimeSpan.FromMilliseconds(200) };
        var config = new ConfluentKafka.ClientConfig();

        // Act
        clientConfig.ApplyTo(config);

        // Assert
        Assert.Equal(200, config.RetryBackoffMs);
    }

    [Fact]
    public void GivenRetryBackoffMaxSet_WhenApplyTo_ThenConfigRetryBackoffMaxMsIsSet()
    {
        // Arrange
        var clientConfig = new KafkaClientOptions { RetryBackoffMax = TimeSpan.FromSeconds(5) };
        var config = new ConfluentKafka.ClientConfig();

        // Act
        clientConfig.ApplyTo(config);

        // Assert
        Assert.Equal(5000, config.RetryBackoffMaxMs);
    }

    [Fact]
    public void GivenReceiveMessageMaxBytesSet_WhenApplyTo_ThenConfigReceiveMessageMaxBytesIsSet()
    {
        // Arrange
        var clientConfig = new KafkaClientOptions { ReceiveMessageMaxBytes = 104857600 };
        var config = new ConfluentKafka.ClientConfig();

        // Act
        clientConfig.ApplyTo(config);

        // Assert
        Assert.Equal(104857600, config.ReceiveMessageMaxBytes);
    }

    [Fact]
    public void GivenMaxInFlightRequestsPerConnectionSet_WhenApplyTo_ThenConfigMaxInFlightIsSet()
    {
        // Arrange
        var clientConfig = new KafkaClientOptions { MaxInFlightRequestsPerConnection = 5 };
        var config = new ConfluentKafka.ClientConfig();

        // Act
        clientConfig.ApplyTo(config);

        // Assert
        Assert.Equal(5, config.MaxInFlight);
    }

    [Fact]
    public void GivenBrokerAddressFamilySet_WhenApplyTo_ThenConfigBrokerAddressFamilyIsSet()
    {
        // Arrange
        var clientConfig = new KafkaClientOptions { BrokerAddressFamily = ConfluentKafka.BrokerAddressFamily.V4 };
        var config = new ConfluentKafka.ClientConfig();

        // Act
        clientConfig.ApplyTo(config);

        // Assert
        Assert.Equal(ConfluentKafka.BrokerAddressFamily.V4, config.BrokerAddressFamily);
    }

    [Fact]
    public void GivenAllowAutoCreateTopicsSet_WhenApplyTo_ThenConfigAllowAutoCreateTopicsIsSet()
    {
        // Arrange
        var clientConfig = new KafkaClientOptions { AllowAutoCreateTopics = false };
        var config = new ConfluentKafka.ClientConfig();

        // Act
        clientConfig.ApplyTo(config);

        // Assert
        Assert.False(config.AllowAutoCreateTopics);
    }

    [Fact]
    public void GivenDebugSet_WhenApplyTo_ThenConfigDebugIsSet()
    {
        // Arrange
        var clientConfig = new KafkaClientOptions { Debug = "broker,topic" };
        var config = new ConfluentKafka.ClientConfig();

        // Act
        clientConfig.ApplyTo(config);

        // Assert
        Assert.Equal("broker,topic", config.Debug);
    }

    [Fact]
    public void GivenNoPropertiesSet_WhenApplyTo_ThenConfigUnchanged()
    {
        // Arrange
        var clientConfig = new KafkaClientOptions();
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
        var clientConfig = new KafkaClientOptions
        {
            BootstrapServers = "broker1:9092,broker2:9092",
            ClientId = "my-app",
            ClientRack = "rack-1",
            SecurityProtocol = ConfluentKafka.SecurityProtocol.SaslSsl,
            SaslMechanism = ConfluentKafka.SaslMechanism.Plain,
            SaslUsername = "user",
            SaslPassword = "pass",
            SaslOauthbearerMethod = ConfluentKafka.SaslOauthbearerMethod.Oidc,
            SaslOauthbearerClientId = "my-client-id",
            SaslOauthbearerClientSecret = "my-secret",
            SaslOauthbearerTokenEndpointUrl = "https://idp.example.com/token",
            SaslOauthbearerScope = "openid profile",
            SaslOauthbearerExtensions = "key1=val1,key2=val2",
            SslCaLocation = "/certs/ca.pem",
            SslCertificateLocation = "/certs/cert.pem",
            SslKeyLocation = "/certs/key.pem",
            SslKeyPassword = "secret",
            SslEndpointIdentificationAlgorithm = ConfluentKafka.SslEndpointIdentificationAlgorithm.Https,
            EnableSslCertificateVerification = true,
            SocketTimeout = TimeSpan.FromSeconds(30),
            SocketConnectionSetupTimeout = TimeSpan.FromSeconds(15),
            ConnectionsMaxIdle = TimeSpan.FromMinutes(9),
            MetadataMaxAge = TimeSpan.FromMinutes(5),
            TopicMetadataRefreshInterval = TimeSpan.FromMinutes(5),
            ReconnectBackoff = TimeSpan.FromMilliseconds(100),
            ReconnectBackoffMax = TimeSpan.FromSeconds(10),
            RetryBackoff = TimeSpan.FromMilliseconds(200),
            RetryBackoffMax = TimeSpan.FromSeconds(5),
            StatisticsInterval = TimeSpan.FromSeconds(5),
            SocketKeepaliveEnable = true,
            MessageMaxBytes = 2097152,
            ReceiveMessageMaxBytes = 104857600,
            MaxInFlightRequestsPerConnection = 5,
            BrokerAddressFamily = ConfluentKafka.BrokerAddressFamily.V4,
            AllowAutoCreateTopics = false,
            Debug = "broker,topic"
        };
        var config = new ConfluentKafka.ClientConfig();

        // Act
        clientConfig.ApplyTo(config);

        // Assert
        Assert.Equal("broker1:9092,broker2:9092", config.BootstrapServers);
        Assert.Equal("my-app", config.ClientId);
        Assert.Equal("rack-1", config.ClientRack);
        Assert.Equal(ConfluentKafka.SecurityProtocol.SaslSsl, config.SecurityProtocol);
        Assert.Equal(ConfluentKafka.SaslMechanism.Plain, config.SaslMechanism);
        Assert.Equal("user", config.SaslUsername);
        Assert.Equal("pass", config.SaslPassword);
        Assert.Equal(ConfluentKafka.SaslOauthbearerMethod.Oidc, config.SaslOauthbearerMethod);
        Assert.Equal("my-client-id", config.SaslOauthbearerClientId);
        Assert.Equal("my-secret", config.SaslOauthbearerClientSecret);
        Assert.Equal("https://idp.example.com/token", config.SaslOauthbearerTokenEndpointUrl);
        Assert.Equal("openid profile", config.SaslOauthbearerScope);
        Assert.Equal("key1=val1,key2=val2", config.SaslOauthbearerExtensions);
        Assert.Equal("/certs/ca.pem", config.SslCaLocation);
        Assert.Equal("/certs/cert.pem", config.SslCertificateLocation);
        Assert.Equal("/certs/key.pem", config.SslKeyLocation);
        Assert.Equal("secret", config.SslKeyPassword);
        Assert.Equal(ConfluentKafka.SslEndpointIdentificationAlgorithm.Https, config.SslEndpointIdentificationAlgorithm);
        Assert.True(config.EnableSslCertificateVerification);
        Assert.Equal(30000, config.SocketTimeoutMs);
        Assert.Equal(15000, config.SocketConnectionSetupTimeoutMs);
        Assert.Equal(540000, config.ConnectionsMaxIdleMs);
        Assert.Equal(300000, config.MetadataMaxAgeMs);
        Assert.Equal(300000, config.TopicMetadataRefreshIntervalMs);
        Assert.Equal(100, config.ReconnectBackoffMs);
        Assert.Equal(10000, config.ReconnectBackoffMaxMs);
        Assert.Equal(200, config.RetryBackoffMs);
        Assert.Equal(5000, config.RetryBackoffMaxMs);
        Assert.Equal(5000, config.StatisticsIntervalMs);
        Assert.True(config.SocketKeepaliveEnable);
        Assert.Equal(2097152, config.MessageMaxBytes);
        Assert.Equal(104857600, config.ReceiveMessageMaxBytes);
        Assert.Equal(5, config.MaxInFlight);
        Assert.Equal(ConfluentKafka.BrokerAddressFamily.V4, config.BrokerAddressFamily);
        Assert.False(config.AllowAutoCreateTopics);
        Assert.Equal("broker,topic", config.Debug);
    }

    [Fact]
    public void GivenApplyToProducerConfig_WhenCalled_ThenProducerConfigInheritsSettings()
    {
        // Arrange
        var clientConfig = new KafkaClientOptions { BootstrapServers = "localhost:9092" };
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
        var clientConfig = new KafkaClientOptions { BootstrapServers = "localhost:9092" };
        var consumerConfig = new ConfluentKafka.ConsumerConfig();

        // Act
        clientConfig.ApplyTo(consumerConfig);

        // Assert
        Assert.Equal("localhost:9092", consumerConfig.BootstrapServers);
    }
}
