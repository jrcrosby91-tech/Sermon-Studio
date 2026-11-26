namespace SermonStudio;

/// <summary>
/// Protects asynchronous work (outline generation, transcription, audio import) from writing its result into
/// a different sermon. Work captures a ticket before it starts; the result is applied only if the workspace
/// revision, the open sermon and the captured inputs are all unchanged.
/// </summary>
internal sealed class WorkspaceGuard
{
    internal readonly record struct Ticket(int Revision, string? SermonId, string[] Inputs);

    private int _revision;

    internal int Revision => Volatile.Read(ref _revision);

    /// <summary>Call whenever the open sermon changes (switch, new, delete, restore).</summary>
    internal void Advance() => Interlocked.Increment(ref _revision);

    internal Ticket Capture(string? sermonId, params string[] inputs) => new(Revision, sermonId, inputs.ToArray());

    internal bool IsCurrent(Ticket ticket, string? sermonId, params string[] inputs) =>
        ticket.Revision == Revision
        && string.Equals(ticket.SermonId, sermonId, StringComparison.Ordinal)
        && ticket.Inputs.Length == inputs.Length
        && ticket.Inputs.Zip(inputs).All(pair => string.Equals(pair.First, pair.Second, StringComparison.Ordinal));
}
