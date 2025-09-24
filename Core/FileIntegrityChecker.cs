using MinecraftLauncher.Core;
using MinecraftLauncher.Models;
using Newtonsoft.Json;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace MinecraftLauncher.Core
{
    public class FileIntegrityChecker
    {
        private readonly string _gameDirectory;
        private readonly string _filesBaseUrl;
        private readonly string _manifestFile;
        private readonly HttpClient _httpClient;

        public FileIntegrityChecker()
        {
            _gameDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecrafttest");
            _filesBaseUrl = ConfigManager.GetAppSetting("GameFilesUrl");
            _manifestFile = ConfigManager.GetAppSetting("ManifestFile");

            // Используем HttpHelper для создания HttpClient
            _httpClient = HttpHelper.CreateHttpClient();
        }

        /// <summary>
        /// Проверяет целостность файлов и обновляет при необходимости
        /// </summary>
        public async Task<bool> VerifyAndRepairFilesAsync(Action<int, int, string> progressCallback = null)
        {
            try
            {
                // Создаем директорию игры, если её нет
                Directory.CreateDirectory(_gameDirectory);

                // Загружаем манифест
                FileManifest manifest = await DownloadManifestAsync();
                if (manifest == null)
                    throw new Exception("Не удалось загрузить манифест файлов");

                // Проверяем файлы
                var filesToDownload = new List<FileEntry>();
                var filesToDelete = new List<string>();

                // Проверяем существующие файлы
                foreach (var fileEntry in manifest.Files)
                {
                    string localPath = Path.Combine(_gameDirectory, fileEntry.Path);
                    string localDir = Path.GetDirectoryName(localPath);

                    if (!Directory.Exists(localDir))
                        Directory.CreateDirectory(localDir);

                    if (File.Exists(localPath))
                    {
                        // Проверяем хеш
                        string currentHash = CalculateFileHash(localPath);
                        if (!string.Equals(currentHash, fileEntry.Hash, StringComparison.OrdinalIgnoreCase))
                        {
                            filesToDownload.Add(fileEntry);
                            File.Delete(localPath); // Удаляем поврежденный файл
                        }
                    }
                    else if (fileEntry.Required)
                    {
                        filesToDownload.Add(fileEntry);
                    }
                }

                // Ищем лишние файлы
                var allLocalFiles = Directory.GetFiles(_gameDirectory, "*", SearchOption.AllDirectories);
                foreach (string localFile in allLocalFiles)
                {
                    string relativePath = localFile.Substring(_gameDirectory.Length + 1).Replace("\\", "/");

                    if (!manifest.Files.Any(f => f.Path == relativePath))
                    {
                        filesToDelete.Add(localFile);
                    }
                }

                // Удаляем лишние файлы
                foreach (string file in filesToDelete)
                {
                    try
                    {
                        File.Delete(file);
                    }
                    catch
                    {
                        // Игнорируем ошибки удаления (возможно, файл используется)
                    }
                }

                // Скачиваем недостающие файлы
                int downloaded = 0;
                foreach (var fileEntry in filesToDownload)
                {
                    await DownloadFileAsync(fileEntry);
                    downloaded++;
                    progressCallback?.Invoke(downloaded, filesToDownload.Count, fileEntry.Path);
                }

                return true;
            }
            catch (TaskCanceledException ex)
            {
                // Обрабатываем таймаут отдельно
                throw new Exception($"Время ожидания истекло при проверке файлов. Проверьте подключение к интернету и доступность сервера: {_filesBaseUrl}", ex);
            }
            catch (Exception ex)
            {
                // Логируем ошибку
                string errorLog = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Ошибка проверки файлов:\n{ex}\n\n";
                File.AppendAllText(Path.Combine(_gameDirectory, "error.log"), errorLog);
                throw;
            }
        }

        private async Task<FileManifest> DownloadManifestAsync()
        {
            try
            {
                string manifestUrl = $"{_filesBaseUrl}{_manifestFile}";
                string manifestJson = await _httpClient.GetStringAsync(manifestUrl);
                return JsonConvert.DeserializeObject<FileManifest>(manifestJson);
            }
            catch (HttpRequestException httpEx)
            {
                throw new Exception($"Ошибка HTTP при загрузке манифеста: {httpEx.Message}\nURL: {_filesBaseUrl}{_manifestFile}", httpEx);
            }
            catch (TaskCanceledException ex)
            {
                throw new Exception($"Время ожидания истекло при загрузке манифеста. Проверьте подключение к интернету и доступность сервера: {_filesBaseUrl}", ex);
            }
            catch (Exception ex)
            {
                throw new Exception($"Неизвестная ошибка при загрузке манифеста: {ex.Message}", ex);
            }
        }

        private async Task DownloadFileAsync(FileEntry fileEntry)
        {
            string url = $"{_filesBaseUrl}{fileEntry.Path}";
            string localPath = Path.Combine(_gameDirectory, fileEntry.Path);

            // Создаем директорию, если её нет
            Directory.CreateDirectory(Path.GetDirectoryName(localPath));

            try
            {
                using (var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();

                    using (var contentStream = await response.Content.ReadAsStreamAsync())
                    using (var fileStream = new FileStream(localPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        await contentStream.CopyToAsync(fileStream);
                    }
                }

                // Проверяем хеш после загрузки
                string downloadedHash = CalculateFileHash(localPath);
                if (!string.Equals(downloadedHash, fileEntry.Hash, StringComparison.OrdinalIgnoreCase))
                {
                    // Добавляем подробное логирование несоответствия
                    string logMessage = $"\n===== НЕСООТВЕТСТВИЕ ХЕША =====\n" +
                                       $"Файл: {fileEntry.Path}\n" +
                                       $"Ожидаемый хеш: {fileEntry.Hash}\n" +
                                       $"Полученный хеш: {downloadedHash}\n" +
                                       $"Размер файла: {new FileInfo(localPath).Length} байт\n" +
                                       $"URL: {url}\n" +
                                       $"===============================\n";

                    File.AppendAllText(Path.Combine(_gameDirectory, "hash_mismatch.log"), logMessage);

                    // Попытка определить тип проблемы
                    if (fileEntry.Path.EndsWith(".txt") || fileEntry.Path.EndsWith(".cfg") ||
                        fileEntry.Path.EndsWith(".properties") || fileEntry.Path.EndsWith(".json"))
                    {
                        try
                        {
                            string expectedContent = await _httpClient.GetStringAsync(url);
                            string actualContent = File.ReadAllText(localPath);

                            if (expectedContent != actualContent)
                            {
                                // Проверяем разницу в переносах строк
                                string expectedNormalized = expectedContent.Replace("\r\n", "\n");
                                string actualNormalized = actualContent.Replace("\r\n", "\n");

                                if (expectedNormalized == actualNormalized)
                                {
                                    File.AppendAllText(Path.Combine(_gameDirectory, "hash_mismatch.log"),
                                        "Обнаружена проблема с переносами строк (CRLF vs LF)\n");
                                }
                                else
                                {
                                    // Попробуем найти различия
                                    int diffIndex = -1;
                                    for (int i = 0; i < Math.Min(expectedContent.Length, actualContent.Length); i++)
                                    {
                                        if (expectedContent[i] != actualContent[i])
                                        {
                                            diffIndex = i;
                                            break;
                                        }
                                    }

                                    File.AppendAllText(Path.Combine(_gameDirectory, "hash_mismatch.log"),
                                        $"Первое различие на позиции {diffIndex}:\n" +
                                        $"Ожидаемый символ: '{(diffIndex >= 0 && diffIndex < expectedContent.Length ? expectedContent[diffIndex].ToString() : "N/A")}'\n" +
                                        $"Полученный символ: '{(diffIndex >= 0 && diffIndex < actualContent.Length ? actualContent[diffIndex].ToString() : "N/A")}'\n");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            File.AppendAllText(Path.Combine(_gameDirectory, "hash_mismatch.log"),
                                $"Не удалось сравнить содержимое: {ex.Message}\n");
                        }
                    }

                    File.Delete(localPath);
                    throw new Exception($"Хеш файла {fileEntry.Path} не совпадает после загрузки. Подробности в hash_mismatch.log");
                }
            }
            catch (HttpRequestException httpEx)
            {
                if (File.Exists(localPath))
                    File.Delete(localPath);

                throw new Exception($"Ошибка HTTP при загрузке файла {fileEntry.Path}: {httpEx.Message}\nURL: {url}", httpEx);
            }
            catch (TaskCanceledException ex)
            {
                if (File.Exists(localPath))
                    File.Delete(localPath);

                throw new Exception($"Время ожидания истекло при загрузке файла {fileEntry.Path}. Проверьте подключение к интернету и доступность сервера.", ex);
            }
        }

        private string CalculateFileHash(string filePath)
        {
            try
            {
                using (var sha256 = SHA256.Create())
                using (var stream = File.OpenRead(filePath))
                {
                    byte[] hashBytes = sha256.ComputeHash(stream);
                    return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
                }
            }
            catch (Exception ex)
            {
                string errorLog = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Ошибка вычисления хеша для {filePath}: {ex.Message}\n";
                File.AppendAllText(Path.Combine(_gameDirectory, "error.log"), errorLog);
                throw;
            }
        }

        /// <summary>
        /// Установка клиента Minecraft 1.12.2
        /// </summary>
        public async Task InstallClientAsync(Action<int, int, string> progressCallback)
        {
            // Загружаем базовый клиент
            string clientUrl = $"{_filesBaseUrl}minecraft_client_1.12.2.zip";
            string tempZip = Path.Combine(Path.GetTempPath(), "minecraft_client.zip");

            try
            {
                using (var response = await _httpClient.GetAsync(clientUrl, HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();

                    using (var contentStream = await response.Content.ReadAsStreamAsync())
                    using (var fileStream = new FileStream(tempZip, FileMode.Create, FileAccess.Write, FileShare.None))
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
                                progressCallback?.Invoke(progress, 100, "Загрузка клиента");
                            }
                        }
                    }
                }

                // Распаковываем архив
                using (var archive = ArchiveFactory.Open(tempZip))
                {
                    int totalFiles = archive.Entries.Count();
                    int processedFiles = 0;

                    foreach (var entry in archive.Entries)
                    {
                        if (!entry.IsDirectory)
                        {
                            string outputPath = Path.Combine(_gameDirectory, entry.Key);
                            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
                            entry.WriteToFile(outputPath, new ExtractionOptions { Overwrite = true });

                            processedFiles++;
                            progressCallback?.Invoke(processedFiles, totalFiles, $"Распаковка: {entry.Key}");
                        }
                    }
                }

                // Удаляем временный архив
                File.Delete(tempZip);
            }
            catch (HttpRequestException httpEx)
            {
                throw new Exception($"Ошибка HTTP при загрузке клиента: {httpEx.Message}\nURL: {clientUrl}", httpEx);
            }
            catch (TaskCanceledException ex)
            {
                throw new Exception($"Время ожидания истекло при загрузке клиента. Проверьте подключение к интернету и доступность сервера.", ex);
            }
        }
    }
}