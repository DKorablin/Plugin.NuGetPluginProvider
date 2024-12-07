using System;
using System.IO;
using System.Xml.Serialization;
using Newtonsoft.Json;

namespace Plugin.NuGetPluginProvider
{
	internal static class Serializer
	{
		/// <summary>Десериализовать строку в объект</summary>
		/// <typeparam name="T">Тип объекта</typeparam>
		/// <param name="json">Строка в формате JSON</param>
		/// <returns>Десериализованный объект</returns>
		public static T JavaScriptDeserialize<T>(String json)
			=> String.IsNullOrEmpty(json)
				? default
				: (T)JsonConvert.DeserializeObject(json,typeof(T));

		/// <summary>Сериализовать объект</summary>
		/// <param name="item">Объект для сериализации</param>
		/// <returns>Строка в формате JSON</returns>
		public static String JavaScriptSerialize(Object item)
			=> item == null
				? null
				: JsonConvert.SerializeObject(item);

		/// <summary>Десериализовать объект</summary>
		/// <typeparam name="T">Тип объекта</typeparam>
		/// <param name="xml">Объект в виде XML</param>
		/// <returns>Десериализованный объект</returns>
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