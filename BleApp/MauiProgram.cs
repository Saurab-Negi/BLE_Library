using BleLibrary.Abstractions;
using BleLibrary.Services;
using Microsoft.Extensions.Logging;

namespace BleApp
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
            builder.Services.AddSingleton<IBleService, BleService>();

            builder.Services.AddSingleton<MainPage>();

            return builder.Build();
        }
    }
}
