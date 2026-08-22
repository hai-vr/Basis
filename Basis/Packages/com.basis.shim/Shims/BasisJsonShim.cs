using System;
using System.Globalization;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Basis.Shims
{
	public static class BasisJson
	{
		public const int MaxTextLength = 2 * 1024 * 1024, MaxDepth = 64;
		public static BasisJsonNode Parse(string text) => Parse(text, out _);
		public static BasisJsonNode Parse(string text, out string error)
		{
			error = null;
			if (text == null) { error = "No text"; return null; }
			if (text.Length > MaxTextLength) { error = "Text too long (" + text.Length + " > " + MaxTextLength + ")"; return null; }
			try
			{
				using (StringReader stringReader = new StringReader(text))
				using (JsonTextReader reader = new JsonTextReader(stringReader) { MaxDepth = MaxDepth, DateParseHandling = DateParseHandling.None, FloatParseHandling = FloatParseHandling.Double })
				{
					JToken token = JToken.ReadFrom(reader);
					if (reader.Read()) { error = "Unexpected content after the JSON value"; return null; }
					return new BasisJsonNode(token);
				}
			}
			catch (Exception e) { error = e.Message; return null; }
		}
	}
	public sealed class BasisJsonNode
	{
		readonly JToken token;
		internal BasisJsonNode(JToken token) { this.token = token; }
		public bool IsObject => token.Type == JTokenType.Object;
		public bool IsArray => token.Type == JTokenType.Array;
		public bool IsString => token.Type == JTokenType.String;
		public bool IsNumber => token.Type == JTokenType.Integer || token.Type == JTokenType.Float;
		public bool IsBool => token.Type == JTokenType.Boolean;
		public bool IsNull => token.Type == JTokenType.Null || token.Type == JTokenType.Undefined;
		public int Count => token is JArray array ? array.Count : token is JObject obj ? obj.Count : 0;
		public BasisJsonNode Get(int index) => token is JArray array && index >= 0 && index < array.Count ? new BasisJsonNode(array[index]) : null;
		public BasisJsonNode Get(string key)
		{
			if (key == null || !(token is JObject obj)) return null;
			JToken child = obj[key];
			return child == null ? null : new BasisJsonNode(child);
		}
		public bool Has(string key) => key != null && token is JObject obj && obj.ContainsKey(key);
		public string KeyAt(int index)
		{
			if (!(token is JObject obj) || index < 0 || index >= obj.Count) return null;
			int i = 0;
			foreach (JProperty property in obj.Properties()) { if (i == index) return property.Name; i++; }
			return null;
		}
		public string[] Keys
		{
			get
			{
				if (!(token is JObject obj)) return new string[0];
				string[] keys = new string[obj.Count];
				int i = 0;
				foreach (JProperty property in obj.Properties()) keys[i++] = property.Name;
				return keys;
			}
		}
		public string AsString(string fallback)
		{
			switch (token.Type)
			{
				case JTokenType.String: return (string)token;
				case JTokenType.Integer:
				case JTokenType.Float:
				case JTokenType.Boolean: return Convert.ToString(((JValue)token).Value, CultureInfo.InvariantCulture);
				default: return fallback;
			}
		}
		bool TryDouble(out double value)
		{
			value = 0;
			switch (token.Type)
			{
				case JTokenType.Integer:
				case JTokenType.Float:
					object raw = ((JValue)token).Value;
					if (raw is IConvertible convertible) { value = convertible.ToDouble(CultureInfo.InvariantCulture); return true; }
					if (raw is System.Numerics.BigInteger big) { value = (double)big; return true; }
					return false;
				case JTokenType.String: return double.TryParse((string)token, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
				case JTokenType.Boolean: value = (bool)token ? 1 : 0; return true;
				default: return false;
			}
		}
		public double AsDouble(double fallback) => TryDouble(out double value) ? value : fallback;
		public float AsFloat(float fallback) => TryDouble(out double value) ? (float)value : fallback;
		public int AsInt(int fallback)
		{
			if (!TryDouble(out double value) || double.IsNaN(value)) return fallback;
			if (value >= int.MaxValue) return int.MaxValue;
			if (value <= int.MinValue) return int.MinValue;
			return (int)value;
		}
		public bool AsBool(bool fallback)
		{
			switch (token.Type)
			{
				case JTokenType.Boolean: return (bool)token;
				case JTokenType.Integer:
				case JTokenType.Float: return TryDouble(out double value) ? value != 0 : fallback;
				case JTokenType.String:
					string text = ((string)token).Trim();
					if (string.Equals(text, "true", StringComparison.OrdinalIgnoreCase) || text == "1") return true;
					if (string.Equals(text, "false", StringComparison.OrdinalIgnoreCase) || text == "0") return false;
					return fallback;
				default: return fallback;
			}
		}
		public string GetString(string key, string fallback) { BasisJsonNode node = Get(key); return node == null ? fallback : node.AsString(fallback); }
		public int GetInt(string key, int fallback) { BasisJsonNode node = Get(key); return node == null ? fallback : node.AsInt(fallback); }
		public float GetFloat(string key, float fallback) { BasisJsonNode node = Get(key); return node == null ? fallback : node.AsFloat(fallback); }
		public bool GetBool(string key, bool fallback) { BasisJsonNode node = Get(key); return node == null ? fallback : node.AsBool(fallback); }
		public string[] GetStringArray(string key)
		{
			BasisJsonNode node = Get(key);
			if (node == null) return new string[0];
			if (node.token is JArray array)
			{
				string[] values = new string[array.Count];
				for (int i = 0; i < values.Length; i++) values[i] = new BasisJsonNode(array[i]).AsString("");
				return values;
			}
			string single = node.AsString(null);
			return single == null ? new string[0] : new string[] { single };
		}
	}
}
