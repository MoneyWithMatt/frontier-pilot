namespace FrontierAirportBuilder.Workflow;

/// <summary>MSFS exposes one user aircraft. Serialize Frontier cargo/fuel/passenger operations across app instances.</summary>
internal sealed class AircraftPayloadOperationLease : IDisposable
{
    private readonly Semaphore semaphore;
    private AircraftPayloadOperationLease(Semaphore semaphore) => this.semaphore = semaphore;
    public static async Task<AircraftPayloadOperationLease> AcquireAsync(CancellationToken token)
    {
        var semaphore = new Semaphore(1, 1, "Local\\FrontierPilot-AircraftPayload-v1");
        try
        {
            token.ThrowIfCancellationRequested();
            if (semaphore.WaitOne(0)) return new(semaphore);
            if (!await Task.Run(() => semaphore.WaitOne(TimeSpan.FromSeconds(10)), token).ConfigureAwait(false))
                throw new IOException("Another Frontier aircraft loading operation is running. Retry after it finishes.");
            return new(semaphore);
        }
        catch { semaphore.Dispose(); throw; }
    }
    public void Dispose() { semaphore.Release(); semaphore.Dispose(); }
}
