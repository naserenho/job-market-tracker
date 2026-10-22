using Tracker.TestSupport;

namespace Tracker.Core.Tests.TestSupport;

[CollectionDefinition("postgres")]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>;
