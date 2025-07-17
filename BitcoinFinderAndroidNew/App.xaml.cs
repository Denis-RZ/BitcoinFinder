#nullable enable
using BitcoinFinderAndroidNew.Services;
using Microsoft.Maui.Controls;

namespace BitcoinFinderAndroidNew
{
	public partial class App : Application
	{
		public App()
		{
			InitializeComponent();
		}

		protected override Window CreateWindow(IActivationState? activationState)
		{
			return new Window(new AppShell());
		}
	}
}