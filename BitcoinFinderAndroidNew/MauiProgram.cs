using Microsoft.Extensions.Logging;
using Microsoft.Maui.Controls;
using BitcoinFinderAndroidNew.Services;

namespace BitcoinFinderAndroidNew
{
	public static class MauiProgram
	{
		public static MauiApp CreateMauiApp()
		{
			var builder = MauiApp.CreateBuilder();
			builder
				.UseMauiApp<App>()
				.ConfigureFonts(fonts =>
				{
					fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
					fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
				});

#if DEBUG
			builder.Logging.AddDebug();
#endif

			// Регистрируем сервисы
			builder.Services.AddSingleton<ProgressManager>();
			builder.Services.AddSingleton<BackgroundSearchService>();
			builder.Services.AddSingleton<BitcoinKeyGenerator>();

			return builder.Build();
		}
	}
}
