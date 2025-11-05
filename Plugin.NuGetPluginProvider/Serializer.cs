using System;
using System.IO;
using System.Xml.Serialization;
using Newtonsoft.Json;

namespace Plugin.NuGetPluginProvider
{
	internal static class Serializer
	{
		/// <summary>Deserialize a string into an object</summary>
		/// <typeparam name="T">Object type</typeparam>
		/// <param name="json">String in JSON format</param>
		/// <returns>Deserialized object</returns>
		public static T JavaScriptDeserialize<T>(String json)
			=> String.IsNullOrEmpty(json)
				? default
				: (T)JsonConvert.DeserializeObject(json,typeof(T));

		/// <summary>Serialize object</summary>
		/// <param name="item">Object to serialize</param>
		/// <returns>String in JSON format</returns>
		public static String JavaScriptSerialize(Object item)
			=> item == null
				? null
				: JsonConvert.SerializeObject(item);

		/// <summary>Deserialize object</summary>
		/// <typeparam name="T">Object type</typeparam>
		/// <param name="xml">Object as XML</param>
		/// <returns>Deserialized object</returns>
		public static T XmlDeserialize<T>(String xml)
		{
			if(String.IsNullOrEmpty(xml))
				return default;

			XmlSerializer serializer = new XmlSerializer(typeof(T));
			using(StringReader reader = new StringReader(xml))
				return (T)serializer.Deserialize(reader);
		}
	}
}