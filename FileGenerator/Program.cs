using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Konsole;

namespace FileGenerator
{
    class Program
    {
        private static DirectoryInfo __WorkDirectory = new DirectoryInfo(Path.Combine(Environment.CurrentDirectory,"Test Directory"));
        private static Encoding __Encoding = Encoding.UTF8;
        private static string[] __FileMasks = { "*.txt", "*.cs", "*.xml", "*.xaml", "*.htm", "*.html", "*.c", "*.cpp", "*.h", "*.js", "*.asm", };
        private static Encoding[] _Encodings = new []{Encoding.ASCII, Encoding.BigEndianUnicode, Encoding.UTF32, Encoding.UTF7, Encoding.Unicode, Encoding.UTF8};
        private static Random _Random = new Random();
        private static Dictionary<Encoding, int> ListInformation = new Dictionary<Encoding, int>();
        private static int CountFiles = 0;
        static void Main(string[] args)
        {
            foreach (var encoding in _Encodings)
                ListInformation.Add(encoding,0);

            if (!__WorkDirectory.Exists)
                try
                {
                    Directory.CreateDirectory(__WorkDirectory.FullName);
                }
                catch (UnauthorizedAccessException e)
                {
                    Console.WriteLine($"No access to work directory\n{e.Message}");
                    Console.ReadLine();
                    return;
                }
            else
            {
                try
                {
                    Directory.Delete(__WorkDirectory.FullName, true);
                    Directory.CreateDirectory(__WorkDirectory.FullName);
                }
                catch (UnauthorizedAccessException e)
                {
                    Console.WriteLine($"No access to work directory\n{e.Message}");
                    Console.ReadLine();
                    return;
                }
            }
            Console.WriteLine("Input max random count for file generation");
            while (!int.TryParse(Console.ReadLine(), out CountFiles))
                Console.WriteLine("This is not Number");

            Console.WriteLine($"-----------Generate files-----------");

            Console.WriteLine();
            GenerateDirectoriesAndFiles();
            Console.WriteLine();

            Console.WriteLine($"---------------FINISH---------------");
            Console.WriteLine();
            Console.WriteLine($"--------------SCANNING--------------");
            Console.WriteLine();
            var count = 0;
            foreach (var encoding in ListInformation)
            {
                Console.WriteLine($"{encoding.Key} - {encoding.Value} files was create");
                count += encoding.Value;
            }

            Console.WriteLine();
            Console.WriteLine($"Total was created {count} files");
            Console.WriteLine();
            Console.ReadLine();
        }

        private static void GenerateDirectoriesAndFiles()
        {
            var tasks = new List<Task>();
            var bars = new List<ProgressBar>();

            foreach (var fileMask in __FileMasks)
            {
                var count = _Random.Next(0, CountFiles);
                var pb = new ProgressBar(count);
                bars.Add(pb);
                var dir_name = fileMask.Trim(new[] { '*', '.' });
                var directory = __WorkDirectory.CreateSubdirectory(dir_name);
                tasks.Add(new Task(() => GenerateFiles(directory, dir_name,pb)));
            }

            foreach (var task in tasks)
                task.Start();

            Task.WaitAll(tasks.ToArray());


        }

        private static void GenerateFiles(DirectoryInfo directory, string extension, ProgressBar pb)
        {
            for (var i = 1; i < pb.Max+1; i++)
            {
                _Random = new Random();
                pb.Refresh(i,$"Create files in {directory.Name}");
                //создаём файл
                var file_name = $"{directory.Name} - {i}.{extension}";
                var file = new FileInfo(Path.Combine(directory.FullName,file_name));
                //рандомно выбираем кодировку
                var encoding = _Encodings[_Random.Next(0, _Encodings.Length)];
                var word_min = _Random.Next(5, 20);
                var word_max = _Random.Next(30, 100);
                using var writer = new StreamWriter(file.OpenWrite(),encoding);
                writer.WriteLine(LoremIpsum(word_min, word_max, _Random.Next(1, 20)));
                writer.Close();
                ListInformation[encoding] ++;
            }
        }
        private static string LoremIpsum(int minWords, int maxWords, int numLines)
        {
            int minSentences = minWords;
            int maxSentences = maxWords;

           var words = new[] { "lorem", "ipsum", "dolor", "sit", "amet", "consectetuer", "adipiscing", "elit", "sed", "diam", "nonummy", "nibh", "euismod", "tincidunt", "ut", "laoreet", "dolore", "magna", "aliquam", "erat" };

            int numSentences = _Random.Next(maxSentences - minSentences)
                               + minSentences + 1;
            int numWords = _Random.Next(maxWords - minWords) + minWords + 1;

            var sb = new StringBuilder();
            for (int p = 0; p < numLines; p++)
            {
                for (int s = 0; s < numSentences; s++)
                {
                    for (int w = 0; w < numWords; w++)
                    {
                        if (w > 0) { sb.Append(" "); }
                        sb.Append(words[_Random.Next(words.Length)]);
                    }
                    sb.Append(". ");
                }
                sb.AppendLine();
            }
            return sb.ToString();
        }
    }
}
