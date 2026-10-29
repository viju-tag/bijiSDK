using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace BijiSDK.Unity
{
    /// <summary>Creates config clients using Unity's main thread and persistent data path.</summary>
    public static class UnityConfigClientFactory
    {
        /// <summary>Call from Awake or Start. The caller owns initialization and disposal.</summary>
        public static ConfigClient Create(ConfigOptions options, IEnumerable<IConfigDefinition> definitions)
        {
            return Create(options, definitions, null);
        }

        /// <summary>Creates a client with optional HTTP refresh; initialization still uses local data only.</summary>
        public static ConfigClient Create(ConfigOptions options, IEnumerable<IConfigDefinition> definitions,
            ConfigRemoteOptions remoteOptions)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (definitions == null) throw new ArgumentNullException(nameof(definitions));

            var dispatcher = new UnityMainThreadDispatcher();
            var directory = Path.Combine(Application.persistentDataPath, "bijisdk");
            var storage = new RecoverableFileConfigStorage(directory);
            IConfigTransport transport = remoteOptions == null ? null
                : new UnityWebRequestConfigTransport(dispatcher, remoteOptions.AllowInsecureHttp);
            return new ConfigClient(options, definitions, storage, dispatcher, transport, remoteOptions);
        }
    }
}
