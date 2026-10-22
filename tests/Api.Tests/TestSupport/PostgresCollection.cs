using Tracker.TestSupport;

namespace Api.Tests.TestSupport;

[CollectionDefinition("postgres")]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>;
