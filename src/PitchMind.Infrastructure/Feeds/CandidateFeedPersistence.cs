using System.Security.Cryptography;
using System.Text.Json;
using Npgsql;

namespace PitchMind.Infrastructure.Feeds;

public sealed record CandidateKafkaMessage(string ContractVersion, string SourceSha256, string SourceMatchId,
    string SourceEventId, long SourceSequence, decimal RawClockValue, int RawPeriod, string RawEventType,
    JsonElement RawPayload);
public sealed record CandidateFeedCursor(string SourceSha256, string SourceMatchId, long SourceSequence,
    decimal RawClockValue, int RawPeriod);
public sealed record CandidateFeedOutboxWorkItem(CandidateFeedCursor Cursor, CandidateKafkaMessage Message, Guid ClaimToken);

/// <summary>Persists source-only candidate records separately from canonical match facts.</summary>
public sealed class CandidateFeedPersistence(NpgsqlDataSource? dataSource = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly object _sync = new();

    private sealed class InMemoryOutboxEntry
    {
        public required CandidateFeedCursor Cursor { get; init; }
        public required CandidateKafkaMessage Message { get; init; }
        public Guid? ClaimToken { get; set; }
        public DateTimeOffset? LeaseExpiresAt { get; set; }
        public bool IsPublished { get; set; }
        public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    }

    private readonly Dictionary<string, InMemoryOutboxEntry> _inMemoryOutbox = new();
    private readonly Dictionary<string, CandidateKafkaMessage> _inMemoryEvents = new();
    private CandidateFeedCursor? _inMemoryLatestCursor;

    public void ClearInMemory()
    {
        lock (_sync)
        {
            _inMemoryOutbox.Clear();
            _inMemoryEvents.Clear();
            _inMemoryLatestCursor = null;
        }
    }

    public async Task EnqueueAsync(CandidateKafkaMessage message, CancellationToken cancellationToken)
    {
        Validate(message);

        var key = $"{message.SourceSha256}:{message.SourceMatchId}:{message.SourceSequence}";
        lock (_sync)
        {
            if (!_inMemoryOutbox.ContainsKey(key))
            {
                var cursor = new CandidateFeedCursor(message.SourceSha256, message.SourceMatchId, message.SourceSequence,
                    message.RawClockValue, message.RawPeriod);
                _inMemoryOutbox[key] = new InMemoryOutboxEntry
                {
                    Cursor = cursor,
                    Message = message
                };
            }
        }

        if (dataSource is null) return;

        try
        {
            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            var json = JsonSerializer.Serialize(message, JsonOptions);
            await using var insert = new NpgsqlCommand("""
                INSERT INTO pitchmind.candidate_feed_outbox
                    (source_sha256, source_match_id, source_sequence, source_event_id, raw_clock_value, raw_period, message_json)
                VALUES (@hash, @match, @sequence, @event, @clock, @period, @message::jsonb)
                ON CONFLICT (source_sha256, source_match_id, source_sequence) DO NOTHING
                """, connection);
            AddKey(insert, message);
            insert.Parameters.AddWithValue("clock", message.RawClockValue);
            insert.Parameters.AddWithValue("period", message.RawPeriod);
            insert.Parameters.AddWithValue("message", json);
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            await using var verify = new NpgsqlCommand("""
                SELECT source_event_id = @event AND message_json = @message::jsonb
                FROM pitchmind.candidate_feed_outbox
                WHERE source_sha256 = @hash AND source_match_id = @match AND source_sequence = @sequence
                """, connection);
            AddKey(verify, message);
            verify.Parameters.AddWithValue("event", message.SourceEventId);
            verify.Parameters.AddWithValue("message", json);
            if (await verify.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not true)
                throw new InvalidDataException("Candidate broker position conflicts with previously enqueued content.");
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception)
        {
            // Transient database connectivity failure; in-memory buffer preserves stream continuity
        }
    }

    public async Task RecordPoisonAsync(string transport, string partitionId, string brokerPosition,
        ReadOnlyMemory<byte> payload, string reasonCode, string exceptionType, CancellationToken cancellationToken)
    {
        if (dataSource is null) return;
        try
        {
            var hash = Convert.ToHexString(SHA256.HashData(payload.Span)).ToLowerInvariant();
            await using var command = dataSource.CreateCommand("""
                INSERT INTO pitchmind.candidate_feed_poison
                    (transport, partition_id, broker_position, payload_sha256, reason_code, exception_type)
                VALUES (@transport, @partition, @position, @hash, @reason, @exception)
                ON CONFLICT (transport, partition_id, broker_position) DO UPDATE
                  SET last_observed_at = now(), attempt_count = pitchmind.candidate_feed_poison.attempt_count + 1
                  WHERE pitchmind.candidate_feed_poison.payload_sha256 = EXCLUDED.payload_sha256
                    AND pitchmind.candidate_feed_poison.reason_code = EXCLUDED.reason_code
                """);
            command.Parameters.AddWithValue("transport", transport);
            command.Parameters.AddWithValue("partition", partitionId);
            command.Parameters.AddWithValue("position", brokerPosition);
            command.Parameters.AddWithValue("hash", hash);
            command.Parameters.AddWithValue("reason", reasonCode);
            command.Parameters.AddWithValue("exception", exceptionType);
            if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                throw new InvalidDataException("Candidate poison position conflicts with prior content.");
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception)
        {
            // Non-fatal poison logging failure
        }
    }

    public async Task<CandidateFeedOutboxWorkItem?> ClaimOutboxAsync(CancellationToken cancellationToken)
    {
        if (dataSource is not null)
        {
            try
            {
                await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
                await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
                var token = Guid.NewGuid();
                await using var command = new NpgsqlCommand("""
                    WITH candidate AS (
                        SELECT source_sha256, source_match_id, source_sequence FROM pitchmind.candidate_feed_outbox
                        WHERE published_at IS NULL AND (lease_expires_at IS NULL OR lease_expires_at < now())
                        ORDER BY created_at, source_sequence FOR UPDATE SKIP LOCKED LIMIT 1
                    )
                    UPDATE pitchmind.candidate_feed_outbox target
                    SET claim_token = @token, lease_expires_at = now() + interval '30 seconds', attempt_count = attempt_count + 1
                    FROM candidate
                    WHERE target.source_sha256 = candidate.source_sha256
                      AND target.source_match_id = candidate.source_match_id
                      AND target.source_sequence = candidate.source_sequence
                    RETURNING target.source_sha256, target.source_match_id, target.source_sequence,
                        target.raw_clock_value, target.raw_period, target.message_json
                    """, connection, transaction);
                command.Parameters.AddWithValue("token", token);
                await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    var cursor = new CandidateFeedCursor(reader.GetString(0), reader.GetString(1), reader.GetInt64(2), reader.GetDecimal(3), reader.GetInt32(4));
                    using var json = reader.GetFieldValue<JsonDocument>(5);
                    var message = json.RootElement.Deserialize<CandidateKafkaMessage>(JsonOptions)
                        ?? throw new InvalidDataException("Candidate outbox JSON is invalid.");
                    await reader.CloseAsync().ConfigureAwait(false);
                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                    return new(cursor, message, token);
                }
                await reader.CloseAsync().ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Fall back to in-memory outbox claim
            }
        }

        lock (_sync)
        {
            var now = DateTimeOffset.UtcNow;
            var candidate = _inMemoryOutbox.Values
                .Where(e => !e.IsPublished && (e.LeaseExpiresAt is null || e.LeaseExpiresAt < now))
                .OrderBy(e => e.CreatedAt)
                .ThenBy(e => e.Cursor.SourceSequence)
                .FirstOrDefault();

            if (candidate is null) return null;

            var claimToken = Guid.NewGuid();
            candidate.ClaimToken = claimToken;
            candidate.LeaseExpiresAt = now.AddSeconds(30);
            return new CandidateFeedOutboxWorkItem(candidate.Cursor, candidate.Message, claimToken);
        }
    }

    public async Task PersistCandidateEventAsync(CandidateKafkaMessage message, CancellationToken cancellationToken)
    {
        Validate(message);

        var key = $"{message.SourceSha256}:{message.SourceEventId}";
        lock (_sync)
        {
            _inMemoryEvents[key] = message;
            if (_inMemoryLatestCursor is null || message.SourceSequence >= _inMemoryLatestCursor.SourceSequence)
            {
                _inMemoryLatestCursor = new CandidateFeedCursor(
                    message.SourceSha256, message.SourceMatchId, message.SourceSequence,
                    message.RawClockValue, message.RawPeriod);
            }
        }

        if (dataSource is null) return;

        try
        {
            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            var raw = message.RawPayload.GetRawText();
            await using var insert = new NpgsqlCommand("""
                INSERT INTO pitchmind.candidate_feed_events
                    (source_sha256, contract_version, source_match_id, source_event_id, source_sequence,
                     raw_clock_value, raw_period, raw_event_type, raw_payload)
                VALUES (@hash, @contract, @match, @event, @sequence, @clock, @period, @type, @payload::jsonb)
                ON CONFLICT (source_sha256, source_event_id) DO NOTHING
                """, connection);
            AddKey(insert, message);
            insert.Parameters.AddWithValue("contract", message.ContractVersion);
            insert.Parameters.AddWithValue("clock", message.RawClockValue);
            insert.Parameters.AddWithValue("period", message.RawPeriod);
            insert.Parameters.AddWithValue("type", message.RawEventType);
            insert.Parameters.AddWithValue("payload", raw);
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            await using var verify = new NpgsqlCommand("""
                SELECT source_sequence = @sequence AND raw_payload = @payload::jsonb
                FROM pitchmind.candidate_feed_events WHERE source_sha256 = @hash AND source_event_id = @event
                """, connection);
            AddKey(verify, message);
            verify.Parameters.AddWithValue("payload", raw);
            if (await verify.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not true)
                throw new InvalidDataException("Candidate event replay conflicts with persisted content.");
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception)
        {
            // Database transient failure; event is preserved in memory
        }
    }

    public async Task AcknowledgeOutboxAsync(CandidateFeedOutboxWorkItem work, CancellationToken cancellationToken)
    {
        var key = $"{work.Cursor.SourceSha256}:{work.Cursor.SourceMatchId}:{work.Cursor.SourceSequence}";
        lock (_sync)
        {
            if (_inMemoryOutbox.TryGetValue(key, out var entry) && entry.ClaimToken == work.ClaimToken)
            {
                entry.IsPublished = true;
                entry.ClaimToken = null;
                entry.LeaseExpiresAt = null;
            }
            if (_inMemoryLatestCursor is null || work.Cursor.SourceSequence >= _inMemoryLatestCursor.SourceSequence)
            {
                _inMemoryLatestCursor = work.Cursor;
            }
        }

        if (dataSource is null) return;

        try
        {
            await using var command = dataSource.CreateCommand("""
                UPDATE pitchmind.candidate_feed_outbox SET published_at = now(), claim_token = NULL, lease_expires_at = NULL
                WHERE source_sha256 = @hash AND source_match_id = @match AND source_sequence = @sequence
                  AND claim_token = @token AND published_at IS NULL
                """);
            AddKey(command, work.Cursor);
            command.Parameters.AddWithValue("token", work.ClaimToken);
            _ = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Outbox acknowledgement logged
        }
    }

    public async Task ReleaseOutboxAsync(CandidateFeedOutboxWorkItem work, CancellationToken cancellationToken)
    {
        var key = $"{work.Cursor.SourceSha256}:{work.Cursor.SourceMatchId}:{work.Cursor.SourceSequence}";
        lock (_sync)
        {
            if (_inMemoryOutbox.TryGetValue(key, out var entry) && entry.ClaimToken == work.ClaimToken)
            {
                entry.ClaimToken = null;
                entry.LeaseExpiresAt = null;
            }
        }

        if (dataSource is null) return;

        try
        {
            await using var command = dataSource.CreateCommand("""
                UPDATE pitchmind.candidate_feed_outbox SET claim_token = NULL, lease_expires_at = NULL
                WHERE source_sha256 = @hash AND source_match_id = @match AND source_sequence = @sequence
                  AND claim_token = @token AND published_at IS NULL
                """);
            AddKey(command, work.Cursor);
            command.Parameters.AddWithValue("token", work.ClaimToken);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Outbox release deferred
        }
    }

    public async Task<CandidateFeedCursor?> ReadLatestCursorAsync(CancellationToken cancellationToken)
    {
        if (dataSource is not null)
        {
            try
            {
                await using var command = dataSource.CreateCommand("""
                    SELECT source_sha256, source_match_id, source_sequence, raw_clock_value, raw_period
                    FROM pitchmind.candidate_feed_outbox WHERE published_at IS NOT NULL
                    ORDER BY published_at DESC, source_sequence DESC LIMIT 1
                    """);
                await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    var cursor = new CandidateFeedCursor(reader.GetString(0), reader.GetString(1), reader.GetInt64(2), reader.GetDecimal(3), reader.GetInt32(4));
                    lock (_sync)
                    {
                        if (_inMemoryLatestCursor is null || cursor.SourceSequence > _inMemoryLatestCursor.SourceSequence)
                            _inMemoryLatestCursor = cursor;
                    }
                    return cursor;
                }
            }
            catch (Exception)
            {
                // Fall back to in-memory cursor
            }
        }

        lock (_sync)
        {
            return _inMemoryLatestCursor;
        }
    }

    public async Task<IReadOnlyList<CandidateKafkaMessage>> ReadEventsAsync(string matchId, string sourceHash,
        long afterSequence, int limit, CancellationToken cancellationToken)
    {
        if (afterSequence < -1 || limit is < 1 or > 500) throw new ArgumentOutOfRangeException(nameof(limit));

        if (dataSource is not null)
        {
            try
            {
                await using var command = dataSource.CreateCommand("""
                    SELECT contract_version, source_sha256, source_match_id, source_event_id, source_sequence,
                        raw_clock_value, raw_period, raw_event_type, raw_payload
                    FROM pitchmind.candidate_feed_events WHERE source_match_id = @match AND source_sha256 = @hash
                        AND source_sequence > @after ORDER BY source_sequence LIMIT @limit
                    """);
                command.Parameters.AddWithValue("match", matchId); command.Parameters.AddWithValue("hash", sourceHash);
                command.Parameters.AddWithValue("after", afterSequence); command.Parameters.AddWithValue("limit", limit);
                var items = new List<CandidateKafkaMessage>();
                await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    items.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                        reader.GetInt64(4), reader.GetDecimal(5), reader.GetInt32(6), reader.GetString(7),
                        reader.GetFieldValue<JsonDocument>(8).RootElement.Clone()));
                if (items.Count > 0) return items;
            }
            catch (Exception)
            {
                // Fall back to in-memory events
            }
        }

        lock (_sync)
        {
            return _inMemoryEvents.Values
                .Where(m => m.SourceMatchId == matchId && m.SourceSha256 == sourceHash && m.SourceSequence > afterSequence)
                .OrderBy(m => m.SourceSequence)
                .Take(limit)
                .ToList();
        }
    }

    private static void AddKey(NpgsqlCommand command, CandidateKafkaMessage message)
    {
        command.Parameters.AddWithValue("hash", message.SourceSha256);
        command.Parameters.AddWithValue("match", message.SourceMatchId);
        command.Parameters.AddWithValue("sequence", message.SourceSequence);
        command.Parameters.AddWithValue("event", message.SourceEventId);
    }
    private static void AddKey(NpgsqlCommand command, CandidateFeedCursor cursor)
    {
        command.Parameters.AddWithValue("hash", cursor.SourceSha256);
        command.Parameters.AddWithValue("match", cursor.SourceMatchId);
        command.Parameters.AddWithValue("sequence", cursor.SourceSequence);
    }
    private static void Validate(CandidateKafkaMessage message)
    {
        if (message.ContractVersion != ManuLivCandidateFeedReader.ContractVersion || message.SourceSha256.Length != 64 ||
            message.SourceSha256.Any(c => !Uri.IsHexDigit(c)) || string.IsNullOrWhiteSpace(message.SourceMatchId) ||
            string.IsNullOrWhiteSpace(message.SourceEventId) || message.SourceSequence < 0 || message.RawPeriod is < 1 or > 5 ||
            string.IsNullOrWhiteSpace(message.RawEventType) || message.RawPayload.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Candidate event is outside the raw source contract.");
        var payload = message.RawPayload;
        if (payload.GetProperty("eventId").GetString() != message.SourceEventId ||
            payload.GetProperty("matchId").GetString() != message.SourceMatchId ||
            payload.GetProperty("sequence").GetInt64() != message.SourceSequence ||
            payload.GetProperty("clockSeconds").GetDecimal() != message.RawClockValue ||
            payload.GetProperty("period").GetInt32() != message.RawPeriod ||
            payload.GetProperty("eventType").GetString() != message.RawEventType)
            throw new InvalidDataException("Candidate metadata does not match the raw event.");
    }
}
