using System;
using Newtonsoft.Json;

namespace Plugin.NuGetPluginProvider
{
	internal static class Serializer
	{
		private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings()
		{
			// Replace default collection values (e.g. the default feed list) instead of appending to them
			ObjectCreationHandling = ObjectCreationHandling.Replace,
			Formatting = Formatting.Indented,
		};

		/// <summary>Deserialize a string into an object</summary>
		/// <typeparam name="T">Object type</typeparam>
		/// <param name="json">String in JSON format</param>
		/// <returns>Deserialized object</returns>
		public static T JavaScriptDeserialize<T>(String json)
			=> String.IsNullOrEmpty(json)
				? default
				: JsonConvert.DeserializeObject<T>(json, Settings);

		/// <summary>Serialize object</summary>
		/// <param name="item">Object to serialize</param>
		/// <returns>String in JSON format</returns>
		public static String JavaScriptSerialize(Object item)
			=> item == null
				? null
				: JsonConvert.SerializeObject(item, Settings);
	}
}