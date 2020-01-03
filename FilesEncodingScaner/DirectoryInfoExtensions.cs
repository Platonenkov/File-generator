using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO.Compression;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using MathCore.Annotations;

// ReSharper disable once CheckNamespace
namespace System.IO
{
    public static class DirectoryInfoExtensions
    {
        public static IEnumerable<FileInfo> EnumerateFiles(this DirectoryInfo Directory, IEnumerable<string> SearchPatterns, SearchOption Options = SearchOption.TopDirectoryOnly) =>
            SearchPatterns.SelectMany(mask => Directory.EnumerateFiles(mask, Options));

        [NotNull]
        public static FileInfo Zip([NotNull] this DirectoryInfo Directory, [NotNull] FileInfo ArchiveFile)
        {
            Directory.ThrowIfNotFound("Архивируемая дирректория не найдена");

            ZipArchive GetArchive(FileInfo file) => file.Exists
                ? new ZipArchive(file.OpenWrite(), ZipArchiveMode.Update)
                : new ZipArchive(file.Create(), ZipArchiveMode.Create, false);

            using (var zip = GetArchive(ArchiveFile))
            {

            }

            ArchiveFile.Refresh();
            return ArchiveFile;
        }

        /// <summary>Проверка, что директория существует</summary>
        /// <param name="Dir">Проверяемая директория</param>
        /// <param name="Message">Сообщение, добавляемое в исключение, если директория не найдена</param>
        /// <returns>Директория, гарантированно существующая</returns>
        /// <exception cref="T:System.IO.DirectoryNotFoundException">В случае если <paramref name="Dir"/> не существует.</exception>
        [NotNull]
        public static DirectoryInfo ThrowIfNotFound([CanBeNull] this DirectoryInfo Dir, [CanBeNull] string Message = null)
        {
            var dir = Dir.NotNull("Отсутствует ссылка на директории");
            if (!dir.Exists) throw new DirectoryNotFoundException(Message ?? $"ДИректория {dir.FullName} не найдена");
            return dir;
        }

        [NotNull]
        public static FileSystemWatcher GetWatcher([NotNull] this DirectoryInfo directory, [CanBeNull] Action<FileSystemWatcher> initializer = null) => directory.GetWatcher(null, initializer);

        //public static bool ContainsFile([NotNull] this DirectoryInfo directory, [NotNull] string file) => File.Exists(Path.Combine(directory.FullName, file));
        // закомментировано т.к. есть в MathService

        public static bool ContainsFileMask([NotNull] this DirectoryInfo directory, [NotNull] string mask) => directory.EnumerateFiles(mask).Any();


        [NotNull] private static readonly WindowsIdentity __CurrentSystemUser = WindowsIdentity.GetCurrent();

        /// <summary>
        /// Рекурсивно проверяет есть ли деректория и если ее нет - пробует создать
        /// </summary>
        /// <param name="dir">директория</param>
        /// <returns>истина если есть или удалось создать, лож если создать не удалось</returns>
        public static bool CheckExistsOrCreate([NotNull] this DirectoryInfo dir)
        {
            if (Directory.Exists(dir.FullName)) return true;
            else if (!(dir.Parent is null) && dir.Parent.CheckExistsOrCreate())
            {
                if (!dir.Parent.CanAccessToDirectory(FileSystemRights.CreateDirectories)) return false;
                Directory.CreateDirectory(dir.FullName);
                return true;
            }
            return false;
        }
        /// <summary>
        /// Проверяет право просмотреть директорию в списке
        /// </summary>
        /// <param name="dir">директория</param>
        /// <returns></returns>
        public static bool CanAccessToDirectoryListItems([NotNull] this DirectoryInfo dir) => dir.CanAccessToDirectory(FileSystemRights.ListDirectory);

        /// <summary>
        /// проверяет право на директорию в соответствии с заданными правами
        /// </summary>
        /// <param name="dir">директория</param>
        /// <param name="AccessRight">искомые права (по умолчанию права на изменение)</param>
        /// <returns></returns>
        public static bool CanAccessToDirectory([NotNull] this DirectoryInfo dir, FileSystemRights AccessRight = FileSystemRights.Modify)
            => dir.CanAccessToDirectory(__CurrentSystemUser, AccessRight);
        /// <summary>
        /// хранит список директорий с заблокированным доступом на любые права
        /// </summary>
        private static readonly ConcurrentDictionary<int, bool> __BadDirectories = new ConcurrentDictionary<int, bool>();
        /// <summary>
        /// проверяет права на директорию в соответсвии с заданными правами и уровнем достпа юзера
        /// </summary>
        /// <param name="dir">директория</param>
        /// <param name="user">пользователь WindowsIdentity </param>
        /// <param name="AccessRight">уровень доступа (поумолчанию права на изменение)</param>
        /// <returns></returns>
        public static bool CanAccessToDirectory([NotNull] this DirectoryInfo dir, [NotNull] WindowsIdentity user, FileSystemRights AccessRight = FileSystemRights.Modify)
        {
            if (dir is null) throw new ArgumentNullException(nameof(dir));
            if (!dir.Exists) throw new InvalidOperationException($"Директория {dir.FullName} не существует");
            if (user is null) throw new ArgumentNullException(nameof(user));
            if (user.Groups is null) throw new ArgumentException("В идетнификаторе пользователя отсутствует ссылка на группы", nameof(user));

            if (__BadDirectories.ContainsKey(dir.FullName.GetHashCode()))
                return false;

            AuthorizationRuleCollection rules;
            try
            {
                rules = dir.GetAccessControl(AccessControlSections.Access).GetAccessRules(true, true, typeof(SecurityIdentifier));
            }
            catch (UnauthorizedAccessException)
            {
                __BadDirectories[dir.FullName.GetHashCode()] = false;
                Trace.WriteLine($"CanAccessToDirectory: Отсутствует разрешение на просмотр разрешений каталога {dir.FullName}");
                return false;
            }
            catch (InvalidOperationException)
            {
                Trace.WriteLine($"CanAccessToDirectory: Ошибка чтения каталога {dir.FullName}");
                return false;
            }
            catch (DirectoryNotFoundException)
            {
                Trace.WriteLine($"CanAccessToDirectory: Директория не существует {dir.FullName}");
                return false;
            }
            catch (Exception)
            {
                Trace.WriteLine($"CanAccessToDirectory: неизвестная ошибка {dir.FullName}");
                return false;
            }

            var allow = false;
            var deny = false;

            #region Проверка прав для локальных папок

            var access_local_rights = new List<FileSystemAccessRule>(rules.Count);

            foreach (FileSystemAccessRule rule in rules)
            {

                var sid = (SecurityIdentifier)rule.IdentityReference;
                if ((sid.IsAccountSid() && user.User == sid) || (!sid.IsAccountSid() && user.Groups.Contains(sid)))
                {
                    var rights = MapGenericRightsToFileSystemRights(rule.FileSystemRights); //Преобразование составного ключа
                    if (((int)rule.FileSystemRights != -1) && (rights & AccessRight) == AccessRight)
                        access_local_rights.Add(rule);
                }

            }

            foreach (var rule in access_local_rights)
                switch (rule.AccessControlType)
                {
                    case AccessControlType.Allow:
                        allow = true;
                        break;
                    case AccessControlType.Deny:
                        deny = true;
                        break;
                }

            var local_access = allow && !deny;

            #endregion

            #region проверка прав для серверных папок
            allow = false;
            deny = false;

            var access_server_rights = rules.OfType<FileSystemAccessRule>()
               .Where(rule => user.Groups.Contains(rule.IdentityReference) && (int)rule.FileSystemRights != -1 && (rule.FileSystemRights & AccessRight) == AccessRight).ToArray();


            //if (access_server_rights.Length == 0 && access_local_rights.Count == 0)
            //{
            //    Trace.WriteLine($"CanAccessToDirectory: В списке прав доступа к {dir.FullName} не найдено записей");
            //    return false;
            //}

            foreach (var rule in access_server_rights)
                switch (rule.AccessControlType)
                {
                    case AccessControlType.Allow:
                        allow = true;
                        break;
                    case AccessControlType.Deny:
                        deny = true;
                        break;
                }

            var server_access = allow && !deny;

            #endregion

            #region Финальная проверка прав на чтение

            var look_dir = false;
            if (AccessRight == FileSystemRights.ListDirectory)
                try
                {
                    dir.GetDirectories();
                    look_dir = true;
                }
                catch (UnauthorizedAccessException) { }

            #endregion

            return local_access || server_access || look_dir;
        }

        /// <summary>Cоставные ключи прав доступа</summary>
        [Flags]
        private enum GenericRights : uint
        {
            Read = 0x80000000,
            Write = 0x40000000,
            Execute = 0x20000000,
            All = 0x10000000
        }
        /// <summary>Преобразование прав доступа из составных ключей</summary>
        /// <param name="OriginalRights"></param>
        /// <returns></returns>
        private static FileSystemRights MapGenericRightsToFileSystemRights(FileSystemRights OriginalRights)
        {
            var mapped_rights = new FileSystemRights();
            var was_number = false;
            if (Convert.ToBoolean(Convert.ToInt64(OriginalRights) & Convert.ToInt64(GenericRights.Execute)))
            {
                mapped_rights = mapped_rights | FileSystemRights.ExecuteFile | FileSystemRights.ReadPermissions | FileSystemRights.ReadAttributes | FileSystemRights.Synchronize;
                was_number = true;
            }

            if (Convert.ToBoolean(Convert.ToInt64(OriginalRights) & Convert.ToInt64(GenericRights.Read)))
            {
                mapped_rights = mapped_rights | FileSystemRights.ReadAttributes | FileSystemRights.ReadData | FileSystemRights.ReadExtendedAttributes | FileSystemRights.ReadPermissions | FileSystemRights.Synchronize;
                was_number = true;
            }
            if (Convert.ToBoolean(Convert.ToInt64(OriginalRights) & Convert.ToInt64(GenericRights.Write)))
            {
                mapped_rights = mapped_rights | FileSystemRights.AppendData | FileSystemRights.WriteAttributes | FileSystemRights.WriteData | FileSystemRights.WriteExtendedAttributes | FileSystemRights.ReadPermissions | FileSystemRights.Synchronize;
                was_number = true;
            }
            if (Convert.ToBoolean(Convert.ToInt64(OriginalRights) & Convert.ToInt64(GenericRights.All)))
            {
                mapped_rights |= FileSystemRights.FullControl;
                was_number = true;
            }

            return was_number ? mapped_rights : OriginalRights;
        }

        [NotNull]
        public static Process ShowInFileExplorer([NotNull] this FileSystemInfo dir) => Process.Start("explorer", $"/select,\"{dir.FullName}\"") ?? throw new InvalidOperationException();

        [NotNull]
        public static Process OpenInFileExplorer([NotNull] this DirectoryInfo dir) => Process.Start("explorer", dir.FullName) ?? throw new InvalidOperationException();

        [CanBeNull]
        public static string GetRelativePosition([NotNull] this DirectoryInfo current, [NotNull] DirectoryInfo other)
        {
            if (current is null) throw new ArgumentNullException(nameof(current));
            if (other is null) throw new ArgumentNullException(nameof(other));
            return GetRelativePosition(current.FullName, other.FullName);
        }

        [CanBeNull]
        public static string GetRelativePosition([NotNull] string current, [NotNull] string other)
        {
            if (current is null) throw new ArgumentNullException(nameof(current));
            if (other is null) throw new ArgumentNullException(nameof(other));

            const StringComparison str_cmp = StringComparison.InvariantCultureIgnoreCase;
            return !string.Equals(Path.GetPathRoot(current), Path.GetPathRoot(other), str_cmp)
                ? null
                : current.StartsWith(other, str_cmp)
                    ? current.Remove(0, other.Length)
                    : other.StartsWith(current, str_cmp)
                        ? other.Remove(0, current.Length)
                        : null;
        }

        public static void MoveTo(this DirectoryInfo Directory, DirectoryInfo Destination) => Directory.MoveTo(Destination.FullName);

        /// <summary>Получение поддиректории по заданному пути. Если поддиректория отсутствует, то создать новую</summary>
        /// <param name="ParentDirectory">Родительская директория</param>
        /// <param name="SubDirectoryPath">Относительный путь к поддиректории</param>
        /// <returns>Поддиректория</returns>
        [NotNull]
        public static DirectoryInfo SubDirectoryOrCreate([NotNull] this DirectoryInfo ParentDirectory, [NotNull] string SubDirectoryPath)
        {
            if (ParentDirectory is null) throw new ArgumentNullException(nameof(ParentDirectory));
            if (SubDirectoryPath is null) throw new ArgumentNullException(nameof(SubDirectoryPath));
            if (string.IsNullOrWhiteSpace(SubDirectoryPath)) throw new ArgumentException("Не указан путь дочернего каталога", nameof(SubDirectoryPath));

            var sub_dir_path = Path.Combine(ParentDirectory.FullName, SubDirectoryPath);
            var sub_dir = new DirectoryInfo(sub_dir_path);
            if (sub_dir.Exists) return sub_dir;
            sub_dir.Create();
            sub_dir.Refresh();
            return sub_dir;
        }

        /// <summary>Формирование информации о поддиректории, заданной своим именем, либо относительным путём</summary>
        /// <param name="Directory">Корнневая директория</param><param name="SubDirectoryPath">Путь к поддиректории</param>
        /// <exception cref="ArgumentNullException">Если указана пустая ссылка на <paramref name="Directory"/></exception>
        /// <exception cref="ArgumentNullException">Если указана пустая ссылка на <paramref name="SubDirectoryPath"/></exception>
        /// <returns>Информация о поддиректории</returns>
        [NotNull]
        public static DirectoryInfo SubDirectory([NotNull] this DirectoryInfo Directory, [NotNull] string SubDirectoryPath)
        {
            if (Directory is null) throw new ArgumentNullException(nameof(Directory));
            if (SubDirectoryPath is null) throw new ArgumentNullException(nameof(SubDirectoryPath));
            return string.IsNullOrEmpty(SubDirectoryPath)
                ? Directory
                : new DirectoryInfo(Path.Combine(Directory.FullName, SubDirectoryPath));
        }

        /// <summary>Получает список всех вложенных директорий</summary>
        /// <param name="ParentDirectory">родительская директория</param>
        /// <returns></returns>
        [NotNull]
        public static IEnumerable<DirectoryInfo> GetAllSubDirectory([NotNull] this DirectoryInfo ParentDirectory) =>
            CanAccessToDirectoryListItems(ParentDirectory)
                ? ParentDirectory
                   .GetDirectories(searchOption: SearchOption.AllDirectories, searchPattern: ".")
                   .Where(dir => dir.CanAccessToDirectory(FileSystemRights.ListDirectory))
                : Array.Empty<DirectoryInfo>();

        /// <summary>Получает список всех вложенных директорий на основании прав доступа</summary>
        /// <param name="ParentDirectory">родительская директория</param>
        /// <param name="rights">право доступа</param>
        /// <returns></returns>
        [NotNull]
        public static IEnumerable<DirectoryInfo> GetAllSubDirectory([NotNull] this DirectoryInfo ParentDirectory, FileSystemRights rights) =>
            ParentDirectory.CanAccessToDirectory(rights)
                ? ParentDirectory.GetDirectoryInfo(rights)
                : Array.Empty<DirectoryInfo>();

        private static IEnumerable<DirectoryInfo> GetDirectoryInfo(this DirectoryInfo ParentDirectory, FileSystemRights rights)
        {
            if (!ParentDirectory.CanAccessToDirectory(rights)) yield break;
            foreach (var directory in ParentDirectory.GetDirectories())
                if (directory.CanAccessToDirectory(rights))
                {
                    yield return directory;
                    foreach (var sub_dir in directory.GetDirectoryInfo(rights))
                        yield return sub_dir;
                }

        }

        /// <summary>Получает список всех вложенных директорий на основании прав доступа</summary>
        /// <param name="ParentDirectory">родительская директория</param>
        /// <param name="rights">право доступа</param>
        /// <param name="Cancel">Флаг отмены асинхронной операции</param>
        /// <returns></returns>
        public static async Task<IEnumerable<DirectoryInfo>> GetAllSubDirectoryAsync(
            [NotNull] this DirectoryInfo ParentDirectory,
            FileSystemRights rights = FileSystemRights.ListDirectory,
            CancellationToken Cancel = default)
        {
            Cancel.ThrowIfCancellationRequested();
            return !ParentDirectory.CanAccessToDirectory(rights)
                ? Array.Empty<DirectoryInfo>()
                : await ParentDirectory.GetDirectoryInfoAsync(rights, Cancel).ConfigureAwait(false);
        }

        [ItemNotNull]
        private static async Task<IEnumerable<DirectoryInfo>> GetDirectoryInfoAsync(
            [NotNull] this DirectoryInfo ParentDirectory,
            FileSystemRights rights,
            CancellationToken Cancel = default)
        {
            Cancel.ThrowIfCancellationRequested();
            if (!ParentDirectory.CanAccessToDirectory(FileSystemRights.ListDirectory)) return Enumerable.Empty<DirectoryInfo>();
            var dirs = new List<DirectoryInfo>();
            DirectoryInfo[] directories;
            try
            {
                directories = await ParentDirectory.Async(dir => dir.GetDirectories(), Cancel).ConfigureAwait(false);
            }
            catch (UnauthorizedAccessException)
            {
                return Enumerable.Empty<DirectoryInfo>();
            }

            foreach (var dir in directories)
                if (dir.CanAccessToDirectory(rights))
                {
                    Cancel.ThrowIfCancellationRequested();

                    dirs.Add(dir);

                    dirs.AddRange(await dir.GetDirectoryInfoAsync(rights, Cancel).ConfigureAwait(false));
                }
            return dirs;
        }

        public static FileInfo GetFile([NotNull] this DirectoryInfo Directory, string FileName) => new FileInfo(Path.Combine(Directory.FullName, FileName));
    }
}