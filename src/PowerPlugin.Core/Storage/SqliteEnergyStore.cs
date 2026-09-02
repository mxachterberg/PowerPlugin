using Microsoft.Data.Sqlite;

namespace PowerPlugin.Core.Storage;

/// <summary>
/// SQLite backed history. The database is a single file in the application data folder and
/// is opened with WAL journaling so an unexpected shutdown cannot corrupt it.
/// </summary>
public sealed class SqliteEnergyStore : IEnergyStore
{
    private const int SchemaVersion = 1;

    private readonly SqliteConnection _connection;
    private readonly object _gate = new();
    private bool _disposed;

    public SqliteEnergyStore(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        string? directory = Path.GetDirectoryName(Path.GetFullPath(databasePath));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
        };

        _connection = new SqliteConnection(builder.ToString());
        _connection.Open();
    }

    public void Initialize()
    {
        lock (_gate)
        {
            Execute("PRAGMA journal_mode=WAL;");
            Execute("PRAGMA synchronous=NORMAL;");

            Execute("""
                CREATE TABLE IF NOT EXISTS minute_samples (
                    bucket_utc INTEGER PRIMARY KEY,
                    local_day  INTEGER NOT NULL,
                    energy_wh  REAL    NOT NULL,
                    covered_s  REAL    NOT NULL,
                    peak_w     REAL    NOT NULL,
                    min_w      REAL    NOT NULL,
                    samples    INTEGER NOT NULL
                );
                """);

            Execute("CREATE INDEX IF NOT EXISTS ix_minute_local_day ON minute_samples(local_day);");

            Execute("""
                CREATE TABLE IF NOT EXISTS component_hourly (
                    bucket_utc INTEGER NOT NULL,
                    component  TEXT    NOT NULL,
                    name       TEXT    NOT NULL,
                    category   INTEGER NOT NULL,
                    energy_wh  REAL    NOT NULL,
                    covered_s  REAL    NOT NULL,
                    PRIMARY KEY (bucket_utc, component)
                );
                """);

            Execute("CREATE TABLE IF NOT EXISTS meta (key TEXT PRIMARY KEY, value TEXT NOT NULL);");
            Execute("INSERT OR REPLACE INTO meta(key, value) VALUES ('schema_version', $v);",
                ("$v", SchemaVersion.ToString()));
        }
    }

    public void UpsertBucket(EnergyBucket bucket)
    {
        ArgumentNullException.ThrowIfNull(bucket);

        if (bucket.CoveredSeconds <= 0 && bucket.SampleCount == 0)
        {
            return;
        }

        lock (_gate)
        {
            Execute("""
                INSERT INTO minute_samples (bucket_utc, local_day, energy_wh, covered_s, peak_w, min_w, samples)
                VALUES ($t, $d, $e, $c, $p, $m, $n)
                ON CONFLICT(bucket_utc) DO UPDATE SET
                    energy_wh = energy_wh + excluded.energy_wh,
                    covered_s = covered_s + excluded.covered_s,
                    peak_w    = MAX(peak_w, excluded.peak_w),
                    min_w     = MIN(min_w, excluded.min_w),
                    samples   = samples + excluded.samples;
                """,
                ("$t", bucket.StartUtc.ToUnixTimeSeconds()),
                ("$d", bucket.LocalDay),
                ("$e", bucket.EnergyWattHours),
                ("$c", bucket.CoveredSeconds),
                ("$p", bucket.PeakWatts),
                ("$m", bucket.MinWatts),
                ("$n", bucket.SampleCount));
        }
    }

    public void UpsertComponentEnergy(DateTimeOffset hourStartUtc, IReadOnlyCollection<ComponentEnergy> components)
    {
        ArgumentNullException.ThrowIfNull(components);

        if (components.Count == 0)
        {
            return;
        }

        lock (_gate)
        {
            using SqliteTransaction transaction = _connection.BeginTransaction();

            foreach (ComponentEnergy component in components)
            {
                using SqliteCommand command = _connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = """
                    INSERT INTO component_hourly (bucket_utc, component, name, category, energy_wh, covered_s)
                    VALUES ($t, $k, $n, $c, $e, $s)
                    ON CONFLICT(bucket_utc, component) DO UPDATE SET
                        energy_wh = energy_wh + excluded.energy_wh,
                        covered_s = covered_s + excluded.covered_s,
                        name      = excluded.name;
                    """;
                command.Parameters.AddWithValue("$t", hourStartUtc.ToUnixTimeSeconds());
                command.Parameters.AddWithValue("$k", component.Key);
                command.Parameters.AddWithValue("$n", component.Name);
                command.Parameters.AddWithValue("$c", component.Category);
                command.Parameters.AddWithValue("$e", component.EnergyWattHours);
                command.Parameters.AddWithValue("$s", component.CoveredSeconds);
                command.ExecuteNonQuery();
            }

            transaction.Commit();
        }
    }

    public IReadOnlyList<EnergyBucket> GetBuckets(DateTimeOffset fromUtc, DateTimeOffset toUtc)
    {
        lock (_gate)
        {
            var result = new List<EnergyBucket>();

            using SqliteCommand command = _connection.CreateCommand();
            command.CommandText = """
                SELECT bucket_utc, local_day, energy_wh, covered_s, peak_w, min_w, samples
                FROM minute_samples
                WHERE bucket_utc >= $from AND bucket_utc <= $to
                ORDER BY bucket_utc;
                """;
            command.Parameters.AddWithValue("$from", fromUtc.ToUnixTimeSeconds());
            command.Parameters.AddWithValue("$to", toUtc.ToUnixTimeSeconds());

            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                result.Add(new EnergyBucket(
                    DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(0)),
                    reader.GetInt32(1),
                    reader.GetDouble(2),
                    reader.GetDouble(3),
                    reader.GetDouble(4),
                    reader.GetDouble(5),
                    reader.GetInt32(6)));
            }

            return result;
        }
    }

    public IReadOnlyList<DailyEnergy> GetDailyEnergy(DateOnly fromDay, DateOnly toDay)
    {
        lock (_gate)
        {
            var result = new List<DailyEnergy>();

            using SqliteCommand command = _connection.CreateCommand();
            command.CommandText = """
                SELECT local_day, SUM(energy_wh), SUM(covered_s), MAX(peak_w)
                FROM minute_samples
                WHERE local_day >= $from AND local_day <= $to
                GROUP BY local_day
                ORDER BY local_day;
                """;
            command.Parameters.AddWithValue("$from", DayKey.Encode(fromDay));
            command.Parameters.AddWithValue("$to", DayKey.Encode(toDay));

            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                result.Add(new DailyEnergy(
                    DayKey.Decode(reader.GetInt32(0)),
                    reader.GetDouble(1),
                    reader.GetDouble(2),
                    reader.GetDouble(3)));
            }

            return result;
        }
    }

    public IReadOnlyList<ComponentEnergy> GetComponentEnergy(DateTimeOffset fromUtc, DateTimeOffset toUtc)
    {
        lock (_gate)
        {
            var result = new List<ComponentEnergy>();

            using SqliteCommand command = _connection.CreateCommand();
            command.CommandText = """
                SELECT component, name, category, SUM(energy_wh), SUM(covered_s)
                FROM component_hourly
                WHERE bucket_utc >= $from AND bucket_utc <= $to
                GROUP BY component
                ORDER BY SUM(energy_wh) DESC;
                """;
            command.Parameters.AddWithValue("$from", fromUtc.ToUnixTimeSeconds());
            command.Parameters.AddWithValue("$to", toUtc.ToUnixTimeSeconds());

            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                result.Add(new ComponentEnergy(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetInt32(2),
                    reader.GetDouble(3),
                    reader.GetDouble(4)));
            }

            return result;
        }
    }

    public PeakRecord? GetAllTimePeak()
    {
        lock (_gate)
        {
            using SqliteCommand command = _connection.CreateCommand();
            command.CommandText = "SELECT bucket_utc, peak_w FROM minute_samples ORDER BY peak_w DESC LIMIT 1;";

            using SqliteDataReader reader = command.ExecuteReader();
            return reader.Read()
                ? new PeakRecord(reader.GetDouble(1), DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(0)))
                : null;
        }
    }

    public StoreTotals GetTotals()
    {
        lock (_gate)
        {
            using SqliteCommand command = _connection.CreateCommand();
            command.CommandText = """
                SELECT COALESCE(SUM(energy_wh), 0), COALESCE(SUM(covered_s), 0),
                       MIN(local_day), MAX(local_day), COUNT(DISTINCT local_day)
                FROM minute_samples;
                """;

            using SqliteDataReader reader = command.ExecuteReader();
            if (!reader.Read())
            {
                return StoreTotals.Empty;
            }

            DateOnly? first = reader.IsDBNull(2) ? null : DayKey.Decode(reader.GetInt32(2));
            DateOnly? last = reader.IsDBNull(3) ? null : DayKey.Decode(reader.GetInt32(3));

            return new StoreTotals(reader.GetDouble(0), reader.GetDouble(1), first, last, reader.GetInt32(4));
        }
    }

    public int PurgeOlderThan(DateTimeOffset cutoffUtc)
    {
        lock (_gate)
        {
            long cutoff = cutoffUtc.ToUnixTimeSeconds();
            int removed = Execute("DELETE FROM minute_samples WHERE bucket_utc < $t;", ("$t", cutoff));
            Execute("DELETE FROM component_hourly WHERE bucket_utc < $t;", ("$t", cutoff));
            return removed;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            Execute("DELETE FROM minute_samples;");
            Execute("DELETE FROM component_hourly;");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _connection.Dispose();
        SqliteConnection.ClearPool(_connection);
    }

    private int Execute(string sql, params (string Name, object Value)[] parameters)
    {
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText = sql;

        foreach ((string name, object value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return command.ExecuteNonQuery();
    }
}

/// <summary>Encodes a local calendar day as yyyyMMdd so it can be grouped in SQL.</summary>
public static class DayKey
{
    public static int Encode(DateOnly day) => (day.Year * 10000) + (day.Month * 100) + day.Day;

    public static DateOnly Decode(int key) => new(key / 10000, key / 100 % 100, key % 100);
}
