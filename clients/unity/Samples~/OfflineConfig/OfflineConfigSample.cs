using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BijiSDK.Unity;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace BijiSDK.Samples
{
    /// <summary>Fetches or stages JSON, then explicitly activates validated configuration.</summary>
    public sealed class OfflineConfigSample : MonoBehaviour
    {
        [SerializeField] private TextAsset sampleJson;
        [SerializeField] private bool useRemoteServer = true;
        [SerializeField] private bool useGitHubPages;
        [SerializeField] private string pagesBaseUrl = "https://viju-tag.github.io/bijiSDK/";
        [SerializeField] private string serverBaseUrl = "http://127.0.0.1:8765";
        [SerializeField] private bool allowDevelopmentHttp = true;
        [SerializeField] private bool refreshOnStartup = true;
        [SerializeField] private bool refreshOnResume = true;

        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private ConfigClient _client;
        private bool _busy;
        private bool _refreshing;
        private bool _hasRemote;
        private bool _destroyed;
        private string _message = "Initializing local configuration...";
        private string _platform;

        private async void Start()
        {
            await RunOperationAsync(InitializeClientAsync);
            if (refreshOnStartup) await RefreshAsync(false);
        }

        private void OnGUI()
        {
            if (_destroyed)
                return;

            GUILayout.BeginArea(new Rect(20, 20, Math.Max(300, Math.Min(620, Screen.width - 40)), 540), GUI.skin.box);
            GUILayout.Label("bijiSDK configuration sample");
            GUILayout.Label(_hasRemote ? "Server: " + (useGitHubPages ? pagesBaseUrl : serverBaseUrl) : "Local JSON mode");
            GUILayout.Label(_message);

            var initialized = _client != null && _client.GetStatus().IsInitialized;
            if (initialized)
            {
                var status = _client.GetStatus();
                var snapshot = _client.GetSnapshot();
                var energy = snapshot.Get<EnergySettings>("energy");
                GUILayout.Label("Active revision: " + (status.ActiveRevision ?? "(bundled defaults)"));
                GUILayout.Label("Pending revision: " + (status.PendingRevision ?? "(none)"));
                GUILayout.Label("Energy source: " + snapshot.GetSource("energy"));
                GUILayout.Label("Last attempt: " + FormatTime(status.LastAttemptUtc));
                GUILayout.Label("Last success: " + FormatTime(status.LastSuccessUtc));
                GUILayout.Label("Next eligible refresh: " + FormatTime(status.NextEligibleRefreshUtc));
                if (_refreshing) GUILayout.Label("Fetching in background; active values remain available.");
                GUILayout.Label("Maximum: " + energy.Maximum + "; refill seconds: " + energy.RefillSeconds);
            }

            var previousEnabled = GUI.enabled;
            GUI.enabled = previousEnabled && !_busy && initialized;
            if (GUILayout.Button("Stage sample JSON"))
                StageSample();
            if (GUILayout.Button("Activate pending"))
                ActivatePending();
            GUI.enabled = previousEnabled && !_busy && !_refreshing && initialized && _hasRemote;
            if (GUILayout.Button("Refresh from server"))
                RefreshFromServer();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (GUILayout.Button("Force refresh (development)"))
                ForceRefreshFromServer();
#endif
            GUI.enabled = previousEnabled && !_busy && !_refreshing;
            if (GUILayout.Button("Reload saved state"))
                ReloadSavedState();
            GUI.enabled = previousEnabled;
            GUILayout.EndArea();
        }

        [ContextMenu("Stage sample JSON")]
        private async void StageSample()
        {
            await RunOperationAsync(async () =>
            {
                if (_client == null || !_client.GetStatus().IsInitialized)
                    throw new InvalidOperationException("Initialize the sample before staging data.");

                var result = await _client.StageJsonAsync(CreateSampleJson(), _lifetime.Token);
                ShowResult("Stage", result);
            });
        }

        [ContextMenu("Activate pending")]
        private async void ActivatePending()
        {
            await RunOperationAsync(async () =>
            {
                if (_client == null || !_client.GetStatus().IsInitialized)
                    throw new InvalidOperationException("Initialize the sample before activation.");

                var result = await _client.ActivatePendingAsync(_lifetime.Token);
                ShowResult("Activate", result);
            });
        }

        [ContextMenu("Reload saved state")]
        private async void ReloadSavedState()
        {
            if (_refreshing) return;
            await RunOperationAsync(InitializeClientAsync);
        }

        private async Task InitializeClientAsync()
        {
            ReleaseClient();
            _platform = GetPlatform();
            var options = new ConfigOptions(
                "bijisdk-offline-sample", "development", _platform, 120, 1);
            var definition = new ConfigDefinition<EnergySettings>(
                "energy",
                new EnergySettings(80, 180),
                SerializeEnergy,
                DeserializeEnergy,
                ValidateEnergy);

            ConfigRemoteOptions remoteOptions = null;
            if (useRemoteServer)
            {
                var baseUri = new Uri(useGitHubPages ? pagesBaseUrl : serverBaseUrl, UriKind.Absolute);
                remoteOptions = new ConfigRemoteOptions(baseUri,
                    allowInsecureHttp: !useGitHubPages && allowDevelopmentHttp && baseUri.Scheme == Uri.UriSchemeHttp);
            }
            if (useGitHubPages && remoteOptions != null)
            {
                var dispatcher = new UnityMainThreadDispatcher();
                var storage = new RecoverableFileConfigStorage(
                    Path.Combine(Application.persistentDataPath, "bijisdk"));
                var transport = new PagesConfigTransport(new UnityWebRequestConfigTransport(dispatcher),
                    remoteOptions.BaseUri, options);
                _client = new ConfigClient(options, new IConfigDefinition[] { definition },
                    storage, dispatcher, transport, remoteOptions);
            }
            else
            {
                _client = UnityConfigClientFactory.Create(options, new IConfigDefinition[] { definition }, remoteOptions);
            }
            _hasRemote = remoteOptions != null;
            _client.UpdateAvailable += OnUpdateAvailable;
            _client.ConfigActivated += OnConfigActivated;
            _client.ErrorOccurred += OnErrorOccurred;
            var result = await _client.InitializeAsync(_lifetime.Token);
            ShowResult("Initialize", result);
        }

        private static string FormatTime(DateTimeOffset? value)
        {
            return value.HasValue ? value.Value.ToString("u") : "(none)";
        }

        [ContextMenu("Refresh from server")]
        private async void RefreshFromServer()
        {
            await RefreshAsync(false);
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        [ContextMenu("Force refresh from server")]
        private async void ForceRefreshFromServer()
        {
            await RefreshAsync(true);
        }
#endif

        private async Task RefreshAsync(bool force)
        {
            if (_destroyed || _refreshing || !_hasRemote || _client == null ||
                !_client.GetStatus().IsInitialized)
                return;
            _refreshing = true;
            try
            {
                var result = await _client.RefreshAsync(_lifetime.Token, force);
                ShowResult("Refresh", result);
            }
            catch (OperationCanceledException)
            {
                if (!_destroyed) _message = "Refresh cancelled.";
            }
            catch (Exception exception)
            {
                if (!_destroyed)
                {
                    _message = exception.Message;
                    Debug.LogException(exception, this);
                }
            }
            finally
            {
                _refreshing = false;
            }
        }

        private async void OnApplicationPause(bool paused)
        {
            if (!paused && refreshOnResume) await RefreshAsync(false);
        }

        private async Task RunOperationAsync(Func<Task> operation)
        {
            if (_busy || _destroyed)
                return;

            _busy = true;
            try
            {
                await operation();
            }
            catch (OperationCanceledException)
            {
                if (!_destroyed)
                    _message = "Operation cancelled.";
            }
            catch (Exception exception)
            {
                if (!_destroyed)
                {
                    _message = exception.Message;
                    Debug.LogException(exception, this);
                }
            }
            finally
            {
                _busy = false;
            }
        }

        private string CreateSampleJson()
        {
            JObject payload;
            if (sampleJson != null)
            {
                payload = JObject.Parse(sampleJson.text, new JsonLoadSettings
                {
                    DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error
                });
            }
            else
            {
                payload = new JObject
                {
                    ["schemaVersion"] = 1,
                    ["projectId"] = "bijisdk-offline-sample",
                    ["environment"] = "development",
                    ["configContractVersion"] = 1,
                    ["revision"] = "sample-001",
                    ["publishedAt"] = "2026-10-29T10:00:00Z",
                    ["compatibility"] = new JObject
                    {
                        ["minAppBuild"] = 120,
                        ["maxAppBuild"] = 120
                    },
                    ["values"] = new JObject
                    {
                        ["energy"] = SerializeEnergy(new EnergySettings(100, 120))
                    }
                };
            }

            payload["platform"] = _platform;
            return payload.ToString(Formatting.None);
        }

        private static JToken SerializeEnergy(EnergySettings settings)
        {
            return new JObject
            {
                ["maximum"] = settings.Maximum,
                ["refillSeconds"] = settings.RefillSeconds
            };
        }

        private static EnergySettings DeserializeEnergy(JToken value)
        {
            if (!(value is JObject data))
                throw new FormatException("Energy settings must be an object.");

            return new EnergySettings(
                ReadRequiredInteger(data, "maximum"),
                ReadRequiredInteger(data, "refillSeconds"));
        }

        private static int ReadRequiredInteger(JObject data, string key)
        {
            var value = data[key];
            if (value == null || value.Type != JTokenType.Integer)
                throw new FormatException("Energy field '" + key + "' must be an integer.");

            var number = value.Value<long>();
            if (number < int.MinValue || number > int.MaxValue)
                throw new FormatException("Energy field '" + key + "' is outside the supported range.");

            return (int)number;
        }

        private static string ValidateEnergy(EnergySettings settings)
        {
            if (settings.Maximum <= 0)
                return "Energy maximum must be positive.";
            if (settings.RefillSeconds <= 0)
                return "Energy refill seconds must be positive.";
            return null;
        }

        private static string GetPlatform()
        {
            if (Application.isEditor)
                return "editor";
            if (Application.platform == RuntimePlatform.Android)
                return "android";
            if (Application.platform == RuntimePlatform.IPhonePlayer)
                return "ios";
            throw new NotSupportedException("This sample supports the Unity Editor, Android, and iOS.");
        }

        private void ShowResult(string operation, ConfigResult result)
        {
            if (_destroyed)
                return;
            _message = operation + ": " + result.Outcome;
            if (result.Error != null)
                _message += " (" + result.Error.Category + ": " + result.Error.Message + ")";
        }

        private void OnUpdateAvailable(string revision)
        {
            if (!_destroyed)
                Debug.Log("bijiSDK pending revision: " + revision, this);
        }

        private void OnConfigActivated(string revision)
        {
            if (!_destroyed)
                Debug.Log("bijiSDK activated revision: " + revision, this);
        }

        private void OnErrorOccurred(ConfigError error)
        {
            if (!_destroyed)
                Debug.LogWarning("bijiSDK " + error.Category + ": " + error.Message, this);
        }

        private void ReleaseClient()
        {
            if (_client == null)
                return;
            _client.UpdateAvailable -= OnUpdateAvailable;
            _client.ConfigActivated -= OnConfigActivated;
            _client.ErrorOccurred -= OnErrorOccurred;
            _client.Dispose();
            _client = null;
            _hasRemote = false;
        }

        private void OnDestroy()
        {
            _destroyed = true;
            _lifetime.Cancel();
            ReleaseClient();
            _lifetime.Dispose();
        }

        private sealed class EnergySettings
        {
            public int Maximum { get; }
            public int RefillSeconds { get; }

            public EnergySettings(int maximum, int refillSeconds)
            {
                Maximum = maximum;
                RefillSeconds = refillSeconds;
            }
        }
    }
}
