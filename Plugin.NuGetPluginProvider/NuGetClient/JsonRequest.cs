using System;
using System.IO;
using System.Net;

namespace Plugin.NuGetPluginProvider.NuGetClient
{
	/// <summary>JSON запрос удалённого сервера</summary>
	public class JsonRequest
	{
		/// <summary>Послать запрос на удалённый сервер</summary>
		/// <typeparam name="J">Тип ожидаемого результата при ответе сервера HTTP 200</typeparam>
		/// <param name="postValue">POST запросу удалённого сервера. Если <c>postValue</c>==null, то запрос будет отправлен как GET</param>
		/// <param name="absoluteUrl">Релятивный путь до сервера</param>
		/// <param name="method">HTTP метод</param>
		/// <param name="safeCode">Массив дополнительных HTTP кодов при которых сериализовать ответ</param>
		/// <exception cref="ApplicationException">Ответ от сервера не равен 200 или 204</exception>
		/// <returns>Десериализованный ответ от сервера или default(T)</returns>
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
			{//Для разбора ситуаций с неверным ответом от сервера
				exc.Data.Add("json", response);
				throw;
			} catch(ArgumentException exc)
			{
				exc.Data.Add("json", response);
				throw;
			}
		}

		/// <summary>Послать запрос на удалённый сервер</summary>
		/// <param name="absoluteUrl">Релятивный путь до сервера</param>
		/// <param name="contentType">XML/JSON</param>
		/// <exception cref="ApplicationException">Ответ от сервера не равен 200 или 204</exception>
		/// <returns>Строковое представление ответа от сервера</returns>
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

		/// <summary>В заваисимости от передаваемых данных меняем полезную нагрузку в вид JSON, метод и заголовок содержимого запроса</summary>
		/// <param name="postValues">Данные, которые необходимо передать на сервер</param>
		/// <param name="method">HTTP method</param>
		/// <param name="request">Изменяемый Request</param>
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

		/// <summary>Отправить запрос на сервер и получить ответ</summary>
		/// <param name="postValues">Данные для отправки POST запроса. Иначе GET</param>
		/// <param name="absoluteUrl">Релятивная ссылка до сервиса на удалённом серере</param>
		/// <param name="cookies">Печеньки</param>
		/// <param name="method">Метод выполнения</param>
		/// <returns>HTTP ответ от сервера</returns>
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
					throw exc;
				} else
					return (HttpWebResponse)exc.Response;
			}
		}
	}
}