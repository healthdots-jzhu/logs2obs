using Logs2Obs.Adapters.Local.Tests.Fixtures;

namespace Logs2Obs.Adapters.Local.Tests.Collections;

[CollectionDefinition("PostgreSql")]
public class PostgreSqlCollection : ICollectionFixture<PostgreSqlFixture>;

[CollectionDefinition("Redis")]
public class RedisCollection : ICollectionFixture<RedisFixture>;

[CollectionDefinition("Minio")]
public class MinioCollection : ICollectionFixture<MinioFixture>;

[CollectionDefinition("RabbitMq")]
public class RabbitMqCollection : ICollectionFixture<RabbitMqFixture>;
