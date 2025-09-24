using System;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace MinecraftLauncher.Core
{
    public static class HttpHelper
    {
        public static HttpClient CreateHttpClient()
        {
            // Устанавливаем TLS 1.2 как протокол по умолчанию
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;

            // Создаем обработчик с настройками прокси
            var handler = new HttpClientHandler();

            // Используем системные настройки прокси
            handler.UseProxy = true;
            handler.Proxy = new WebProxy();

            // Для тестирования с самоподписанными сертификатами
#if DEBUG
            handler.ServerCertificateCustomValidationCallback = (message, cert, chain, sslPolicyErrors) =>
            {
                if (sslPolicyErrors == SslPolicyErrors.None)
                    return true;

                Console.WriteLine($"SSL Error: {sslPolicyErrors}");
                return true; // Принимаем все сертификаты в режиме отладки
            };
#endif

            // Создаем HttpClient с настройками
            var httpClient = new HttpClient(handler);
            httpClient.Timeout = TimeSpan.FromSeconds(60);

            // Добавляем User-Agent, чтобы сервер не блокировал запросы
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("MinecraftLauncher/1.0");

            return httpClient;
        }
    }
}