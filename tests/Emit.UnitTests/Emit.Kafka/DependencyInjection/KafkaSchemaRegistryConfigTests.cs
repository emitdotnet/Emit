namespace Emit.Kafka.Tests.DependencyInjection;

using global::Emit.Kafka.DependencyInjection;
using Xunit;
using ConfluentSchemaRegistry = Confluent.SchemaRegistry;

public sealed class KafkaSchemaRegistryConfigTests
{
    [Fact]
    public void GivenUrlSet_WhenApplyTo_ThenConfigUrlIsSet()
    {
        // Arrange
        var config = new KafkaSchemaRegistryConfig { Url = "http://localhost:8081" };
        var target = new ConfluentSchemaRegistry.SchemaRegistryConfig();

        // Act
        config.ApplyTo(target);

        // Assert
        Assert.Equal("http://localhost:8081", target.Url);
    }

    [Fact]
    public void GivenRequestTimeoutSet_WhenApplyTo_ThenConfigRequestTimeoutMsIsSet()
    {
        // Arrange
        var config = new KafkaSchemaRegistryConfig { RequestTimeout = TimeSpan.FromSeconds(10) };
        var target = new ConfluentSchemaRegistry.SchemaRegistryConfig();

        // Act
        config.ApplyTo(target);

        // Assert
        Assert.Equal(10000, target.RequestTimeoutMs);
    }

    [Fact]
    public void GivenMaxRetriesSet_WhenApplyTo_ThenConfigMaxRetriesIsSet()
    {
        // Arrange
        var config = new KafkaSchemaRegistryConfig { MaxRetries = 5 };
        var target = new ConfluentSchemaRegistry.SchemaRegistryConfig();

        // Act
        config.ApplyTo(target);

        // Assert
        Assert.Equal(5, target.MaxRetries);
    }

    [Fact]
    public void GivenRetriesWaitSet_WhenApplyTo_ThenConfigRetriesWaitMsIsSet()
    {
        // Arrange
        var config = new KafkaSchemaRegistryConfig { RetriesWait = TimeSpan.FromSeconds(2) };
        var target = new ConfluentSchemaRegistry.SchemaRegistryConfig();

        // Act
        config.ApplyTo(target);

        // Assert
        Assert.Equal(2000, target.RetriesWaitMs);
    }

    [Fact]
    public void GivenRetriesMaxWaitSet_WhenApplyTo_ThenConfigRetriesMaxWaitMsIsSet()
    {
        // Arrange
        var config = new KafkaSchemaRegistryConfig { RetriesMaxWait = TimeSpan.FromSeconds(30) };
        var target = new ConfluentSchemaRegistry.SchemaRegistryConfig();

        // Act
        config.ApplyTo(target);

        // Assert
        Assert.Equal(30000, target.RetriesMaxWaitMs);
    }

    [Fact]
    public void GivenMaxConnectionsPerServerSet_WhenApplyTo_ThenConfigMaxConnectionsPerServerIsSet()
    {
        // Arrange
        var config = new KafkaSchemaRegistryConfig { MaxConnectionsPerServer = 10 };
        var target = new ConfluentSchemaRegistry.SchemaRegistryConfig();

        // Act
        config.ApplyTo(target);

        // Assert
        Assert.Equal(10, target.MaxConnectionsPerServer);
    }

    [Fact]
    public void GivenMaxCachedSchemasSet_WhenApplyTo_ThenConfigMaxCachedSchemasIsSet()
    {
        // Arrange
        var config = new KafkaSchemaRegistryConfig { MaxCachedSchemas = 500 };
        var target = new ConfluentSchemaRegistry.SchemaRegistryConfig();

        // Act
        config.ApplyTo(target);

        // Assert
        Assert.Equal(500, target.MaxCachedSchemas);
    }

    [Fact]
    public void GivenLatestCacheTtlSet_WhenApplyTo_ThenConfigLatestCacheTtlSecsIsSet()
    {
        // Arrange
        var config = new KafkaSchemaRegistryConfig { LatestCacheTtl = TimeSpan.FromMinutes(2) };
        var target = new ConfluentSchemaRegistry.SchemaRegistryConfig();

        // Act
        config.ApplyTo(target);

        // Assert
        Assert.Equal(120, target.LatestCacheTtlSecs);
    }

    [Fact]
    public void GivenSslCaLocationSet_WhenApplyTo_ThenConfigSslCaLocationIsSet()
    {
        // Arrange
        var config = new KafkaSchemaRegistryConfig { SslCaLocation = "/certs/ca.pem" };
        var target = new ConfluentSchemaRegistry.SchemaRegistryConfig();

        // Act
        config.ApplyTo(target);

        // Assert
        Assert.Equal("/certs/ca.pem", target.SslCaLocation);
    }

    [Fact]
    public void GivenSslKeystoreSet_WhenApplyTo_ThenConfigSslKeystoreIsSet()
    {
        // Arrange
        var config = new KafkaSchemaRegistryConfig
        {
            SslKeystoreLocation = "/certs/keystore.p12",
            SslKeystorePassword = "secret"
        };
        var target = new ConfluentSchemaRegistry.SchemaRegistryConfig();

        // Act
        config.ApplyTo(target);

        // Assert
        Assert.Equal("/certs/keystore.p12", target.SslKeystoreLocation);
        Assert.Equal("secret", target.SslKeystorePassword);
    }

    [Fact]
    public void GivenEnableSslCertificateVerificationSet_WhenApplyTo_ThenConfigEnableSslCertificateVerificationIsSet()
    {
        // Arrange
        var config = new KafkaSchemaRegistryConfig { EnableSslCertificateVerification = false };
        var target = new ConfluentSchemaRegistry.SchemaRegistryConfig();

        // Act
        config.ApplyTo(target);

        // Assert
        Assert.False(target.EnableSslCertificateVerification);
    }

    [Fact]
    public void GivenBasicAuthSet_WhenApplyTo_ThenConfigBasicAuthIsSet()
    {
        // Arrange
        var config = new KafkaSchemaRegistryConfig
        {
            BasicAuthCredentialsSource = ConfluentSchemaRegistry.AuthCredentialsSource.UserInfo,
            BasicAuthUserInfo = "user:pass"
        };
        var target = new ConfluentSchemaRegistry.SchemaRegistryConfig();

        // Act
        config.ApplyTo(target);

        // Assert
        Assert.Equal(ConfluentSchemaRegistry.AuthCredentialsSource.UserInfo, target.BasicAuthCredentialsSource);
        Assert.Equal("user:pass", target.BasicAuthUserInfo);
    }

    [Fact]
    public void GivenNoPropertiesSet_WhenApplyTo_ThenConfigUnchanged()
    {
        // Arrange
        var config = new KafkaSchemaRegistryConfig();
        var target = new ConfluentSchemaRegistry.SchemaRegistryConfig
        {
            Url = "http://existing:8081",
            MaxRetries = 10
        };

        // Act
        config.ApplyTo(target);

        // Assert
        Assert.Equal("http://existing:8081", target.Url);
        Assert.Equal(10, target.MaxRetries);
    }

    [Fact]
    public void GivenAllPropertiesSet_WhenApplyTo_ThenAllApplied()
    {
        // Arrange
        var config = new KafkaSchemaRegistryConfig
        {
            Url = "http://registry:8081",
            RequestTimeout = TimeSpan.FromSeconds(15),
            MaxRetries = 5,
            RetriesWait = TimeSpan.FromSeconds(2),
            RetriesMaxWait = TimeSpan.FromSeconds(30),
            MaxConnectionsPerServer = 10,
            MaxCachedSchemas = 500,
            LatestCacheTtl = TimeSpan.FromMinutes(2),
            SslCaLocation = "/certs/ca.pem",
            SslKeystoreLocation = "/certs/keystore.p12",
            SslKeystorePassword = "secret",
            EnableSslCertificateVerification = true,
            BasicAuthCredentialsSource = ConfluentSchemaRegistry.AuthCredentialsSource.UserInfo,
            BasicAuthUserInfo = "user:pass"
        };
        var target = new ConfluentSchemaRegistry.SchemaRegistryConfig();

        // Act
        config.ApplyTo(target);

        // Assert
        Assert.Equal("http://registry:8081", target.Url);
        Assert.Equal(15000, target.RequestTimeoutMs);
        Assert.Equal(5, target.MaxRetries);
        Assert.Equal(2000, target.RetriesWaitMs);
        Assert.Equal(30000, target.RetriesMaxWaitMs);
        Assert.Equal(10, target.MaxConnectionsPerServer);
        Assert.Equal(500, target.MaxCachedSchemas);
        Assert.Equal(120, target.LatestCacheTtlSecs);
        Assert.Equal("/certs/ca.pem", target.SslCaLocation);
        Assert.Equal("/certs/keystore.p12", target.SslKeystoreLocation);
        Assert.Equal("secret", target.SslKeystorePassword);
        Assert.True(target.EnableSslCertificateVerification);
        Assert.Equal(ConfluentSchemaRegistry.AuthCredentialsSource.UserInfo, target.BasicAuthCredentialsSource);
        Assert.Equal("user:pass", target.BasicAuthUserInfo);
    }
}
