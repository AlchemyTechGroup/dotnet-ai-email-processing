namespace AiGmailLabeler.Core.Models;

public enum ProcessorStatus
{
    Handled,
    Skipped,
    Failed,
    StopChain
}

public record ProcessorOutcome
{
    public ProcessorStatus Status { get; init; }
    public IReadOnlyList<string> AddedLabels { get; init; } = [];
    public IReadOnlyList<string> RemovedLabels { get; init; } = [];
    public string? Notes { get; init; }

    public static ProcessorOutcome Handled(string? notes = null) =>
        new() { Status = ProcessorStatus.Handled, Notes = notes };

    public static ProcessorOutcome Handled(IEnumerable<string> addedLabels, string? notes = null) =>
        new() { Status = ProcessorStatus.Handled, AddedLabels = addedLabels.ToList(), Notes = notes };

    public static ProcessorOutcome Handled(IEnumerable<string> addedLabels, IEnumerable<string> removedLabels, string? notes = null) =>
        new() { Status = ProcessorStatus.Handled, AddedLabels = addedLabels.ToList(), RemovedLabels = removedLabels.ToList(), Notes = notes };

    public static ProcessorOutcome Skipped(string? notes = null) =>
        new() { Status = ProcessorStatus.Skipped, Notes = notes };

    public static ProcessorOutcome Failed(string? notes = null) =>
        new() { Status = ProcessorStatus.Failed, Notes = notes };

    public static ProcessorOutcome StopChain(string? notes = null) =>
        new() { Status = ProcessorStatus.StopChain, Notes = notes };

    public static ProcessorOutcome StopChain(IEnumerable<string> addedLabels, string? notes = null) =>
        new() { Status = ProcessorStatus.StopChain, AddedLabels = addedLabels.ToList(), Notes = notes };
}
