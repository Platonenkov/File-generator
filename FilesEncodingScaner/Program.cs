using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Konsole;
using static System.IO.SearchOption;

namespace FilesEncodingScaner
{
    class Program
    {
        private static DirectoryInfo __WorkDirectory = new DirectoryInfo(Environment.CurrentDirectory);
        private static Encoding __Encoding = Encoding.UTF8;
        private static string[] __FileMasks = { "*.txt", "*.cs", "*.xml", "*.xaml", "*.htm", "*.html", "*.c", "*.cpp", "*.h", "*.js", "*.asm", };
        private static Encoding[] _Encodings = new[] { Encoding.ASCII, Encoding.BigEndianUnicode, Encoding.UTF32, Encoding.UTF7, Encoding.Unicode, Encoding.UTF8 };
        private static Random _Random = new Random();
        private static Dictionary<Encoding, int> ListInformation = new Dictionary<Encoding, int>();

        static void Main(string[] args)
        {
            foreach (var encoding in _Encodings)
                ListInformation.Add(encoding, 0);

            Console.WriteLine("Input work directory or press enter to start scanning current directory");
            var dir = Console.ReadLine();
            if (dir.IsNullOrWhiteSpace()) dir = __WorkDirectory.FullName;
            ScaningDirectory(dir);
            var count = 0;
            foreach (var encoding in ListInformation)
            {
                Console.WriteLine($"{encoding.Key} - {encoding.Value} files was find");
                count += encoding.Value;
            }

            Console.WriteLine();
            Console.WriteLine($"Total was find {count} files");
            Console.WriteLine();
            Console.ReadLine();

        }

        private static void ScaningDirectory(string parent_directory)
        {
            DirectoryInfo parentDirectory = new DirectoryInfo(parent_directory);

            DirectoryInfo[] directories = parentDirectory.GetAllSubDirectory().ToArray();

            var tasks = new List<Task>();
            var bars = new List<ProgressBar>();

            var count = directories.Length > 0 ? directories.Length : 1;

            foreach (var directory in directories)
            {
                var files = directory.EnumerateFiles(__FileMasks, TopDirectoryOnly).Where(f => f.Length > 0).ToArray();
                if(files.Length == 0) continue;
                var pb = new ProgressBar(files.Length);
                bars.Add(pb);

                tasks.Add(new Task(() => ScanningFiles(directory.Name,files,pb)));
            }
            if(directories.Length==0) return;
            foreach (var task in tasks)
            {
                task.Start();
            }

            Task.WaitAll(tasks.ToArray());
        }

        private static void ScanningFiles(string directoryName, FileInfo[] files, ProgressBar pb)
        {
            var count = 1;
            foreach (var file in files)
            {
                pb.Refresh(count,directoryName);
                if(ListInformation.ContainsKey(file.GetEncoding()))ListInformation[file.GetEncoding()]++;
                else
                    ListInformation.Add(file.GetEncoding(),1);
                count++;
            }
        }
    }
}
