using System.Collections.Concurrent;
using simple_bloomberg_terminal.Models.ViewModels;

namespace simple_bloomberg_terminal.Services.Extraction.Chat;

public enum ScanJobStatus { Running, Done, Error }

// Live state for one parallel agent call, including bundled titles and its current status.
public class ScanChunkState
{
    public IReadOnlyList<string> Titles { get; init; } = [];
    public string Status { get; set; } = "Queued";
    public int Found { get; set; }
    // Stores the exact prompt and response so the widget can inspect each call and its failures.
    public string Prompt { get; set; } = "";
    public string Response { get; set; } = "";
}

// Groups the agent calls for one SEC Item so the widget can display them together.
public class ScanSection
{
    public string Item { get; init; } = "";
    public List<ScanChunkState> Chunks { get; } = new();
}

// State for one detached scan. It lives in the singleton store so work survives the starting request
// and the widget can poll it after navigation.
public class ScanJob
{
    public string Id { get; init; } = Guid.NewGuid().ToString("n");
    public long CompanyId { get; init; }
    public string CompanyName { get; init; } = "";
    public string Accession { get; init; } = "";
    public string Doc { get; init; } = "";
    public string Node { get; init; } = "REVENUE";
    public string? Form { get; init; }
    public string FilingLabel { get; init; } = "";   // e.g. "10-K 2024-01-31" for the widget header

    public ScanJobStatus Status { get; set; } = ScanJobStatus.Running;
    public string Progress { get; set; } = "Queued…"; // live phase text shown while running

    // The live scan tree is shared by concurrent workers and status polling, so access requires the lock.
    public List<ScanSection> Sections { get; } = new();
    public List<ScanChunkState> ChunkList { get; } = new();  // flat, index-aligned with the scan plan
    public object SectionsLock { get; } = new();
    public FastWorkerScanResult? Report { get; set; }
    public string Summary { get; set; } = "";        // auto AI prose shown first in the widget
    public string? Error { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }

    // Detached reply buffers let chat generation continue across navigation while the widget polls updates.
    public bool Replying { get; set; }
    public string ReplyBuffer { get; set; } = "";    // incremental answer text
    public string ReplyThink { get; set; } = "";     // incremental reasoning/thinking
    public string? ReplyError { get; set; }
}

// Tracks detached scans across requests. The browser keeps job IDs locally; this single-user store
// intentionally has no per-user partitioning.
public class ScanJobStore
{
    private readonly ConcurrentDictionary<string, ScanJob> _jobs = new();

    public void Add(ScanJob job) => _jobs[job.Id] = job;

    public ScanJob? Get(string id) => _jobs.TryGetValue(id, out var j) ? j : null;

    public void Remove(string id) => _jobs.TryRemove(id, out _);
}
