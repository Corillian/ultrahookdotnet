using System;
using System.Collections.Generic;
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
using System.Windows.Shapes;
using UltraHook;

namespace UltraHookProxy
{
	/// <summary>
	/// Interaction logic for ConnectionWindow.xaml
	/// </summary>
	public partial class ConnectionWindow : Window
	{
		public string Subdomain
		{
			get { return m_txtSubdomain.Text; }
			set { m_txtSubdomain.Text = value; }
		}

		public string Destination
		{
			get { return m_txtDestination.Text; }
			set { m_txtDestination.Text = value; }
		}

		public string Key
		{
			get { return m_txtKey.Text; }
			set { m_txtKey.Text = value; }
		}

		public ConnectionWindow()
		{
			InitializeComponent();
		}

		private void OK_Click(object sender, RoutedEventArgs e)
		{
			Uri dest;

			if(!string.IsNullOrWhiteSpace(this.Key)
				&& !string.IsNullOrWhiteSpace(this.Destination)
				&& !string.IsNullOrWhiteSpace(this.Subdomain)
				&& Proxy.IsValidSubdomain(this.Subdomain)
				&& Uri.TryCreate(this.Destination, UriKind.Absolute, out dest))
			{
				this.DialogResult = true;
				this.Close();
			}
		}

		private void Cancel_Click(object sender, RoutedEventArgs e)
		{
			this.DialogResult = false;
			this.Close();
		}
	}
}
