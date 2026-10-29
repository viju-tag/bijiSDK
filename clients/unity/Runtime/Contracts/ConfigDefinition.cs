using System;
using Newtonsoft.Json.Linq;

namespace BijiSDK
{
    /// <summary>
    /// A game-owned typed definition. Delegates must be pure and thread-safe.
    /// Deserialize must return a fresh instance for mutable values.
    /// Validation returns null or an empty string on success.
    /// </summary>
    public sealed class ConfigDefinition<T> : IConfigDefinition
    {
        private readonly T defaultValue;
        private readonly Func<T, JToken> serialize;
        private readonly Func<JToken, T> deserialize;
        private readonly Func<T, string> validate;
        public string Key { get; }
        public Type ValueType => typeof(T);

        public ConfigDefinition(string key, T defaultValue, Func<T, JToken> serialize,
            Func<JToken, T> deserialize, Func<T, string> validate = null)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("A config key must not be blank.", nameof(key));
            Key = key;
            this.defaultValue = defaultValue;
            this.serialize = serialize ?? throw new ArgumentNullException(nameof(serialize));
            this.deserialize = deserialize ?? throw new ArgumentNullException(nameof(deserialize));
            this.validate = validate;
        }

        public JToken CreateDefaultValue()
        {
            return ValidateAndCopy(serialize(defaultValue) ?? JValue.CreateNull());
        }

        public JToken ValidateAndCopy(JToken value)
        {
            var copy = (value ?? JValue.CreateNull()).DeepClone();
            JsonContract.ValidateNumbers(copy);
            T typed = deserialize(copy.DeepClone());
            string problem = validate?.Invoke(typed);
            if (!string.IsNullOrEmpty(problem))
                throw new ArgumentException("Config '" + Key + "' is invalid: " + problem);
            return copy;
        }

        public object DeserializeCopy(JToken value)
        {
            return deserialize(value.DeepClone());
        }
    }
}
