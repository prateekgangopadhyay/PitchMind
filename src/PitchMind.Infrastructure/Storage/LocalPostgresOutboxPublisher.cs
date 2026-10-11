using PitchMind.Application.Abstractions;
using Npgsql;

namespace PitchMind.Infrastructure.Storage;

/// <summary>
/// Durable LOCAL handoff for outbox messages. This is a development adapter, not a broker
/// or canonical data store; Azure profiles must register an Azure transport publisher instead.
/// </summary>
public sealed class LocalPostgresOutboxPublisher(NpgsqlDataSource dataSource) : IOutboxPublisher
{
    public async Task PublishAsync(Guid messageId, string messageJson, CancellationToken cancellationToken)
    {
        if (messageId == Guid.Empty)
            throw new ArgumentException("Outbox message ID is required.", nameof(messageId));
        ArgumentException.ThrowIfNullOrWhiteSpace(messageJson);

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var insert = new NpgsqlCommand("""
            INSERT INTO pitchmind_local.outbox_deliveries (message_id, payload)
            VALUES ($1, $2::jsonb)
            ON CONFLICT (message_id) DO NOTHING
            """, connection, transaction))
        {
            insert.Parameters.AddWithValue(messageId);
            insert.Parameters.AddWithValue(messageJson);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var verify = new NpgsqlCommand("""
            SELECT payload = $2::jsonb
            FROM pitchmind_local.outbox_deliveries
            WHERE message_id = $1
            """, connection, transaction))
        {
            verify.Parameters.AddWithValue(messageId);
            verify.Parameters.AddWithValue(messageJson);
            var matches = (bool?)await verify.ExecuteScalarAsync(cancellationToken);
            if (matches != true)
                throw new InvalidOperationException("Outbox message ID was already delivered with a different payload.");
        }

        await transaction.CommitAsync(cancellationToken);
    }
}
