using System.Collections.Concurrent;

namespace simple_bloomberg_terminal.Services.Extraction;

public enum MeasureJobStatus { Running, Done, Error }

/// <summary>One progress event from a measurement run. The fast layer's events mirror
/// <see cref="ScanProgress"/> (a plan once, then per-chunk transitions); the strong layer reports
/// only when its single call lands. <paramref name="Filing"/> is stamped by the controller, which
/// knows the batch, not by the service, which sees one filing at a time.</summary>
public record MeasureProgress(
    int Run, string Phase,
    IReadOnlyList<ScanChunkInfo>? Plan = null,
    int ChunkIndex = -1, int Found = 0,
    int WorkerItems = 0, int LeadItems = 0,
    string Filing = "", string? Error = null);

/// <summary>One run's live state: the fast agent's per-chunk tree, then the strong agent's result.
/// <see cref="ScanChunkState"/> is reused from the scan widget — same shape, same meaning.</summary>
public class MeasureRunState
{
    public string Filing { get; init; } = "";
    public int Run { get; init; }
    public string Phase { get; set; } = "queued";   // queued | scanning | lead | done
    public List<ScanChunkState> Chunks { get; } = new();
    public int WorkerItems { get; set; }
    public int Errors { get; set; }
    public int LeadItems { get; set; }
}

/// <summary>
/// One detached measurement batch. Lives in <see cref="MeasureJobStore"/> (a singleton) so it
/// outlives the request that started it: the measurement runs for minutes and the page polls for
/// progress rather than holding a request open, exactly as <see cref="ScanJob"/> does for a scan.
/// </summary>
public class MeasureJob
{
    public string Id { get; init; } = Guid.NewGuid().ToString("n");
    public int Runs { get; init; }
    public bool StrictCounterparties { get; init; }
    public MeasureJobStatus Status { get; set; } = MeasureJobStatus.Running;
    public string? Error { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    // Mutated from N concurrent runs' progress callbacks and read by the status poll, so both sides
    // take the lock — these are plain mutable objects, not thread-safe on their own.
    public List<MeasureRunState> RunStates { get; } = new();
    public object Lock { get; } = new();

    public List<FilingMeasurement> Results { get; } = new();
    public string RowsJson { get; set; } = "[]";

    /// <summary>Fold one progress event into the live tree. Creates the run's row on first sight, so
    /// the order events arrive in doesn't matter.</summary>
    public void Apply(MeasureProgress p)
    {
        lock (Lock)
        {
            var state = RunStates.FirstOrDefault(r => r.Run == p.Run && r.Filing == p.Filing);
            if (state is null)
            {
                state = new MeasureRunState { Run = p.Run, Filing = p.Filing };
                RunStates.Add(state);
            }

            switch (p.Phase)
            {
                case "plan":
                    state.Phase = "scanning";
                    state.Chunks.Clear();
                    foreach (var c in p.Plan ?? [])
                        state.Chunks.Add(new ScanChunkState { Titles = c.Titles });
                    break;

                case "chunk-running":
                    if (Chunk(state, p.ChunkIndex) is { } running) running.Status = "Running";
                    break;

                case "chunk-done":
                    if (Chunk(state, p.ChunkIndex) is { } done)
                    {
                        done.Status = "Done";
                        done.Found = p.Found;
                    }
                    break;

                case "chunk-error":
                    if (Chunk(state, p.ChunkIndex) is { } failed)
                    {
                        failed.Status = "Error";
                        failed.Response = p.Error ?? "Unknown worker error.";
                    }
                    state.Errors++;
                    break;

                case "scan-done":
                    state.Phase = "lead";
                    state.WorkerItems = p.WorkerItems;
                    break;

                case "lead-done":
                    state.Phase = "done";
                    state.LeadItems = p.LeadItems;
                    break;
            }
        }
    }

    private static ScanChunkState? Chunk(MeasureRunState state, int index) =>
        index >= 0 && index < state.Chunks.Count ? state.Chunks[index] : null;
}

/// <summary>Tracks detached measurement batches across requests. Singleton, like
/// <see cref="ScanJobStore"/>; the browser holds the job id for the page it started.</summary>
public class MeasureJobStore
{
    private readonly ConcurrentDictionary<string, MeasureJob> _jobs = new();

    public void Add(MeasureJob job) => _jobs[job.Id] = job;

    public MeasureJob? Get(string id) => _jobs.TryGetValue(id, out var j) ? j : null;

    public void Remove(string id) => _jobs.TryRemove(id, out _);
}
