using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using UltraHook;

namespace UltraHookProxy
{
	/// <summary>
	/// Interaction logic for MainWindow.xaml
	/// </summary>
	public partial class MainWindow : Window
	{
		Dictionary<int, Proxy> m_proxies = new Dictionary<int, Proxy>();
		List<Task> m_closingTasks = new List<Task>();
		bool m_loadedCfg;

		public MainWindow()
		{
			InitializeComponent();
		}

		public override void OnApplyTemplate()
		{
			base.OnApplyTemplate();

			if(!m_loadedCfg)
			{
				m_loadedCfg = true;
				Load();
			}
		}

		protected override void OnClosing(CancelEventArgs e)
		{
			foreach(var pair in m_proxies)
			{
				if(pair.Value.State == ProxyState.Running)
				{
					m_closingTasks.Add(pair.Value.Stop());
				}
			}

			base.OnClosing(e);
		}

		protected override void OnClosed(EventArgs e)
		{
			base.OnClosed(e);

			if(m_closingTasks.Count > 0)
			{
				Task.WaitAll(m_closingTasks.ToArray());
			}
		}

		private void AddConnection_Click(object sender, RoutedEventArgs e)
		{
			ConnectionWindow win = new ConnectionWindow();
			win.Title = "Create Connection";
			win.Owner = this;

			var result = win.ShowDialog();

			if(result.HasValue && result.Value)
			{
				Proxy proxy = new Proxy(win.Key, win.Subdomain, new Uri(win.Destination));
				int hashCode = proxy.GetHashCode();

				if(!m_proxies.ContainsKey(hashCode))
				{
					m_proxies[hashCode] = proxy;

					m_viewEntries.Items.Add(proxy);

					Save();

					System.Threading.ThreadPool.QueueUserWorkItem(async (state) =>
					{
						try
						{
							await proxy.Start().ConfigureAwait(false);
						}
						catch(Exception ex)
						{
							System.Diagnostics.Debug.WriteLine(ex.ToString());
						}
					});
				}
			}
		}

		void Save()
		{
			ConnectionConfig cfg = new ConnectionConfig();
			cfg.Connections = (from curPair in m_proxies
							   select new ConnectionInfo()
							   {
								   Subdomain = curPair.Value.Subdomain,
								   Key = curPair.Value.Key,
								   Destination = curPair.Value.Destination.ToString()

							   }).ToArray();

			cfg.Save();
		}

		void Load()
		{
			System.Diagnostics.Debug.Assert(m_proxies.Count == 0);

			ConnectionConfig cfg = ConnectionConfig.Load();

			if(cfg != null && cfg.Connections != null)
			{
				foreach(var info in cfg.Connections)
				{
					Uri dest;

					if(Uri.TryCreate(info.Destination, UriKind.Absolute, out dest))
					{
						Proxy proxy = new Proxy(info.Key, info.Subdomain, dest);
						int hashCode = proxy.GetHashCode();

						if(!m_proxies.ContainsKey(hashCode))
						{
							m_proxies[hashCode] = proxy;
							m_viewEntries.Items.Add(proxy);

							System.Threading.ThreadPool.QueueUserWorkItem(async (state) =>
							{
								try
								{
									await proxy.Start().ConfigureAwait(false);
								}
								catch(Exception ex)
								{
									System.Diagnostics.Debug.WriteLine(ex.ToString());
								}
							});
						}
					}
				}
			}
		}

		private void m_viewEntries_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			if(m_viewEntries.SelectedItems.Count > 0)
			{
				m_btnRemove.IsEnabled = true;
				m_btnEdit.IsEnabled = true;
			}
			else
			{
				m_btnRemove.IsEnabled = false;
				m_btnEdit.IsEnabled = false;
			}
		}

		private void Edit_Click(object sender, RoutedEventArgs e)
		{
			Proxy proxy = m_viewEntries.SelectedItem as Proxy;

			if(proxy != null)
			{
				ConnectionWindow win = new ConnectionWindow();
				win.Title = "Edit Connection";
				win.Owner = this;
				win.Subdomain = proxy.Subdomain;
				win.Destination = proxy.Destination.ToString();
				win.Key = proxy.Key;

				var result = win.ShowDialog();

				if(result.HasValue && result.Value)
				{
					Proxy newProxy = new Proxy(win.Key, win.Subdomain, new Uri(win.Destination));
					int newProxyHashCode = newProxy.GetHashCode();
					int oldProxyHashCode = proxy.GetHashCode();

					if(newProxyHashCode != oldProxyHashCode)
					{
						m_proxies.Remove(oldProxyHashCode);

						if(!m_proxies.ContainsKey(newProxyHashCode))
						{
							int index = m_viewEntries.Items.IndexOf(proxy);

							if(index != -1)
							{
								m_viewEntries.Items[index] = newProxy;
							}
							else
							{
								m_viewEntries.Items.Add(newProxy);
							}

							m_proxies[newProxyHashCode] = newProxy;

							Save();
							m_viewEntries.InvalidateVisual();

							System.Threading.ThreadPool.QueueUserWorkItem(async (state) =>
							{
								try
								{
									await proxy.Stop().ConfigureAwait(false);
								}
								catch(Exception ex)
								{
									System.Diagnostics.Debug.WriteLine(ex.ToString());
								}

								try
								{
									await newProxy.Start().ConfigureAwait(false);
								}
								catch(Exception ex)
								{
									System.Diagnostics.Debug.WriteLine(ex.ToString());
								}
							});
						}
						else
						{
							m_viewEntries.Items.Remove(proxy);

							Save();

							System.Threading.ThreadPool.QueueUserWorkItem(async (state) =>
							{
								try
								{
									await proxy.Stop().ConfigureAwait(false);
								}
								catch(Exception ex)
								{
									System.Diagnostics.Debug.WriteLine(ex.ToString());
								}
							});
						}
					}
				}
			}
		}

		private void Remove_Click(object sender, RoutedEventArgs e)
		{
			Proxy proxy = m_viewEntries.SelectedItem as Proxy;

			if(proxy != null)
			{
				m_proxies.Remove(proxy.GetHashCode());
				m_viewEntries.Items.Remove(proxy);

				Save();

				System.Threading.ThreadPool.QueueUserWorkItem(async (state) =>
				{
					try
					{
						await proxy.Stop().ConfigureAwait(false);
					}
					catch(Exception ex)
					{
						System.Diagnostics.Debug.WriteLine(ex.ToString());
					}
				});
			}
		}
	}
}
