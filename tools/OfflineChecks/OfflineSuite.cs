using System;
using System.Threading.Tasks;

namespace BijiSDK.OfflineChecks
{
    internal static class OfflineSuite
    {
        internal static async Task<int> RunAsync(Action<string> report)
        {
            var tests = new (string Name, Func<Task> Run)[]
            {
                ("Defaults and defensive copies", CoreChecks.DefaultsAndIsolation),
                ("Staging, activation, immutable snapshots", CoreChecks.StageActivateAndSnapshots),
                ("Restore active and pending state", CoreChecks.RestoreActiveAndPending),
                ("Restore pending without auto-activation", CoreChecks.PendingOnlySurvivesRestart),
                ("Missing and empty values use defaults", CoreChecks.MissingAndEmptyUseDefaults),
                ("Invalid responses preserve state", CoreChecks.InvalidResponsesPreserveState),
                ("Strict envelope and feature validation", CoreChecks.StrictEnvelopeValidation),
                ("Idempotency, revision conflicts, rollback", CoreChecks.RevisionsAndRollback),
                ("Failed writes never apply", CoreChecks.FailedWritesDoNotApply),
                ("Cleanup failure cannot reactivate", CoreChecks.CleanupFailureCannotReactivate),
                ("Corrupt current restores backup", CoreChecks.CorruptCurrentRestoresBackup),
                ("Context isolation", CoreChecks.ContextIsolation),
                ("Subscriber failures are isolated", CoreChecks.SubscriberFailureIsIsolated),
                ("Cancellation and payload bounds", CoreChecks.CancellationAndPayloadBound),
                ("Concurrent stages remain consistent", CoreChecks.ConcurrentStagesRemainConsistent)
            };
            var failed = 0;
            foreach (var test in tests)
            {
                try
                {
                    await test.Run();
                    report("PASS: " + test.Name);
                }
                catch (Exception exception)
                {
                    failed++;
                    report("FAIL: " + test.Name + Environment.NewLine + exception);
                }
            }
            report("Offline core checks: " + (tests.Length - failed) + "/" + tests.Length + " passed.");
            return failed;
        }
    }
}
