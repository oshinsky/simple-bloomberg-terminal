using System.Collections.Concurrent;

namespace simple_bloomberg_terminal.Services.Extraction.Measurement;

public enum MeasureJobStatus { Running, Done, Error }

// One measurement progress event. Fast workers report planning and chunk transitions; the lead agent
// reports completion. The controller adds the filing because it owns the batch context.
public record MeasureProgress(
    int Run, string Phase,
    IReadOnlyList<FastWorkerChunkInfo>? Plan = null,
    int ChunkIndex = -1, int Found = 0,
    int FastWorkerClaims = 0, int LeadAgentClaims = 0,
    string Filing = "", string? Error = null);

// Live state for one run, reusing the scan widget's chunk model before storing the lead-agent result.
public class MeasureRunState
{
    public string Filing { get; init; } = "";
    public int Run { get; init; }
    public string Phase { get; set; } = "queued";   // queued | scanning | lead-agent | done
    public List<ScanChunkState> Chunks { get; } = new();
    public int FastWorkerClaims { get; set; }
    public int Errors { get; set; }
    public int LeadAgentClaims { get; set; }
}

// State for one detached measurement batch. The singleton store lets long runs outlive the request
// while the page polls their progress.
public class MeasureJob
{
    public string Id { get; init; } = Guid.NewGuid().ToString("n");
    public int Runs { get; init; }
    public bool StrictCounterparties { get; init; }
    public MeasureJobStatus Status { get; set; } = MeasureJobStatus.Running;
    public string? Error { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    // Concurrent run callbacks and status polling share mutable state, so both require the lock.
    public List<MeasureRunState> RunStates { get; } = new();
    public object Lock { get; } = new();

    public List<CounterpartyMeasurementResult> Results { get; } = new();
    public string RowsJson { get; set; } = "[]";

    // Applies a progress event, creating its run on first sight so event order does not matter.
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

                case "fast-worker-scan-done":
                    state.Phase = "lead-agent";
                    state.FastWorkerClaims = p.FastWorkerClaims;
                    break;

                case "lead-agent-done":
                    state.Phase = "done";
                    state.LeadAgentClaims = p.LeadAgentClaims;
                    break;

                case "lead-agent-error":
                    state.Errors++;
                    break;
            }
        }
    }

    private static ScanChunkState? Chunk(MeasureRunState state, int index) =>
        index >= 0 && index < state.Chunks.Count ? state.Chunks[index] : null;
}

// Tracks detached measurement batches across requests while the browser retains the active job ID.
public class MeasureJobStore
{
    private readonly ConcurrentDictionary<string, MeasureJob> _jobs = new();

    public void Add(MeasureJob job) => _jobs[job.Id] = job;

    public MeasureJob? Get(string id) => _jobs.TryGetValue(id, out var j) ? j : null;

    public void Remove(string id) => _jobs.TryRemove(id, out _);
}
