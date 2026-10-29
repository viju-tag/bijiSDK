using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace BijiSDK
{
    /// <summary>A stable resolved configuration. Reads return deserialized copies.</summary>
    public sealed class ConfigSnapshot
    {
        private readonly Dictionary<string, IConfigDefinition> definitions;
        private readonly Dictionary<string, JToken> values;
        private readonly Dictionary<string, ConfigValueSource> sources;
        public string Revision { get; }

        internal ConfigSnapshot(string revision, Dictionary<string, IConfigDefinition> definitions,
            Dictionary<string, JToken> values, Dictionary<string, ConfigValueSource> sources)
        {
            Revision = revision;
            this.definitions = new Dictionary<string, IConfigDefinition>(definitions, StringComparer.Ordinal);
            this.values = new Dictionary<string, JToken>(StringComparer.Ordinal);
            this.sources = new Dictionary<string, ConfigValueSource>(sources, StringComparer.Ordinal);
            foreach (var entry in values)
                this.values.Add(entry.Key, entry.Value.DeepClone());
        }

        public T Get<T>(string key)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (!definitions.TryGetValue(key, out IConfigDefinition definition))
                throw new KeyNotFoundException("Config key is not registered: " + key);
            if (definition.ValueType != typeof(T))
                throw new InvalidOperationException("Requested type does not match config key: " + key);
            return (T)definition.DeserializeCopy(values[key]);
        }

        public ConfigValueSource GetSource(string key)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (!sources.TryGetValue(key, out ConfigValueSource source))
                throw new KeyNotFoundException("Config key is not registered: " + key);
            return source;
        }
    }
}
