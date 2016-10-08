using System;
using System.Collections.Generic;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace UltraHook
{
	public enum ProxyState
	{
		Stopped = 0,
		Stopping,
		Starting,
		Running,
	}

    public class Proxy
    {
		const string InitUrl = "http://www.ultrahook.com/init";

		static readonly HttpClient m_globalClient = new HttpClient();
		static readonly HttpClient m_streamClient = new HttpClient()
		{
			Timeout = TimeSpan.FromMilliseconds(Timeout.Infinite)
		};

		object m_lock = new object();
		CancellationTokenSource m_cancelSrc;
		Task m_runTask;
		int m_state;

		public string Key
		{
			get;
			private set;
		}

		public Uri Destination
		{
			get;
			private set;
		}

		public string Subdomain
		{
			get;
			private set;
		}

		public ProxyState State
		{
			get { return (ProxyState)Interlocked.Add(ref m_state, 0); }
			private set { Interlocked.Exchange(ref m_state, (int)value); }
		}

		public Proxy(string key, string subdomain, Uri destination)
		{
			if(key == null)
			{
				throw new ArgumentNullException(nameof(key));
			}

			if(subdomain == null)
			{
				throw new ArgumentNullException(nameof(subdomain));
			}

			if(destination == null)
			{
				throw new ArgumentNullException(nameof(destination));
			}

			if(string.IsNullOrWhiteSpace(key))
			{
				throw new ArgumentException("A valid key must be provided.", nameof(key));
			}

			if(string.IsNullOrWhiteSpace(subdomain))
			{
				throw new ArgumentException("A valid subdomain must be provided.", nameof(subdomain));
			}

			if(subdomain.StartsWith("-") || subdomain.EndsWith("-"))
			{
				throw new ArgumentException("A subdomain must not begin or end with a \'-\' character.", nameof(subdomain));
			}

			this.Key = key.Trim();
			this.Destination = destination;
			this.Subdomain = subdomain.Trim();

			for(int i = 0; i < this.Subdomain.Length; ++i)
			{
				if(!IsValidSubdomain(this.Subdomain))
				{
					throw new ArgumentException("A subdomain may only contain alpha-numeric characters and hyphens. Two or more consecutive hyphens are not allowed.", nameof(subdomain));
				}
			}
		}

		public static bool IsValidSubdomain(string subdomain)
		{
			for(int i = 0; i < subdomain.Length; ++i)
			{
				if(!char.IsLetterOrDigit(subdomain[i])
					&& (subdomain[i] != '-'
						|| (subdomain[i] == '-'
							&& (i + 1 >= subdomain.Length || subdomain[i + 1] == '-'))))
				{
					return false;
				}
			}

			return true;
		}

		public async Task<bool> Start()
		{
			if((ProxyState)Interlocked.Exchange(ref m_state, (int)ProxyState.Starting) != ProxyState.Stopped)
			{
				return false;
			}

			Uri streamUrl;
			string nameSpace;

			try
			{
				using(var responseMsg = await m_globalClient.PostAsync(InitUrl, new FormUrlEncodedContent(new Dictionary<string, string>()
				{
					["key"] = this.Key,
					["host"] = this.Subdomain,
					["version"] = "0.1.4"

				})).ConfigureAwait(false))
				{
					var jsonStr = await responseMsg.Content.ReadAsStringAsync().ConfigureAwait(false);
					dynamic json = Newtonsoft.Json.JsonConvert.DeserializeObject(jsonStr);

					streamUrl = new Uri((string)json.url);
					nameSpace = json["namespace"];
				}
			}
			catch(Exception ex)
			{
				ProxyState exPrevState = (ProxyState)Interlocked.Exchange(ref m_state, (int)ProxyState.Stopped);

				if(exPrevState != ProxyState.Starting)
				{
					throw new UltraHookException($"Invalid proxy state \'{exPrevState}'. Starting was expected.", ex);
				}

				throw;
			}

			var sp = ServicePointManager.FindServicePoint(new Uri(streamUrl.GetComponents(UriComponents.Scheme | UriComponents.Host, UriFormat.UriEscaped)));

			System.Diagnostics.Debug.WriteLine($"Retrieved service point {sp.GetHashCode()}.");

			sp.MaxIdleTime = Timeout.Infinite;
			sp.SetTcpKeepAlive(true, 10000, 1000);
			sp.ConnectionLimit = 20;

			m_cancelSrc = new CancellationTokenSource();

			ProxyState prevState = (ProxyState)Interlocked.Exchange(ref m_state, (int)ProxyState.Running);

			System.Diagnostics.Debug.Assert(prevState == ProxyState.Starting);

			m_runTask = Task.Run(async () =>
			{
				byte[] blockBuf = new byte[4096];
				char[] block = new char[4096]; // 4k
				char[] buf = new char[2048];
				int bufIndex = 0;
				var token = m_cancelSrc.Token;

				while(this.State == ProxyState.Running)
				{
					try
					{
						using(var responseStream = await m_streamClient.GetStreamAsync(streamUrl).ConfigureAwait(false))
						{
							token.Register(() =>
							{
								responseStream.Close();

							}, false);

							int charsRead = 0;

							do
							{
								int bytesRead = await responseStream.ReadAsync(blockBuf, 0, blockBuf.Length);

								if(bytesRead > 0)
								{
									int charCount = Encoding.UTF8.GetCharCount(blockBuf, 0, bytesRead);

									while(charCount > block.Length)
									{
										block = new char[block.Length * 2];
									}

									try
									{
										charsRead = Encoding.UTF8.GetChars(blockBuf, 0, bytesRead, block, 0);
									}
									catch(Exception ex)
									{
										System.Diagnostics.Debug.WriteLine(ex.ToString());
										charsRead = 0;
									}
								}
								else
								{
									charsRead = 0;
								}

								for(int i = 0; i < charsRead; ++i)
								{
									if(bufIndex >= buf.Length)
									{
										char[] temp = buf;
										buf = new char[buf.Length * 2];

										Buffer.BlockCopy(temp, 0, buf, 0, temp.Length * sizeof(char));
									}

									buf[bufIndex] = block[i];
									++bufIndex;

									// use -2 because we just incremented bufIndex to the next position
									if(block[i] == '\n' && bufIndex > 1 && buf[bufIndex - 2] == '\n')
									{
										try
										{
											if(bufIndex > 2)
											{
												string jsonStr = Encoding.UTF8.GetString(Convert.FromBase64CharArray(buf, 0, bufIndex));

												System.Diagnostics.Debug.WriteLine($"Request:\n{jsonStr}");

												dynamic json = Newtonsoft.Json.JsonConvert.DeserializeObject(jsonStr);

												ProcessMsg(json, nameSpace);
											}
										}
										catch(Exception ex)
										{
											System.Diagnostics.Debug.WriteLine(ex.ToString());
										}
										finally
										{
											bufIndex = 0;
										}
									}
								}

							} while(charsRead > 0 && this.State == ProxyState.Running);
						}
					}
					catch(Exception ex)
					{
						System.Diagnostics.Debug.WriteLine(ex.ToString());
					}
				}
			});

			return true;
		}

		void ProcessMsg(dynamic json, string nameSpace)
		{
			switch((string)json.type)
			{
				case "init":
					{
						System.Diagnostics.Debug.WriteLine($"Forwarding Activated: http://{this.Subdomain}.{nameSpace}.ultrahook.com -> {this.Destination}");
						break;
					}
				case "error":
					{
						System.Diagnostics.Debug.WriteLine($"Error: {(string)json.mesage}");
						break;
					}
				case "warning":
					{
						System.Diagnostics.Debug.WriteLine($"Warning: {(string)json.mesage}");
						break;
					}
				case "request":
					{
						System.Diagnostics.Debug.WriteLine($"Forwarding Request: http://{this.Subdomain}.{nameSpace}.ultrahook.com -> {this.Destination}");

						ThreadPool.QueueUserWorkItem(async (state) =>
						{
							try
							{
								byte[] payload = Encoding.UTF8.GetBytes((string)json.body);

								UriBuilder bldr = new UriBuilder(this.Destination);
								bldr.Path += (string)json.path;
								bldr.Query = (string)json.query;

								Uri reqUri = bldr.Uri;

								HttpWebRequest req = (HttpWebRequest)HttpWebRequest.Create(reqUri);
								req.Method = "POST";
								req.Headers.Clear();
								req.ServerCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) => true;
								req.ContentLength = payload.Length;

								IDynamicMetaObjectProvider headers = (IDynamicMetaObjectProvider)json.headers;

								foreach(var headerEntry in headers.GetMetaObject(Expression.Constant(headers)).GetDynamicMemberNames())
								{
									string value = json.headers[headerEntry];

									switch(headerEntry)
									{
										case "User-Agent":
											{
												req.UserAgent = value;
												break;
											}
										case "Accept":
											{
												req.Accept = value;
												break;
											}
										case "Content-Type":
											{
												req.ContentType = value;
												break;
											}
										default:
											{
												req.Headers.Add(headerEntry, value);
												break;
											}
									}
								}

								using(var strm = await req.GetRequestStreamAsync().ConfigureAwait(false))
								{
									await strm.WriteAsync(payload, 0, payload.Length).ConfigureAwait(false);
								}

								using(var response = (HttpWebResponse)await req.GetResponseAsync().ConfigureAwait(false))
								{
									using(var strm = new StreamReader(response.GetResponseStream()))
									{
										var str = await strm.ReadToEndAsync().ConfigureAwait(false);
										System.Diagnostics.Debug.WriteLine($"Request http://{this.Subdomain}.{nameSpace}.ultrahook.com -> {reqUri} returned {response.StatusCode} ({(int)response.StatusCode}) with body:\n{str}\n");
									}
								}
							}
							catch(Exception ex)
							{
								System.Diagnostics.Debug.WriteLine(ex.ToString());
							}
						});

						break;
					}
			}
		}

		public async Task Stop()
		{
			if(this.State == ProxyState.Running)
			{
				if(Interlocked.Exchange(ref m_state, (int)ProxyState.Stopping) == (int)ProxyState.Running)
				{
					System.Diagnostics.Debug.Assert(m_runTask != null);
					System.Diagnostics.Debug.Assert(m_cancelSrc != null);

					try
					{
						m_cancelSrc.Cancel();
						await m_runTask.ConfigureAwait(false);
					}
					finally
					{
						ProxyState prevState = (ProxyState)Interlocked.Exchange(ref m_state, (int)ProxyState.Stopped);

						System.Diagnostics.Debug.Assert(prevState == ProxyState.Stopping);
					}
				}
			}
		}

		public override bool Equals(object obj)
		{
			if(obj is Proxy)
			{
				return Equals((Proxy)obj);
			}

			return false;
		}

		public bool Equals(Proxy proxy)
		{
			if(proxy == null)
			{
				return false;
			}

			return proxy == this
				|| (this.Subdomain == proxy.Subdomain
					&& this.Key == proxy.Key
					&& this.Destination == proxy.Destination);
		}

		public override int GetHashCode()
		{
			return (this.Subdomain + this.Key + this.Destination).GetHashCode();
		}
	}
}
