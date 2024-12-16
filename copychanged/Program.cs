using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace copychanged
{

    class FileToCopy {
        public string from;
        public string to;
        public bool existsAndDifferent;
    }


    class Program
    {
        static void Main(string[] args)
        {
            if(args.Length != 2)
            {
                Console.WriteLine("Call with 2 arguments: Reference folder and destination folder");
                return;
            }

            string folder1 = Path.GetFullPath(args[0]);
            string folder2 = Path.GetFullPath(args[1]);

            List<FileToCopy> filesToCopy = new List<FileToCopy>();

            UInt64 totalCompared = 0;

            Console.Write("\nStarting file compare...");
            
            Stopwatch sw = new Stopwatch();

            sw.Start();

            DoFolderRecursive(folder1, folder2, folder1, filesToCopy, ref totalCompared, sw);

            Console.WriteLine();
            Console.WriteLine($"{filesToCopy.Count} files must be copied.");



            //for (int i = 0; i < 10; i++)
            //{
            //    try
            //    {
            //        string file1 = args[0];
            //        string file2 = args[1];
            //        CancellationTokenSource cts = new CancellationTokenSource();

            //        Stopwatch sw = new Stopwatch();
            //        sw.Restart();
            //        bool same;
            //        using(FileStream fs1 = File.OpenRead(file1))
            //        {
            //            using(FileStream fs2 = File.OpenRead(file2))
            //            {
            //                same = VectorizedComparer.Same(fs1, fs2, cts.Token, default, true);
            //            }
            //        }
            //        sw.Stop();
            //        double time = (double)sw.ElapsedTicks / (double)Stopwatch.Frequency;
            //        Console.WriteLine($"Comparison took {time} seconds. Same: {same}");
            //    }
            //    catch (Exception e)
            //    {
            //        Console.WriteLine(e.ToString());
            //    }
            //}
            //Console.ReadKey();
        }

        static void DoFolderRecursive(string basePathReference, string basePathDestination, string reference, List<FileToCopy> filesToCopy, ref UInt64 totalCompared, Stopwatch sw)
        {
            if (!Directory.Exists(reference))
            {
                Console.WriteLine($"DoFolderRecursive: Reference path {reference} doesn't exist. Exiting.");
                return;
            }
            CancellationTokenSource cts = new CancellationTokenSource();

            string destinationFolder = Path.Combine(basePathDestination,Path.GetRelativePath(basePathReference,reference));
            string[] files = Directory.GetFiles(reference);
            foreach(string file in files)
            {
                string targetFile = Path.Combine(basePathDestination, Path.GetRelativePath(basePathReference, file));
                if (!File.Exists(targetFile))
                {
                    //Console.WriteLine($"target file {targetFile} doesn't exist. must copy.");
                    filesToCopy.Add(new FileToCopy() { 
                        from=file,
                        to=targetFile,
                        existsAndDifferent = false
                    });
                    continue;
                }
                else
                {
                    bool same;
                    UInt64 length1 = 0;
                    using (FileStream fs1 = File.OpenRead(file))
                    {
                        length1 = (UInt64)fs1.Length;
                        using (FileStream fs2 = File.OpenRead(targetFile))
                        {
                            same = VectorizedComparer.Same(fs1, fs2, cts.Token, default, true);
                        }
                    }
                    double time = (double)sw.ElapsedTicks / (double)Stopwatch.Frequency;
                    totalCompared += length1;

                    double bytesPerSecond = totalCompared / time;
                    string bpsString = bytesPerSecond.ToString("#,##0.00");
                    Console.Write($"\r{bpsString} bytes per second");

                    if (same)
                    {
                        filesToCopy.Add(new FileToCopy()
                        {
                            from = file,
                            to = targetFile,
                            existsAndDifferent = true
                        });
                    }

                    //Console.WriteLine($"Comparison of {file} took {time} seconds. Files are same: {same}");
                    //if (!same) { 
                    //   Console.WriteLine($"target file {targetFile} is different. must copy.");
                    //}
                }
            }

            string[] folders = Directory.GetDirectories(reference);
            foreach (string folder in folders)
            {
                DoFolderRecursive(basePathReference, basePathDestination, folder, filesToCopy,ref totalCompared,sw);
            }

        }

    }
}
