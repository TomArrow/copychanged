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
        static void PrintHelp()
        {

            Console.WriteLine("Usage: copychanged [-n,--donew] [-c,--dochanged] referencefolder destinationfolder");
            Console.WriteLine("Optional param meaning:");
            Console.WriteLine("-n,--donew        Copy files that didn't exist yet.");
            Console.WriteLine("-c,--dochanged    Copy files that are different (overwrite!).");
            Console.WriteLine("By default you are only told the amount of needed copy operations, no copying happens.");
            Console.WriteLine("Copy operations are ALWAYS verified and will be repeated if a difference is found.");
        }

        static void Main(string[] args)
        {
            if(args.Length < 2)
            {
                PrintHelp();
                return;
            }

            bool doNew = false;
            bool doChanged = false;

            string folder1 = null;
            string folder2 = null;

            foreach(string arg in args)
            {
                if (arg.Equals("--donew",StringComparison.OrdinalIgnoreCase) || arg.Equals("-n", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine("--donew/-n option detected. Will copy files that don't exist yet.");
                    doNew = true;
                }
                else if (arg.Equals("--dochanged",StringComparison.OrdinalIgnoreCase) || arg.Equals("-c", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine("--dochanged/-c option detected. Will overwrite files that changed.");
                    doChanged = true;
                } else if(folder1 == null)
                {
                    folder1 = arg;
                } else if(folder2 == null)
                {
                    folder2 = arg;
                }
                else
                {
                    Console.WriteLine($"Too many arguments/unrecognized argument: {arg}");
                    PrintHelp();
                    return;
                }
            }

            folder1 = Path.GetFullPath(folder1);
            folder2 = Path.GetFullPath(folder2);

            List<FileToCopy> filesToCopy = new List<FileToCopy>();
            List<FileToCopy> filesToFix = new List<FileToCopy>();

            UInt64 totalCompared = 0;

            Console.Write("\nStarting file compare...");
            
            Stopwatch sw = new Stopwatch();

            sw.Start();

            DoFolderRecursive(folder1, folder2, folder1, filesToCopy,filesToFix, ref totalCompared, sw);

            Console.WriteLine();
            Console.WriteLine($"{filesToCopy.Count} files don't exist yet.");
            Console.WriteLine($"{filesToFix.Count} files are different:");
            foreach(FileToCopy fileToFix in filesToFix)
            {
                Console.WriteLine($"{fileToFix.to}");
            }

            CancellationTokenSource cts = new CancellationTokenSource();

            if (doChanged)
            {
                Console.WriteLine("Overwriting changed files now"); 
                foreach (FileToCopy fileToFix in filesToFix)
                {
                    Console.Write($"{fileToFix.to}...");
                    try
                    {
                        bool different = true;
                        int currentAttempt = 0;
                        while (different)
                        {
                            File.Delete(fileToFix.to);
                            File.Copy(fileToFix.from, fileToFix.to);
                            different = !FilesAreSame(fileToFix.from, fileToFix.to,cts.Token);
                            if (different)
                            {
                                currentAttempt++;
                                Console.Write($"Overwritten file is STILL different. Retry {currentAttempt}....");
                            }
                            else
                            {
                                Console.Write($"File successfully updated.");
                            }
                        }
                    } catch(Exception ex)
                    {
                        Console.Write($"Error happened when trying to overwrite.");
                    }
                    Console.WriteLine();
                }
            }


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

        static bool FilesAreSame(string file1, string file2, CancellationToken ct)
        {
            using (FileStream fs1 = File.OpenRead(file1))
            {
                using (FileStream fs2 = File.OpenRead(file2))
                {
                    return VectorizedComparer.Same(fs1, fs2, ct, default, true);
                }
            }
        }

        static void DoFolderRecursive(string basePathReference, string basePathDestination, string reference, List<FileToCopy> filesToCopy, List<FileToCopy> filesToFix, ref UInt64 totalCompared, Stopwatch sw)
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

                    if (!same)
                    {
                        filesToFix.Add(new FileToCopy()
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
                DoFolderRecursive(basePathReference, basePathDestination, folder, filesToCopy,filesToFix,ref totalCompared,sw);
            }

        }

    }
}
