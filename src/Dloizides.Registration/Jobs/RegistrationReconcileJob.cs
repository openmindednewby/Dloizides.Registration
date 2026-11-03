using System.Globalization;
using Dloizides.Jobs.Abstractions;
using Dloizides.Jobs.Model;
using Dloizides.Registration.UseCases;

namespace Dloizides.Registration.Jobs;

public sealed class RegistrationReconcileJob(RegistrationReconciler reconciler) : ICheckpointableJob
{
    private const int CadenceMinutes = 15;

    public string Name => "registration-reconcile";

    public JobCadence Cadence => JobCadence.Every(TimeSpan.FromMinutes(CadenceMinutes));

    public async Task RunAsync(IJobContext context, CancellationToken cancellationToken)
    {
        var summary = await reconciler.RunOnceAsync(cancellationToken);
        if (summary.Failed > 0)
            throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture, $"Registration reconcile left {summary.Failed} attempt(s) failing; see logs for detail."));
    }
}
