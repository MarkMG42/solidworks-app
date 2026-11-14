using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace SWToolsHub.Portable
{
    internal sealed class ExcelEntry
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string FileName { get; set; }
        public string Sheet { get; set; }
        public string Cell { get; set; }
        public List<ExcelInput> Inputs { get; set; } = new List<ExcelInput>();
    }

    internal sealed class ExcelLibrary
    {
        public readonly string Root;
        public ExcelLibrary(string root = null)
        {
            Root = Path.GetFullPath(root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SW-Assistant", "ExcelBooks"));
            Directory.CreateDirectory(Root);
        }

        private string Folder(string id)
        {
            Guid guid;
            if (!Guid.TryParseExact(id, "N", out guid))
                throw new InvalidDataException("Некорректный идентификатор книги");
            return Path.Combine(Root, id);
        }

        public string OriginalPath(ExcelEntry entry) => Path.Combine(Folder(entry.Id), "original.xlsx");
        public IEnumerable<ExcelEntry> Entries()
        {
            foreach (var folder in Directory.EnumerateDirectories(Root).OrderBy(f => Directory.GetCreationTimeUtc(f)))
            {
                Guid id;
                if (!Guid.TryParseExact(Path.GetFileName(folder), "N", out id))
                    continue;
                var path = Path.Combine(folder, "state.json");
                if (!File.Exists(path) || !File.Exists(Path.Combine(folder, "original.xlsx")))
                    continue;
                ExcelEntry entry = null;
                try
                {
                    var info = new FileInfo(path);
                    if (info.Length > 64000000)
                        continue;
                    entry = JsonConvert.DeserializeObject<ExcelEntry>(File.ReadAllText(path), new JsonSerializerSettings { MaxDepth = 64, TypeNameHandling = TypeNameHandling.None });
                    if (entry == null || entry.Id != Path.GetFileName(folder))
                        entry = null;
                }
                catch
                {
                }

                if (entry != null)
                    yield return entry;
            }
        }

        public ExcelEntry Add(string name, byte[] bytes)
        {
            if (!string.Equals(Path.GetExtension(name), ".xlsx", StringComparison.OrdinalIgnoreCase) || bytes.Length > 15 * 1024 * 1024)
                throw new InvalidDataException("Выберите XLSX размером до 15 МБ");
            string title = Path.GetFileNameWithoutExtension(name), original = title;
            var existing = Entries().Select(e => e.Title).ToArray();
            for (int n = 2; existing.Contains(title, StringComparer.OrdinalIgnoreCase); n++)
                title = original + " (" + n + ")";
            var entry = new ExcelEntry
            {
                Id = Guid.NewGuid().ToString("N"),
                Title = title,
                FileName = Path.GetFileName(name)
            };
            string folder = Folder(entry.Id);
            try
            {
                Directory.CreateDirectory(folder);
                File.WriteAllBytes(OriginalPath(entry), bytes);
                Save(entry);
                return entry;
            }
            catch
            {
                if (Directory.Exists(folder))
                    Directory.Delete(folder, true);
                throw;
            }
        }

        public void Save(ExcelEntry entry)
        {
            if (!Directory.Exists(Folder(entry.Id)))
                throw new IOException("Книга удалена из библиотеки");
            AtomicWrite(Path.Combine(Folder(entry.Id), "state.json"), System.Text.Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(entry)));
        }

        public void Delete(ExcelEntry entry)
        {
            string folder = Folder(entry.Id);
            if (Directory.Exists(folder))
                Directory.Delete(folder, true);
        }

        public void SaveAs(ExcelEntry entry, ExcelBook book, string path)
        {
            string full = Path.GetFullPath(path);
            if (full.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Выберите папку вне внутреннего хранилища приложения");
            AtomicWrite(full, book.Export());
        }

        internal static void AtomicWrite(string path, byte[] bytes)
        {
            string folder = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!Directory.Exists(folder))
                throw new DirectoryNotFoundException("Папка не найдена");
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllBytes(temporary, bytes);
                if (File.Exists(path))
                    File.Replace(temporary, path, null);
                else
                    File.Move(temporary, path);
            }
            finally
            {
                if (File.Exists(temporary))
                    File.Delete(temporary);
            }
        }
    }
}
