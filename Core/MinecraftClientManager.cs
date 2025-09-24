using MinecraftLauncher.Models;
using MinecraftLauncher.Core;
using Newtonsoft.Json;
using SharpCompress.Archives;
using SharpCompress.Archives.Zip;
using SharpCompress.Common;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Windows;

namespace MinecraftLauncher.Core
{

    public class MinecraftClientManager
    {
        private readonly string _gameDirectory;
        private readonly string _logDirectory;
        private readonly HttpClient _httpClient;
        private readonly string _filesBaseUrl;

        private bool VerifyAssetIntegrity(string filePath, string expectedHash)
        {
            try
            {
                using (var sha1 = SHA1.Create())
                using (var stream = File.OpenRead(filePath))
                {
                    byte[] hashBytes = sha1.ComputeHash(stream);
                    string actualHash = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();

                    return string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase);
                }
            }
            catch
            {
                return false;
            }
        }
        public MinecraftClientManager()
        {
            _gameDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecrafttest");
            _logDirectory = Path.Combine(_gameDirectory, "logs");
            _filesBaseUrl = ConfigManager.GetAppSetting("GameFilesUrl", "https://sqmccraft.online/minecraft/");

            // Создаем папку для логов, если её нет
            Directory.CreateDirectory(_logDirectory);

            // Инициализируем HttpClient
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(60);
        }

        public void StartForge()
        {
            try
            {
                Log("Начало процесса запуска Forge 1.12.2");
                Log($"Директория игры: {_gameDirectory}");

                // Проверяем, установлен ли Forge
                string versionDir = Path.Combine(_gameDirectory, "versions", "forge-" + _forgeVersion);
                string versionJson = Path.Combine(versionDir, $"forge-{_forgeVersion}.json");

                if (!File.Exists(versionJson))
                {
                    Log("Forge не установлен. Начало установки...");
                    InstallForge();
                }

                // Проверяем наличие ресурсов (assets)
                VerifyAssetsExist();

                // Распаковываем natives
                ExtractNatives();

                // Создаем директорию для natives, если её нет
                string nativesDir = Path.Combine(_gameDirectory, "natives");
                Directory.CreateDirectory(nativesDir);

                // Генерируем уникальный токен, если его нет
                User currentUser = GetCurrentUser();
                if (currentUser == null || string.IsNullOrEmpty(currentUser.AccessToken))
                {
                    currentUser = new User
                    {
                        Username = "Player",
                        AccessToken = SecurityManager.GenerateToken(),
                        ClientToken = SecurityManager.GenerateToken()
                    };

                    using (var db = new DatabaseManager())
                    {
                        db.UpdateUserTokens(0, currentUser.AccessToken, currentUser.ClientToken);
                    }
                }

                // Формируем аргументы запуска
                string arguments = GetMinecraftArguments(currentUser);
                Log($"Аргументы запуска: {arguments}");

                // Определяем путь к Java 8
                string javaPath = GetJava8Path();
                if (string.IsNullOrEmpty(javaPath))
                {
                    // Если Java 8 установлена, но путь не найден, пробуем использовать системный путь
                    javaPath = "java";
                    Log("Не удалось найти точный путь к Java 8, используем системный путь");
                }
                else
                {
                    Log($"Используется Java 8 из: {javaPath}");
                }

                // Запускаем процесс
                Log("Запуск процесса Java...");
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = javaPath,
                    Arguments = arguments,
                    WorkingDirectory = _gameDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                using (Process process = new Process { StartInfo = startInfo })
                {
                    // Перехватываем вывод для логирования
                    process.OutputDataReceived += (s, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                        {
                            Log($"[Игра] {e.Data}");
                        }
                    };

                    process.ErrorDataReceived += (s, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                        {
                            Log($"[Игра ОШИБКА] {e.Data}");
                        }
                    };

                    // Запускаем процесс
                    bool started = process.Start();
                    if (!started)
                    {
                        Log("ОШИБКА: Не удалось запустить процесс Java");
                        throw new InvalidOperationException("Не удалось запустить процесс Java");
                    }

                    Log("Процесс игры запущен успешно");
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    // Даем процессу время на запуск
                    System.Threading.Thread.Sleep(2000);

                    // Проверяем, не завершился ли процесс сразу
                    if (process.HasExited)
                    {
                        int exitCode = process.ExitCode;
                        Log($"Процесс игры завершился сразу с кодом: {exitCode}");
                        throw new Exception($"Процесс игры завершился сразу с кодом: {exitCode}");
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"КРИТИЧЕСКАЯ ОШИБКА при запуске игры: {ex}");
                throw;
            }
        }
        private void InstallForge()
        {
            try
            {
                Log("Начало установки Forge для Minecraft 1.12.2...");

                // Создаем необходимые директории
                string versionsDir = Path.Combine(_gameDirectory, "versions");
                string forgeVersionDir = Path.Combine(versionsDir, "forge-" + _forgeVersion);
                Directory.CreateDirectory(forgeVersionDir);

                // URL для загрузки Forge installer
                string forgeUrl = ConfigManager.GetAppSetting("ForgeInstallerUrl");
                string installerPath = Path.Combine(Path.GetTempPath(), "forge-installer.jar");

                // Скачиваем установщик
                Log($"Скачивание установщика Forge из: {forgeUrl}");
                using (var response = _httpClient.GetAsync(forgeUrl).Result)
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        Log($"Не удалось загрузить установщик Forge: HTTP {response.StatusCode}");
                        throw new Exception("Не удалось загрузить установщик Forge");
                    }

                    using (var contentStream = response.Content.ReadAsStreamAsync().Result)
                    using (var fileStream = new FileStream(installerPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        contentStream.CopyTo(fileStream);
                    }
                }

                Log("Запуск установки Forge...");

                // Запускаем установщик Forge
                using (var process = new Process())
                {
                    process.StartInfo.FileName = "java";
                    process.StartInfo.Arguments = $"-jar \"{installerPath}\" --installClient";
                    process.StartInfo.WorkingDirectory = _gameDirectory;
                    process.StartInfo.UseShellExecute = false;
                    process.StartInfo.CreateNoWindow = true;
                    process.StartInfo.RedirectStandardOutput = true;
                    process.StartInfo.RedirectStandardError = true;

                    process.OutputDataReceived += (s, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                        {
                            Log($"[Forge Installer] {e.Data}");
                        }
                    };

                    process.ErrorDataReceived += (s, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                        {
                            Log($"[Forge Installer ОШИБКА] {e.Data}");
                        }
                    };

                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                    process.WaitForExit();

                    if (process.ExitCode != 0)
                    {
                        Log($"Установка Forge завершилась с кодом: {process.ExitCode}");
                        throw new Exception($"Установка Forge завершилась с кодом: {process.ExitCode}");
                    }
                }

                // Удаляем установщик
                File.Delete(installerPath);

                // Создаем Forge-версию
                CreateForgeVersion();

                // Загружаем ресурсы (assets)
                DownloadAssets();

                Log("Forge успешно установлен и настроен");
            }
            catch (Exception ex)
            {
                Log($"Ошибка установки Forge: {ex.Message}");
                throw;
            }
        }

        private void CreateForgeVersion()
        {
            string version = "forge-" + _forgeVersion;
            string versionDir = Path.Combine(_gameDirectory, "versions", version);
            string versionJson = Path.Combine(versionDir, $"forge-{_forgeVersion}.json");

            // Создаем директорию версии, если ее нет
            Directory.CreateDirectory(versionDir);

            // Создаем JSON-конфигурацию для Forge
            var forgeConfig = new
            {
                id = version,
                mainClass = "net.minecraftforge.fml.client.Launcher",
                minecraftArguments = "--username ${auth_player_name} --version ${version_name} --gameDir ${game_directory} --assetsDir ${assets_root} --assetIndex ${assets_index_name} --uuid ${auth_uuid} --accessToken ${auth_access_token} --userType ${user_type} --versionType ${version_type}",
                libraries = new List<object>()
            };

            // Добавляем библиотеки Forge
            string forgeLibPath = Path.Combine(_gameDirectory, "libraries", "net", "minecraftforge", "forge", _forgeVersion, $"forge-{_forgeVersion}.jar");

            if (File.Exists(forgeLibPath))
            {
                forgeConfig.libraries.Add(new
                {
                    name = $"net.minecraftforge:forge:{_forgeVersion}",
                    downloads = new
                    {
                        artifact = new
                        {
                            path = $"net/minecraftforge/forge/{_forgeVersion}/forge-{_forgeVersion}.jar",
                            url = $"file:///{forgeLibPath.Replace("\\", "/")}"
                        }
                    }
                });
            }

            // Добавляем обязательные библиотеки Minecraft
            AddMinecraftLibraries(forgeConfig.libraries);

            // Добавляем Log4j (обязательно для Forge)
            AddLog4jLibraries(forgeConfig.libraries);

            // Сохраняем JSON-конфигурацию
            File.WriteAllText(versionJson, JsonConvert.SerializeObject(forgeConfig, Formatting.Indented));

            Log($"Создана Forge-версия: {version}");
        }

        private void AddMinecraftLibraries(List<object> libraries)
        {
            // Добавляем все необходимые библиотеки Minecraft вручную
            var minecraftLibs = new[]
            {
        new { name = "net.minecraft:launchwrapper:1.12", path = "net/minecraft/launchwrapper/1.12/launchwrapper-1.12.jar" },
        new { name = "org.lwjgl.lwjgl:lwjgl:2.9.4-nightly-20150209", path = "org/lwjgl/lwjgl/lwjgl/2.9.4-nightly-20150209/lwjgl-2.9.4-nightly-20150209.jar" },
        new { name = "org.lwjgl.lwjgl:lwjgl_util:2.9.4-nightly-20150209", path = "org/lwjgl/lwjgl/lwjgl_util/2.9.4-nightly-20150209/lwjgl_util-2.9.4-nightly-20150209.jar" },
        new { name = "com.mojang:patchy:1.1", path = "com/mojang/patchy/1.1/patchy-1.1.jar" },
        new { name = "oshi-project:oshi-core:1.1", path = "oshi-project/oshi-core/1.1/oshi-core-1.1.jar" },
        new { name = "net.java.dev.jna:jna:4.4.0", path = "net/java/dev/jna/4.4.0/jna-4.4.0.jar" },
        new { name = "net.java.dev.jna:platform:3.4.0", path = "net/java/dev/jna/platform/3.4.0/platform-3.4.0.jar" },
        new { name = "com.ibm.icu:icu4j-core-mojang:51.2", path = "com/ibm/icu/icu4j-core-mojang/51.2/icu4j-core-mojang-51.2.jar" },
        new { name = "net.sf.jopt-simple:jopt-simple:5.0.3", path = "net/sf/jopt-simple/jopt-simple/5.0.3/jopt-simple-5.0.3.jar" },
        new { name = "com.paulscode:codecjorbis:20101023", path = "com/paulscode/codecjorbis/20101023/codecjorbis-20101023.jar" },
        new { name = "com.paulscode:codecwav:20101023", path = "com/paulscode/codecwav/20101023/codecwav-20101023.jar" },
        new { name = "com.paulscode:libraryjavasound:20101123", path = "com/paulscode/libraryjavasound/20101123/libraryjavasound-20101123.jar" },
        new { name = "com.paulscode:librarylwjglopenal:20100824", path = "com/paulscode/librarylwjglopenal/20100824/librarylwjglopenal-20100824.jar" },
        new { name = "com.paulscode:soundsystem:20120107", path = "com/paulscode/soundsystem/20120107/soundsystem-20120107.jar" },
        new { name = "io.netty:netty-all:4.1.9.Final", path = "io/netty/netty-all/4.1.9.Final/netty-all-4.1.9.Final.jar" },
        new { name = "com.google.guava:guava:21.0", path = "com/google/guava/guava/21.0/guava-21.0.jar" },
        new { name = "org.apache.commons:commons-lang3:3.5", path = "org/apache/commons/commons-lang3/3.5/commons-lang3-3.5.jar" },
        new { name = "commons-io:commons-io:2.5", path = "commons-io/commons-io/2.5/commons-io-2.5.jar" },
        new { name = "commons-codec:commons-codec:1.10", path = "commons-codec/commons-codec/1.10/commons-codec-1.10.jar" },
        new { name = "com.google.code.gson:gson:2.8.0", path = "com/google/code/gson/gson/2.8.0/gson-2.8.0.jar" },
        new { name = "com.mojang:authlib:1.5.25", path = "com/mojang/authlib/1.5.25/authlib-1.5.25.jar" },
        new { name = "com.mojang:realms:1.10.16", path = "com/mojang/realms/1.10.16/realms-1.10.16.jar" },
        new { name = "org.apache.commons:commons-compress:1.8.1", path = "org/apache/commons/commons-compress/1.8.1/commons-compress-1.8.1.jar" },
        new { name = "org.apache.httpcomponents:httpclient:4.3.3", path = "org/apache/httpcomponents/httpclient/4.3.3/httpclient-4.3.3.jar" },
        new { name = "commons-logging:commons-logging:1.1.3", path = "commons-logging/commons-logging/1.1.3/commons-logging-1.1.3.jar" },
        new { name = "org.apache.httpcomponents:httpcore:4.3.2", path = "org/apache/httpcomponents/httpcore/4.3.2/httpcore-4.3.2.jar" },
        new { name = "it.unimi.dsi:fastutil:7.0.12_mojang", path = "it/unimi/dsi/fastutil/7.0.12_mojang/fastutil-7.0.12_mojang.jar" },
        new { name = "com.mojang:netty:1.6", path = "com/mojang/netty/1.6/netty-1.6.jar" }
    };

            foreach (var lib in minecraftLibs)
            {
                string libPath = Path.Combine(_gameDirectory, "libraries", lib.path);
                string libUrl = $"{_mavenRepositoryUrl}{lib.path}";

                // Если библиотека не существует, скачиваем её
                if (!File.Exists(libPath))
                {
                    Log($"Библиотека не найдена: {lib.name}. Попытка загрузки...");
                    DownloadLibraryFromUrl(libUrl, libPath);
                }

                if (File.Exists(libPath))
                {
                    libraries.Add(new
                    {
                        name = lib.name,
                        downloads = new
                        {
                            artifact = new
                            {
                                path = lib.path,
                                url = $"file:///{libPath.Replace("\\", "/")}"
                            }
                        }
                    });
                }
            }
        }

        private void AddLog4jLibraries(List<object> libraries)
        {
            // Добавляем Log4j (обязательно для Forge)
            var log4jLibs = new[]
            {
        new { name = "org.apache.logging.log4j:log4j-api:2.8.1", path = "org/apache/logging/log4j/log4j-api/2.8.1/log4j-api-2.8.1.jar" },
        new { name = "org.apache.logging.log4j:log4j-core:2.8.1", path = "org/apache/logging/log4j/log4j-core/2.8.1/log4j-core-2.8.1.jar" }
    };

            foreach (var lib in log4jLibs)
            {
                string libPath = Path.Combine(_gameDirectory, "libraries", lib.path);
                string libUrl = $"{_mavenRepositoryUrl}{lib.path}";

                // Если библиотека не существует, скачиваем её
                if (!File.Exists(libPath))
                {
                    Log($"Библиотека не найдена: {lib.name}. Попытка загрузки...");
                    DownloadLibraryFromUrl(libUrl, libPath);
                }

                if (File.Exists(libPath))
                {
                    libraries.Add(new
                    {
                        name = lib.name,
                        downloads = new
                        {
                            artifact = new
                            {
                                path = lib.path,
                                url = $"file:///{libPath.Replace("\\", "/")}"
                            }
                        }
                    });
                }
            }
        }

        private void DownloadLibraryFromUrl(string url, string targetPath)
        {
            try
            {
                // Создаем директорию
                Directory.CreateDirectory(Path.GetDirectoryName(targetPath));

                // Скачиваем файл
                Log($"Попытка загрузки библиотеки из: {url}");

                using (var response = _httpClient.GetAsync(url).Result)
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        Log($"Не удалось загрузить библиотеку: HTTP {response.StatusCode}");

                        // Попробуем альтернативный источник - Maven Central
                        string mavenUrl = url.Replace("https://maven.minecraftforge.net/", "https://repo1.maven.org/maven2/");
                        Log($"Попытка загрузки из Maven Central: {mavenUrl}");

                        using (var mavenResponse = _httpClient.GetAsync(mavenUrl).Result)
                        {
                            if (mavenResponse.IsSuccessStatusCode)
                            {
                                using (var contentStream = mavenResponse.Content.ReadAsStreamAsync().Result)
                                using (var fileStream = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None))
                                {
                                    contentStream.CopyTo(fileStream);
                                }
                                Log($"Библиотека загружена из Maven Central: {targetPath}");
                                return;
                            }
                        }

                        // Попробуем GitHub как последний вариант
                        string githubUrl = url.Replace("https://maven.minecraftforge.net/", "https://raw.githubusercontent.com/minecraft-legacy/minecraft-libraries/master/");
                        Log($"Попытка загрузки из GitHub: {githubUrl}");

                        using (var githubResponse = _httpClient.GetAsync(githubUrl).Result)
                        {
                            if (githubResponse.IsSuccessStatusCode)
                            {
                                using (var contentStream = githubResponse.Content.ReadAsStreamAsync().Result)
                                using (var fileStream = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None))
                                {
                                    contentStream.CopyTo(fileStream);
                                }
                                Log($"Библиотека загружена из GitHub: {targetPath}");
                                return;
                            }
                        }

                        // Если все источники не работают, логируем ошибку
                        Log($"Не удалось загрузить библиотеку из всех источников");
                        return;
                    }

                    using (var contentStream = response.Content.ReadAsStreamAsync().Result)
                    using (var fileStream = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        contentStream.CopyTo(fileStream);
                    }

                    Log($"Библиотека загружена: {targetPath}");
                }
            }
            catch (Exception ex)
            {
                Log($"Ошибка при загрузке библиотеки: {ex.Message}");
            }
        }
        private void DownloadAssets()
        {
            try
            {
                Log("Начало загрузки ресурсов (assets)...");

                string assetsDir = Path.Combine(_gameDirectory, "assets");
                string objectsDir = Path.Combine(assetsDir, "objects");
                string indexesDir = Path.Combine(assetsDir, "indexes");

                // Создаем директории
                Directory.CreateDirectory(objectsDir);
                Directory.CreateDirectory(indexesDir);

                // Загружаем индекс ресурсов
                string indexUrl = "https://resources.download.minecraft.net/1.12.json";
                string indexFile = Path.Combine(indexesDir, "1.12.json");

                Log($"Загрузка индекса ресурсов из: {indexUrl}");

                using (var response = _httpClient.GetAsync(indexUrl).Result)
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        Log($"Не удалось загрузить индекс ресурсов: HTTP {response.StatusCode}");
                        throw new Exception("Не удалось загрузить индекс ресурсов");
                    }

                    using (var contentStream = response.Content.ReadAsStreamAsync().Result)
                    using (var fileStream = new FileStream(indexFile, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        contentStream.CopyTo(fileStream);
                    }
                }

                // Загружаем ресурсы
                string jsonContent = File.ReadAllText(indexFile);
                var assets = JsonConvert.DeserializeObject<AssetsIndex>(jsonContent);

                int totalAssets = assets.objects.Count;
                int downloadedAssets = 0;
                int failedAssets = 0;

                Log($"Загрузка ресурсов (всего: {totalAssets})...");

                foreach (var entry in assets.objects)
                {
                    string hash = entry.Value.hash;
                    string hashPrefix = hash.Substring(0, 2);
                    string objectDir = Path.Combine(objectsDir, hashPrefix);
                    string objectPath = Path.Combine(objectDir, hash);

                    Directory.CreateDirectory(objectDir);

                    // Проверяем, существует ли файл
                    if (File.Exists(objectPath))
                    {
                        // Проверяем целостность
                        if (VerifyAssetIntegrity(objectPath, hash))
                        {
                            continue;
                        }

                        Log($"Поврежденный файл: {entry.Key}. Перезагрузка...");
                        File.Delete(objectPath);
                    }

                    // Загружаем файл
                    string assetUrl = $"https://resources.download.minecraft.net/{hashPrefix}/{hash}";

                    try
                    {
                        using (var response = _httpClient.GetAsync(assetUrl).Result)
                        {
                            if (response.IsSuccessStatusCode)
                            {
                                using (var contentStream = response.Content.ReadAsStreamAsync().Result)
                                using (var fileStream = new FileStream(objectPath, FileMode.Create, FileAccess.Write, FileShare.None))
                                {
                                    contentStream.CopyTo(fileStream);
                                }

                                // Проверяем целостность
                                if (VerifyAssetIntegrity(objectPath, hash))
                                {
                                    downloadedAssets++;
                                    Log($"Загружен ресурс: {entry.Key}");
                                }
                                else
                                {
                                    File.Delete(objectPath);
                                    failedAssets++;
                                    Log($"ОШИБКА: Неверный хеш для ресурса: {entry.Key}");
                                }
                            }
                            else
                            {
                                failedAssets++;
                                Log($"ОШИБКА: Не удалось загрузить ресурс {entry.Key}: HTTP {response.StatusCode}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        failedAssets++;
                        Log($"ОШИБКА при загрузке ресурса {entry.Key}: {ex.Message}");
                    }
                }

                Log($"Статус ресурсов: Загружено: {downloadedAssets}, Ошибок: {failedAssets}");
            }
            catch (Exception ex)
            {
                Log($"Критическая ошибка при загрузке ресурсов: {ex.Message}");
                throw;
            }
        }

        // Вспомогательный класс для десериализации индекса ресурсов
        private class AssetsIndex
        {
            public Dictionary<string, AssetObject> objects { get; set; }
        }

        private class AssetObject
        {
            public string hash { get; set; }
            public int size { get; set; }
        }


        public void StartMinecraft(User user)
        {
            try
            {
                Log("Начало процесса запуска Minecraft 1.12.2 с Forge (без vanilla версии)");
                Log($"Пользователь: {user.Username}");
                Log($"Директория игры: {_gameDirectory}");

                // Проверяем, установлен ли Forge
                string version = "forge-1.12.2";
                string jsonPath = Path.Combine(_gameDirectory, "versions", version, $"{version}.json");

                if (!File.Exists(jsonPath))
                {
                    Log("Forge не установлен. Начало установки...");
                    InstallForge();

                    // Проверяем снова
                    if (!File.Exists(jsonPath))
                    {
                        throw new FileNotFoundException("Файл конфигурации Forge не найден", jsonPath);
                    }
                }

                // Убедимся, что все необходимые библиотеки существуют
                EnsureLibrariesExist();

                // Проверяем наличие ресурсов
                VerifyAssetsExist();

                // Распаковываем natives
                ExtractNatives();

                // Создаем директорию для natives, если её нет
                string nativesDir = Path.Combine(_gameDirectory, "natives");
                Directory.CreateDirectory(nativesDir);

                // Генерируем уникальный токен, если его нет
                if (string.IsNullOrEmpty(user.AccessToken))
                {
                    user.AccessToken = SecurityManager.GenerateToken();
                    user.ClientToken = SecurityManager.GenerateToken();

                    // Обновляем токены в базе данных
                    using (var db = new DatabaseManager())
                    {
                        db.UpdateUserTokens(user.Id, user.AccessToken, user.ClientToken);
                    }

                    Log("Сгенерированы новые токены доступа");
                }

                // Формируем аргументы запуска
                string arguments = GetMinecraftArguments(user);
                Log($"Аргументы запуска: {arguments}");

                // Определяем путь к Java 8
                string javaPath = GetJava8Path();
                if (string.IsNullOrEmpty(javaPath))
                {
                    // Если Java 8 установлена, но путь не найден, пробуем использовать системный путь
                    javaPath = "java";
                    Log("Не удалось найти точный путь к Java 8, используем системный путь");
                }
                else
                {
                    Log($"Используется Java 8 из: {javaPath}");
                }

                // Запускаем процесс
                Log("Запуск процесса Java...");
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = javaPath,
                    Arguments = arguments,
                    WorkingDirectory = _gameDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                using (Process process = new Process { StartInfo = startInfo })
                {
                    // Перехватываем вывод для логирования
                    process.OutputDataReceived += (s, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                        {
                            Log($"[Игра] {e.Data}");
                        }
                    };

                    process.ErrorDataReceived += (s, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                        {
                            Log($"[Игра ОШИБКА] {e.Data}");
                        }
                    };

                    // Запускаем процесс
                    bool started = process.Start();
                    if (!started)
                    {
                        Log("ОШИБКА: Не удалось запустить процесс Java");
                        throw new InvalidOperationException("Не удалось запустить процесс Java");
                    }

                    Log("Процесс игры запущен успешно");
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    // Даем процессу время на запуск
                    System.Threading.Thread.Sleep(2000);

                    // Проверяем, не завершился ли процесс сразу
                    if (process.HasExited)
                    {
                        int exitCode = process.ExitCode;
                        Log($"Процесс игры завершился сразу с кодом: {exitCode}");

                        // Если ошибка связана с параметрами JVM, пытаемся использовать Java 8
                        if (exitCode == 1 && arguments.Contains("Unrecognized VM option"))
                        {
                            Log("Обнаружена проблема с параметрами JVM. Попытка установки Java 8...");

                            if (InstallJava8())
                            {
                                // Перезапускаем с Java 8
                                Log("Перезапуск с Java 8...");
                                StartMinecraft(user);
                                return;
                            }
                        }

                        throw new Exception($"Процесс игры завершился сразу с кодом: {exitCode}");
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"КРИТИЧЕСКАЯ ОШИБКА при запуске игры: {ex}");
                throw; // Перебрасываем исключение для обработки в лаунчере
            }
        }

        private string GetMinecraftArguments(User user)
        {
            string version = "forge-1.12.2";
            string jsonPath = Path.Combine(_gameDirectory, "versions", version, $"{version}.json");

            // Проверяем наличие JSON-файла
            if (!File.Exists(jsonPath))
            {
                Log($"ОШИБКА: Не найден файл конфигурации: {jsonPath}");
                throw new FileNotFoundException("Файл конфигурации не найден", jsonPath);
            }

            // Загружаем конфигурацию из JSON
            string jsonContent = File.ReadAllText(jsonPath);
            var config = JsonConvert.DeserializeObject<MinecraftVersionConfig>(jsonContent);

            // Путь к директории игр
            string gameDir = _gameDirectory;

            // Путь к директории ресурсов
            string assetsDir = Path.Combine(_gameDirectory, "assets");

            // Убедимся, что assetsIndex не пустой
            string assetsIndex = "1.12";

            // Собираем classpath со всеми необходимыми библиотеками
            var classpath = new List<string>();

            // Добавляем все библиотеки из конфигурации
            foreach (var lib in config.libraries)
            {
                if (ShouldLoadLibrary(lib, Environment.OSVersion.Platform))
                {
                    string libPath = GetLibraryPath(lib);
                    if (File.Exists(libPath))
                    {
                        classpath.Add(libPath);
                    }
                    else
                    {
                        Log($"ПРЕДУПРЕЖДЕНИЕ: Библиотека не найдена: {libPath}");
                    }
                }
            }

            // Добавляем моды
            string modsDir = Path.Combine(_gameDirectory, "mods");
            if (Directory.Exists(modsDir))
            {
                foreach (string modFile in Directory.GetFiles(modsDir, "*.jar"))
                {
                    classpath.Add(modFile);
                    Log($"Добавлен мод: {Path.GetFileName(modFile)}");
                }
            }

            // Формируем classpath для аргументов
            string classpathStr = string.Join(";", classpath);

            // Генерируем аргументы из конфигурации
            string args = config.minecraftArguments;

            // Заменяем переменные
            args = args
                .Replace("${auth_player_name}", user.Username)
                .Replace("${version_name}", version)
                .Replace("${game_directory}", gameDir)
                .Replace("${assets_root}", assetsDir)
                .Replace("${assets_index_name}", assetsIndex)
                .Replace("${auth_uuid}", Guid.NewGuid().ToString("N"))
                .Replace("${auth_access_token}", user.AccessToken)
                .Replace("${user_type}", "legacy")
                .Replace("${version_type}", "custom");

            // Добавляем дополнительные аргументы для Forge
            args += " -Dfml.ignorePatchDiscrepancies=true -Dfml.ignoreInvalidMinecraftCertificates=true";

            // Получаем параметры JVM
            string jvmArgs = GetJvmArguments();

            // Формируем окончательные аргументы
            var argsBuilder = new StringBuilder();

            // Добавляем параметры JVM
            argsBuilder.Append(jvmArgs);

            // Добавляем natives
            string nativesDir = Path.Combine(_gameDirectory, "natives");
            Directory.CreateDirectory(nativesDir);

            argsBuilder.Append($"-Djava.library.path=\"{nativesDir}\" ");

            // Добавляем classpath
            argsBuilder.Append($"-cp \"{classpathStr}\" ");

            // Добавляем основной класс
            argsBuilder.Append(config.mainClass);

            // Добавляем аргументы Minecraft
            argsBuilder.Append(" ");
            argsBuilder.Append(args);

            return argsBuilder.ToString();
        }

        private void VerifyAssetsExist()
        {
            string assetsDir = Path.Combine(_gameDirectory, "assets");
            string indexesDir = Path.Combine(assetsDir, "indexes");
            string indexFile = Path.Combine(indexesDir, "1.12.json");

            if (!File.Exists(indexFile))
            {
                Log($"ОШИБКА: Не найден файл индекса ресурсов: {indexFile}");
                DownloadAssets();
            }
        }

        private string GetJvmArguments()
        {
            try
            {
                Log("Определение версии Java...");

                // Проверяем версию Java
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "java",
                        Arguments = "-version",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardError = true
                    }
                };

                string javaVersionOutput = "";
                process.ErrorDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        javaVersionOutput += e.Data + "\n";
                        Log($"[Java Version] {e.Data}");
                    }
                };

                process.Start();
                process.BeginErrorReadLine();
                process.WaitForExit();

                // Определяем версию Java из вывода
                int javaMajorVersion = ParseJavaVersion(javaVersionOutput);
                Log($"Определена версия Java: {javaMajorVersion}");

                // Формируем параметры в зависимости от версии Java
                var jvmArgs = new StringBuilder();

                // Память
                jvmArgs.Append("-Xmx2G ");

                // Опции GC в зависимости от версии Java
                if (javaMajorVersion <= 8)
                {
                    // Параметры GC для Java 8
                    jvmArgs.Append("-XX:+UseConcMarkSweepGC ");
                    jvmArgs.Append("-XX:+CMSIncrementalPacing ");
                    jvmArgs.Append("-XX:+AggressiveOpts ");
                    jvmArgs.Append("-XX:+UseFastAccessorMethods ");
                }
                else if (javaMajorVersion <= 14)
                {
                    // Параметры GC для Java 9-14
                    jvmArgs.Append("-XX:+UnlockExperimentalVMOptions ");
                    jvmArgs.Append("-XX:+UseG1GC ");
                    jvmArgs.Append("-XX:G1NewSizePercent=20 ");
                    jvmArgs.Append("-XX:G1ReservePercent=20 ");
                    jvmArgs.Append("-XX:MaxGCPauseMillis=50 ");
                    jvmArgs.Append("-XX:G1HeapRegionSize=32M ");
                }
                else
                {
                    // Параметры GC для Java 15+
                    jvmArgs.Append("-XX:+UseG1GC ");
                    jvmArgs.Append("-XX:G1NewSizePercent=20 ");
                    jvmArgs.Append("-XX:G1ReservePercent=20 ");
                    jvmArgs.Append("-XX:MaxGCPauseMillis=50 ");
                    jvmArgs.Append("-XX:G1HeapRegionSize=32M ");
                    jvmArgs.Append("-XX:ParallelGCThreads=2 ");
                    jvmArgs.Append("-XX:ConcGCThreads=2 ");
                    jvmArgs.Append("-XX:+UnlockExperimentalVMOptions ");
                    jvmArgs.Append("-XX:+DisableExplicitGC ");
                }

                // Дополнительные параметры для всех версий
                jvmArgs.Append("-XX:-OmitStackTraceInFastThrow ");
                jvmArgs.Append("-XX:+AlwaysPreTouch ");
                jvmArgs.Append("-Dfml.ignoreInvalidMinecraftCertificates=true ");
                jvmArgs.Append("-Dfml.ignorePatchDiscrepancies=true ");

                Log($"Используются параметры JVM для Java {javaMajorVersion}: {jvmArgs.ToString()}");
                return jvmArgs.ToString();
            }
            catch (Exception ex)
            {
                Log($"Ошибка определения версии Java: {ex.Message}");
                // Если не удалось определить версию, используем параметры для Java 8
                return GetJvmArgumentsForJava8();
            }
        }

        private int ParseJavaVersion(string versionOutput)
        {
            try
            {
                // Примеры вывода:
                // java version "1.8.0_291"
                // openjdk version "11.0.11" 2021-04-20
                // openjdk version "17" 2021-09-14

                // Ищем строку с версией
                foreach (string line in versionOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (line.Contains("version") && (line.Contains("java") || line.Contains("openjdk")))
                    {
                        // Извлекаем версию
                        int startIndex = line.IndexOf('"');
                        if (startIndex >= 0)
                        {
                            int endIndex = line.IndexOf('"', startIndex + 1);
                            if (endIndex > startIndex)
                            {
                                string versionString = line.Substring(startIndex + 1, endIndex - startIndex - 1);

                                // Обрабатываем разные форматы версий
                                if (versionString.StartsWith("1."))
                                {
                                    // Java 8 и ниже: "1.8.0_291"
                                    return int.Parse(versionString.Substring(2, 1));
                                }
                                else
                                {
                                    // Java 9+: "11.0.11", "17", "24.0.1"
                                    int dotIndex = versionString.IndexOf('.');
                                    if (dotIndex > 0)
                                    {
                                        return int.Parse(versionString.Substring(0, dotIndex));
                                    }
                                    else
                                    {
                                        return int.Parse(versionString);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Ошибка парсинга версии Java: {ex.Message}");
            }

            // Если не удалось определить версию, предполагаем Java 8
            return 8;
        }

        private string GetJvmArgumentsForJava8()
        {
            Log("Используются параметры JVM для Java 8");

            var jvmArgs = new StringBuilder();

            // Память
            jvmArgs.Append("-Xmx2G ");

            // Параметры GC для Java 8
            jvmArgs.Append("-XX:+UseConcMarkSweepGC ");
            jvmArgs.Append("-XX:+CMSIncrementalPacing ");
            jvmArgs.Append("-XX:+AggressiveOpts ");
            jvmArgs.Append("-XX:+UseFastAccessorMethods ");

            // Дополнительные параметры
            jvmArgs.Append("-XX:-OmitStackTraceInFastThrow ");
            jvmArgs.Append("-XX:+AlwaysPreTouch ");
            jvmArgs.Append("-Dfml.ignoreInvalidMinecraftCertificates=true ");
            jvmArgs.Append("-Dfml.ignorePatchDiscrepancies=true ");

            return jvmArgs.ToString();
        }

        // Добавьте эти вспомогательные классы
        private class MinecraftVersionConfig
        {
            public string id { get; set; }
            public string mainClass { get; set; }
            public string minecraftArguments { get; set; }
            public List<Library> libraries { get; set; }
        }

        private class Library
        {
            public string name { get; set; }
            public Downloads downloads { get; set; }
            public List<Rule> rules { get; set; }
            public Extract extract { get; set; }
            public string url { get; set; }
        }

        private class Downloads
        {
            public Artifact artifact { get; set; }
            public Dictionary<string, Artifact> classifiers { get; set; }
        }

        private class Artifact
        {
            public string path { get; set; }
            public string url { get; set; }
            public string sha1 { get; set; }
            public int size { get; set; }
        }

        private class Rule
        {
            public string action { get; set; }
            public Os os { get; set; }
        }

        private class Os
        {
            public string name { get; set; }
        }

        private class Extract
        {
            public List<string> exclude { get; set; }
        }

        private bool ShouldLoadLibrary(Library lib, PlatformID platform)
        {
            // Если нет правил, загружаем библиотеку
            if (lib.rules == null || lib.rules.Count == 0)
                return true;

            // По умолчанию разрешаем загрузку
            bool shouldLoad = true;

            foreach (Rule rule in lib.rules)
            {
                if (rule.action == "allow")
                {
                    // Если правило разрешает, но есть ограничение по ОС
                    if (rule.os != null)
                    {
                        bool osMatches = false;

                        if (rule.os.name == "windows" && platform == PlatformID.Win32NT)
                            osMatches = true;
                        else if (rule.os.name == "linux" && platform == PlatformID.Unix)
                            osMatches = true;
                        else if (rule.os.name == "osx" && platform == PlatformID.MacOSX)
                            osMatches = true;

                        // Если ОС не совпадает, не применяем это правило
                        if (!osMatches)
                            continue;
                    }

                    // Если правило разрешает и ОС совпадает (или нет ограничения по ОС)
                    shouldLoad = true;
                }
                else if (rule.action == "disallow")
                {
                    // Если правило запрещает, но есть ограничение по ОС
                    if (rule.os != null)
                    {
                        bool osMatches = false;

                        if (rule.os.name == "windows" && platform == PlatformID.Win32NT)
                            osMatches = true;
                        else if (rule.os.name == "linux" && platform == PlatformID.Unix)
                            osMatches = true;
                        else if (rule.os.name == "osx" && platform == PlatformID.MacOSX)
                            osMatches = true;

                        // Если ОС не совпадает, не применяем это правило
                        if (!osMatches)
                            continue;
                    }

                    // Если правило запрещает и ОС совпадает (или нет ограничения по ОС)
                    shouldLoad = false;
                }
            }

            return shouldLoad;
        }

        private string GetLibraryPath(Library lib)
        {
            // Пытаемся получить путь из downloads
            if (lib.downloads != null && lib.downloads.artifact != null)
            {
                // Используем корректные слеши для Windows
                return Path.Combine(_gameDirectory, "libraries", lib.downloads.artifact.path.Replace("/", "\\"));
            }

            // Старый формат (до 1.6)
            string[] parts = lib.name.Split(':');
            if (parts.Length < 3) return "";

            string groupId = parts[0].Replace('.', '\\');
            string artifactId = parts[1];
            string version = parts[2];

            return Path.Combine(_gameDirectory, "libraries", groupId, artifactId, version,
                $"{artifactId}-{version}.jar");
        }

        private void EnsureLibrariesExist()
        {
            string jsonPath = Path.Combine(_gameDirectory, "versions", "1.12.2", "1.12.2.json");
            if (!File.Exists(jsonPath))
                return;

            string jsonContent = File.ReadAllText(jsonPath);
            var config = JsonConvert.DeserializeObject<MinecraftVersionConfig>(jsonContent);

            foreach (var lib in config.libraries)
            {
                if (ShouldLoadLibrary(lib, Environment.OSVersion.Platform))
                {
                    string libPath = GetLibraryPath(lib);
                    if (!File.Exists(libPath))
                    {
                        Log($"Библиотека не найдена: {libPath}. Попытка загрузки...");
                        DownloadLibrary(lib);
                    }
                }
            }
        }

        private void DownloadLibrary(Library lib)
        {
            try
            {
                string libPath = GetLibraryPath(lib);
                string url = "";

                // Определяем URL для загрузки
                if (lib.downloads != null && lib.downloads.artifact != null && !string.IsNullOrEmpty(lib.downloads.artifact.url))
                {
                    url = lib.downloads.artifact.url;
                }
                else
                {
                    // Пытаемся построить URL из имени библиотеки
                    string[] parts = lib.name.Split(':');
                    if (parts.Length >= 3)
                    {
                        string groupId = parts[0].Replace('.', '/');
                        string artifactId = parts[1];
                        string version = parts[2];

                        // Используем правильный URL без лишних пробелов
                        url = $"https://libraries.minecraft.net/{groupId}/{artifactId}/{version}/{artifactId}-{version}.jar";
                    }
                }

                if (string.IsNullOrEmpty(url))
                {
                    Log($"Не удалось определить URL для библиотеки: {lib.name}");
                    return;
                }

                // Создаем директорию
                Directory.CreateDirectory(Path.GetDirectoryName(libPath));

                // Скачиваем файл
                try
                {
                    using (var response = _httpClient.GetAsync(url).Result)
                    {
                        if (!response.IsSuccessStatusCode)
                        {
                            // libraries.minecraft.net не работает, пробуем альтернативные источники
                            Log($"Официальный сервер библиотек недоступен (HTTP {response.StatusCode}). Используем альтернативные источники...");

                            // 1. Пытаемся использовать наш собственный сервер
                            string altUrl = $"{_filesBaseUrl}libraries/{lib.downloads.artifact.path}";
                            Log($"Попытка загрузки из альтернативного источника: {altUrl}");

                            using (var altResponse = _httpClient.GetAsync(altUrl).Result)
                            {
                                if (altResponse.IsSuccessStatusCode)
                                {
                                    using (var contentStream = altResponse.Content.ReadAsStreamAsync().Result)
                                    using (var fileStream = new FileStream(libPath, FileMode.Create, FileAccess.Write, FileShare.None))
                                    {
                                        contentStream.CopyTo(fileStream);
                                    }

                                    Log($"Библиотека загружена из альтернативного источника: {libPath}");
                                    return;
                                }
                            }

                            // 2. Пытаемся использовать maven-репозиторий
                            string mavenUrl = $"https://repo1.maven.org/maven2/{lib.downloads.artifact.path}";
                            Log($"Попытка загрузки из Maven репозитория: {mavenUrl}");

                            using (var mavenResponse = _httpClient.GetAsync(mavenUrl).Result)
                            {
                                if (mavenResponse.IsSuccessStatusCode)
                                {
                                    using (var contentStream = mavenResponse.Content.ReadAsStreamAsync().Result)
                                    using (var fileStream = new FileStream(libPath, FileMode.Create, FileAccess.Write, FileShare.None))
                                    {
                                        contentStream.CopyTo(fileStream);
                                    }

                                    Log($"Библиотека загружена из Maven репозитория: {libPath}");
                                    return;
                                }
                            }

                            // 3. Если ничего не помогло, пытаемся использовать GitHub
                            string githubUrl = $"https://github.com/minecraft-legacy/minecraft-libraries/raw/master/{lib.downloads.artifact.path}";
                            Log($"Попытка загрузки из GitHub: {githubUrl}");

                            using (var githubResponse = _httpClient.GetAsync(githubUrl).Result)
                            {
                                if (githubResponse.IsSuccessStatusCode)
                                {
                                    using (var contentStream = githubResponse.Content.ReadAsStreamAsync().Result)
                                    using (var fileStream = new FileStream(libPath, FileMode.Create, FileAccess.Write, FileShare.None))
                                    {
                                        contentStream.CopyTo(fileStream);
                                    }

                                    Log($"Библиотека загружена из GitHub: {libPath}");
                                    return;
                                }
                            }

                            // Если все источники недоступны
                            Log($"Не удалось загрузить библиотеку {lib.name} из всех доступных источников");
                            throw new Exception($"Не удалось загрузить библиотеку {lib.name}");
                        }

                        using (var contentStream = response.Content.ReadAsStreamAsync().Result)
                        using (var fileStream = new FileStream(libPath, FileMode.Create, FileAccess.Write, FileShare.None))
                        {
                            contentStream.CopyTo(fileStream);
                        }

                        Log($"Библиотека загружена: {libPath}");
                    }
                }
                catch (Exception ex)
                {
                    Log($"Ошибка при загрузке библиотеки: {ex.Message}");
                    throw;
                }
            }
            catch (Exception ex)
            {
                Log($"Критическая ошибка загрузки библиотеки: {ex.Message}");

                // Показываем пользователю сообщение
                string message = "Не удалось загрузить необходимые библиотеки для Minecraft.\n\n" +
                                "Это может быть связано с тем, что официальный сервер библиотек недоступен.\n\n" +
                                "Пожалуйста, убедитесь, что все необходимые библиотеки загружены на ваш сервер.\n" +
                                "Структура папки libraries должна соответствовать структуре Maven-репозитория.";

                MessageBox.Show(message, "Ошибка загрузки библиотек", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ExtractNatives()
        {
            string nativesDir = Path.Combine(_gameDirectory, "natives");
            Directory.CreateDirectory(nativesDir);

            // Ищем все native-архивы
            var nativeLibraries = new List<Library>();
            string jsonPath = Path.Combine(_gameDirectory, "versions", "1.12.2", "1.12.2.json");

            if (File.Exists(jsonPath))
            {
                string jsonContent = File.ReadAllText(jsonPath);
                var config = JsonConvert.DeserializeObject<MinecraftVersionConfig>(jsonContent);

                foreach (var lib in config.libraries)
                {
                    if (lib.downloads?.classifiers != null)
                    {
                        foreach (var classifier in lib.downloads.classifiers)
                        {
                            if (classifier.Key.Contains("natives"))
                            {
                                nativeLibraries.Add(new Library
                                {
                                    name = lib.name,
                                    downloads = new Downloads
                                    {
                                        artifact = classifier.Value
                                    }
                                });
                            }
                        }
                    }
                }
            }

            // Загружаем и распаковываем native-библиотеки
            foreach (var lib in nativeLibraries)
            {
                string libPath = GetLibraryPath(lib);

                if (!File.Exists(libPath))
                {
                    Log($"Загрузка native-библиотеки: {lib.name}");
                    DownloadLibrary(lib);
                }

                if (File.Exists(libPath))
                {
                    Log($"Распаковка native-библиотеки: {libPath}");
                    try
                    {
                        using (var archive = ZipArchive.Open(libPath))
                        {
                            foreach (var entry in archive.Entries)
                            {
                                if (!entry.IsDirectory)
                                {
                                    string fileName = Path.GetFileName(entry.Key);
                                    if (!string.IsNullOrEmpty(fileName))
                                    {
                                        string outputPath = Path.Combine(nativesDir, fileName);
                                        entry.WriteToDirectory(nativesDir, new ExtractionOptions { ExtractFullPath = false, Overwrite = true });
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"Ошибка распаковки native-библиотеки: {ex.Message}");
                    }
                }
            }
        }

        private bool IsJavaInstalled()
        {
            try
            {
                using (var process = new Process())
                {
                    process.StartInfo.FileName = "java";
                    process.StartInfo.Arguments = "-version";
                    process.StartInfo.UseShellExecute = false;
                    process.StartInfo.CreateNoWindow = true;
                    process.StartInfo.RedirectStandardError = true;

                    process.Start();
                    process.WaitForExit();

                    return process.ExitCode == 0;
                }
            }
            catch
            {
                return false;
            }
        }

        private bool IsJava8Installed()
        {
            try
            {
                using (var process = new Process())
                {
                    process.StartInfo.FileName = "java";
                    process.StartInfo.Arguments = "-version";
                    process.StartInfo.UseShellExecute = false;
                    process.StartInfo.CreateNoWindow = true;
                    process.StartInfo.RedirectStandardError = true;

                    process.Start();
                    string output = process.StandardError.ReadToEnd();
                    process.WaitForExit();

                    // Ищем версию Java 8 в выводе
                    return output.Contains("1.8") || (output.Contains("8.") && !output.Contains("9.") && !output.Contains("10."));
                }
            }
            catch
            {
                return false;
            }
        }

        private string GetJava8Path()
        {
            try
            {
                // Проверяем стандартные пути установки Java 8
                string[] possiblePaths = new string[]
                {
                    @"C:\Program Files\Java\jre1.8.0_461\bin\java.exe",
                    @"C:\Program Files (x86)\Java\jre1.8.0_461\bin\java.exe",
                    @"C:\Program Files\Eclipse Adoptium\jre-8.0.462.8-hotspot\bin\java.exe",
                };

                foreach (string path in possiblePaths)
                {
                    if (File.Exists(path))
                    {
                        return path;
                    }
                }

                // Проверяем переменную окружения JAVA_HOME
                string javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
                if (!string.IsNullOrEmpty(javaHome))
                {
                    string javaExe = Path.Combine(javaHome, "bin", "java.exe");
                    if (File.Exists(javaExe))
                    {
                        return javaExe;
                    }
                }

                // Проверяем системный PATH
                string pathEnv = Environment.GetEnvironmentVariable("PATH");
                foreach (string dir in pathEnv.Split(';'))
                {
                    string javaExe = Path.Combine(dir, "java.exe");
                    if (File.Exists(javaExe))
                    {
                        // Проверяем, что это Java 8
                        using (var process = new Process())
                        {
                            process.StartInfo.FileName = javaExe;
                            process.StartInfo.Arguments = "-version";
                            process.StartInfo.UseShellExecute = false;
                            process.StartInfo.CreateNoWindow = true;
                            process.StartInfo.RedirectStandardError = true;

                            process.Start();
                            string output = process.StandardError.ReadToEnd();
                            process.WaitForExit();

                            if (output.Contains("1.8"))
                            {
                                return javaExe;
                            }
                        }
                    }
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        private bool InstallJava8()
        {
            try
            {
                Log("Начало установки Java 8...");

                // URL для загрузки Java 8 (Adoptium Temurin 8)
                string javaUrl = "https://github.com/adoptium/temurin8-binaries/releases/download/jdk8u392-b08/OpenJDK8U-jre_x64_windows_hotspot_8u392b08.msi";
                string installerPath = Path.Combine(Path.GetTempPath(), "java8_installer.msi");

                // Проверяем, не установлена ли уже Java 8
                if (IsJava8Installed())
                {
                    Log("Java 8 уже установлена");
                    return true;
                }

                // Скачиваем установщик
                Log($"Скачивание установщика Java 8 из: {javaUrl}");
                using (var response = _httpClient.GetAsync(javaUrl).Result)
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        Log($"Не удалось загрузить установщик Java 8: HTTP {response.StatusCode}");

                        // Попробуем альтернативный источник
                        javaUrl = "https://github.com/adoptium/temurin8-binaries/releases/download/jdk8u392-b08/OpenJDK8U-jdk_x64_windows_hotspot_8u392b08.msi";
                        Log($"Попытка альтернативного URL: {javaUrl}");

                        using (var altResponse = _httpClient.GetAsync(javaUrl).Result)
                        {
                            if (!altResponse.IsSuccessStatusCode)
                            {
                                Log($"Не удалось загрузить установщик Java 8 с альтернативного URL: HTTP {altResponse.StatusCode}");

                                // Показываем пользователю сообщение с инструкцией
                                ShowJava8ManualInstallDialog();
                                return false;
                            }

                            using (var contentStream = altResponse.Content.ReadAsStreamAsync().Result)
                            using (var fileStream = new FileStream(installerPath, FileMode.Create, FileAccess.Write, FileShare.None))
                            {
                                contentStream.CopyTo(fileStream);
                            }
                        }
                    }
                    else
                    {
                        using (var contentStream = response.Content.ReadAsStreamAsync().Result)
                        using (var fileStream = new FileStream(installerPath, FileMode.Create, FileAccess.Write, FileShare.None))
                        {
                            contentStream.CopyTo(fileStream);
                        }
                    }
                }

                // Запускаем установку с правами администратора
                Log("Запуск установки Java 8...");
                using (var process = new Process())
                {
                    process.StartInfo.FileName = "msiexec.exe";
                    process.StartInfo.Arguments = $"/i \"{installerPath}\" /quiet /norestart";
                    process.StartInfo.UseShellExecute = true;
                    process.StartInfo.Verb = "runas"; // Запуск от имени администратора
                    process.StartInfo.CreateNoWindow = true;

                    try
                    {
                        process.Start();
                        process.WaitForExit();

                        if (process.ExitCode != 0)
                        {
                            Log($"Установка Java 8 завершилась с кодом: {process.ExitCode}");

                            // Обработка конкретных кодов ошибок
                            if (process.ExitCode == 1603)
                            {
                                Log("Ошибка 1603: Возможно, проблема с правами администратора или конфликт с существующей установкой");

                                // Показываем пользователю сообщение с инструкцией
                                ShowJava8ManualInstallDialog();
                                return false;
                            }

                            return false;
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"Ошибка при запуске установщика: {ex.Message}");
                        ShowJava8ManualInstallDialog();
                        return false;
                    }
                }

                // Удаляем установщик
                File.Delete(installerPath);

                Log("Java 8 успешно установлена");

                // Проверяем установку
                if (IsJava8Installed())
                {
                    Log("Проверка установки Java 8: УСПЕШНО");
                    return true;
                }
                else
                {
                    Log("Проверка установки Java 8: НЕ УСПЕШНО");
                    ShowJava8ManualInstallDialog();
                    return false;
                }
            }
            catch (Exception ex)
            {
                Log($"Ошибка установки Java 8: {ex.Message}");
                ShowJava8ManualInstallDialog();
                return false;
            }
        }

        private void ShowJava8ManualInstallDialog()
        {
            string message = "Не удалось автоматически установить Java 8.\n\n" +
                            "Minecraft 1.12.2 требует Java 8 для корректной работы.\n\n" +
                            "Пожалуйста, установите Java 8 вручную:\n" +
                            "1. Перейдите по ссылке: https://adoptium.net/temurin/releases/?version=8\n" +
                            "2. Выберите: Version 8, Package Type = JRE, Architecture = x64\n" +
                            "3. Скачайте и установите файл\n\n" +
                            "После установки перезапустите лаунчер.";

            MessageBoxResult result = MessageBox.Show(
                message,
                "Требуется ручная установка Java 8",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private void Log(string message)
        {
            string logFile = Path.Combine(_logDirectory, $"launcher_{DateTime.Now:yyyyMMdd}.log");
            string logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";

            File.AppendAllText(logFile, logEntry + Environment.NewLine);
            Console.WriteLine(logEntry);
        }

        // Вспомогательный класс для MessageBox (если не используется WPF)
        private static class MessageBox
        {
            public static MessageBoxResult Show(string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon, MessageBoxResult defaultResult = MessageBoxResult.None)
            {
                // Если мы в консольном приложении, используем Console
                if (Environment.UserInteractive)
                {
                    return System.Windows.MessageBox.Show(messageBoxText, caption, button, icon, defaultResult);
                }
                else
                {
                    Console.WriteLine($"{caption}: {messageBoxText}");
                    Console.WriteLine("Нажмите Y для продолжения или N для отмены...");
                    var key = Console.ReadKey();
                    return key.Key == ConsoleKey.Y ? MessageBoxResult.Yes : MessageBoxResult.No;
                }
            }
        }

    }

}