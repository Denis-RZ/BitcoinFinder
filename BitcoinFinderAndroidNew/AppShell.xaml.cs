namespace BitcoinFinderAndroidNew;

public partial class AppShell : Shell
{
	public AppShell()
	{
		InitializeComponent();
		
		// Регистрируем маршруты для навигации
		Routing.RegisterRoute(nameof(StolenWalletRecoveryPage), typeof(StolenWalletRecoveryPage));
	}
}
