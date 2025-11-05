using System;
using System.IO;
using System.Net;

namespace Plugin.NuGetPluginProvider.NuGetClient
{
	/// <summary>JSON request to a remote server</summary>
	public class JsonRequest
	{
		/// <summary>Send a request to a remote server</summary>
		/// <typeparam name="J">Expected result type when the server responds with HTTP 200</typeparam>
		/// <param name="postValue">POST a request to a remote server. If <c>postValue</c>==null, the request will be sent as GET</param>
		/// <param name="absoluteUrl">Relative path to the server</param>
		/// <param name="method">HTTP method</param>
		/// <param name="safeCode">Array of additional HTTP codes for serializing the response</param>
		/// <exception cref="ApplicationException">The server response is not 200 or 204</exception>
		/// <returns>Deserialized response from the server or default(T)</returns>
		public void InvokeRequest<J, X>(Uri absoluteUrl, out J json, out X xml)
		{
			String response = this.InvokeRequest(absoluteUrl, out String contentType);

			if(response == null)
			{
				json = default;
				xml = default;
			}

			try
			{
				switch(contentType)
				{
				case "application/json":
					json = Serializer.JavaScriptDeserialize<J>(response);
					xml = default;
					break;
				case "application/atom+xml; type=entry; charset=utf-8":
				case "application/atom+xml; type=feed; charset=utf-8":
					json = default;
					xml = Serializer.XmlDeserialize<X>(response);
					break;
				default:
					json = default;
					xml = default;
					break;
				}
			} catch(FormatException exc)
			{//To analyze situations with an incorrect response from the server
				exc.Data.Add("json", response);
				throw;
			} catch(ArgumentException exc)
			{
				exc.Data.Add("json", response);
				throw;
			}
		}

		/// <summary>Send a request to a remote server</summary>
		/// <param name="absoluteUrl">Relative path to the server</param>
		/// <param name="contentType">XML/JSON</param>
		/// <exception cref="ApplicationException">The server response is not 200 or 204</exception>
		/// <returns>String representation of the server response</returns>
		public String InvokeRequest(Uri absoluteUrl, out String contentType)
		{
			HttpWebResponse response = null;
			try
			{
				response = this.InvokeRequestI(null, absoluteUrl, "GET");

				HttpStatusCode code = response.StatusCode;
				contentType = response.ContentType;
				if(code == HttpStatusCode.OK)
				{
					using(StreamReader reader = new StreamReader(response.GetResponseStream()))
						return reader.ReadToEnd();
				} else
					switch(code)
					{
					case HttpStatusCode.NoContent:
						return null;
					case HttpStatusCode.Unauthorized:
						throw new UnauthorizedAccessException();
					default:
						throw new ApplicationException(String.Format("Invalid response recieved: {0}", code));
					}
			} finally
			{
				response?.Close();
			}
		}

		/// <summary>Depending on the data being transferred, we change the JSON payload, method, and request content header.</summary>
		/// <param name="postValues">Data to be transferred to the server.</param>
		/// <param name="method">HTTP method.</param>
		/// <param name="request">Request to be modified.</param>
		protected virtual void ModifyRequest(Object postValues, String method, ref HttpWebRequest request)
		{
			request.Method = method ?? (postValues == null ? "GET" : "POST");

			if(postValues != null)
			{
				request.ContentType = "application/json";

				String post = Serializer.JavaScriptSerialize(postValues);

				using(StreamWriter writer = new StreamWriter(request.GetRequestStream()))
					writer.Write(post);
			}
		}

		/// <summary>Send a request to the server and receive a response</summary>
		/// <param name="postValues">Data for sending a POST request. Otherwise, GET</param>
		/// <param name="absoluteUrl">Relative URL to the service on the remote server</param>
		/// <param name="cookies">Cookies</param>
		/// <param name="method">Execution method</param>
		/// <returns>HTTP response from the server</returns>
		protected virtual HttpWebResponse InvokeRequestI(Object postValues, Uri absoluteUrl, String method = null)
		{
			HttpWebRequest request = (HttpWebRequest)WebRequest.Create(absoluteUrl);
			request.Proxy = new WebProxy() { UseDefaultCredentials = true, };

			this.ModifyRequest(postValues, method, ref request);

			try
			{
				HttpWebResponse response = (HttpWebResponse)request.GetResponse();
				return response;
			} catch(WebException exc)
			{
				if(exc.Response == null)
				{
					exc.Data.Add("Url", absoluteUrl);
					exc.Data.Add("POST", postValues);
					throw;
				} else
					return (HttpWebResponse)exc.Response;
			}
		}
	}
}