namespace BuildingSentinel.MongoDB.Repositories;

using BuildingSentinel.Common.Domain;
using BuildingSentinel.Common.Repositories;
using global::MongoDB.Bson.Serialization.Attributes;
using global::MongoDB.Driver;

internal sealed class MongoAlertRepository(global::MongoDB.Driver.IMongoDatabase database) : IAlertRepository
{
    private readonly global::MongoDB.Driver.IMongoCollection<AlertDocument> collection =
        database.GetCollection<AlertDocument>("access_denial_alerts");

    public async Task<AccessDenialAlert> IncrementDenialAsync(
        string badgeId,
        int threshold,
        CancellationToken cancellationToken = default)
    {
        var filter = global::MongoDB.Driver.Builders<AlertDocument>.Filter.Eq(x => x.BadgeId, badgeId);
        var now = DateTimeOffset.UtcNow;

        var update = global::MongoDB.Driver.Builders<AlertDocument>.Update
            .Inc(x => x.DenialCount, 1)
            .Set(x => x.UpdatedAt, now)
            .SetOnInsert(x => x.BadgeId, badgeId)
            .SetOnInsert(x => x.AlarmRaised, false);

        var options = new global::MongoDB.Driver.FindOneAndUpdateOptions<AlertDocument>
        {
            IsUpsert = true,
            ReturnDocument = global::MongoDB.Driver.ReturnDocument.After
        };

        var doc = await collection
            .FindOneAndUpdateAsync(filter, update, options, cancellationToken)
            .ConfigureAwait(false);

        if (doc.DenialCount >= threshold && !doc.AlarmRaised)
        {
            var raiseFilter = global::MongoDB.Driver.Builders<AlertDocument>.Filter.And(
                global::MongoDB.Driver.Builders<AlertDocument>.Filter.Eq(x => x.BadgeId, badgeId),
                global::MongoDB.Driver.Builders<AlertDocument>.Filter.Eq(x => x.AlarmRaised, false));

            var raiseUpdate = global::MongoDB.Driver.Builders<AlertDocument>.Update.Set(x => x.AlarmRaised, true);

            await collection.UpdateOneAsync(raiseFilter, raiseUpdate, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            doc.AlarmRaised = true;
        }

        return new AccessDenialAlert
        {
            BadgeId = doc.BadgeId,
            DenialCount = doc.DenialCount,
            AlarmRaised = doc.AlarmRaised,
            UpdatedAt = doc.UpdatedAt
        };
    }
}

internal sealed class AlertDocument
{
    [BsonId] public global::MongoDB.Bson.ObjectId Id { get; set; }
    public string BadgeId { get; set; } = default!;
    public int DenialCount { get; set; }
    public bool AlarmRaised { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
