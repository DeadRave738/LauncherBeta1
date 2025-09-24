using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Security;
using System;
using System.Security.Cryptography;

namespace MinecraftLauncher.Core
{
    public static class SecurityManager
    {
        private const int SaltSize = 16;
        private const int HashSize = 32;
        private const int Iterations = 10000;

        /// <summary>
        /// Хеширование пароля с использованием PBKDF2
        /// </summary>
        public static (string hashedPassword, byte[] salt) HashPassword(string password)
        {
            // Генерация случайной "соли"
            byte[] salt = new byte[SaltSize];
            using (var rng = new RNGCryptoServiceProvider())
            {
                rng.GetBytes(salt);
            }

            // Хеширование пароля
            var keyGenerator = new Pkcs5S2ParametersGenerator(new Sha256Digest());
            keyGenerator.Init(
                PbeParametersGenerator.Pkcs5PasswordToBytes(password.ToCharArray()),
                salt,
                Iterations
            );

            // Исправлено: Получаем ключ правильно
            var keyParam = (KeyParameter)keyGenerator.GenerateDerivedMacParameters(HashSize * 8);
            byte[] key = keyParam.GetKey();

            // Соединяем соль и хеш
            byte[] hashBytes = new byte[SaltSize + HashSize];
            Array.Copy(salt, 0, hashBytes, 0, SaltSize);
            Array.Copy(key, 0, hashBytes, SaltSize, HashSize);

            return (Convert.ToBase64String(hashBytes), salt);
        }

        /// <summary>
        /// Проверка пароля
        /// </summary>
        public static bool VerifyPassword(string password, string hashedPassword)
        {
            byte[] hashBytes = Convert.FromBase64String(hashedPassword);

            // Извлечение соли
            byte[] salt = new byte[SaltSize];
            Array.Copy(hashBytes, 0, salt, 0, SaltSize);

            // Хеширование введенного пароля с той же солью
            var keyGenerator = new Pkcs5S2ParametersGenerator(new Sha256Digest());
            keyGenerator.Init(
                PbeParametersGenerator.Pkcs5PasswordToBytes(password.ToCharArray()),
                salt,
                Iterations
            );

            // Исправлено: Получаем ключ правильно
            var keyParam = (KeyParameter)keyGenerator.GenerateDerivedMacParameters(HashSize * 8);
            byte[] key = keyParam.GetKey();

            // Сравнение хешей
            for (int i = 0; i < HashSize; i++)
            {
                if (hashBytes[i + SaltSize] != key[i])
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Генерация случайного токена
        /// </summary>
        public static string GenerateToken(int length = 32)
        {
            using (var rng = new RNGCryptoServiceProvider())
            {
                byte[] tokenBytes = new byte[length];
                rng.GetBytes(tokenBytes);
                return Convert.ToBase64String(tokenBytes);
            }
        }
    }
}