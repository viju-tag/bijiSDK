using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BijiSDK.Unity;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using static BijiSDK.OfflineChecks.TestSupport;

namespace BijiSDK.OfflineChecks
{
    public static class UnityOfflineValidation
    {
        private static Task<int> checks;

        public static void Run()
        {
            Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);
            TestSupport.Root = Path.GetFullPath(Path.Combine(Application.dataPath, "../../.."));
            checks = RunAllAsync();
            EditorApplication.update += FinishWhenReady;
        }

        private static async Task<int> RunAllAsync()
        {
            int failures = await OfflineSuite.RunAsync(message => Debug.Log(message));
            failures += await HttpChecks.RunAllAsync(message => Debug.Log(message));
            failures += await PagesChecks.RunAllAsync(message => Debug.Log(message));
            try
            {
                await CheckUnityAdapterAsync();
                Debug.Log("PASS: Unity main-thread callbacks and real-file restart");
                if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("BIJI_HTTP_TEST_URL")))
                {
                    await UnityHttpIntegration.RunAsync();
                    Debug.Log("PASS: Live HTTP publication, ETag, explicit activation, rollback, offline restart");
                    await UnityHttpIntegration.CheckFaultTransportAsync();
                    Debug.Log("PASS: Unity HTTP byte limit, UTF-8, timeout, cancellation, redirect, throttling, malformed JSON");
                }
            }
            catch (Exception exception)
            {
                failures++;
                Debug.LogException(exception);
            }
            return failures;
        }

        private static async Task CheckUnityAdapterAsync()
        {
            int mainThread = Thread.CurrentThread.ManagedThreadId;
            var options = new ConfigOptions("example-game", "production", "editor", 120, 1);
            string directory = Path.Combine(TestSupport.Root, "verification", "unity-storage-data", Guid.NewGuid().ToString("N"));
            var storage = new RecoverableFileConfigStorage(directory);
            var dispatcher = new UnityMainThreadDispatcher();
            using (var factoryClient = UnityConfigClientFactory.Create(options, Definitions()))
            {
                True(factoryClient != null, "Unity factory creation failed.");
            }
            using (var client = new ConfigClient(options, Definitions(), storage, dispatcher))
            {
                await client.InitializeAsync();
                var staged = new TaskCompletionSource<int>();
                var activated = new TaskCompletionSource<int>();
                client.UpdateAvailable += _ => staged.TrySetResult(Thread.CurrentThread.ManagedThreadId);
                client.ConfigActivated += _ => activated.TrySetResult(Thread.CurrentThread.ManagedThreadId);
                var response = JObject.Parse(Fixture());
                response["platform"] = "editor";
                Equal(ConfigOutcome.UpdateAvailable, (await client.StageJsonAsync(response.ToString())).Outcome);
                Equal(mainThread, await WithTimeout(staged.Task));
                Equal(80, client.Get<Energy>("energy").Maximum);
                Equal(ConfigOutcome.Activated, (await client.ActivatePendingAsync()).Outcome);
                Equal(mainThread, await WithTimeout(activated.Task));
            }
            using (var restored = new ConfigClient(options, Definitions(), storage, dispatcher))
            {
                Equal(ConfigOutcome.ReadyWithSavedConfig, (await restored.InitializeAsync()).Outcome);
                Equal(100, restored.Get<Energy>("energy").Maximum);
            }
        }

        private static async Task<T> WithTimeout<T>(Task<T> task)
        {
            if (await Task.WhenAny(task, Task.Delay(5000)) != task)
                throw new TimeoutException("Unity callback was not delivered.");
            return await task;
        }

        private static void FinishWhenReady()
        {
            if (!checks.IsCompleted) return;
            EditorApplication.update -= FinishWhenReady;
            if (checks.IsFaulted)
            {
                Debug.LogException(checks.Exception);
                EditorApplication.Exit(1);
                return;
            }
            if (checks.IsCanceled)
            {
                Debug.LogError("Offline validation cancelled.");
                EditorApplication.Exit(1);
                return;
            }
            Debug.Log("BIJISDK_UNITY_CHECKS_COMPLETE failures=" + checks.Result);
            EditorApplication.Exit(checks.Result == 0 ? 0 : 1);
        }
    }
}
