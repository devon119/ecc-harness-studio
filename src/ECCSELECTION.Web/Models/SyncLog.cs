using ECCSELECTION.Web.Models.Enums;

namespace ECCSELECTION.Web.Models;

public class SyncLog
{
    public int Id { get; set; }

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    public DateTime? CompletedAt { get; set; }

    public SyncStatus Status { get; set; } = SyncStatus.Running;

    public int TotalFound { get; set; }

    public int AddedCount { get; set; }

    public int UpdatedCount { get; set; }

    public int RemovedCount { get; set; }

    public string? ErrorMessage { get; set; }
}
