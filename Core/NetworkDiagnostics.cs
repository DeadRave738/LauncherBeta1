using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace MinecraftLauncher.Core
{
    public static class NetworkDiagnostics
    {
        public static async Task<bool> CheckInternetConnectionAsync()
        {
            try
            {
                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(5);
                    var result = await client.GetStringAsync("http://www.google.com");
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        public static async Task<string> CheckUrlAvailabilityAsync(string url)
        {
            try
            {
                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(10);
                    var response = await client.GetAsync(url);
                    return $"URL {url} доступен. Статус: {response.StatusCode}";
                }
            }
            catch (Exception ex)
            {
                return $"URL {url} недоступен. Ошибка: {ex.Message}";
            }
        }

        public static string GetNetworkInfo()
        {
            try
            {
                string hostName = Dns.GetHostName();
                IPHostEntry hostEntry = Dns.GetHostEntry(hostName);

                string info = $"Имя компьютера: {hostName}\nIP-адреса:\n";

                foreach (IPAddress ip in hostEntry.AddressList)
                {
                    info += $"  - {ip}\n";
                }

                return info;
            }
            catch (Exception ex)
            {
                return $"Не удалось получить информацию о сети: {ex.Message}";
            }
        }
    }
}