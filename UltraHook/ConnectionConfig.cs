using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Serialization;

namespace UltraHook
{
	[Serializable]
	public class ConnectionConfig
	{
		[XmlArray(ElementName = "ConnectionList")]
		[XmlArrayItem(ElementName = "Connection")]
		public ConnectionInfo[] Connections
		{
			get;
			set;
		}

		public void Save()
		{
			string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "UltraHook");

			if(!Directory.Exists(dir))
			{
				Directory.CreateDirectory(dir);
			}

			string path = Path.Combine(dir, "connections.xml");

			using(var fileStrm = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.None))
			{
				XmlWriterSettings settings = new XmlWriterSettings();
				settings.Indent = true;

				using(XmlWriter writer = XmlTextWriter.Create(fileStrm, settings))
				{
					XmlSerializer serializer = new XmlSerializer(typeof(ConnectionConfig));
					serializer.Serialize(writer, this);
				}
			}
		}

		public static ConnectionConfig Load()
		{
			string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "UltraHook", "connections.xml");

			if(File.Exists(path))
			{
				using(var fileStrm = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read))
				{
					XmlSerializer serializer = new XmlSerializer(typeof(ConnectionConfig));
					return serializer.Deserialize(fileStrm) as ConnectionConfig;
				}
			}

			return null;
		}
	}
}
