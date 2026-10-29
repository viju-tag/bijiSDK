using System;
using System.IO;
using System.Threading.Tasks;
using BijiSDK.Samples;
using BijiSDK.Unity;
using Newtonsoft.Json;
using UnityEngine;

namespace BijiSDK.MobileVerification
{
    /// <summary>Captures durable launch/lifecycle state separately from the imported sample.</summary>
    public sealed class MobileConfigProbe : MonoBehaviour
    {
        private ConfigClient client;
        private bool busy;
        private bool destroyed;
        private string message = "Starting verification probe...";

        private async void Start()
        {
            await RunSafelyAsync(async () =>
            {
                string platform = Application.isEditor ? "editor" :
                    Application.platform == RuntimePlatform.Android ? "android" : "ios";
                string root = Path.Combine(Application.persistentDataPath, "mobile-verification");
                var context = new ConfigOptions("bijisdk-offline-sample", "development", platform, 120, 1);
                var dispatcher = new UnityMainThreadDispatcher();
                var remote = new ConfigRemoteOptions(new Uri(MobileConfigChecks.PagesUrl));
                var pages = new PagesConfigTransport(new UnityWebRequestConfigTransport(dispatcher), remote.BaseUri, context);
                client = MobileConfigChecks.CreateClient(context,
                    new RecoverableFileConfigStorage(Path.Combine(root, "relaunch")), dispatcher, pages, remote);
                ConfigResult initialized = await client.InitializeAsync();
                LogState("launch_before_network", initialized);
                if (initialized.Outcome == ConfigOutcome.ReadyWithSavedConfig)
                {
                    message = "Saved state restored before any network request. Refresh is manual.";
                    return;
                }
                int failures = await MobileConfigChecks.RunAsync(platform, Path.Combine(root, "runs", Guid.NewGuid().ToString("N")));
                message = "Automatic checks: " + (failures == 0 ? "passed" : "failed; see logcat");
            });
        }

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(20, 570, Math.Max(300, Math.Min(620, Screen.width - 40)), 280), GUI.skin.box);
            GUILayout.Label("Isolated lifecycle probe (separate from sample above)");
            GUILayout.Label(message);
            if (client != null && client.GetStatus().IsInitialized)
                GUILayout.Label(MobileConfigChecks.DescribeState(client).ToString(Formatting.None));
            bool previous = GUI.enabled;
            GUI.enabled = previous && !busy && client != null && client.GetStatus().IsInitialized;
            if (GUILayout.Button("Probe: force Pages refresh"))
                RunOperation(() => client.RefreshAsync(force: true), "refresh");
            if (GUILayout.Button("Probe: activate pending"))
                RunOperation(() => client.ActivatePendingAsync(), "activate");
            GUI.enabled = previous;
            GUILayout.EndArea();
        }

        private async void RunOperation(Func<Task<ConfigResult>> operation, string name)
        {
            await RunSafelyAsync(async () => LogState(name, await operation()));
        }

        private async Task RunSafelyAsync(Func<Task> operation)
        {
            if (busy || destroyed) return;
            busy = true;
            try { await operation(); }
            catch (Exception exception)
            {
                message = exception.Message;
                Debug.LogException(exception);
            }
            finally { busy = false; }
        }

        private void LogState(string operation, ConfigResult result = null)
        {
            if (destroyed || client == null || !client.GetStatus().IsInitialized) return;
            var state = MobileConfigChecks.DescribeState(client);
            state["operation"] = operation;
            state["platform"] = Application.platform.ToString();
            state["outcome"] = result?.Outcome.ToString();
            state["error"] = result?.Error?.ToString();
            message = operation + ": " + (result?.Outcome.ToString() ?? "readable");
            Debug.Log("BIJISDK_LIFECYCLE " + state.ToString(Formatting.None));
        }

        private void OnApplicationPause(bool paused) => LogState(paused ? "background" : "resume");

        private void OnDestroy()
        {
            destroyed = true;
            client?.Dispose();
        }
    }
}
