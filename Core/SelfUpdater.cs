using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using MinecraftLauncher.Models;
using Newtonsoft.Json;
using MinecraftLauncher.Core;

namespace MinecraftLauncher.Core
{
    public class SelfUpdater
    {
        private readonly string _updateInfoUrl;
        private readonly string _currentVersion;
        private readonly HttpClient _httpClient;

        public SelfUpdater()
        {
            _updateInfoUrl = ConfigManager.GetAppSetting("UpdateCheckUrl");
            _currentVersion = ConfigManager.GetAppSetting("CurrentVersion");

            // Используем HttpHelper для создания HttpClient
            _httpClient = HttpHelper.CreateHttpClient();
        }

        /// <summary>
        /// Проверяет наличие обновлений
        /// </summary>
        public async Task<UpdateInfo> CheckForUpdatesAsync()
        {
            try
            {
                string updateInfoJson = await _httpClient.GetStringAsync(_updateInfoUrl);
                UpdateInfo updateInfo = JsonConvert.DeserializeObject<UpdateInfo>(updateInfoJson);

                if (IsNewerVersion(updateInfo.Version))
                {
                    return updateInfo;
                }
            }
            catch (HttpRequestException httpEx)
            {
                throw new Exception($"Ошибка HTTP при проверке обновлений: {httpEx.Message}\nURL: {_updateInfoUrl}", httpEx);
            }
            catch (TaskCanceledException ex)
            {
                throw new Exception($"Время ожидания истекло при проверке обновлений. Проверьте подключение к интернету и доступность сервера.", ex);
            }
            catch
            {
                // Ошибки проверки обновлений игнорируем
            }

            return null;
        }

        private bool IsNewerVersion(string newVersion)
        {
            Version current = new Version(_currentVersion);
            Version available = new Version(newVersion);

            return available > current;
        }

        /// <summary>
        /// Выполняет обновление лаунчера
        /// </summary>
        public async Task<bool> PerformUpdateAsync(UpdateInfo updateInfo, Action<int, string> progressCallback)
        {
            string tempFile = Path.Combine(Path.GetTempPath(), "MinecraftLauncher_new.exe");

            try
            {
                // Скачиваем новую версию
                using (var response = await _httpClient.GetAsync(updateInfo.DownloadUrl, HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();

                    using (var contentStream = await response.Content.ReadAsStreamAsync())
                    using (var fileStream = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        long totalBytes = response.Content.Headers.ContentLength ?? 0;
                        long bytesCopied = 0;
                        byte[] buffer = new byte[81920];
                        int bytesRead;

                        while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                        {
                            await fileStream.WriteAsync(buffer, 0, bytesRead);
                            bytesCopied += bytesRead;

                            if (totalBytes > 0)
                            {
                                int progress = (int)((double)bytesCopied / totalBytes * 100);
                                progressCallback?.Invoke(progress, $"Загрузка обновления: {progress}%");
                            }
                        }
                    }
                }

                // Создаем скрипт для замены файлов
                string batchScript = Path.Combine(Path.GetTempPath(), "update_launcher.bat");
                string currentExe = Process.GetCurrentProcess().MainModule.FileName;

                string scriptContent = $@"
@echo off
timeout /t 2 /nobreak > NUL
taskkill /f /im ""{Path.GetFileName(currentExe)}"" > NUL 2>&1
:retry
del ""{currentExe}"" > NUL 2>&1
if exist ""{currentExe}"" (
    timeout /t 1 /nobreak > NUL
    goto retry
)
move ""{tempFile}"" ""{currentExe}"" > NUL 2>&1
start """" ""{currentExe}""
del ""%~f0""";

                File.WriteAllText(batchScript, scriptContent);

                // Запускаем скрипт обновления
                Process.Start(new ProcessStartInfo
                {
                    FileName = batchScript,
                    CreateNoWindow = true,
                    UseShellExecute = false
                });

                return true;
            }
            catch (HttpRequestException httpEx)
            {
                if (File.Exists(tempFile))
                    File.Delete(tempFile);

                throw new Exception($"Ошибка HTTP при загрузке обновления: {httpEx.Message}\nURL: {updateInfo.DownloadUrl}", httpEx);
            }
            catch (TaskCanceledException ex)
            {
                if (File.Exists(tempFile))
                    File.Delete(tempFile);

                throw new Exception($"Время ожидания истекло при загрузке обновления. Проверьте подключение к интернету и доступность сервера.", ex);
            }
            catch
            {
                // Очищаем временные файлы в случае ошибки
                if (File.Exists(tempFile))
                    File.Delete(tempFile);

                return false;
            }
        }
    }
}