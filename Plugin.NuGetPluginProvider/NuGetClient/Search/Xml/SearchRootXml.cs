using System;
using System.Runtime.Serialization;
using System.Xml.Serialization;

namespace Plugin.NuGetPluginProvider.NuGetClient.Search.Xml
{
	[DataContract(Name = "feed", Namespace = "http://www.w3.org/2005/Atom")]
	[XmlRoot("feed", Namespace = "http://www.w3.org/2005/Atom")]
	public class SearchRootXml
	{
		[XmlRoot("entry")]
		[DataContract(Name = "entry")]
		public class SearchEntryXml
		{
			[XmlRoot("properties", Namespace = "http://schemas.microsoft.com/ado/2007/08/dataservices")]
			public class SearchPropertiesXml
			{
				[XmlElement(ElementName = "Version")]
				public String Version { get; set; }

				[XmlElement(ElementName = "Description")]
				public String Description { get; set; }

				public Version GetVersion()
				{
					return new Version(this.Version);
				}
			}

			[XmlRoot("content")]
			public class SearchDownloadXml
			{
				[XmlAttribute("type")]
				public String type { get; set; }

				[XmlAttribute("src")]
				public String src { get; set; }
			}

			[XmlElement("entry")]
			[DataMember(Name = "entry")]
			public String id { get; set; }

			[XmlElement("title")]
			[DataMember(Name = "title")]
			public String title { get; set; }

			[XmlElement("content")]
			[DataMember(Name = "content")]
			public SearchDownloadXml content { get; set; }

			[XmlElement("properties", Namespace = "http://schemas.microsoft.com/ado/2007/08/dataservices/metadata")]
			public SearchPropertiesXml properties { get; set; }
		}

		[XmlElement("entry")]
		public SearchEntryXml[] entry { get; set; }
	}
}
