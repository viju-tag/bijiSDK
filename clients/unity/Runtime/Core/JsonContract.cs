using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BijiSDK
{
    internal sealed class ConfigException : Exception
    {
        public ConfigErrorCategory Category { get; }
        public ConfigException(ConfigErrorCategory category, string message) : base(message)
        {
            Category = category;
        }
    }

    internal static class JsonContract
    {
        internal static JObject ParseObject(string json, int maxBytes)
        {
            if (json == null || Encoding.UTF8.GetByteCount(json) > maxBytes)
                throw new ConfigException(ConfigErrorCategory.Protocol, "JSON is null or exceeds the payload size limit.");
            try
            {
                // Json.NET accepts comments and nonstandard syntax by default. A lexical pass
                // restricts the input to JSON tokens before loading and rejecting duplicates.
                StrictJsonSyntax.Validate(json);
                using (var text = new StringReader(json))
                using (var reader = new JsonTextReader(text))
                {
                    reader.DateParseHandling = DateParseHandling.None;
                    reader.MaxDepth = 64;
                    var token = JToken.Load(reader, new JsonLoadSettings
                    {
                        CommentHandling = CommentHandling.Load,
                        DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error
                    });
                    if (reader.Read())
                        throw new JsonReaderException("Trailing content is not allowed.");
                    if (!(token is JObject root))
                        throw new JsonReaderException("The root must be an object.");
                    ValidateNumbers(root);
                    return root;
                }
            }
            catch (ConfigException) { throw; }
            catch (Exception exception) when (exception is JsonException || exception is FormatException ||
                                               exception is OverflowException)
            {
                throw new ConfigException(ConfigErrorCategory.Protocol, "Invalid JSON: " + exception.Message);
            }
        }

        internal static void ValidateNumbers(JToken token)
        {
            var tokens = token is JContainer container ? container.DescendantsAndSelf() : new[] { token };
            foreach (var current in tokens)
            {
                if (current.Type == JTokenType.Integer)
                {
                    var number = BigInteger.Parse(current.ToString(Formatting.None), CultureInfo.InvariantCulture);
                    if (number < -9007199254740991L || number > 9007199254740991L)
                        throw new ConfigException(ConfigErrorCategory.Protocol, "Integer exceeds the interoperable JSON range.");
                }
                else if (current.Type == JTokenType.Float)
                {
                    double number = current.Value<double>();
                    if (double.IsNaN(number) || double.IsInfinity(number))
                        throw new ConfigException(ConfigErrorCategory.Protocol, "Non-finite numbers are not allowed.");
                }
            }
        }

        internal static string Text(JObject source, string key)
        {
            JToken token = source[key];
            if (token == null || token.Type != JTokenType.String || string.IsNullOrWhiteSpace(token.Value<string>()))
                throw new ConfigException(ConfigErrorCategory.Protocol, "Required nonblank string: " + key);
            return token.Value<string>();
        }

        internal static long Integer(JObject source, string key)
        {
            JToken token = source[key];
            if (token == null || token.Type != JTokenType.Integer)
                throw new ConfigException(ConfigErrorCategory.Protocol, "Required integer: " + key);
            return token.Value<long>();
        }

        internal static JObject Object(JObject source, string key)
        {
            if (!(source[key] is JObject result))
                throw new ConfigException(ConfigErrorCategory.Protocol, "Required object: " + key);
            return result;
        }

        internal static string CanonicalJson(JToken token)
        {
            return Sort(token).ToString(Formatting.None);
        }

        private static JToken Sort(JToken token)
        {
            if (token is JObject obj)
                return new JObject(obj.Properties().OrderBy(p => p.Name, StringComparer.Ordinal)
                    .Select(p => new JProperty(p.Name, Sort(p.Value))));
            if (token is JArray array)
                return new JArray(array.Select(Sort));
            return token.DeepClone();
        }

        internal static string Hash(JToken token)
        {
            using (var sha = SHA256.Create())
            {
                byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(CanonicalJson(token)));
                return BitConverter.ToString(bytes).Replace("-", string.Empty).ToLowerInvariant();
            }
        }
    }
}
