using Npgsql;

namespace PitchMind.Infrastructure.Storage;

/// <summary>Deduplicates a consumer effect in the same database transaction as its inbox receipt.</summary>
public sealed class PostgresConsumerInbox(NpgsqlDataSource dataSource)
{
    public async Task<bool> ExecuteOnceAsync(string consumerId, Guid messageId,
        Func<NpgsqlConnection, NpgsqlTransaction, CancellationToken, Task> applyEffect,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(consumerId) || consumerId.Length > 128)
            throw new ArgumentException("Consumer ID must contain 1 to 128 characters.", nameof(consumerId));
        if (messageId == Guid.Empty) throw new ArgumentException("Message ID is required.", nameof(messageId));
        ArgumentNullException.ThrowIfNull(applyEffect);

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var insert = new NpgsqlCommand("INSERT INTO pitchmind.consumer_inbox(consumer_id, message_id) VALUES ($1, $2) ON CONFLICT DO NOTHING", connection, transaction))
        {
            insert.Parameters.AddWithValue(consumerId);
            insert.Parameters.AddWithValue(messageId);
            if (await insert.ExecuteNonQueryAsync(cancellationToken) == 0)
            {
                await transaction.CommitAsync(cancellationToken);
                return false;
            }
        }

        await applyEffect(connection, transaction, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }
}
