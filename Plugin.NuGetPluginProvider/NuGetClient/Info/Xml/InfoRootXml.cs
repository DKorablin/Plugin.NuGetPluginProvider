using System;
using System.Runtime.Serialization;
using System.Xml.Serialization;

namespace Plugin.NuGetPluginProvider.NuGetClient.Info.Xml
{
	[DataContract(Name = "entry", Namespace = "http://www.w3.org/2005/Atom")]
	[XmlRoot("entry", Namespace = "http://www.w3.org/2005/Atom")]
	public class InfoRootXml
	{
		[XmlRoot("content")]
		public class InfoDownloadXml
		{
			[XmlAttribute("type")]
			public String type { get; set; }

			[XmlAttribute("src")]
			public String src { get; set; }
		}

		[XmlRoot("properties", Namespace = "http://schemas.microsoft.com/ado/2007/08/dataservices")]
		public class InfoPropertiesXml
		{
			[XmlElement(ElementName = "Version")]
			public String Version { get; set; }

			public Version GetVersion()
				=> new Version(this.Version);
		}

		[DataMember(Name = "id")]
		[XmlElement("id")]
		public String id { get; set; }

		[DataMember(Name = "title")]
		[XmlElement("title")]
		public String title { get; set; }

		[XmlElement("content")]
		[DataMember(Name = "content")]
		public InfoDownloadXml content { get; set; }

		[XmlElement("properties", Namespace = "http://schemas.microsoft.com/ado/2007/08/dataservices/metadata")]
		public InfoPropertiesXml properties { get; set; }
	}
}